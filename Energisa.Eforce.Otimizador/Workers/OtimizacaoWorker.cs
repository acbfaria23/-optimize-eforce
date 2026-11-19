using Energisa.Context;
using Energisa.Context.Interface;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Services;
using Energisa.God.Domain.Repository.Config;
using Energisa.God.Domain.Util.Interface;
using Energisa.God.InfraData.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Energisa.Eforce.Otimizador.Workers;

/// <summary>
/// Worker que executa o pipeline completo de otimização de rotas via CronJob.
/// Delega toda a lógica de negócio ao <see cref="OtimizacaoPipelineService"/>.
/// </summary>
public class OtimizacaoWorker(
    GoogleCloudStorageService storageService,
    IOptions<GcsSettings> gcsSettings,
    IOptions<OtimizacaoSettings> otimizacaoSettings,
    ILogger<OtimizacaoWorker> logger,
    IHostApplicationLifetime lifetime,
    IServiceProvider serviceProvider) : SequencialMultiTenantBackgroundService(serviceProvider, new ContextRepository(),logger)
{
    private readonly GoogleCloudStorageService _storageService = storageService;
    private readonly GcsSettings _gcsSettings = gcsSettings.Value;
    private readonly OtimizacaoSettings _otimizacaoSettings = otimizacaoSettings.Value;
    private readonly ILogger<OtimizacaoWorker> _logger = logger;
    private readonly IHostApplicationLifetime _lifetime = lifetime;

    private const string ChaveHorarioOtimizacao = "Eforce:HorarioOtimizacaoAgendada";
    private const int ToleranciaAgendamentoMinutos = 10;

    public override void DoWork(
        IContextRepository context,
        IServiceScope scope)
    {
        var cancellationToken = CancellationToken.None;
        // A classe-base já abre o escopo de logging com este contexto por empresa.
        // Preenchemos os campos nativos reconhecidos pelo logger da Energisa.
        if (string.IsNullOrWhiteSpace(context.RequestId))
            context.RequestId = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(context.SourceRequestId))
            context.SourceRequestId = context.RequestId;

        var execucaoOtimizacaoId = context.RequestId;
        var stopwatchTotal = Stopwatch.StartNew();
        var stopwatchEtapa = Stopwatch.StartNew();
        var etapaAtual = "Inicializacao";
        var modoOtimizacao = _otimizacaoSettings.ModoAsync ? "Assincrono" : "Sincrono";
        var instancia = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;

        string? labelOtimizacao = null;
        string? inputUri = null;

        try
        {
            context.User = "MsGodOtimizador";

            _logger.LogInformation(
                "Tentativa de otimização iniciada. StatusExecucao: {StatusExecucao}. " +
                "ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. SourceRequestId: {SourceRequestId}. " +
                "Empresa: {Empresa}. ModoOtimizacao: {ModoOtimizacao}. " +
                "Instancia: {Instancia}. ProjectId: {ProjectId}.",
                "Iniciada",
                execucaoOtimizacaoId,
                context.SourceRequestId,
                context.Empresa,
                modoOtimizacao,
                instancia,
                _otimizacaoSettings.ProjectId);

            etapaAtual = "ValidacaoAgendamento";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando validação do agendamento. Etapa: {Etapa}. Chave: {Chave}.",
                etapaAtual,
                ChaveHorarioOtimizacao);

            var dateTimeUtil = scope.ServiceProvider.GetRequiredService<IDateTimeUtil>();
            var parametroConfigRepo = scope.ServiceProvider.GetRequiredService<IParametroConfigRepository>();
            var parametro = parametroConfigRepo.ObterPorChaveAsync(ChaveHorarioOtimizacao).GetAwaiter().GetResult();

#if DEBUG
            if (context.Empresa != 226) //--> Para teste manual, retirar depois
            {
#endif
                if (parametro?.Valores == null || !parametro.Valores.Any())
                {
                    _logger.LogWarning(
                        "Tentativa de otimização ignorada porque o parâmetro de agendamento está ausente ou vazio. " +
                        "Chave: {Chave}. StatusExecucao: {StatusExecucao}. " +
                        "ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}. " +
                        "DuracaoTotalMs: {DuracaoTotalMs}.",
                        ChaveHorarioOtimizacao,
                        "IgnoradaSemAgendamento",
                        execucaoOtimizacaoId,
                        context.Empresa,
                        stopwatchTotal.ElapsedMilliseconds);
                    return;
                }

                var agora = dateTimeUtil.Now()!.Value;
                var horariosAgendados = parametro.Valores.Values.ToList();
                var horariosValidos = horariosAgendados
                    .Select(valor => new
                    {
                        Valor = valor,
                        Valido = TimeSpan.TryParse(valor, out var horario),
                        Horario = horario
                    })
                    .Where(item => item.Valido)
                    .Select(item => item.Horario)
                    .ToList();

                var horariosInvalidos = horariosAgendados
                    .Where(valor => !TimeSpan.TryParse(valor, out _))
                    .ToList();

                if (horariosInvalidos.Count > 0)
                {
                    _logger.LogWarning(
                        "Parâmetro de agendamento contém horários inválidos. Chave: {Chave}. " +
                        "HorariosInvalidos: {HorariosInvalidos}.",
                        ChaveHorarioOtimizacao,
                        string.Join(", ", horariosInvalidos));
                }

                if (horariosValidos.Count == 0)
                {
                    _logger.LogWarning(
                        "Tentativa de otimização ignorada porque nenhum horário configurado é válido. " +
                        "Chave: {Chave}. StatusExecucao: {StatusExecucao}. " +
                        "ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}. " +
                        "DuracaoTotalMs: {DuracaoTotalMs}.",
                        ChaveHorarioOtimizacao,
                        "IgnoradaAgendamentoInvalido",
                        execucaoOtimizacaoId,
                        context.Empresa,
                        stopwatchTotal.ElapsedMilliseconds);
                    return;
                }

                if (!horariosValidos.Any(horarioAgendado =>
                    Math.Abs((agora.TimeOfDay - horarioAgendado).TotalMinutes) <= ToleranciaAgendamentoMinutos))
                {
                    _logger.LogInformation(
                        "Tentativa de otimização fora da janela de agendamento. HoraAtual: {HoraAtual}. " +
                        "HorariosAgendados: {HorariosAgendados}. ToleranciaMinutos: {ToleranciaMinutos}. " +
                        "StatusExecucao: {StatusExecucao}. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. " +
                        "Empresa: {Empresa}. DuracaoTotalMs: {DuracaoTotalMs}.",
                        agora.ToString("HH:mm:ss"),
                        string.Join(", ", horariosValidos.Select(horario => horario.ToString(@"hh\:mm\:ss"))),
                        ToleranciaAgendamentoMinutos,
                        "IgnoradaForaJanela",
                        execucaoOtimizacaoId,
                        context.Empresa,
                        stopwatchTotal.ElapsedMilliseconds);
                    return;
                }

                _logger.LogInformation(
                    "Janela de agendamento validada. HoraAtual: {HoraAtual}. " +
                    "HorariosAgendados: {HorariosAgendados}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                    agora.ToString("HH:mm:ss"),
                    string.Join(", ", horariosValidos.Select(horario => horario.ToString(@"hh\:mm\:ss"))),
                    stopwatchEtapa.ElapsedMilliseconds);
#if DEBUG
            }
            else
            {
                _logger.LogWarning(
                    "Validação da janela de agendamento não aplicada devido à exceção de teste existente na compilação DEBUG. " +
                    "Empresa: {Empresa}. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}.",
                    context.Empresa,
                    execucaoOtimizacaoId);
            }
#endif

            etapaAtual = "CargaDadosNoSql";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando carga dos dados para otimização. Etapa: {Etapa}. Empresa: {Empresa}.",
                etapaAtual,
                context.Empresa);

            var _pipeline = scope.ServiceProvider.GetRequiredService<OtimizacaoPipelineService>();

            var dados = _pipeline.CarregarDadosAsync(cancellationToken).GetAwaiter().GetResult();
            if (dados is null)
            {
                _logger.LogWarning(
                    "Tentativa de otimização encerrada sem dados elegíveis, antes da construção da request e do envio ao GMPRO. " +
                    "StatusExecucao: {StatusExecucao}. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. " +
                    "Empresa: {Empresa}. Etapa: {Etapa}. " +
                    "DuracaoEtapaMs: {DuracaoEtapaMs}. DuracaoTotalMs: {DuracaoTotalMs}.",
                    "SemDadosElegiveis",
                    execucaoOtimizacaoId,
                    context.Empresa,
                    etapaAtual,
                    stopwatchEtapa.ElapsedMilliseconds,
                    stopwatchTotal.ElapsedMilliseconds);
                return;
            }

            _logger.LogInformation(
                "Carga de dados concluída. OS: {Os}. Equipes: {Equipes}. " +
                "Perfis: {Perfis}. Regioes: {Regioes}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                dados.OrdensServico.Count,
                dados.Equipes.Count,
                dados.PerfisEquipe.Count,
                dados.Regioes.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            var labelSufixo = $"Cronjob, Metodo {(_otimizacaoSettings.ModoAsync ? "Assincrono" : "Sincrono")}";

            if (_otimizacaoSettings.ModoAsync)
            {
                etapaAtual = "ConstrucaoRequest";
                stopwatchEtapa.Restart();

                _logger.LogInformation(
                    "Iniciando construção e persistência da request de otimização. " +
                    "Etapa: {Etapa}. OS: {Os}. Equipes: {Equipes}. Perfis: {Perfis}.",
                    etapaAtual,
                    dados.OrdensServico.Count,
                    dados.Equipes.Count,
                    dados.PerfisEquipe.Count);

                var construcao = _pipeline.ConstruirRequestAsync(
                    dados, labelSufixo, modoAsync: true, cancellationToken).GetAwaiter().GetResult() ;

                labelOtimizacao = construcao.Label;

                if (!string.IsNullOrEmpty(construcao.ErroInterrupcao))
                {
                    _logger.LogWarning(
                        "Construção da request interrompida. Label: {Label}. Motivo: {Motivo}.",
                        construcao.Label, construcao.ErroInterrupcao);

                    throw new InvalidOperationException(construcao.ErroInterrupcao);
                }

                _logger.LogInformation(
                    "Request de otimização construída e persistida. Label: {Label}. " +
                    "OsProcessadas: {OsProcessadas}. EquipesProcessadas: {EquipesProcessadas}. " +
                    "ShipmentsTotais: {ShipmentsTotais}. Veiculos: {Veiculos}. " +
                    "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                    construcao.Label,
                    construcao.ResultadoConversao.OrdensProcessadas.Count,
                    construcao.ResultadoConversao.EquipesProcessadas.Count,
                    construcao.Request.Model?.Shipments.Count ?? 0,
                    construcao.Request.Model?.Vehicles.Count ?? 0,
                    stopwatchEtapa.ElapsedMilliseconds);

                var modoNome = _otimizacaoSettings.ModoAsync ? "assincrono" : "sincrono";
                var prefixo = _pipeline.ObterPrefixoArquivo();
                var objectName = $"input/{prefixo}{modoNome}-{Guid.NewGuid():N}.json";

                inputUri = $"gs://{_gcsSettings.BucketName}/{objectName}";

                etapaAtual = "UploadInputGcs";
                stopwatchEtapa.Restart();

                _logger.LogInformation(
                    "Iniciando upload do input para o GCS. Label: {Label}. Bucket: {Bucket}. " +
                    "ObjectName: {ObjectName}. InputUri: {InputUri}.",
                    construcao.Label,
                    _gcsSettings.BucketName,
                    objectName,
                    inputUri);

                _storageService.UploadJsonAsync(
                    _gcsSettings.BucketName, objectName, construcao.RequestJson, cancellationToken).GetAwaiter().GetResult();

                _logger.LogInformation(
                    "Upload do input concluído. Label: {Label}. Bucket: {Bucket}. " +
                    "ObjectName: {ObjectName}. InputUri: {InputUri}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                    construcao.Label,
                    _gcsSettings.BucketName,
                    objectName,
                    inputUri,
                    stopwatchEtapa.ElapsedMilliseconds);

                etapaAtual = "DisparoGoogleERastreamento";
                stopwatchEtapa.Restart();

                _logger.LogInformation(
                    "Iniciando disparo da otimização assíncrona e notificação de rastreamento. " +
                    "Etapa: {Etapa}. Label: {Label}. InputUri: {InputUri}.",
                    etapaAtual,
                    construcao.Label,
                    inputUri);

                var resultadoDisparo = _pipeline.DispararOtimizacaoAssincronaAsync(
                    construcao.Request, _gcsSettings.BucketName, objectName, cancellationToken).GetAwaiter().GetResult();

                if (resultadoDisparo.RastreamentoNotificado)
                {
                    _logger.LogInformation(
                        "Disparo da otimização concluído com rastreamento confirmado; o processamento assíncrono será acompanhado pelo retorno. " +
                        "Label: {Label}. OperationName: {OperationName}. InputUri: {InputUri}. OutputUri: {OutputUri}. " +
                        "StatusExecucao: {StatusExecucao}. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. " +
                        "Empresa: {Empresa}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                        "DuracaoTotalMs: {DuracaoTotalMs}.",
                        construcao.Label,
                        resultadoDisparo.OperationName,
                        resultadoDisparo.InputUri,
                        resultadoDisparo.OutputUri,
                        "DespachadaERastreada",
                        execucaoOtimizacaoId,
                        context.Empresa,
                        stopwatchEtapa.ElapsedMilliseconds,
                        stopwatchTotal.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogWarning(
                        "Tentativa de otimização despachada, mas sem confirmação do rastreamento. " +
                        "Label: {Label}. OperationName: {OperationName}. InputUri: {InputUri}. " +
                        "OutputUri: {OutputUri}. StatusExecucao: {StatusExecucao}. " +
                        "ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}. " +
                        "DuracaoEtapaMs: {DuracaoEtapaMs}. DuracaoTotalMs: {DuracaoTotalMs}.",
                        construcao.Label,
                        resultadoDisparo.OperationName,
                        resultadoDisparo.InputUri,
                        resultadoDisparo.OutputUri,
                        "DespachadaSemRastreamento",
                        execucaoOtimizacaoId,
                        context.Empresa,
                        stopwatchEtapa.ElapsedMilliseconds,
                        stopwatchTotal.ElapsedMilliseconds);
                }
            }
            else
            {
                etapaAtual = "PipelineSincrono";
                stopwatchEtapa.Restart();

                _logger.LogInformation(
                    "Iniciando pipeline de otimização síncrona. Etapa: {Etapa}. " +
                    "OS: {Os}. Equipes: {Equipes}. Perfis: {Perfis}.",
                    etapaAtual,
                    dados.OrdensServico.Count,
                    dados.Equipes.Count,
                    dados.PerfisEquipe.Count);

                var resultado = _pipeline.ExecutarPipelineSincronoAsync(
                    dados, labelSufixo, modoAsync: false, cancellationToken).GetAwaiter().GetResult();

                labelOtimizacao = resultado.Label;

                _logger.LogInformation(
                    "Tentativa de otimização síncrona concluída com sucesso. Label: {Label}. " +
                    "StatusExecucao: {StatusExecucao}. ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. " +
                    "Empresa: {Empresa}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                    "DuracaoTotalMs: {DuracaoTotalMs}.",
                    resultado.Label,
                    "ConcluidaSincrona",
                    execucaoOtimizacaoId,
                    context.Empresa,
                    stopwatchEtapa.ElapsedMilliseconds,
                    stopwatchTotal.ElapsedMilliseconds);

                resultado.Relatorio.Imprimir();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Tentativa de otimização cancelada. Etapa: {Etapa}. StatusExecucao: {StatusExecucao}. " +
                "ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}. " +
                "Label: {Label}. InputUriPlanejada: {InputUriPlanejada}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}. DuracaoTotalMs: {DuracaoTotalMs}.",
                etapaAtual,
                "Cancelada",
                execucaoOtimizacaoId,
                context.Empresa,
                labelOtimizacao,
                inputUri,
                stopwatchEtapa.ElapsedMilliseconds,
                stopwatchTotal.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;

            _logger.LogError(
                ex,
                "Tentativa de otimização falhou. Etapa: {Etapa}. StatusExecucao: {StatusExecucao}. " +
                "ExecucaoOtimizacaoId: {ExecucaoOtimizacaoId}. Empresa: {Empresa}. " +
                "Label: {Label}. InputUriPlanejada: {InputUriPlanejada}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}. DuracaoTotalMs: {DuracaoTotalMs}.",
                etapaAtual,
                "Falha",
                execucaoOtimizacaoId,
                context.Empresa,
                labelOtimizacao,
                inputUri,
                stopwatchEtapa.ElapsedMilliseconds,
                stopwatchTotal.ElapsedMilliseconds);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var instancia = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;

        try
        {
            _logger.LogInformation(
                "Cronjob do otimizador iniciado. Instancia: {Instancia}. ModoOtimizacao: {ModoOtimizacao}.",
                instancia,
                _otimizacaoSettings.ModoAsync ? "Assincrono" : "Sincrono");

            await base.ExecuteAsync(stoppingToken);

            _logger.LogInformation(
                "Processamento de todos os tenants concluído. ExitCode: {ExitCode}. " +
                "Instancia: {Instancia}. DuracaoTotalMs: {DuracaoTotalMs}.",
                Environment.ExitCode,
                instancia,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Processamento dos tenants cancelado pelo encerramento da aplicação. " +
                "Instancia: {Instancia}. DuracaoTotalMs: {DuracaoTotalMs}.",
                instancia,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;

            _logger.LogCritical(
                ex,
                "Processamento dos tenants encerrado por falha. ExitCode: {ExitCode}. " +
                "Instancia: {Instancia}. DuracaoTotalMs: {DuracaoTotalMs}.",
                Environment.ExitCode,
                instancia,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

   
}