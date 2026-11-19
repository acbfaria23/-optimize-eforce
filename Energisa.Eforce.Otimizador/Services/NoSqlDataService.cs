using Energisa.God.Domain.Definitions;
using Energisa.God.Domain.Model._OrdemServico_.Enum;
using Energisa.God.Domain.Repository._OrdemServico_.Interface;
using Energisa.God.Domain.Repository.Modelo.Interface;
using Energisa.God.Domain.Repository.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Mappers;
using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.Context.Interface;
using Energisa.God.Domain.Model.Modelo;
using Energisa.God.Domain.Model.Modelo.Enum;
using Energisa.God.Domain.Service.Alocacao.Interface;
using Energisa.God.Domain.Util.Interface;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Diagnostics;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço responsável por buscar dados no banco NoSQL via repositórios da LibGod
/// e convertê-los para o modelo local <see cref="DadosEforce"/>.
/// </summary>
public class NoSqlDataService
{
    private readonly IOrdemServicoRepository _osRepository;
    private readonly IServicoRepository _servicoRepository;
    private readonly IEquipeRepository _equipeRepository;
    private readonly IPerfilEquipeRelacaoRepository _perfilRelacaoRepository;
    private readonly IPerfilEquipeRepository _perfilEquipeRepository;
    private readonly IParametroConfigRepository _parametroConfigRepository;
    private readonly ITempoMedioExecucaoService _tempoMedioExecucaoService;
    private readonly OtimizacaoSettings _settings;
    private readonly ILogger<NoSqlDataService> _logger;
    private readonly IDateTimeUtil _dateTimeUtil;

    private const string ChaveTempoChecklistVeiculo = "Eforce:TempoChecklistVeiculo";
    private const string ChaveServicosExclusao = "Eforce:ServicosExclusaoOtimizador";
    private const string ChaveTipoServicoOtimizacao = "Eforce:TipoServicoOtimizacao";
    private const string ChaveValoresPesoOtimizacao = "Eforce:ValoresPesoOtimizacao";

    private readonly IMongoCollection<OrdemServico> _ordensServicoCollection;
    private const string OrdensServicoCollectionName = "ordem_servico";
    private const int LoteLeitura = 500;

    private readonly FiltroElegibilidadeOsService? _filtroElegibilidade;

    public NoSqlDataService(
        IOrdemServicoRepository osRepository,
        IServicoRepository servicoRepository,
        IEquipeRepository equipeRepository,
        IPerfilEquipeRelacaoRepository perfilRelacaoRepository,
        IPerfilEquipeRepository perfilEquipeRepository,
        IParametroConfigRepository parametroConfigRepository,
        ITempoMedioExecucaoService tempoMedioExecucaoService,
        IOptions<OtimizacaoSettings> settings,
        IDateTimeUtil dateTimeUtil,
        IMongoDatabase mongoDatabase,
        ILogger<NoSqlDataService> logger,
        FiltroElegibilidadeOsService? filtroElegibilidade = null)
    {
        _osRepository = osRepository;
        _servicoRepository = servicoRepository;
        _equipeRepository = equipeRepository;
        _perfilRelacaoRepository = perfilRelacaoRepository;
        _perfilEquipeRepository = perfilEquipeRepository;
        _parametroConfigRepository = parametroConfigRepository;
        _tempoMedioExecucaoService = tempoMedioExecucaoService;
        _settings = settings.Value;
        _dateTimeUtil = dateTimeUtil;
        _ordensServicoCollection = mongoDatabase.GetCollection<OrdemServico>(OrdensServicoCollectionName);
        _logger = logger;
        _filtroElegibilidade = filtroElegibilidade;
    }

