using Energisa.Context.Interface;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Helpers;
using Energisa.Eforce.Otimizador.Models;
using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Energisa.God.Domain.Model.Alocacao.Interface;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Google.Maps.RouteOptimization.V1;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Encapsula as etapas comuns de construção, execução e rastreamento da otimização.
/// Utilizado tanto pelo CronJob quanto pela API.
/// </summary>
public class OtimizacaoPipelineService(
    EforceParaGmproOrchestrator conversor,
    GmProBuilderService builderService,
    PosProcessamentoService posProcessamento,
    NoSqlDataService libGodDataService,
    IServiceScopeFactory scopeFactory,
    IOptions<GcsSettings> gcsSettings,
    IOptions<OtimizacaoSettings> otimizacaoSettings,
    IOptions<ReotimizacaoSettings> reotimizacaoSettings,
    ILogger<OtimizacaoPipelineService> logger,
    IContextRepository contextRepository,
    IHostEnvironment hostEnvironment,
    EforceGeoAmbienteApiClient eforceGeoAmbienteApiClient,
    ReotimizacaoRequestBuilderService reotimizacaoRequestBuilderService,
    GoogleCloudStorageService googleCloudStorageService,
    FiltroValidacaoOtimizacaoService filtroValidacao,
    SqlDataService? sqlDataService = null)
{
    private readonly EforceParaGmproOrchestrator _conversor = conversor;
    private readonly GmProBuilderService _builderService = builderService;
    private readonly PosProcessamentoService _posProcessamento = posProcessamento;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly GcsSettings _gcsSettings = gcsSettings.Value;
    private readonly OtimizacaoSettings _otimizacaoSettings = otimizacaoSettings.Value;
    private readonly ReotimizacaoSettings _reotimizacaoSettings = reotimizacaoSettings.Value;
    private readonly ILogger<OtimizacaoPipelineService> _logger = logger;
    private readonly IContextRepository _context = contextRepository;
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly EforceGeoAmbienteApiClient _eforceGeoAmbienteApiClient = eforceGeoAmbienteApiClient;
    private readonly ReotimizacaoRequestBuilderService _reotimizacaoRequestBuilderService = reotimizacaoRequestBuilderService;
    private readonly GoogleCloudStorageService _googleCloudStorageService = googleCloudStorageService;
    private readonly FiltroValidacaoOtimizacaoService _filtroValidacao = filtroValidacao;
    private readonly SqlDataService? _sqlDataService = sqlDataService;

    /// <summary>
    /// Carrega os dados via NoSQL. A origem Oracle permanece restrita ao fluxo de teste SqlDebug.
    /// </summary>
    public async Task<DadosEforce?> CarregarDadosAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var usarSqlDebug = _otimizacaoSettings.SqlDebug is { Habilitado: true }
            && _sqlDataService is not null;
        var origem = usarSqlDebug ? "OracleSqlDebug" : "NoSqlLibGod";

        try
        {
            _logger.LogInformation(
                "Iniciando carga de dados E-force. Empresa: {Empresa}. OrigemDados: {OrigemDados}.",
                _context.Empresa, origem);

            DadosEforce? dados;
            if (usarSqlDebug)
            {
                _logger.LogWarning(
                    "Fluxo de teste SqlDebug ativo: os dados serão consultados no Oracle. Empresa: {Empresa}.",
                    _context.Empresa);

                dados = await _sqlDataService!.ObterDadosViaSqlAsync(cancellationToken);
            }
            else
            {
                dados = await libGodDataService.ObterDadosAsync(_context, cancellationToken);
            }

            if (dados is null)
            {
                _logger.LogWarning(
                    "Carga de dados não retornou cenário para otimização. Empresa: {Empresa}. " +
                    "OrigemDados: {OrigemDados}. Motivo: {Motivo}. DuracaoMs: {DuracaoMs}. " +
                    "Consulte os logs de seleção para identificar o motivo específico.",
                    _context.Empresa, origem, "CargaSemCenario", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "Carga de dados concluída. Empresa: {Empresa}. OrigemDados: {OrigemDados}. " +
                    "Os: {Os}. Equipes: {Equipes}. RelacoesPerfilServico: {RelacoesPerfilServico}. " +
                    "Regioes: {Regioes}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, origem, dados.OrdensServico.Count, dados.Equipes.Count,
                    dados.PerfisEquipe.Count, dados.Regioes.Count, stopwatch.ElapsedMilliseconds);
            }

            return dados;
        }
        catch (Exception ex)
        {
            RegistrarFalha(
                ex, "CargaDados", origem,
                stopwatch.ElapsedMilliseconds, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Converte os dados, constrói a request e a persiste no NoSQL.
    /// </summary>
    public async Task<ResultadoConstrucaoRequest> ConstruirRequestAsync(
        DadosEforce dados,
        string labelSufixo,
        bool modoAsync,
        CancellationToken cancellationToken)
    {
        var stopwatchTotal = Stopwatch.StartNew();
        var stopwatchEtapa = Stopwatch.StartNew();
        var etapaAtual = "Conversao";
        var dataExecucao = DateTime.UtcNow;
        var label = $"Roterizacao Energisa {labelSufixo}: {dataExecucao:yyyyMMdd-HHmmss}";
        var projectId = _otimizacaoSettings.ProjectId;

        try
        {
            _logger.LogInformation(
                "Iniciando construção da request. Label: {Label}. Empresa: {Empresa}. " +
                "ModoAsync: {ModoAsync}. OsRecebidas: {OsRecebidas}. EquipesRecebidas: {EquipesRecebidas}.",
                label, _context.Empresa, modoAsync, dados.OrdensServico.Count, dados.Equipes.Count);

            _logger.LogInformation(
                "Iniciando conversão E-force para GMPRO. Label: {Label}.",
                label);

            var resultadoConversao = _conversor.ConverterComDetalhes(
                dados, dataExecucao, label, _context.Empresa);

            _logger.LogInformation(
                "Conversão concluída. Label: {Label}. OsProcessadas: {OsProcessadas}. " +
                "EquipesProcessadas: {EquipesProcessadas}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, resultadoConversao.OrdensProcessadas.Count,
                resultadoConversao.EquipesProcessadas.Count, stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "FiltroValidacao";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando aplicação de filtros de validação. Label: {Label}.",
                label);

            resultadoConversao = _filtroValidacao.AplicarFiltros(
                resultadoConversao, resultadoConversao.InicioTurno, resultadoConversao.FimTurno);

            _logger.LogInformation(
                "Filtros de validação aplicados. Label: {Label}. OsProcessadas: {OsProcessadas}. " +
                "EquipesProcessadas: {EquipesProcessadas}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, resultadoConversao.OrdensProcessadas.Count,
                resultadoConversao.EquipesProcessadas.Count, stopwatchEtapa.ElapsedMilliseconds);

            // Validar se há dados válidos para otimização
            if (resultadoConversao.OrdensProcessadas.Count == 0 || resultadoConversao.EquipesProcessadas.Count == 0)
            {
                _logger.LogWarning(
                    "Otimização interrompida: dados insuficientes após filtros. Label: {Label}. Empresa: {Empresa}. " +
                    "OsProcessadas: {OsProcessadas}. EquipesProcessadas: {EquipesProcessadas}. " +
                    "Não há cenário válido para otimização.",
                    label, _context.Empresa, resultadoConversao.OrdensProcessadas.Count,
                    resultadoConversao.EquipesProcessadas.Count);

                return new ResultadoConstrucaoRequest
                {
                    Label = label,
                    ErroInterrupcao = "Dados insuficientes após filtros de validação: nenhuma OS ou nenhuma equipe disponível para otimização.",
                    ReferenciaInput = null!
                };
            }

            etapaAtual = "ConstrucaoProtobuf";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando construção do OptimizeToursRequest. Label: {Label}.",
                label);

            var request = _builderService.Build(
                resultadoConversao.OrdensProcessadas,
                resultadoConversao.EquipesProcessadas,
                resultadoConversao.InicioTurno,
                resultadoConversao.FimTurno,
                projectId,
                label,
                modoAsync,
                resultadoConversao.TimeZone);

            var shipments = request.Model?.Shipments.Count ?? 0;
            var vehicles = request.Model?.Vehicles.Count ?? 0;

            _logger.LogInformation(
                "OptimizeToursRequest construído. Label: {Label}. Shipments: {Shipments}. " +
                "Vehicles: {Vehicles}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                "A contagem de shipments inclui refeições quando geradas pelo builder.",
                label, shipments, vehicles, stopwatchEtapa.ElapsedMilliseconds);

            if (vehicles == 0 || shipments == 0)
            {
                _logger.LogWarning(
                    "Request construída sem vehicles ou sem shipments. Label: {Label}. Empresa: {Empresa}. " +
                    "Motivo: {Motivo}. Vehicles: {Vehicles}. Shipments: {Shipments}. " +
                    "Este diagnóstico não interrompe o fluxo existente.",
                    label, _context.Empresa, "RequestSemVehiclesOuShipments", vehicles, shipments);
            }

            etapaAtual = "ValidacaoRequest";
            stopwatchEtapa.Restart();

            var inconsistencias = ValidadorRequestGmPro.Validar(request);
            foreach (var inconsistencia in inconsistencias)
            {
                _logger.LogWarning(
                    "Inconsistência no request. Label: {Label}. Empresa: {Empresa}. Severidade: {Severidade}. " +
                    "Caminho: {Caminho}. Rotulo: {Rotulo}. Detalhe: {Detalhe}.",
                    label, _context.Empresa, inconsistencia.Severidade, inconsistencia.Caminho,
                    inconsistencia.Rotulo, inconsistencia.Mensagem);
            }

            var criticas = inconsistencias.Count(i => i.Severidade == SeveridadeInconsistencia.Critico);
            _logger.LogInformation(
                "Validação pré-envio concluída. Label: {Label}. Criticas: {Criticas}. Alertas: {Alertas}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, criticas, inconsistencias.Count - criticas, stopwatchEtapa.ElapsedMilliseconds);

            if (criticas > 0)
            {
                return new ResultadoConstrucaoRequest
                {
                    Label = label,
                    ErroInterrupcao = $"Request inválido para a GMPRO: {criticas} inconsistência(s) crítica(s). Consulte os logs.",
                    ReferenciaInput = null!
                };
            }

            etapaAtual = "SerializacaoRequest";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando serialização da request. Label: {Label}.",
                label);

            var requestJson = JsonFormatter.Default.Format(request);

            // 3. Upload do request para o GCS, capturando a referência exata e imutável (§17).
            var prefixo = ObterPrefixoArquivo();
            var objectName = $"input/{prefixo}{(modoAsync ? "assincrono" : "sincrono")}-{Guid.NewGuid():N}.json";

            _logger.LogInformation(
                "Request serializada. Label: {Label}. CaracteresJson: {CaracteresJson}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, requestJson.Length, stopwatchEtapa.ElapsedMilliseconds);

            _logger.LogInformation("Upload da request para GCS ({ObjectName})...", objectName);
            var referenciaInput = await _googleCloudStorageService.UploadJsonComReferenciaAsync(
                _gcsSettings.BucketName, objectName, requestJson, cancellationToken);


            etapaAtual = "PersistenciaRequest";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando persistência da request no NoSQL. Label: {Label}. Empresa: {Empresa}.",
                label, _context.Empresa);

            using var scope = _scopeFactory.CreateScope();
            PropagarContexto(scope.ServiceProvider);

            var otimizadorRepository = scope.ServiceProvider
                .GetRequiredService<IOtimizadorRotasRepository>();

            await otimizadorRepository.CriaRequestOtimizadorAsync(
                request, _context.Empresa.ToString(), TipoOtimizacao.Automatica, referenciaInput);

            _logger.LogInformation(
                "Request persistida no NoSQL. Label: {Label}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, stopwatchEtapa.ElapsedMilliseconds);

            _logger.LogInformation(
                "Construção da request concluída. Label: {Label}. Empresa: {Empresa}. " +
                "DuracaoTotalMs: {DuracaoTotalMs}. O envio à Google ainda não foi realizado nesta etapa.",
                label, _context.Empresa, stopwatchTotal.ElapsedMilliseconds);

            return new ResultadoConstrucaoRequest
            {
                Label = label,
                Request = request,
                RequestJson = requestJson,
                ResultadoConversao = resultadoConversao
            };
        }
        catch (Exception ex)
        {
            RegistrarFalha(
                ex, "ConstrucaoRequest", etapaAtual,
                stopwatchTotal.ElapsedMilliseconds, cancellationToken, label);
            throw;
        }
    }

    /// <summary>
    /// Executa a otimização síncrona, persiste a resposta e gera o relatório.
    /// </summary>
    public async Task<ResultadoPipeline> ExecutarPipelineSincronoAsync(
        DadosEforce dados,
        string labelSufixo,
        bool modoAsync,
        CancellationToken cancellationToken)
    {
        var stopwatchTotal = Stopwatch.StartNew();
        var stopwatchEtapa = Stopwatch.StartNew();
        var etapaAtual = "ConstrucaoRequest";
        string? label = null;

        try
        {
            _logger.LogInformation(
                "Iniciando pipeline síncrono. Empresa: {Empresa}. LabelSufixo: {LabelSufixo}.",
                _context.Empresa, labelSufixo);

            var construcao = await ConstruirRequestAsync(
                dados, labelSufixo, modoAsync, cancellationToken);

            label = construcao.Label;

            // Validar se a construção foi interrompida por dados insuficientes
            if (!string.IsNullOrEmpty(construcao.ErroInterrupcao))
            {
                _logger.LogWarning(
                    "Pipeline síncrono abortado por dados insuficientes. Label: {Label}. Empresa: {Empresa}. " +
                    "Motivo: {Motivo}.",
                    label, _context.Empresa, construcao.ErroInterrupcao);

                throw new InvalidOperationException(construcao.ErroInterrupcao);
            }

            var projectId = _otimizacaoSettings.ProjectId;

            etapaAtual = "CriacaoClienteGoogle";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando criação do cliente Google. Label: {Label}. UsarRest: {UsarRest}.",
                label, _otimizacaoSettings.UsarRest);

            var credentialJson = _gcsSettings.GetCredentialJson();
            await using var apiClient = _otimizacaoSettings.UsarRest
                ? await GmProApiClient.CriarComRestAsync(projectId, credentialJson)
                : await GmProApiClient.CriarAsync(projectId, credentialJson);

            _logger.LogInformation(
                "Cliente Google criado. Label: {Label}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "OtimizacaoSincronaGoogle";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando envio síncrono à Google. Label: {Label}.",
                label);

            var response = await apiClient.OtimizarAsync(
                construcao.Request, cancellationToken);

            _logger.LogInformation(
                "Resposta recebida da Google. Label: {Label}. Rotas: {Rotas}. " +
                "ShipmentsIgnorados: {ShipmentsIgnorados}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, response.Routes.Count, response.SkippedShipments.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            if (response.SkippedShipments.Count > 0)
            {
                _logger.LogWarning(
                    "A resposta da Google contém shipments não atendidos. Label: {Label}. " +
                    "ShipmentsIgnorados: {ShipmentsIgnorados}. Consulte a resposta e o relatório para os detalhes.",
                    label, response.SkippedShipments.Count);
            }

            etapaAtual = "SerializacaoResponse";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando serialização da response. Label: {Label}.",
                label);

            var responseJson = JsonFormatter.Default.Format(response);

            _logger.LogInformation(
                "Response serializada. Label: {Label}. CaracteresJson: {CaracteresJson}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, responseJson.Length, stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "PersistenciaResponse";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando persistência da response no NoSQL. Label: {Label}. Empresa: {Empresa}.",
                label, _context.Empresa);

            using var scope = _scopeFactory.CreateScope();
            PropagarContexto(scope.ServiceProvider);

            var otimizadorRepository = scope.ServiceProvider
                .GetRequiredService<IOtimizadorRotasRepository>();

            await otimizadorRepository.CriaResponseOtimizadorAsync(
                response, _context.Empresa.ToString(),
                TipoOtimizacao.Automatica, construcao.Request);

            _logger.LogInformation(
                "Response persistida no NoSQL. Label: {Label}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "PosProcessamento";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando pós-processamento. Label: {Label}.",
                label);

            var relatorio = _posProcessamento.Processar(
                response, construcao.ResultadoConversao.OrdensProcessadas);

            _logger.LogInformation(
                "Pós-processamento concluído. Label: {Label}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                label, stopwatchEtapa.ElapsedMilliseconds);

            _logger.LogInformation(
                "Pipeline síncrono concluído. Label: {Label}. Empresa: {Empresa}. DuracaoTotalMs: {DuracaoTotalMs}.",
                label, _context.Empresa, stopwatchTotal.ElapsedMilliseconds);

            return new ResultadoPipeline
            {
                Label = construcao.Label,
                Relatorio = relatorio,
                RequestJson = construcao.RequestJson,
                ResponseJson = responseJson
            };
        }
        catch (Exception ex)
        {
            RegistrarFalha(
                ex, "PipelineSincrono", etapaAtual,
                stopwatchTotal.ElapsedMilliseconds, cancellationToken, label);
            throw;
        }
    }

    /// <summary>
    /// Dispara a otimização por URI no GCS e registra seu rastreamento,
    /// sem aguardar o resultado da Google.
    /// O upload do input permanece sob responsabilidade do chamador.
    /// </summary>
    public async Task<ResultadoDisparoOtimizacaoAssincrona> DispararOtimizacaoAssincronaAsync(
        OptimizeToursRequest request,
        string bucketName,
        string objectName,
        CancellationToken cancellationToken)
    {
        var stopwatchTotal = Stopwatch.StartNew();
        var stopwatchEtapa = Stopwatch.StartNew();
        var etapaAtual = "ValidacaoLabel";
        var statusDisparo = "NaoIniciado";
        string? operationName = null;
        var inputUri = $"gs://{bucketName}/{objectName}";
        string? outputUri = null;

        try
        {
            if (string.IsNullOrWhiteSpace(request.Label))
                throw new InvalidOperationException(
                    "A request deve possuir um label antes do disparo assíncrono.");

            var projectId = _otimizacaoSettings.ProjectId;

            etapaAtual = "PreparacaoUris";

            var outputObjectName = objectName
                .Replace("input/", "output/")
                .Replace(".json", "-response.json");

            outputUri = $"gs://{bucketName}/{outputObjectName}";

            _logger.LogInformation(
                "Iniciando preparação do disparo assíncrono. Label: {Label}. Empresa: {Empresa}. " +
                "InputUri: {InputUri}. OutputUri: {OutputUri}. UsarRest: {UsarRest}.",
                request.Label, _context.Empresa, inputUri, outputUri,
                _otimizacaoSettings.UsarRest);

            etapaAtual = "CriacaoClienteGoogle";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando criação do cliente Google. Label: {Label}.",
                request.Label);

            var credentialJson = _gcsSettings.GetCredentialJson();
            await using var apiClient = _otimizacaoSettings.UsarRest
                ? await GmProApiClient.CriarComRestAsync(projectId, credentialJson)
                : await GmProApiClient.CriarAsync(projectId, credentialJson);

            _logger.LogInformation(
                "Cliente Google criado. Label: {Label}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                request.Label, stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "DisparoGoogle";

            var uriRequest = new OptimizeToursUriRequest
            {
                Parent = $"projects/{projectId}",
                Input = new Google.Maps.RouteOptimization.V1.Uri { Uri_ = inputUri },
                Output = new Google.Maps.RouteOptimization.V1.Uri { Uri_ = outputUri }
            };

            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando envio assíncrono à Google. Label: {Label}. Empresa: {Empresa}. " +
                "InputUri: {InputUri}. OutputUri: {OutputUri}.",
                request.Label, _context.Empresa, inputUri, outputUri);

            statusDisparo = "RespostaNaoConfirmada";

            operationName = await apiClient.DispararOtimizacaoPorUriAsync(
                uriRequest, cancellationToken);

            if (string.IsNullOrWhiteSpace(operationName))
                throw new InvalidOperationException(
                    $"A Google não retornou o OperationName para a otimização '{request.Label}'.");

            statusDisparo = "Aceito";

            _logger.LogInformation(
                "Google aceitou a otimização assíncrona. Label: {Label}. Empresa: {Empresa}. " +
                "OperationName: {OperationName}. InputUri: {InputUri}. OutputUri: {OutputUri}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}. O resultado da otimização ainda não foi aguardado.",
                request.Label, _context.Empresa, operationName, inputUri, outputUri,
                stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "PersistenciaOperationName";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando persistência do OperationName. Label: {Label}. OperationName: {OperationName}.",
                request.Label, operationName);

            using var scope = _scopeFactory.CreateScope();
            PropagarContexto(scope.ServiceProvider);

            var otimizadorRepository = scope.ServiceProvider
                .GetRequiredService<IOtimizadorRotasRepository>();

            await otimizadorRepository.AtualizarOperationNameAsync(
                request.Label, operationName);

            _logger.LogInformation(
                "Atualização do OperationName no repositório concluída sem exceção. " +
                "Label: {Label}. OperationName: {OperationName}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                request.Label, operationName, stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "NotificacaoRastreamento";
            stopwatchEtapa.Restart();

            var rastreamentoNotificado = false;

            _logger.LogInformation(
                "Iniciando notificação de rastreamento. Label: {Label}. OperationName: {OperationName}.",
                request.Label, operationName);

            try
            {
                // O cliente deve lançar exceção se a notificação não for aceita.
                await _eforceGeoAmbienteApiClient.NotificarInicioOperacaoAsync(
                    operationName, cancellationToken);

                rastreamentoNotificado = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Falha no rastreamento após a Google aceitar a otimização. Label: {Label}. Empresa: {Empresa}. " +
                    "OperationName: {OperationName}. InputUri: {InputUri}. OutputUri: {OutputUri}. " +
                    "StatusRastreamento: {StatusRastreamento}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                    "A falha de rastreamento não cancela a operação já aceita pela Google.",
                    request.Label, _context.Empresa, operationName, inputUri, outputUri,
                    "NaoConfirmado", stopwatchEtapa.ElapsedMilliseconds);
            }

            _logger.Log(
                rastreamentoNotificado ? LogLevel.Information : LogLevel.Warning,
                "Disparo assíncrono finalizado. Label: {Label}. Empresa: {Empresa}. " +
                "OperationName: {OperationName}. InputUri: {InputUri}. OutputUri: {OutputUri}. " +
                "StatusDisparo: {StatusDisparo}. StatusRastreamento: {StatusRastreamento}. " +
                "DuracaoRastreamentoMs: {DuracaoRastreamentoMs}. DuracaoTotalMs: {DuracaoTotalMs}. " +
                "Este evento confirma o disparo, não a conclusão da otimização nem a geração do output.",
                request.Label, _context.Empresa, operationName, inputUri, outputUri,
                statusDisparo, rastreamentoNotificado ? "Confirmado" : "NaoConfirmado",
                stopwatchEtapa.ElapsedMilliseconds, stopwatchTotal.ElapsedMilliseconds);

            return new ResultadoDisparoOtimizacaoAssincrona(
                operationName,
                inputUri,
                outputUri,
                rastreamentoNotificado);
        }
        catch (Exception ex)
        {
            RegistrarFalha(
                ex, "DisparoAssincrono", etapaAtual,
                stopwatchTotal.ElapsedMilliseconds, cancellationToken,
                request?.Label, operationName, inputUri, outputUri, statusDisparo);
            throw;
        }
    }

    /// <summary>
    /// Retorna o prefixo de arquivo conforme SqlDebug ou ambiente.
    /// </summary>
    public string ObterPrefixoArquivo()
    {
        if (_otimizacaoSettings.SqlDebug is { Habilitado: true })
            return "esp_";

        return _hostEnvironment.EnvironmentName switch
        {
            "Development" => "dev_",
            "Staging" => "hml_",
            "Production" => "prd_",
            _ => "dev_"
        };
    }

    /// <summary>
    /// Obtém o resultado persistido no NoSQL a partir do label.
    /// </summary>
    public async Task<ResultadoOtimizacao?> ObterResultadoOtimizacaoAsync(string label)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Iniciando consulta do resultado no NoSQL. Label: {Label}. Empresa: {Empresa}.",
                label, _context.Empresa);

            using var scope = _scopeFactory.CreateScope();

            // Preserva a consulta original por label, propagando apenas a correlação.
            var scopedContext = scope.ServiceProvider.GetRequiredService<IContextRepository>();
            scopedContext.RequestId = _context.RequestId;
            scopedContext.SourceRequestId = _context.SourceRequestId;

            var otimizadorRepository = scope.ServiceProvider
                .GetRequiredService<IOtimizadorRotasRepository>();

            var resultado = await otimizadorRepository.ObterResultadoOtimizacaoAsync(label);

            _logger.LogInformation(
                "Consulta do resultado concluída. Label: {Label}. Empresa: {Empresa}. " +
                "Encontrado: {Encontrado}. DuracaoMs: {DuracaoMs}.",
                label, _context.Empresa, resultado is not null, stopwatch.ElapsedMilliseconds);

            return resultado;
        }
        catch (Exception ex)
        {
            RegistrarFalha(
                ex, "ConsultaResultado", "ConsultaNoSql",
                stopwatch.ElapsedMilliseconds, CancellationToken.None, label);
            throw;
        }
    }

    private void PropagarContexto(IServiceProvider serviceProvider)
    {
        var scopedContext = serviceProvider.GetRequiredService<IContextRepository>();
        scopedContext.User = _context.User;
        scopedContext.Empresa = _context.Empresa;
        scopedContext.RequestId = _context.RequestId;
        scopedContext.SourceRequestId = _context.SourceRequestId;
    }

    private void RegistrarFalha(
        Exception exception,
        string fluxo,
        string etapa,
        long duracaoTotalMs,
        CancellationToken cancellationToken,
        string? label = null,
        string? operationName = null,
        string? inputUri = null,
        string? outputUri = null,
        string? statusDisparo = null)
    {
        var cancelado = exception is OperationCanceledException
            && cancellationToken.IsCancellationRequested;

        _logger.Log(
            cancelado ? LogLevel.Warning : LogLevel.Error,
            exception,
            "Pipeline interrompido. Fluxo: {Fluxo}. Etapa: {Etapa}. Status: {Status}. " +
            "Empresa: {Empresa}. Label: {Label}. OperationName: {OperationName}. " +
            "InputUri: {InputUri}. OutputUri: {OutputUri}. StatusDisparo: {StatusDisparo}. " +
            "DuracaoTotalMs: {DuracaoTotalMs}.",
            fluxo, etapa, cancelado ? "Cancelado" : "Falha", _context.Empresa,
            label, operationName, inputUri, outputUri, statusDisparo, duracaoTotalMs);
    }

    /// <summary>
    /// Executa a reotimização intradiária (§17): constrói o novo request GMPRO a partir do
    /// <see cref="ContextoReotimizacao"/> (snapshot) já validado e classificado, persiste-o no
    /// NoSQL, faz upload do JSON para o GCS capturando uma <see cref="ReferenciaArmazenamentoGcs"/>
    /// exata e imutável (bucket, objeto, geração e hash), e dispara a otimização de forma
    /// assíncrona (fire-and-forget) via URI. A referência de input (e, quando disponível, a de
    /// output) deve ser persistida no NoSQL para permitir a recuperação garantida do protobuf
    /// bruto original em reotimizações futuras, sem depender de convenção de nomes (§17).
    /// </summary>
    /// <param name="contexto">Snapshot operacional completo (§5), já com as OSs classificadas (§7).</param>
    /// <param name="novosShipments">Shipments das novas OSs pendentes, já com pesos calculados sobre
    /// o conjunto completo de OSs (§12).</param>
    /// <param name="label">Label único da reotimização, utilizado para correlação (não deve ser
    /// utilizado como chave de negócio ou caminho de arquivo fora deste escopo, §17).</param>
    /// <param name="cancellationToken">Token de cancelamento propagado até o disparo GMPRO/GCS.</param>
    public async Task<ResultadoConstrucaoReotimizacao> ExecutarReotimizacaoAsync(
        ContextoReotimizacao contexto,
        List<Shipment> novosShipments,
        string label,
        CancellationToken cancellationToken)
    {
        var searchMode = ResolverSearchMode(_reotimizacaoSettings.ModoBusca);

        _logger.LogInformation(
            "Construindo request de reotimização. OtimizacaoOriginalId: {OtimizacaoOriginalId}, Equipes: {Equipes}.",
            contexto.OtimizacaoOriginalId, contexto.EquipesSelecionadas.Count);

        // 1. Construir o novo request GMPRO (serviço puro, sem acesso a banco).
        var resultadoConstrucao = _reotimizacaoRequestBuilderService.Build(contexto, novosShipments, searchMode);
        resultadoConstrucao.NovoRequest.Label = label;

        if (resultadoConstrucao.OrdensNaoAtendidas.Count > 0)
        {
            _logger.LogWarning(
                "Reotimização {Label} possui {Quantidade} OS(s) não atendidas por incompatibilidade com as equipes selecionadas.",
                label, resultadoConstrucao.OrdensNaoAtendidas.Count);
        }

        var requestJson = JsonFormatter.Default.Format(resultadoConstrucao.NovoRequest);

        // 2. Upload do request para o GCS, capturando a referência exata e imutável (§17):
        // nome único (GUID) + geração + hash MD5, nunca reconstruída por convenção.
        var prefixo = ObterPrefixoArquivo();
        var objectName = $"input/{prefixo}reotimizacao-{Guid.NewGuid():N}.json";

        _logger.LogInformation("Upload do request de reotimização para GCS ({ObjectName})...", objectName);
        var referenciaInput = await _googleCloudStorageService.UploadJsonComReferenciaAsync(
            _gcsSettings.BucketName, objectName, requestJson, cancellationToken);

        // 3. Persistir o novo request no NoSQL (via CriaRequestReotimizadorAsync, §15/§17),
        // incluindo a referência exata e imutável de input capturada no upload (§17). A
        // IdempotencyKey (não o label) é a chave de idempotência persistida (§3, §16); o
        // registro é criado com StatusProcessamento = Processando.
        string reotimizacaoId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var scopedContext = scope.ServiceProvider.GetRequiredService<IContextRepository>();
            scopedContext.User = _context.User;
            scopedContext.Empresa = _context.Empresa;
            var otimizadorRepository = scope.ServiceProvider.GetRequiredService<IOtimizadorRotasRepository>();
            var registro = await otimizadorRepository.CriaRequestReotimizadorAsync(
                resultadoConstrucao.NovoRequest,
                _context.Empresa.ToString(),
                contexto.OtimizacaoOriginalId,
                contexto.EquipesSelecionadas.Select(e => e.CodigoEquipe).ToList(),
                contexto.IdempotencyKey,
                contexto.DataCorteUtc,
                contexto.FusoHorario.Id,
                referenciaInput);
            reotimizacaoId = registro.Id;
        }
        _logger.LogInformation(
            "Request de reotimização '{Label}' persistido no NoSQL (Id: {ReotimizacaoId}). Referência GCS do input: gs://{Bucket}/{Objeto}#{Geracao}.",
            label, reotimizacaoId, referenciaInput.BucketName, referenciaInput.ObjectName, referenciaInput.Generation);

        resultadoConstrucao.ReotimizacaoId = reotimizacaoId;

        // 4. Disparar a otimização de forma assíncrona via URI (mesmo fluxo do pipeline automático).
        var operationName = await DispararOtimizacaoAssincronaAsync(
            resultadoConstrucao.NovoRequest, _gcsSettings.BucketName, objectName, cancellationToken);

        _logger.LogInformation(
            "Reotimização {Label} disparada com sucesso. OperationName: {OperationName}.",
            label, operationName);

        return resultadoConstrucao;
    }

    /// <summary>
    /// Resolve o <see cref="OptimizeToursRequest.Types.SearchMode"/> a partir da configuração
    /// <see cref="ReotimizacaoSettings.ModoBusca"/> (§21). Deve aceitar somente valores conhecidos
    /// e nunca retornar <c>Unspecified</c> (§14). -- Revisado, pois o ReturnFast não é usado no Async.
    /// </summary>
    private static OptimizeToursRequest.Types.SearchMode ResolverSearchMode(string modoBusca)
    {
        return modoBusca switch
        {
            "Unspecified" => OptimizeToursRequest.Types.SearchMode.Unspecified,
            "ReturnFast" => OptimizeToursRequest.Types.SearchMode.ReturnFast,
            "ConsumeAllAvailableTime" => OptimizeToursRequest.Types.SearchMode.ConsumeAllAvailableTime,            
            _ => throw new InvalidOperationException(
                $"Valor de configuração 'Reotimizacao:ModoBusca' inválido: '{modoBusca}'. " +
                "Valores aceitos: 'ReturnFast', 'ConsumeAllAvailableTime' ou 'Unspecified'.")
        };
    }
}