    /// <summary>
    /// Busca todos os dados necessários no NoSQL e retorna um <see cref="DadosEforce"/> pronto
    /// para a pipeline de otimização.
    /// </summary>
    public async Task<DadosEforce?> ObterDadosAsync(
        IContextRepository context,
        CancellationToken cancellationToken = default)
    {
        var stopwatchTotal = Stopwatch.StartNew();
        var stopwatchEtapa = Stopwatch.StartNew();
        var etapaAtual = "Inicializacao";

        try
        {
            _logger.LogInformation(
                "Buscando dados NoSQL para empresa '{EmpresaId}'. " +
                "RequestId: {RequestId}. SourceRequestId: {SourceRequestId}.",
                context.Empresa,
                context.RequestId,
                context.SourceRequestId);

            var now = _dateTimeUtil.Now()!.Value;
            var empresaId = context.Empresa.ToString();
            var limiteRegistro = now.AddDays(-90);
            var limiteVencimento = now.AddDays(5);
            var limiteVencimentoTipoX = now.AddDays(-3);

            // 0. Carregar pesos de otimização configuráveis (Eforce:ValoresPesoOtimizacao)
            etapaAtual = "CargaParametrosPesos";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Carregando parâmetros de peso da otimização. EmpresaId: {EmpresaId}. Chave: {Chave}.",
                empresaId,
                ChaveValoresPesoOtimizacao);

            var parametroValoresPeso = await _parametroConfigRepository.ObterPorChaveAsync(ChaveValoresPesoOtimizacao);
            if (parametroValoresPeso?.Valores == null || !parametroValoresPeso.Valores.Any())
                throw new InvalidOperationException(
                    $"Parâmetro '{ChaveValoresPesoOtimizacao}' não encontrado ou sem valores. Processo abortado.");

            ConversaoConstantes.CarregarDeParametro(parametroValoresPeso.Valores!);
            ConversaoConstantes.Validar();

            _logger.LogInformation(
                "Valores de peso de otimização carregados do parâmetro '{Chave}'. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                ChaveValoresPesoOtimizacao,
                stopwatchEtapa.ElapsedMilliseconds);

            // 0.1 Carregar parâmetros de elegibilidade (Eforce:ElegibilidadeSettings)
            etapaAtual = "CargaParametrosElegibilidade";
            stopwatchEtapa.Restart();

            var elegibilidadeCarregada = false;
            if (_filtroElegibilidade is not null)
            {
                _logger.LogInformation(
                    "Carregando parâmetros de elegibilidade. EmpresaId: {EmpresaId}. Chave: {Chave}.",
                    empresaId,
                    ElegibilidadeOsSettings.ChaveElegibilidadeSettings);

                var parametroElegibilidade = await _parametroConfigRepository.ObterPorChaveAsync(ElegibilidadeOsSettings.ChaveElegibilidadeSettings);
                if (parametroElegibilidade?.Valores == null || !parametroElegibilidade.Valores.Any())
                {
                    _logger.LogWarning(
                        "Parâmetro de elegibilidade não encontrado ou sem valores. Chave: {Chave}. " +
                        "Filtros de elegibilidade não serão aplicados. EmpresaId: {EmpresaId}.",
                        ElegibilidadeOsSettings.ChaveElegibilidadeSettings,
                        empresaId);
                }
                else
                {
                    try
                    {
                        ElegibilidadeOsSettings.CarregarDeParametro(parametroElegibilidade.Valores!);
                        ElegibilidadeOsSettings.Validar();

                        var settings = new ElegibilidadeOsSettings();

                        _logger.LogInformation(
                            "Parâmetros de elegibilidade carregados e validados. Chave: {Chave}. " +
                            "IdadeMaximaOsComercialTecnica: {IdadeMaximaOsComercialTecnica} dias. " +
                            "IdadeMaximaLotes: {IdadeMaximaLotes} dias. " +
                            "PrazoMaximoVencimentoOsComercial: {PrazoMaximoVencimentoOsComercial} dias. " +
                            "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                            ElegibilidadeOsSettings.ChaveElegibilidadeSettings,
                            settings.IdadeMaximaOsComercialTecnicaDias,
                            settings.IdadeMaximaLotesDias,
                            settings.PrazoMaximoVencimentoOsComercialDias,
                            stopwatchEtapa.ElapsedMilliseconds);

                        elegibilidadeCarregada = true;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Falha ao carregar parâmetros de elegibilidade. Chave: {Chave}. " +
                            "Filtros de elegibilidade não serão aplicados. EmpresaId: {EmpresaId}. Erro: {Erro}.",
                            ElegibilidadeOsSettings.ChaveElegibilidadeSettings,
                            empresaId,
                            ex.Message);
                    }
                }
            }

            // 1. Ordens de Serviço — via driver nativo (filtro + projeção + cursor em lote)
            etapaAtual = "CargaParametrosSelecao";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Carregando parâmetros de seleção de ordens de serviço. EmpresaId: {EmpresaId}. Chave: {Chave}.",
                empresaId,
                ChaveTipoServicoOtimizacao);

            var parametroTipoServico = await _parametroConfigRepository.ObterPorChaveAsync(ChaveTipoServicoOtimizacao);
            if (parametroTipoServico?.Valores == null || !parametroTipoServico.Valores.Any())
                throw new InvalidOperationException(
                    $"Parâmetro '{ChaveTipoServicoOtimizacao}' não encontrado ou sem valores. Processo abortado.");

            bool ObterBoolParametro(string chave) =>
                parametroTipoServico.Valores.TryGetValue(chave, out var v) &&
                bool.TryParse(v, out var b) && b;

            var permiteComercialRegulada = ObterBoolParametro("PermiteComercialRegulada");
            var permiteComercialNaoRegulada = ObterBoolParametro("PermiteComercialNaoRegulada");
            var permiteTecnica = ObterBoolParametro("PermiteTecnica");
            var permiteLotes = ObterBoolParametro("PermiteLotes");

            if (!permiteComercialRegulada && !permiteComercialNaoRegulada && !permiteTecnica && !permiteLotes)
                throw new InvalidOperationException(
                    $"Parâmetro '{ChaveTipoServicoOtimizacao}': ao menos um tipo de serviço deve ser permitido. Processo abortado.");

            var permiteServicoRural = ObterBoolParametro("PermiteServicoRural");
            var permiteServicoUrbano = ObterBoolParametro("PermiteServicoUrbano");

            if (!permiteServicoRural && !permiteServicoUrbano)
                throw new InvalidOperationException(
                    $"Parâmetro '{ChaveTipoServicoOtimizacao}': ao menos um de 'PermiteServicoRural' ou 'PermiteServicoUrbano' deve ser verdadeiro. Processo abortado.");

            _logger.LogInformation(
                "Tipos de serviço permitidos — ComercialRegulada: {ComercialRegulada}, " +
                "ComercialNaoRegulada: {ComercialNaoRegulada}, Tecnica: {Tecnica}, Lotes: {Lotes}, " +
                "ServicoRural: {ServicoRural}, ServicoUrbano: {ServicoUrbano}.",
                permiteComercialRegulada,
                permiteComercialNaoRegulada,
                permiteTecnica,
                permiteLotes,
                permiteServicoRural,
                permiteServicoUrbano);

            var parametroExclusao = await _parametroConfigRepository.ObterPorChaveAsync(ChaveServicosExclusao);
            var servicosExclusao = parametroExclusao?.Valores?.Values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .SelectMany(v => v!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray() ?? [];

            if (servicosExclusao.Length > 0)
                _logger.LogInformation(
                    "Serviços de exclusão carregados do parâmetro: {Servicos}",
                    string.Join(", ", servicosExclusao));
            else
                _logger.LogWarning(
                    "Nenhum serviço de exclusão configurado no parâmetro '{Chave}'.",
                    ChaveServicosExclusao);

            // Carregar a regulação do cadastro antes de selecionar as OS no Mongo.
            // A mesma fotografia será usada no filtro e no mapeamento para o GMPRO.
            etapaAtual = "ConsultaServicosRegulados";
            stopwatchEtapa.Restart();
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Buscando serviços regulados para a seleção de OS. EmpresaId: {EmpresaId}.",
                empresaId);

            var filtroServicosRegulados = new DataFilter<Servico>(
                new ConditionHolder<Servico>(s =>
                    s.Empresa != null &&
                    s.Empresa.IdExterno == empresaId &&
                    s.Regulado == true));

            var servicosRegulados = await _servicoRepository.GetAsync(filtroServicosRegulados);
            cancellationToken.ThrowIfCancellationRequested();

            var codigosServicosRegulados = servicosRegulados
                .Where(s => s.IdExterno != null)
                .Select(s => s.IdExterno!)
                .Distinct()
                .ToArray();

            var codigosServicosReguladosSet = codigosServicosRegulados.ToHashSet(StringComparer.Ordinal);

            _logger.LogInformation(
                "Serviços regulados carregados. EmpresaId: {EmpresaId}. " +
                "ServicosRegulados: {ServicosRegulados}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                empresaId,
                codigosServicosRegulados.Length,
                stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "MontagemFiltroOrdensServico";
            stopwatchEtapa.Restart();

            var fb = Builders<OrdemServico>.Filter;

            var filters = new List<FilterDefinition<OrdemServico>>
            {
                fb.Ne(os => os.Empresa, null),
                fb.Eq(os => os.Empresa!.IdExterno, empresaId),
                fb.Eq(os => os.RegistroAtivo, true),
                fb.Ne(os => os.Situacao, null),
                fb.Ne(os => os.ObjetoMapa, null),
                fb.Ne(os => os.ObjetoMapa!.Latitude, null),
                fb.Ne(os => os.ObjetoMapa!.Longitude, null),
                fb.Or(
                    fb.Eq(os => os.Bloqueio, null),
                    fb.Eq(os => os.Bloqueio!.RegistroAtivo, false)
                ),
                fb.Gte(os => os.DataRegistro, limiteRegistro),
                fb.Ne(os => os.Servico, null),
                fb.Or(
                    fb.And(
                        fb.Eq(os => os.Servico!.TipoServico, "X"),
                        fb.Lte(os => os.DataVencimento, limiteVencimentoTipoX)),
                    fb.And(
                        fb.Ne(os => os.Servico!.TipoServico, "X"),
                        fb.Lte(os => os.DataVencimento, limiteVencimento))
                ),
                fb.Lt(os => os.Situacao!.Semantica, OrdemServicoSituacaoSemantica.Despachado),
                // Filtro por tipos de serviço permitidos (Eforce:TipoServicoOtimizacao)
                fb.Or(new[]
                {
                    permiteComercialRegulada
                        ? fb.And(
                            fb.Eq(os => os.Servico!.TipoServico, "C"),
                            fb.Or(
                                fb.Eq(os => os.Regulado, true),
                                fb.In(os => os.Servico!.IdExterno, codigosServicosRegulados)))
                        : null,
                    permiteComercialNaoRegulada
                        ? fb.And(
                            fb.Eq(os => os.Servico!.TipoServico, "C"),
                            fb.In(os => os.Regulado, new bool?[] { false, null }),
                            fb.Nin(os => os.Servico!.IdExterno, codigosServicosRegulados))
                        : null,
                    permiteTecnica ? fb.Eq(os => os.Servico!.TipoServico, "T") : null,
                    permiteLotes ? fb.Eq(os => os.Servico!.TipoServico, "X") : null,
                }.Where(f => f is not null).Select(f => f!)),
                // Filtro por zona de localização (Eforce:TipoServicoOtimizacao — PermiteServicoRural / PermiteServicoUrbano)
                // Se ambos forem true, nenhum filtro é aplicado (todas as zonas aceitas).
                // Se ambos forem false, configuração inválida — validação feita antes da montagem do filtro.
                permiteServicoRural && permiteServicoUrbano
                    ? fb.Empty
                    : permiteServicoRural
                        ? fb.Eq(os => os.Localizacao!.Zona, Zona.Rural)
                        : fb.Ne(os => os.Localizacao!.Zona, Zona.Rural),
                fb.In(os => os.Reincidencia, new bool?[] { false, null }),
                fb.Eq(os => os.Despachavel, true),
                fb.Or(
                    fb.Eq(os => os.Agrupado, false),
                    fb.And(fb.Eq(os => os.Agrupado, true), fb.Ne(os => os.Situacao.Tag, 9))
                ),
                fb.Nin(os => os.Servico!.IdExterno, servicosExclusao),
                // "sem filhos" empurrado pro servidor — não baixa mais as OS-pai:
                fb.Or(fb.Eq(os => os.Children, null), fb.Size(os => os.Children, 0))
            };

            var filter = fb.And(filters);

            var find = _ordensServicoCollection.Find(filter, new FindOptions { BatchSize = LoteLeitura });

            var projection = Builders<OrdemServico>.Projection
                .Include(os => os.Id)
                .Include(os => os.IdExterno)
                .Include(os => os.Empresa)
                .Include(os => os.Servico)
                .Include(os => os.ObjetoMapa)
                .Include(os => os.DataRegistro)
                .Include(os => os.DataVencimento)
                .Include(os => os.DataVencimentoUtc)
                .Include(os => os.Caracteristicas)
                .Include(os => os.Regiao)
                .Include(os => os.Regulado)
                .Include(os => os.Reincidencia)
                .Include(os => os.Abrangencia)
                .Include(os => os.Indenizacao)
                .Include(os => os.Indenizacoes)
                .Include(os => os.Prioridade)
                .Include(os => os.TempoMedioExecucao)
                .Include(os => os.NumeroOs)
                .Include(os => os.Localizacao);

            find = find.Project<OrdemServico>(projection);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var reg = BsonSerializer.SerializerRegistry;
                var ser = reg.GetSerializer<OrdemServico>();
                var renderArgs = new RenderArgs<OrdemServico>(ser, reg);
                var renderedFilter = filter.Render(renderArgs).ToJson();
                var renderedProjection = projection.Render(renderArgs).ToJson();

                _logger.LogDebug(
                    "MongoDB query: db.{Collection}.find({Filter}, {Projection})",
                    OrdensServicoCollectionName,
                    renderedFilter,
                    renderedProjection);
            }

            etapaAtual = "ConsultaOrdensServico";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Buscando ordens de serviço elegíveis. EmpresaId: {EmpresaId}. " +
                "DataReferencia: {DataReferencia}. LimiteRegistro: {LimiteRegistro}. " +
                "LimiteVencimento: {LimiteVencimento}. LimiteVencimentoTipoX: {LimiteVencimentoTipoX}.",
                empresaId,
                now,
                limiteRegistro,
                limiteVencimento,
                limiteVencimentoTipoX);

            var ordensServicoList = new List<OrdemServico>();
            using (var cursor = await find.ToCursorAsync(cancellationToken))
            {
                while (await cursor.MoveNextAsync(cancellationToken))
                    ordensServicoList.AddRange(cursor.Current);
            }

            _logger.LogInformation(
                "Ordens de serviço encontradas após os filtros: {Count}. " +
                "EmpresaId: {EmpresaId}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                ordensServicoList.Count,
                empresaId,
                stopwatchEtapa.ElapsedMilliseconds);

            if (ordensServicoList.Count == 0)
            {
                _logger.LogWarning(
                    "Nenhuma ordem de serviço elegível encontrada para a empresa '{EmpresaId}'. " +
                    "Motivo: {Motivo}. Carga encerrada antes da construção da request e do envio ao GMPRO. " +
                    "DuracaoTotalMs: {DuracaoTotalMs}.",
                    context.Empresa,
                    "SemOrdensElegiveis",
                    stopwatchTotal.ElapsedMilliseconds);
                return null;
            }

            // Buscar TME por código de serviço (com soma do filho quando houver)
            etapaAtual = "ConsultaTemposMedios";
            stopwatchEtapa.Restart();

            var codservsTme = ordensServicoList
                .Select(os => os.Servico?.IdExternoNumerico)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Buscando tempos médios de execução. EmpresaId: {EmpresaId}. " +
                "CodigosServico: {CodigosServico}.",
                empresaId,
                codservsTme.Count);

            var tmePorCodserv = new Dictionary<int, TimeSpan?>();
            foreach (var codserv in codservsTme)
            {
                var tme = await _tempoMedioExecucaoService.GetByCodigoServicoAsync(codserv);
                if (tme is null) continue;

                var tmeTotal = tme.TempoMedioDireto;

                if (tme.CodigoServicoFilho.HasValue)
                {
                    var tmeFilho = await _tempoMedioExecucaoService.GetByCodigoServicoAsync(tme.CodigoServicoFilho.Value);
                    if (tmeFilho?.TempoMedioDireto.HasValue == true)
                        tmeTotal = (tmeTotal ?? TimeSpan.Zero) + tmeFilho.TempoMedioDireto.Value;
                }

                tmePorCodserv[codserv] = tmeTotal;
            }

            _logger.LogInformation(
                "Consulta de tempos médios concluída. CodigosConsultados: {CodigosConsultados}. " +
                "CadastrosEncontrados: {CadastrosEncontrados}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                codservsTme.Count,
                tmePorCodserv.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "CargaParametrosPenalidades";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Carregando parâmetros de penalidade por hora e compensação adicional. EmpresaId: {EmpresaId}.",
                empresaId);

            var parametroPenalidadePorHora = await _parametroConfigRepository.ObterPorChaveAsync("Eforce:PenalidadePorHora");
            double penalidadePorHora = 0;
            if (parametroPenalidadePorHora?.Valores != null && parametroPenalidadePorHora.Valores.Any())
            {
                var valorPenalidade = parametroPenalidadePorHora.Valores.Values.FirstOrDefault();
                if (double.TryParse(
                    valorPenalidade,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedPenalidade))
                    penalidadePorHora = parsedPenalidade;
            }

            var parametroCompensacaoAdicional = await _parametroConfigRepository.ObterPorChaveAsync("Eforce:CompensacaoAdicionalVencidas");
            double compensacaoAdicionalVencidas = 0;
            if (parametroCompensacaoAdicional?.Valores != null && parametroCompensacaoAdicional.Valores.Any())
            {
                var valorCompensacao = parametroCompensacaoAdicional.Valores.Values.FirstOrDefault();
                if (double.TryParse(
                    valorCompensacao,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedCompensacao))
                    compensacaoAdicionalVencidas = parsedCompensacao;
            }

            etapaAtual = "MapeamentoOrdensServico";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Convertendo OS selecionadas para o modelo local. Quantidade: {Quantidade}.",
                ordensServicoList.Count);

            var ordensServicoLocais = ordensServicoList
                .Select(os =>
                {
                    var servicoIdExterno = os.Servico?.IdExterno ?? string.Empty;
                    // Preservar a marcação da OS e acrescentar a regulação do cadastro.
                    // Passar o resultado efetivo evita que um override falso rebaixe uma OS regulada.
                    var reguladoEfetivo = os.Regulado == true
                        || codigosServicosReguladosSet.Contains(servicoIdExterno);
                    var codserv = os.Servico?.IdExternoNumerico;
                    var tmeOverride = codserv.HasValue && tmePorCodserv.TryGetValue(codserv.Value, out var tme)
                        ? tme
                        : (TimeSpan?)null;

                    return SourceMapper.ToLocal(
                        os, reguladoEfetivo, tmeOverride, penalidadePorHora, compensacaoAdicionalVencidas);
                })
                .ToList();

            _logger.LogInformation(
                "Mapeamento de OS concluído. Quantidade: {Quantidade}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                ordensServicoLocais.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            // 2. Buscar Equipes ativas
            etapaAtual = "ConsultaEquipes";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Buscando equipes elegíveis. EmpresaId: {EmpresaId}. DataReferencia: {DataReferencia}. " +
                "Critérios: situação ativa, coordenadas de partida, escala disponibilizada, " +
                "início da escala maior ou igual à data de referência e referência de perfil preenchida.",
                empresaId,
                now);

            var equipesGod = await _equipeRepository.GetEquipesAsync(
                e => e.Empresa != null
                     && e.Situacao == 1
                     && e.Empresa.IdExterno == context.Empresa.ToString()
                     && e.PontoPartida != null
                     && e.PontoPartida.Latitude != null
                     && e.PontoPartida.Longitude != null
                     && e.Escala != null
                     && e.Escala.Situacao == SituacaoEscalaEquipe.Disponibilizada
                     && e.Perfil != null
                     && e.Escala.DataInicio >= now
                     && e.Perfil.Id != null);

            var equipesRetornadasConsulta = equipesGod?
                .Cast<Equipe>()
                .ToList() ?? [];

            var equipesSemPeriodoEscala = equipesRetornadasConsulta.Count(e =>
                !e.Escala!.DataInicioUtc.HasValue || !e.Escala!.DataFinalUtc.HasValue);

            var equipesEscalaAcimaLimite = equipesRetornadasConsulta.Count(e =>
            {
                var inicio = e.Escala!.DataInicioUtc;
                var fim = e.Escala!.DataFinalUtc;
                return inicio.HasValue && fim.HasValue && (fim.Value - inicio.Value).TotalHours > 10;
            });

            var equipesGodList = equipesRetornadasConsulta
                .Where(e =>
                {
                    var inicio = e.Escala!.DataInicioUtc;
                    var fim = e.Escala!.DataFinalUtc;
                    return inicio.HasValue && fim.HasValue && (fim.Value - inicio.Value).TotalHours <= 10;
                })
                .ToList();

            _logger.LogInformation(
                "Seleção de equipes concluída. EmpresaId: {EmpresaId}. " +
                "RetornadasPelaConsulta: {RetornadasPelaConsulta}. " +
                "SemPeriodoEscala: {SemPeriodoEscala}. EscalaAcimaDeDezHoras: {EscalaAcimaDeDezHoras}. " +
                "EquipesAposFiltroEscala: {EquipesAposFiltroEscala}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                empresaId,
                equipesRetornadasConsulta.Count,
                equipesSemPeriodoEscala,
                equipesEscalaAcimaLimite,
                equipesGodList.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            if (equipesSemPeriodoEscala > 0 || equipesEscalaAcimaLimite > 0)
            {
                _logger.LogWarning(
                    "Equipes removidas pelo filtro de escala. EmpresaId: {EmpresaId}. " +
                    "SemInicioOuFimUtc: {SemInicioOuFimUtc}. " +
                    "DuracaoAcimaDeDezHoras: {DuracaoAcimaDeDezHoras}.",
                    empresaId,
                    equipesSemPeriodoEscala,
                    equipesEscalaAcimaLimite);
            }

            etapaAtual = "CargaParametrosEquipes";
            stopwatchEtapa.Restart();

            var parametroCarga = await _parametroConfigRepository.ObterPorChaveAsync("Eforce:PercentualAlocacaoEquipe");
            if (parametroCarga?.Valores != null && parametroCarga.Valores.Any())
                _logger.LogWarning(
                    "Parâmetro 'Eforce:PercentualAlocacaoEquipe' presente, mas ignorado: " +
                    "a carga de trabalho agora vem de PerfilEquipe.PercentualAlocacaoEquipe.");

            var parametroChecklist = await _parametroConfigRepository.ObterPorChaveAsync(ChaveTempoChecklistVeiculo);
            if (parametroChecklist?.Valores != null && parametroChecklist.Valores.Any())
                _logger.LogWarning(
                    "Parâmetro '{Chave}' presente, mas ignorado: " +
                    "o tempo de checklist agora vem de PerfilEquipe.TempoAdicionalChecklist.",
                    ChaveTempoChecklistVeiculo);

            etapaAtual = "ConsultaCadastrosPerfilEquipe";
            stopwatchEtapa.Restart();

            var perfilIdsPorEquipe = equipesGodList
                .Where(e => e.Perfil?.Id is not null)
                .Select(e => e.Perfil!.Id!)
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Buscando cadastros de perfil das equipes. EmpresaId: {EmpresaId}. " +
                "PerfisDistintos: {PerfisDistintos}.",
                empresaId,
                perfilIdsPorEquipe.Count);

            var perfisEquipeDict = new Dictionary<string, PerfilEquipe>();
            foreach (var perfilId in perfilIdsPorEquipe)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var perfilEquipe = await _perfilEquipeRepository.GetByIdAsync(perfilId);
                if (perfilEquipe is not null)
                    perfisEquipeDict[perfilId] = perfilEquipe;
            }
            cancellationToken.ThrowIfCancellationRequested();

            var perfisNaoEncontrados = perfilIdsPorEquipe.Count - perfisEquipeDict.Count;

            _logger.LogInformation(
                "Consulta dos cadastros de perfil concluída. EmpresaId: {EmpresaId}. " +
                "PerfisConsultados: {PerfisConsultados}. PerfisEncontrados: {PerfisEncontrados}. " +
                "PerfisNaoEncontrados: {PerfisNaoEncontrados}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                empresaId,
                perfilIdsPorEquipe.Count,
                perfisEquipeDict.Count,
                perfisNaoEncontrados,
                stopwatchEtapa.ElapsedMilliseconds);

            if (perfisNaoEncontrados > 0)
                _logger.LogWarning(
                    "Há referências de perfil sem cadastro encontrado. EmpresaId: {EmpresaId}. " +
                    "PerfisNaoEncontrados: {PerfisNaoEncontrados}. " +
                    "As equipes serão mantidas e mapeadas com perfil nulo, conforme o comportamento anterior.",
                    empresaId,
                    perfisNaoEncontrados);

            etapaAtual = "MapeamentoEquipes";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Convertendo equipes selecionadas para o modelo local com os cadastros de perfil. " +
                "Quantidade: {Quantidade}.",
                equipesGodList.Count);

            var equipesLocais = equipesGodList
                .Select(e =>
                {
                    perfisEquipeDict.TryGetValue(e.Perfil?.Id ?? string.Empty, out var perfilEquipe);
                    return SourceMapper.ToLocal(e, perfilEquipe);
                })
                .ToList();

            _logger.LogInformation(
                "Equipes encontradas: {Count}. EmpresaId: {EmpresaId}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                equipesLocais.Count,
                empresaId,
                stopwatchEtapa.ElapsedMilliseconds);

            if (equipesLocais.Count == 0)
            {
                _logger.LogWarning(
                    "Nenhuma equipe elegível encontrada para a empresa '{EmpresaId}'. " +
                    "Motivo: {Motivo}. EquipesRetornadasPelaConsulta: {EquipesRetornadasPelaConsulta}. " +
                    "Carga encerrada antes da construção da request e do envio ao GMPRO.",
                    context.Empresa,
                    "SemEquipesElegiveis",
                    equipesRetornadasConsulta.Count);

                // Consulta exclusivamente diagnóstica: mantém os demais critérios da seleção,
                // mas procura referências de perfil ausentes. Não altera as equipes selecionadas.
                etapaAtual = "DiagnosticoEquipesSemPerfil";

                try
                {
                    _logger.LogInformation(
                        "Iniciando consulta diagnóstica de equipes sem referência de perfil. EmpresaId: {EmpresaId}.",
                        empresaId);

                    var equipesSemPerfil = await _equipeRepository.GetEquipesAsync(
                        e => e.Empresa != null
                             && e.Situacao == 1
                             && e.Empresa.IdExterno == context.Empresa.ToString()
                             && e.PontoPartida != null
                             && e.PontoPartida.Latitude != null
                             && e.PontoPartida.Longitude != null
                             && e.Escala != null
                             && e.Escala.Situacao == SituacaoEscalaEquipe.Disponibilizada
                             && e.Escala.DataInicio >= now
                             && (e.Perfil == null || e.Perfil.Id == null));

                    var equipesSemPerfilList = equipesSemPerfil?
                        .Cast<Equipe>()
                        .ToList() ?? [];

                    var semPerfilComEscalaElegivel = equipesSemPerfilList.Count(e =>
                    {
                        var inicio = e.Escala!.DataInicioUtc;
                        var fim = e.Escala!.DataFinalUtc;
                        return inicio.HasValue && fim.HasValue && (fim.Value - inicio.Value).TotalHours <= 10;
                    });

                    if (semPerfilComEscalaElegivel > 0)
                    {
                        _logger.LogWarning(
                            "Consulta diagnóstica identificou equipes que atendem aos demais critérios de seleção, " +
                            "mas não possuem referência de perfil. EmpresaId: {EmpresaId}. " +
                            "Motivo: {Motivo}. EquipesSemReferenciaPerfil: {EquipesSemReferenciaPerfil}. " +
                            "Nenhuma dessas equipes foi incluída na otimização.",
                            empresaId,
                            "EquipesSemReferenciaPerfil",
                            semPerfilComEscalaElegivel);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Consulta diagnóstica não encontrou equipes sem referência de perfil " +
                            "que atendam aos demais critérios de seleção e escala. EmpresaId: {EmpresaId}. " +
                            "A ausência de equipes elegíveis não foi explicada exclusivamente pela falta de referência de perfil.",
                            empresaId);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Uma falha no diagnóstico não transforma a ausência de equipes em falha técnica da seleção.
                    _logger.LogWarning(
                        ex,
                        "Não foi possível concluir o diagnóstico de equipes sem referência de perfil. " +
                        "EmpresaId: {EmpresaId}. O encerramento por ausência de equipes elegíveis será mantido.",
                        empresaId);
                }

                _logger.LogWarning(
                    "Carga NoSQL encerrada sem equipes elegíveis. EmpresaId: {EmpresaId}. " +
                    "Motivo: {Motivo}. DuracaoTotalMs: {DuracaoTotalMs}.",
                    empresaId,
                    "SemEquipesElegiveis",
                    stopwatchTotal.ElapsedMilliseconds);
                return null;
            }

            // 3. Buscar relações Perfil
            etapaAtual = "ConsultaRelacoesPerfil";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Buscando relações entre perfis de equipe e serviços. EmpresaId: {EmpresaId}. " +
                "EquipesSelecionadas: {EquipesSelecionadas}.",
                empresaId,
                equipesGodList.Count);

            var relacoes = await _perfilRelacaoRepository.GetByEmpresaAsync(context.Empresa.ToString());

            var perfilNomes = equipesGodList
                .Where(e => e.Perfil?.Id is not null)
                .Select(e => e.Perfil!.Id!)
                .Distinct()
                .ToDictionary(id => id, id => id);

            var perfisLocais = SourceMapper.ToLocalPerfis(relacoes, perfilNomes);

            _logger.LogInformation(
                "Perfis de equipe encontrados: {Count}. " +
                "PerfisDistintosReferenciadosPelasEquipes: {PerfisDistintosReferenciadosPelasEquipes}. " +
                "A contagem encontrada representa relações perfil-serviço utilizadas na conversão. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                perfisLocais.Count,
                perfilNomes.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            if (perfisLocais.Count == 0)
            {
                _logger.LogWarning(
                    "Nenhuma relação perfil-serviço foi produzida para as equipes selecionadas. " +
                    "EmpresaId: {EmpresaId}. Motivo: {Motivo}. " +
                    "EquipesSelecionadas: {EquipesSelecionadas}. " +
                    "PerfisReferenciados: {PerfisReferenciados}. " +
                    "A carga continuará conforme o comportamento existente, sem relações perfil-serviço.",
                    empresaId,
                    "SemRelacoesPerfilServico",
                    equipesGodList.Count,
                    perfilNomes.Count);
            }

            // 4. Extrair Regiões a partir das equipes
            etapaAtual = "ExtracaoRegioes";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Extraindo regiões das equipes selecionadas. EmpresaId: {EmpresaId}.",
                empresaId);

            var regioesLocais = equipesGodList
                .SelectMany(SourceMapper.ToLocalRegioes)
                .ToList();

            _logger.LogInformation(
                "Regiões encontradas: {Count}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                regioesLocais.Count,
                stopwatchEtapa.ElapsedMilliseconds);

            if (regioesLocais.Count == 0)
            {
                _logger.LogWarning(
                    "Nenhuma região foi extraída das equipes selecionadas. EmpresaId: {EmpresaId}. " +
                    "Motivo: {Motivo}. EquipesSelecionadas: {EquipesSelecionadas}. " +
                    "A carga continuará para a etapa de conversão, conforme o comportamento existente.",
                    empresaId,
                    "SemRegioesEquipe",
                    equipesGodList.Count);
            }

            // Aplicar filtros de elegibilidade se o serviço estiver disponível
            etapaAtual = "FiltrosElegibilidade";
            stopwatchEtapa.Restart();

            if (_filtroElegibilidade is not null && elegibilidadeCarregada)
            {
                var osAntesFiltragem = ordensServicoLocais.Count;
                var nowDateTime = now.DateTime;

                _logger.LogInformation(
                    "Iniciando aplicação de filtros de elegibilidade. EmpresaId: {EmpresaId}. " +
                    "OsAntes: {OsAntes}. DataReferencia: {DataReferencia}.",
                    empresaId,
                    osAntesFiltragem,
                    now);

                // Aplicar filtro de idade para OSs Comerciais e Técnicas
                ordensServicoLocais = _filtroElegibilidade.FiltrarPorIdadeOsComercialTecnica(
                    ordensServicoLocais, nowDateTime);

                // Aplicar filtro de vencimento para OSs Comerciais
                ordensServicoLocais = _filtroElegibilidade.FiltrarPorVencimentoOsComercial(
                    ordensServicoLocais, nowDateTime);

                // Aplicar filtro de idade para Lotes
                ordensServicoLocais = _filtroElegibilidade.FiltrarPorIdadeLotes(
                    ordensServicoLocais, nowDateTime);

                var osAposFiltragem = ordensServicoLocais.Count;
                var osRemovidas = osAntesFiltragem - osAposFiltragem;

                if (osRemovidas > 0)
                {
                    _logger.LogInformation(
                        "Filtros de elegibilidade aplicados. EmpresaId: {EmpresaId}. " +
                        "OsAntes: {OsAntes}. OsApos: {OsApos}. OsRemovidas: {OsRemovidas}. " +
                        "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                        empresaId,
                        osAntesFiltragem,
                        osAposFiltragem,
                        osRemovidas,
                        stopwatchEtapa.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "Filtros de elegibilidade aplicados sem remoções. EmpresaId: {EmpresaId}. " +
                        "OS: {Os}. DuracaoEtapaMs: {DuracaoEtapaMs}.",
                        empresaId,
                        osAposFiltragem,
                        stopwatchEtapa.ElapsedMilliseconds);
                }

                // Validar se ainda há OSs após filtragem
                if (ordensServicoLocais.Count == 0)
                {
                    _logger.LogWarning(
                        "Nenhuma ordem de serviço elegível após aplicação de filtros. " +
                        "EmpresaId: {EmpresaId}. Motivo: {Motivo}. OsAntes: {OsAntes}. " +
                        "Carga encerrada antes da construção da request e do envio ao GMPRO. " +
                        "DuracaoTotalMs: {DuracaoTotalMs}.",
                        empresaId,
                        "SemOrdensAposFiltragem",
                        osAntesFiltragem,
                        stopwatchTotal.ElapsedMilliseconds);
                    return null;
                }
            }
            else
            {
                _logger.LogWarning(
                    "FiltroElegibilidadeOsService não está disponível. " +
                    "Filtros de elegibilidade não serão aplicados. EmpresaId: {EmpresaId}. " +
                    "OS: {Os}.",
                    empresaId,
                    ordensServicoLocais.Count);
            }

            _logger.LogInformation(
                "Carga NoSQL concluída. EmpresaId: {EmpresaId}. " +
                "OS: {Os}. Equipes: {Equipes}. RelacoesPerfilServico: {RelacoesPerfilServico}. " +
                "Regioes: {Regioes}. DuracaoTotalMs: {DuracaoTotalMs}.",
                empresaId,
                ordensServicoLocais.Count,
                equipesLocais.Count,
                perfisLocais.Count,
                regioesLocais.Count,
                stopwatchTotal.ElapsedMilliseconds);

            return new DadosEforce
            {
                OrdensServico = ordensServicoLocais,
                Equipes = equipesLocais,
                PerfisEquipe = perfisLocais,
                Regioes = regioesLocais
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Carga NoSQL cancelada. EmpresaId: {EmpresaId}. Etapa: {Etapa}. " +
                "RequestId: {RequestId}. DuracaoTotalMs: {DuracaoTotalMs}.",
                context.Empresa,
                etapaAtual,
                context.RequestId,
                stopwatchTotal.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha na carga NoSQL da otimização. EmpresaId: {EmpresaId}. Etapa: {Etapa}. " +
                "RequestId: {RequestId}. DuracaoTotalMs: {DuracaoTotalMs}.",
                context.Empresa,
                etapaAtual,
                context.RequestId,
                stopwatchTotal.ElapsedMilliseconds);
            throw;
        }
    }
}