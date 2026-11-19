using Energisa.Context.Interface;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Mappers;
using Energisa.Eforce.Otimizador.Models.Enums;
using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model._OrdemServico_.Enum;
using Energisa.God.Domain.Model._OrdemServico_.Interface;
using Energisa.God.Domain.Model.Alocacao.Enum;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Model.Geral;
using Energisa.God.Domain.Model.Modelo;
using Energisa.God.Domain.Repository._OrdemServico_.Interface;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Energisa.God.Domain.Repository.Config;
using Energisa.God.Domain.Repository.Modelo.Interface;
using Energisa.God.Domain.Service.Alocacao.Interface;
using Google.Maps.RouteOptimization.V1;
using Google.Protobuf;
using Google.Type;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DateTime = System.DateTime;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço responsável por montar o <see cref="ContextoReotimizacao"/> (snapshot operacional
/// completo, §5) necessário para a reotimização intradiária, a partir do
/// <see cref="SolicitarReotimizacaoCommand"/> recebido pela API.
/// Consulta o cenário original (referências GCS §17), a situação atual de cada OS na coleção
/// <c>ordem_servico</c> (situação semântica + Id, §7) e a posição/turno atual de cada equipe
/// selecionada via <see cref="IEquipeRepository"/> (propriedade <c>ObjetoMapa</c>, §6).
/// </summary>
public class ReotimizacaoDataService
{
    private readonly IOtimizadorRotasRepository _otimizadorRotasRepository;
    private readonly IOrdemServicoRepository _osRepository;
    private readonly IEquipeRepository _equipeRepository;
    private readonly IPerfilEquipeRepository _perfilEquipeRepository;
    private readonly IParametroConfigRepository _parametroConfigRepository;
    private readonly ITempoMedioExecucaoService _tempoMedioExecucaoService;
    private readonly GoogleCloudStorageService _googleCloudStorageService;
    private readonly EforceParaGmproOrchestrator _orchestrator;
    private readonly GmProBuilderService _builderService;
    private readonly ClassificadorOrdensReotimizacaoService _classificador;
    private readonly ReotimizacaoSettings _reotimizacaoSettings;
    private readonly IContextRepository _context;
    private readonly ILogger<ReotimizacaoDataService> _logger;

    private const string ChaveValoresPesoOtimizacao = "Eforce:ValoresPesoOtimizacao";

    public ReotimizacaoDataService(
        IOtimizadorRotasRepository otimizadorRotasRepository,
        IOrdemServicoRepository osRepository,
        IEquipeRepository equipeRepository,
        IPerfilEquipeRepository perfilEquipeRepository,
        IParametroConfigRepository parametroConfigRepository,
        ITempoMedioExecucaoService tempoMedioExecucaoService,
        GoogleCloudStorageService googleCloudStorageService,
        EforceParaGmproOrchestrator orchestrator,
        GmProBuilderService builderService,
        ClassificadorOrdensReotimizacaoService classificador,
        IOptions<ReotimizacaoSettings> reotimizacaoSettings,
        IContextRepository context,
        ILogger<ReotimizacaoDataService> logger)
    {
        _otimizadorRotasRepository = otimizadorRotasRepository;
        _osRepository = osRepository;
        _equipeRepository = equipeRepository;
        _perfilEquipeRepository = perfilEquipeRepository;
        _parametroConfigRepository = parametroConfigRepository;
        _tempoMedioExecucaoService = tempoMedioExecucaoService;
        _googleCloudStorageService = googleCloudStorageService;
        _orchestrator = orchestrator;
        _builderService = builderService;
        _classificador = classificador;
        _reotimizacaoSettings = reotimizacaoSettings.Value;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Monta o snapshot operacional completo (§5) e os shipments das novas OSs pendentes (§12),
    /// prontos para uso pelo <see cref="ReotimizacaoRequestBuilderService"/>.
    /// </summary>
    public async Task<(ContextoReotimizacao Contexto, List<Shipment> NovosShipments)> MontarContextoAsync(
        SolicitarReotimizacaoCommand command,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation(
            "Iniciando montagem do contexto de reotimização. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}. Equipes: {EquipeCount}.",
            _context.Empresa, command.OtimizacaoOriginalId, command.Equipes?.Count ?? 0);

        // 0. Carregar configurações de reotimização configuráveis (Eforce:ReotimizacaoSettings)
        var parametroReotimizacao = await _parametroConfigRepository.ObterPorChaveAsync(ReotimizacaoSettings.ChaveReotimizacaoSettings);
        if (parametroReotimizacao?.Valores == null || !parametroReotimizacao.Valores.Any())
            throw new InvalidOperationException(
                $"Parâmetro '{ReotimizacaoSettings.ChaveReotimizacaoSettings}' não encontrado ou sem valores. Processo abortado.");

        _reotimizacaoSettings.CarregarDeParametro(parametroReotimizacao.Valores!);
        _reotimizacaoSettings.Validar();

        _logger.LogInformation(
            "Configurações de reotimização carregadas. Empresa: {Empresa}. Enabled: {Enabled}. ModoBusca: {ModoBusca}.",
            _context.Empresa, _reotimizacaoSettings.Enabled, _reotimizacaoSettings.ModoBusca);

        if (!_reotimizacaoSettings.Enabled)
            throw new InvalidOperationException("A funcionalidade de reotimização intradiária está desabilitada (Reotimizacao:Enabled).");

        if (string.IsNullOrWhiteSpace(command.OtimizacaoOriginalId))
            throw new InvalidOperationException("OtimizacaoOriginalId é obrigatório para a reotimização.");

        if (command.Equipes is null || command.Equipes.Count == 0)
            throw new InvalidOperationException("Ao menos uma equipe deve ser selecionada para a reotimização.");

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new InvalidOperationException("O header Idempotency-Key é obrigatório para a reotimização (§3, §16).");

        // §3: DataCorteUtc capturada uma única vez pela API no início da solicitação.
        var dataCorteUtc = DateTime.UtcNow;

        // 1. Recuperar o cenário original persistido (§17): request/response brutos via
        // referências exatas e imutáveis no GCS (nunca por convenção de nomes).
        _logger.LogInformation(
            "Recuperando cenário original da otimização. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}.",
            _context.Empresa, command.OtimizacaoOriginalId);

        var resultadoOriginal = await _otimizadorRotasRepository.ObterCenarioCompletoParaReotimizacaoAsync(command.OtimizacaoOriginalId)
            ?? throw new InvalidOperationException(
                $"Otimização original '{command.OtimizacaoOriginalId}' não encontrada.");

        // §4: a otimização original deve pertencer à empresa informada e não pode ser, ela
        // própria, uma reotimização (a base deve ser sempre a primeira otimização do turno).
        if (resultadoOriginal.TipoOtimizacao == TipoOtimizacao.Reotimizacao)
        {
            throw new InvalidOperationException(
                $"Otimização original '{command.OtimizacaoOriginalId}' é, ela própria, uma reotimização. " +
                "A reotimização deve utilizar como base a primeira otimização do turno (§1, §4).");
        }

        if (resultadoOriginal.ReferenciaInput is null || resultadoOriginal.ReferenciaOutput is null)
        {
            throw new InvalidOperationException(
                $"Otimização original '{command.OtimizacaoOriginalId}' não possui as referências GCS de input/output " +
                "necessárias para reotimização (§17). Cenários anteriores a essa funcionalidade não são elegíveis.");
        }

        var requestJson = await _googleCloudStorageService.DownloadJsonPorReferenciaAsync(resultadoOriginal.ReferenciaInput, cancellationToken);
        var responseJson = await _googleCloudStorageService.DownloadJsonPorReferenciaAsync(resultadoOriginal.ReferenciaOutput, cancellationToken);

        var jsonParser = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));
        var requestOriginal = jsonParser.Parse<OptimizeToursRequest>(requestJson);
        var responseOriginal = jsonParser.Parse<OptimizeToursResponse>(responseJson);

        _logger.LogInformation(
            "Cenário original recuperado. Empresa: {Empresa}. Vehicles: {Vehicles}. Shipments: {Shipments}. Routes: {Routes}. DuracaoMs: {DuracaoMs}.",
            _context.Empresa, requestOriginal.Model.Vehicles.Count, requestOriginal.Model.Shipments.Count, 
            responseOriginal.Routes.Count, stopwatch.ElapsedMilliseconds);

        // §4: validar consistência do cenário original antes de prosseguir — request/response
        // disponíveis, Request.Label == Response.RequestLabel, labels de veículos e de shipments
        // únicas, e todas as equipes selecionadas pertencem ao cenário original.
        ValidarCenarioOriginal(requestOriginal, responseOriginal, command.Equipes);

        _logger.LogInformation(
            "Cenário original validado com sucesso. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}.",
            _context.Empresa, command.OtimizacaoOriginalId);

        // 2. Resolver fuso horário da empresa (mesma regra da otimização completa, §5).
        var fusoHorario = _orchestrator.ResolverTimeZonePorEmpresa(_context.Empresa);

        // 3. Validar e indexar as equipes selecionadas (§6), com posição/turno atuais.
        var equipesSelecionadas = await MontarEquipesSelecionadasAsync(
            command.Equipes, requestOriginal, responseOriginal, dataCorteUtc, cancellationToken);

        // §16: reservar atomicamente todas as equipes selecionadas para o turno original
        // (chave única: empresa + equipe + início/fim do turno original em UTC). O turno é
        // derivado das janelas dos próprios veículos selecionados (StartTimeWindows/EndTimeWindows),
        // nunca do GlobalStartTime/GlobalEndTime do modelo (§2). Mesma Idempotency-Key + mesmo
        // payload retorna a execução existente; equipe já reservada (processamento em andamento
        // ou resultado já criado no turno) retorna conflito (409).
        var inicioTurnoOriginalUtc = equipesSelecionadas.Min(e =>
            requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].StartTimeWindows.Count > 0
                ? requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].StartTimeWindows[0].StartTime.ToDateTime()
                : dataCorteUtc);
        var fimTurnoOriginalUtc = equipesSelecionadas.Max(e =>
            requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].EndTimeWindows.Count > 0
                ? requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].EndTimeWindows[0].StartTime.ToDateTime()
                : dataCorteUtc);

        var reservaConcedida = await _otimizadorRotasRepository.ReservarEquipesTurnoAsync(
            _context.Empresa.ToString(),
            equipesSelecionadas.Select(e => e.CodigoEquipe),
            inicioTurnoOriginalUtc,
            fimTurnoOriginalUtc,
            command.IdempotencyKey);

        if (!reservaConcedida)
        {
            throw new InvalidOperationException(
                "Uma ou mais equipes selecionadas já possuem reotimização em andamento ou concluída para este turno, " +
                "ou a Idempotency-Key informada conflita com uma solicitação anterior com payload diferente (§16).");
        }

        // 4. Buscar a situação atual de todas as OSs presentes no request original (§7),
        // via correlação exata pelo Id (NumeroOs), nunca por prefixo/convenção (§4).
        var labelsShipmentsOriginais = requestOriginal.Model.Shipments
            .Select(s => s.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var idsOsOriginais = labelsShipmentsOriginais
            .Select(ExtrairNumeroOsDoLabel)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();

        _logger.LogInformation("Consultando situação atual de {Total} OS(s) do request original...", idsOsOriginais.Length);

        var ordensAtuais = idsOsOriginais.Length > 0
            ? (await _osRepository.GetOrdemServicoByIdAsync(idsOsOriginais)).ToList()
            : [];

        var ordensAtuaisPorId = ordensAtuais
            .Where(os => os.Id is not null)
            .ToDictionary(os => os.Id!);

        // 5. Classificar cada OS do request original conforme sua situação atual (§7),
        // calculando o TME restante das OSs em execução (§8) e associando cada OS
        // em execução/deslocamento à equipe correspondente (para reserva de tempo/fixação).
        var situacoesAtuais = new List<OsSituacaoAtual>();
        var vehicleLabelPorLabelShipment = ConstruirVehicleLabelPorShipment(requestOriginal, responseOriginal);

        foreach (var labelShipment in labelsShipmentsOriginais)
        {
            var numeroOs = ExtrairNumeroOsDoLabel(labelShipment);
            vehicleLabelPorLabelShipment.TryGetValue(labelShipment, out var vehicleLabelAtivo);

            IOrdemServico? osAtual = !string.IsNullOrWhiteSpace(numeroOs) && ordensAtuaisPorId.TryGetValue(numeroOs, out var os)
                ? os
                : null;

            situacoesAtuais.Add(new OsSituacaoAtual
            {
                LabelOs = labelShipment,
                Status = MapearStatusOs(osAtual),
                VehicleLabelAtivo = vehicleLabelAtivo,
                ExistiaNoRequestOriginal = true,
                InicioRealExecucaoUtc = osAtual?.DataInicioExecucaoUtc,
                TmeTotalSegundos = (int)(osAtual?.TempoMedioExecucao?.TotalSeconds ?? 0)
            });
        }

        var ordensClassificadas = _classificador.Classificar(situacoesAtuais, dataCorteUtc);

        _logger.LogInformation(
            "Ordens do request original classificadas. Empresa: {Empresa}. Total: {Total}. DuracaoMs: {DuracaoMs}.",
            _context.Empresa, ordensClassificadas.Count, stopwatch.ElapsedMilliseconds);

        // 6. Calcular o TME restante das OSs em execução ativa (§8) e reservar o tempo
        // correspondente na disponibilidade da equipe (a equipe só fica livre após o término).
        var avisos = new List<string>();
        AplicarTmeRestanteNaDisponibilidade(situacoesAtuais, ordensClassificadas, equipesSelecionadas, dataCorteUtc, avisos);

        if (avisos.Count > 0)
        {
            _logger.LogWarning(
                "Avisos ao aplicar TME restante. Empresa: {Empresa}. Avisos: {AvisoCount}. Detalhes: {Detalhes}.",
                _context.Empresa, avisos.Count, string.Join("; ", avisos));
        }

        _logger.LogInformation(
            "TME restante aplicado na disponibilidade das equipes. Empresa: {Empresa}. Equipes: {EquipeCount}.",
            _context.Empresa, equipesSelecionadas.Count);

        // 7. Buscar novas OSs pendentes (não presentes no request original, §12) e classificá-las.
        var novasOrdensPendentesGmpro = await BuscarNovasOrdensPendentesAsync(
            labelsShipmentsOriginais, dataCorteUtc, cancellationToken);

        foreach (var novaOs in novasOrdensPendentesGmpro)
        {
            var labelNova = novaOs.NumeroOs;
            ordensClassificadas[labelNova] = new EstadoOsClassificada
            {
                LabelOs = labelNova,
                Estado = EstadoOsReotimizacao.NovaPendente
            };
        }

        // 8. Construir os shipments das novas OSs pendentes (§12), com pesos calculados
        // sobre o conjunto completo e atribuição restrita às equipes selecionadas.
        var novosShipments = await ConstruirNovosShipmentsAsync(
            novasOrdensPendentesGmpro, equipesSelecionadas, requestOriginal, dataCorteUtc, cancellationToken);

        _logger.LogInformation(
            "Novos shipments construídos. Empresa: {Empresa}. ShipmentsCount: {ShipmentsCount}. DuracaoMs: {DuracaoMs}.",
            _context.Empresa, novosShipments.Count, stopwatch.ElapsedMilliseconds);

        var contexto = new ContextoReotimizacao
        {
            OtimizacaoOriginalId = command.OtimizacaoOriginalId,
            RequestOriginal = requestOriginal,
            ResponseOriginal = responseOriginal,
            EquipesSelecionadas = equipesSelecionadas,
            OrdensClassificadas = ordensClassificadas,
            NovasOrdensPendentes = novasOrdensPendentesGmpro,
            DataCorteUtc = dataCorteUtc,
            FusoHorario = fusoHorario,
            IdempotencyKey = command.IdempotencyKey,
            HashSnapshot = ComputarHashSnapshot(requestOriginal, responseOriginal, equipesSelecionadas, dataCorteUtc),
            UsouFallbackRefeicao = false
        };

        _logger.LogInformation(
            "Contexto de reotimização montado com sucesso. Empresa: {Empresa}. Equipes: {EquipeCount}. OSs Classificadas: {OsCount}. Novas OSs: {NovasOsCount}. DuracaoMs: {DuracaoMs}.",
            _context.Empresa, equipesSelecionadas.Count, ordensClassificadas.Count, novasOrdensPendentesGmpro.Count, stopwatch.ElapsedMilliseconds);

        stopwatch.Stop();
        return (contexto, novosShipments);
    }

    /// <summary>
    /// Monta a lista de <see cref="EquipeReotimizacao"/> a partir dos códigos de equipe
    /// informados no comando, buscando a posição atual (§6, propriedade <c>ObjetoMapa</c>)
    /// e demais informações de turno diretamente do <see cref="IEquipeRepository"/>.
    /// </summary>
    private async Task<List<EquipeReotimizacao>> MontarEquipesSelecionadasAsync(
        List<string> codigosEquipe,
        OptimizeToursRequest requestOriginal,
        OptimizeToursResponse responseOriginal,
        DateTime dataCorteUtc,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Iniciando montagem das equipes selecionadas. Empresa: {Empresa}. EquipeCount: {EquipeCount}.",
            _context.Empresa, codigosEquipe.Count);
        var indicesVehiclePorLabel = new Dictionary<string, int>();
        var vehicles = requestOriginal.Model.Vehicles;
        for (var i = 0; i < vehicles.Count; i++)
            indicesVehiclePorLabel[vehicles[i].Label] = i;

        var empresaId = _context.Empresa.ToString();

        var equipesGod = await _equipeRepository.GetEquipesAsync(
            e => e.Empresa != null && e.Empresa.IdExterno == empresaId,
            null);

        var equipesGodPorNome = (equipesGod ?? [])
            .Cast<Equipe>()
            .Where(e => !string.IsNullOrWhiteSpace(e.Nome))
            .GroupBy(e => e.Nome!)
            .ToDictionary(g => g.Key, g => g.First());

        var resultado = new List<EquipeReotimizacao>();

        foreach (var codigoEquipe in codigosEquipe)
        {
            // §9: correlação exata pelo Label do vehicle original — o formato usado pelo
            // GmProBuilderService é "{TipoEquipe}-{CodigoEquipe}, PERFIL: {Perfil}".
            var vehicleLabel = vehicles
                .Select(v => v.Label)
                .FirstOrDefault(label => ExtrairCodigoEquipeDoVehicleLabel(label) == codigoEquipe);

            if (vehicleLabel is null || !indicesVehiclePorLabel.TryGetValue(vehicleLabel, out var indiceVehicle))
            {
                throw new InvalidOperationException(
                    $"Equipe '{codigoEquipe}' não encontrada entre os veículos da otimização original. " +
                    "Verifique se o código informado corresponde exatamente a uma equipe do cenário original (§9).");
            }

            if (!equipesGodPorNome.TryGetValue(codigoEquipe, out var equipeAtual))
            {
                throw new InvalidOperationException(
                    $"Equipe '{codigoEquipe}' não encontrada no cadastro atual (NoSQL). Snapshot rejeitado.");
            }

            var vehicleOriginal = vehicles[indiceVehicle];
            var rotaOriginal = responseOriginal.Routes.FirstOrDefault(r => r.VehicleLabel == vehicleLabel);

            var equipeReotimizacao = MontarEquipeReotimizacao(
                codigoEquipe, indiceVehicle, vehicleOriginal, equipeAtual, rotaOriginal, dataCorteUtc);

            resultado.Add(equipeReotimizacao);

            _logger.LogInformation(
                "Equipe montada para reotimização. Empresa: {Empresa}. Equipe: {Equipe}. Apresentada: {Apresentada}. Disponibilidade: {Disponibilidade:u}.",
                _context.Empresa, codigoEquipe, equipeReotimizacao.Apresentada, equipeReotimizacao.DisponibilidadeUtc);
        }

        _logger.LogInformation(
            "Equipes selecionadas montadas com sucesso. Empresa: {Empresa}. EquipeCount: {EquipeCount}.",
            _context.Empresa, resultado.Count);

        return resultado;
    }

    /// <summary>
    /// Constrói o <see cref="EquipeReotimizacao"/> de uma equipe selecionada (§6): posição atual
    /// (via <c>ObjetoMapa</c>, quando disponível e recente o suficiente conforme
    /// <see cref="ReotimizacaoSettings.MaxIdadePosicaoMinutos"/>) ou ponto de partida cadastrado
    /// quando a equipe não está apresentada. Equipe apresentada sem posição válida ou com posição
    /// expirada é rejeitada (nunca cai silenciosamente no ponto de partida); equipe não
    /// apresentada sem ponto de partida válido também é rejeitada (§6).
    /// </summary>
    private EquipeReotimizacao MontarEquipeReotimizacao(
        string codigoEquipe,
        int indiceVehicle,
        Vehicle vehicleOriginal,
        Equipe equipeAtual,
        ShipmentRoute? rotaOriginal,
        DateTime dataCorteUtc)
    {
        var possuiCoordenada = equipeAtual.ObjetoMapa?.Latitude is not null && equipeAtual.ObjetoMapa?.Longitude is not null;

        var idadePosicaoMinutos = equipeAtual.ObjetoMapa?.DataCapturaCoordenada is not null
            ? (dataCorteUtc - equipeAtual.ObjetoMapa.DataCapturaCoordenada.Value.UtcDateTime).TotalMinutes
            : (double?)null;

        // §6: "apresentada" significa que a equipe está com uma sessão/dispositivo ativo no
        // status atual. Na ausência de um indicador explícito de apresentação no cadastro,
        // considera-se apresentada quando há coordenada registrada (com data de captura).
        var apresentada = possuiCoordenada && idadePosicaoMinutos is not null;

        LatLng? posicao;

        if (apresentada)
        {
            var posicaoValida = idadePosicaoMinutos!.Value >= 0
                && idadePosicaoMinutos.Value <= _reotimizacaoSettings.MaxIdadePosicaoMinutos;

            if (!posicaoValida)
            {
                throw new InvalidOperationException(
                    $"Equipe '{codigoEquipe}' está apresentada, mas sua posição está expirada ou inválida " +
                    $"(idade: {idadePosicaoMinutos.Value:F1} min, limite: {_reotimizacaoSettings.MaxIdadePosicaoMinutos} min). " +
                    "A reotimização é rejeitada para esta equipe: não é permitido usar o ponto de partida como fallback silencioso (§6).");
            }

            posicao = new LatLng { Latitude = equipeAtual.ObjetoMapa!.Latitude!.Value, Longitude = equipeAtual.ObjetoMapa.Longitude!.Value };
        }
        else
        {
            var pontoPartidaValido = equipeAtual.PontoPartida?.Latitude is not null && equipeAtual.PontoPartida?.Longitude is not null;

            if (!pontoPartidaValido)
            {
                throw new InvalidOperationException(
                    $"Equipe '{codigoEquipe}' não está apresentada e não possui ponto de partida cadastrado " +
                    "com latitude/longitude válidas. A reotimização é rejeitada para esta equipe (§6).");
            }

            posicao = new LatLng { Latitude = equipeAtual.PontoPartida!.Latitude!.Value, Longitude = equipeAtual.PontoPartida.Longitude!.Value };
        }

        // §2: disponibilidade inicial = agora (data de corte). Pode ser adiada por TME restante
        // de OS em execução (aplicado posteriormente em AplicarTmeRestanteNaDisponibilidade).
        var disponibilidadeUtc = dataCorteUtc;

        var fimTurnoOriginal = vehicleOriginal.EndTimeWindows.Count > 0
            ? vehicleOriginal.EndTimeWindows[0].StartTime.ToDateTime()
            : dataCorteUtc;

        var turnoEncerrado = disponibilidadeUtc >= fimTurnoOriginal;

        var capacidadeOriginal = vehicleOriginal.LoadLimits.TryGetValue(ConversaoConstantes.ChaveCapacidadeVisitas, out var limite)
            ? (int)limite.MaxLoad
            : 0;

        var visitasConcluidasOuEmExecucao = rotaOriginal?.Visits.Count ?? 0;
        var capacidadeRestante = Math.Max(0, capacidadeOriginal - visitasConcluidasOuEmExecucao);

        var situacaoRefeicao = ResolverSituacaoRefeicao(vehicleOriginal, dataCorteUtc, out var disponibilidadeAjustadaRefeicao);
        if (disponibilidadeAjustadaRefeicao is not null && disponibilidadeAjustadaRefeicao.Value > disponibilidadeUtc)
            disponibilidadeUtc = disponibilidadeAjustadaRefeicao.Value;

        return new EquipeReotimizacao
        {
            VehicleLabel = vehicleOriginal.Label,
            IndiceVehicleOriginal = indiceVehicle,
            CodigoEquipe = codigoEquipe,
            Apresentada = apresentada,
            Posicao = posicao,
            DataCoordenadaUtc = equipeAtual.ObjetoMapa?.DataCapturaCoordenada?.UtcDateTime,
            DisponibilidadeUtc = disponibilidadeUtc,
            TurnoEncerrado = turnoEncerrado,
            CapacidadeRestante = capacidadeRestante,
            SituacaoRefeicao = situacaoRefeicao,
            PerfilAtual = equipeAtual.Perfil?.Id,
            RegioesAtuais = equipeAtual.Regioes?
                .Select(r => SourceMapper.FormatCodRegiao(equipeAtual.Empresa?.IdExterno, r.IdExterno))
                .Where(s => s.Length > 0)
                .ToList() ?? [],
            AtuacaoAtual = ResolverAtuacaoAtual(equipeAtual.RealizaServicosRurais, equipeAtual.RealizaServicosUrbanos),
            ColaboradoresAtuais = equipeAtual.Funcionarios?.Count ?? 1
        };
    }

    /// <summary>
    /// Resolve a situação atual da refeição da equipe (§11) com base na regra de almoço (BreakRule)
    /// do vehicle original. Quando a refeição está em andamento, a disponibilidade da equipe é
    /// adiada para o fim da janela de refeição.
    /// </summary>
    private static SituacaoRefeicaoReotimizacao ResolverSituacaoRefeicao(
        Vehicle vehicleOriginal, DateTime dataCorteUtc, out DateTime? disponibilidadeAjustada)
    {
        disponibilidadeAjustada = null;

        if (vehicleOriginal.BreakRule is null || vehicleOriginal.BreakRule.BreakRequests.Count == 0)
            return SituacaoRefeicaoReotimizacao.NaoAplicavel;

        var breakRequest = vehicleOriginal.BreakRule.BreakRequests[0];
        var inicioJanela = breakRequest.EarliestStartTime.ToDateTime();
        var fimJanela = breakRequest.LatestStartTime.ToDateTime();
        var duracaoMinima = breakRequest.MinDuration.ToTimeSpan();
        var fimEstimado = fimJanela + duracaoMinima;

        if (dataCorteUtc < inicioJanela)
            return SituacaoRefeicaoReotimizacao.Futura;

        if (dataCorteUtc >= fimEstimado)
            return SituacaoRefeicaoReotimizacao.Concluida;

        disponibilidadeAjustada = fimEstimado;
        return SituacaoRefeicaoReotimizacao.EmAndamento;
    }

    /// <summary>
    /// Aplica o TME restante (§8) das OSs em execução ativa na disponibilidade da equipe
    /// correspondente, adiando-a para o horário estimado de término.
    /// </summary>
    private void AplicarTmeRestanteNaDisponibilidade(
        List<OsSituacaoAtual> situacoesAtuais,
        Dictionary<string, EstadoOsClassificada> ordensClassificadas,
        List<EquipeReotimizacao> equipesSelecionadas,
        DateTime dataCorteUtc,
        List<string> avisos)
    {
        var equipesPorVehicleLabel = equipesSelecionadas.ToDictionary(e => e.VehicleLabel);

        foreach (var situacao in situacoesAtuais)
        {
            if (!ordensClassificadas.TryGetValue(situacao.LabelOs, out var classificacao)) continue;
            if (classificacao.Estado != EstadoOsReotimizacao.ExecucaoAtiva) continue;
            if (situacao.VehicleLabelAtivo is null) continue;
            if (!equipesPorVehicleLabel.TryGetValue(situacao.VehicleLabelAtivo, out var equipe)) continue;

            var (tmeRestanteSegundos, usouFallback) = _classificador.CalcularTmeRestante(situacao, dataCorteUtc);

            equipe.OsEmExecucaoLabel = situacao.LabelOs;
            equipe.TmeRestanteSegundos = tmeRestanteSegundos;
            equipe.UsouFallbackTme = usouFallback;

            var disponibilidadeAposExecucao = dataCorteUtc.AddSeconds(tmeRestanteSegundos);
            if (disponibilidadeAposExecucao > equipe.DisponibilidadeUtc)
                equipe.DisponibilidadeUtc = disponibilidadeAposExecucao;

            if (usouFallback)
                avisos.Add($"OS '{situacao.LabelOs}' em execução sem horário real de início; TME integral aplicado como fallback (§8).");
        }

        foreach (var situacao in situacoesAtuais)
        {
            if (!ordensClassificadas.TryGetValue(situacao.LabelOs, out var classificacao)) continue;
            if (classificacao.Estado != EstadoOsReotimizacao.Deslocamento) continue;
            if (situacao.VehicleLabelAtivo is null) continue;
            if (!equipesPorVehicleLabel.TryGetValue(situacao.VehicleLabelAtivo, out var equipe)) continue;

            equipe.OsEmDeslocamentoLabel = situacao.LabelOs;
        }
    }

    /// <summary>
    /// Busca as novas OSs pendentes no NoSQL (§12): mesmos filtros de elegibilidade da otimização
    /// completa, excluindo as que já existiam no request original.
    /// </summary>
    private async Task<List<Models.Eforce.OrdemServicoGmpro>> BuscarNovasOrdensPendentesAsync(
        List<string> labelsShipmentsOriginais,
        DateTime dataCorteUtc,
        CancellationToken cancellationToken)
    {
        var idsOriginais = labelsShipmentsOriginais
            .Select(ExtrairNumeroOsDoLabel)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();

        var empresaId = _context.Empresa.ToString();
        var limiteRegistro = dataCorteUtc.AddDays(-90);
        var limiteVencimento = dataCorteUtc.AddDays(5);

        var ordens = await _osRepository.GetOrdensServicoAsync(os =>
            os.Empresa != null
            && os.Empresa.IdExterno == empresaId
            && os.RegistroAtivo == true
            && os.Situacao != null
            && os.ObjetoMapa != null
            && os.ObjetoMapa.Latitude != null
            && os.ObjetoMapa.Longitude != null
            && os.DataRegistro >= limiteRegistro
            && os.DataVencimento <= limiteVencimento
            && os.Situacao.Semantica < OrdemServicoSituacaoSemantica.Despachado
            && os.Despachavel == true);

        var novasOrdens = (ordens ?? [])
            .Where(os => os.Id is not null && !idsOriginais.Contains(os.Id))
            .Select(os => SourceMapper.ToLocal((OrdemServico)os))
            .ToList();

        _logger.LogInformation("Novas OSs pendentes encontradas (não presentes no request original): {Total}.", novasOrdens.Count);

        return novasOrdens;
    }

    /// <summary>
    /// Constrói os shipments das novas OSs pendentes (§12): pesos e atribuição de equipes
    /// calculados exatamente como na otimização completa, restringindo a atribuição às
    /// equipes selecionadas para esta reotimização.
    /// </summary>
    private async Task<List<Shipment>> ConstruirNovosShipmentsAsync(
        List<Models.Eforce.OrdemServicoGmpro> novasOrdens,
        List<EquipeReotimizacao> equipesSelecionadas,
        OptimizeToursRequest requestOriginal,
        DateTime dataCorteUtc,
        CancellationToken cancellationToken)
    {
        if (novasOrdens.Count == 0) return [];

        var parametroValoresPeso = await _parametroConfigRepository.ObterPorChaveAsync(ChaveValoresPesoOtimizacao);
        if (parametroValoresPeso?.Valores == null || !parametroValoresPeso.Valores.Any())
            throw new InvalidOperationException(
                $"Parâmetro '{ChaveValoresPesoOtimizacao}' não encontrado ou sem valores. Reotimização abortada.");
        ConversaoConstantes.CarregarDeParametro(parametroValoresPeso.Valores!);
        ConversaoConstantes.Validar();

        var perfisPorCodigoEquipe = new Dictionary<string, PerfilEquipe?>();
        foreach (var equipe in equipesSelecionadas)
        {
            if (equipe.PerfilAtual is null) continue;
            perfisPorCodigoEquipe[equipe.CodigoEquipe] = await _perfilEquipeRepository.GetByIdAsync(equipe.PerfilAtual);
        }

        var perfis = equipesSelecionadas
            .Where(e => perfisPorCodigoEquipe.TryGetValue(e.CodigoEquipe, out var p) && p is not null)
            .SelectMany(e => Enumerable.Empty<Models.Eforce.PerfilEquipeGmpro>()) // resolvido via TME abaixo, mantido vazio
            .ToList();

        var codservsDasNovasOs = novasOrdens.Select(o => o.Codserv).Distinct().ToList();
        var perfisEquipeGmpro = new List<Models.Eforce.PerfilEquipeGmpro>();
        foreach (var equipe in equipesSelecionadas)
        {
            if (!perfisPorCodigoEquipe.TryGetValue(equipe.CodigoEquipe, out var perfilEquipe) || perfilEquipe is null)
                continue;

            foreach (var codserv in codservsDasNovasOs)
            {
                perfisEquipeGmpro.Add(new Models.Eforce.PerfilEquipeGmpro
                {
                    Codserv = codserv,
                    Perfil = equipe.PerfilAtual ?? string.Empty,
                    Codigo = codserv
                });
            }
        }

        // TME por código de serviço, reaproveitando o mesmo serviço da otimização completa.
        var tmePorCodserv = new Dictionary<int, TimeSpan?>();
        foreach (var codserv in codservsDasNovasOs)
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

        foreach (var novaOs in novasOrdens)
        {
            if (tmePorCodserv.TryGetValue(novaOs.Codserv, out var tme) && tme.HasValue)
                novaOs.MetaTme = tme.Value.TotalMinutes + 5;
        }

        // §9: montar EquipeIndexada usando os mesmos índices de vehicle do request original,
        // para que AllowedVehicleIndices aponte corretamente para as equipes selecionadas.
        var equipesIndexadas = equipesSelecionadas
            .Select(e => new EquipeIndexada
            {
                IndiceVehicle = e.IndiceVehicleOriginal,
                Equipe = new Models.Eforce.EquipeGmpro
                {
                    CodigoEquipe = e.CodigoEquipe,
                    TipoEquipe = requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].Label,
                    AtuacaoEquipe = e.AtuacaoAtual ?? "U/R",
                    PerfilEquipe = e.PerfilAtual ?? string.Empty,
                    LatitudeGaragem = e.Posicao?.Latitude,
                    LongitudeGaragem = e.Posicao?.Longitude,
                    NPessoas = e.ColaboradoresAtuais
                },
                CodigosRegiao = e.RegioesAtuais
            })
            .ToList();

        var inicioTurno = dataCorteUtc;
        var fimTurno = equipesSelecionadas.Count > 0
            ? equipesSelecionadas.Max(e =>
                requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].EndTimeWindows.Count > 0
                    ? requestOriginal.Model.Vehicles[e.IndiceVehicleOriginal].EndTimeWindows[0].StartTime.ToDateTime()
                    : dataCorteUtc)
            : dataCorteUtc.AddHours(9);

        var ordensProcessadas = _orchestrator.ProcessarNovasOrdensParaReotimizacao(
            novasOrdens, perfisEquipeGmpro, equipesIndexadas, inicioTurno, fimTurno, dataCorteUtc);

        return _builderService.ConstruirShipmentsParaReotimizacao(ordensProcessadas, inicioTurno, fimTurno);
    }

    /// <summary>
    /// Constrói o mapa Label do shipment → Label do vehicle atualmente responsável (§7),
    /// a partir das rotas da response original, para permitir fixação/reserva de tempo.
    /// </summary>
    private static Dictionary<string, string> ConstruirVehicleLabelPorShipment(
        OptimizeToursRequest requestOriginal, OptimizeToursResponse responseOriginal)
    {
        var resultado = new Dictionary<string, string>();
        foreach (var rota in responseOriginal.Routes)
        {
            foreach (var visita in rota.Visits)
            {
                if (!string.IsNullOrWhiteSpace(visita.ShipmentLabel))
                    resultado[visita.ShipmentLabel] = rota.VehicleLabel;
            }
        }
        return resultado;
    }

    /// <summary>
    /// Extrai o número da OS (Id) a partir do label composto do shipment (§4), correlação
    /// exata: o primeiro token do label é sempre o identificador único da OS/lote
    /// (ver <c>GmProBuilderService.PrepararLabelsEDuracoes</c>).
    /// </summary>
    private static string ExtrairNumeroOsDoLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;
        return label.Split(' ')[0];
    }

    /// <summary>
    /// Extrai o código da equipe a partir do label do vehicle, no formato
    /// "{TipoEquipe}-{CodigoEquipe}, PERFIL: {Perfil}" (ver <c>GmProBuilderService.ConstruirVeiculos</c>).
    /// </summary>
    private static string ExtrairCodigoEquipeDoVehicleLabel(string vehicleLabel)
    {
        if (string.IsNullOrWhiteSpace(vehicleLabel)) return string.Empty;

        var semPerfil = vehicleLabel.Split(',')[0];
        var indiceTraco = semPerfil.IndexOf('-');
        return indiceTraco >= 0 ? semPerfil[(indiceTraco + 1)..].Trim() : semPerfil.Trim();
    }

    /// <summary>
    /// Valida a consistência do cenário original antes de utilizá-lo como base para a
    /// reotimização (§4): request/response disponíveis (garantido pelo chamador antes de
    /// invocar este método), <c>Request.Label == Response.RequestLabel</c>, labels de
    /// veículos e de shipments únicas, e todas as equipes selecionadas pertencentes ao
    /// cenário original (correlação exata pelo label do vehicle, nunca por prefixo).
    /// Se o cenário legado não permitir associação inequívoca, a reotimização é rejeitada.
    /// </summary>
    private static void ValidarCenarioOriginal(
        OptimizeToursRequest requestOriginal,
        OptimizeToursResponse responseOriginal,
        List<string> codigosEquipeSelecionados)
    {
        // §4: Request.Label deve ser igual a Response.RequestLabel — garante que a response
        // persistida realmente corresponde ao request persistido (correlação inequívoca).
        if (!string.Equals(requestOriginal.Label, responseOriginal.RequestLabel, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Inconsistência no cenário original: Request.Label ('{requestOriginal.Label}') difere de " +
                $"Response.RequestLabel ('{responseOriginal.RequestLabel}'). Reotimização rejeitada (§4).");
        }

        // §4: labels de veículos devem ser únicas — sem unicidade não há como garantir
        // correlação exata entre vehicle e equipe.
        var vehicleLabelsDuplicados = requestOriginal.Model.Vehicles
            .Select(v => v.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .GroupBy(l => l)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (vehicleLabelsDuplicados.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cenário original possui labels de veículos duplicadas ({string.Join(", ", vehicleLabelsDuplicados)}). " +
                "Não é possível garantir associação inequívoca entre vehicle e equipe. Reotimização rejeitada (§4).");
        }

        // §4: labels de shipments devem ser únicas — sem unicidade não há como garantir
        // correlação exata entre shipment e OS.
        var shipmentLabelsDuplicados = requestOriginal.Model.Shipments
            .Select(s => s.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .GroupBy(l => l)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (shipmentLabelsDuplicados.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cenário original possui labels de shipments duplicadas ({string.Join(", ", shipmentLabelsDuplicados)}). " +
                "Não é possível garantir associação inequívoca entre shipment e OS. Reotimização rejeitada (§4).");
        }

        // §4: todas as equipes selecionadas devem pertencer ao cenário original (correlação
        // exata pelo código extraído do label do vehicle, nunca por StartsWith/prefixo).
        var codigosEquipeNoCenario = requestOriginal.Model.Vehicles
            .Select(v => ExtrairCodigoEquipeDoVehicleLabel(v.Label))
            .ToHashSet();

        var equipesNaoPertencentes = codigosEquipeSelecionados
            .Where(codigo => !codigosEquipeNoCenario.Contains(codigo))
            .ToList();

        if (equipesNaoPertencentes.Count > 0)
        {
            throw new InvalidOperationException(
                $"As equipes ({string.Join(", ", equipesNaoPertencentes)}) não pertencem ao cenário original da " +
                "otimização informada. Reotimização rejeitada (§4).");
        }
    }

    /// <summary>
    /// Mapeia a situação atual de uma OS (coleção <c>ordem_servico</c>) para o
    /// <see cref="StatusOs"/> local, usado pelo classificador (§7). Retorna <c>null</c>
    /// quando a OS ainda não foi despachada (situação semântica anterior a Despachado).
    /// </summary>
    private static StatusOs? MapearStatusOs(IOrdemServico? osAtual)
    {
        if (osAtual?.Situacao?.Semantica is null) return null;

        // Situações anteriores ao despacho (Novo, Agendado, Aguardando, Programado) mantêm a OS
        // como "original não despachada" (Status null, §7).
        if (osAtual.Situacao.Semantica < OrdemServicoSituacaoSemantica.Despachado)
            return null;

        if (osAtual.Situacao.Semantica is OrdemServicoSituacaoSemantica.Cancelado or OrdemServicoSituacaoSemantica.Arquivado)
            return StatusOs.Retirada;

        // A partir de Despachado, refina-se pelo estado semântico mais recente registrado nas
        // equipes ativas da OS (histórico de AlocacaoSituacaoSemantica), que reflete com mais
        // precisão o andamento operacional (deslocamento, chegada, execução etc.) do que a
        // situação genérica da OS.
        var situacaoAlocacao = osAtual.EquipesAtivas?
            .SelectMany(e => e.Historico ?? [])
            .Where(h => h.Situacao.HasValue)
            .OrderByDescending(h => h.DataHoraEvento)
            .Select(h => h.Situacao!.Value)
            .FirstOrDefault();

        return situacaoAlocacao switch
        {
            AlocacaoSituacaoSemantica.Deslocamento => StatusOs.Deslocamento,
            AlocacaoSituacaoSemantica.Chegada => StatusOs.Chegada,
            AlocacaoSituacaoSemantica.Localizacao => StatusOs.Localizacao,
            AlocacaoSituacaoSemantica.Execucao => StatusOs.Execucao,
            AlocacaoSituacaoSemantica.Finalizacao => StatusOs.Finalizacao,
            AlocacaoSituacaoSemantica.Encerramento => StatusOs.Encerramento,
            AlocacaoSituacaoSemantica.Retirada => StatusOs.Retirada,
            AlocacaoSituacaoSemantica.Redirecionada => StatusOs.Redirecionada,
            AlocacaoSituacaoSemantica.Reconhecido => StatusOs.Reconhecido,
            AlocacaoSituacaoSemantica.Lido => StatusOs.Lido,
            AlocacaoSituacaoSemantica.Interrompida => StatusOs.Interrompida,
            AlocacaoSituacaoSemantica.DespachoAutomatico => StatusOs.DespachoAutomatico,
            _ => StatusOs.Despachado
        };
    }

    private static string ResolverAtuacaoAtual(bool? realizaRurais, bool? realizaUrbanos)
    {
        if (realizaRurais == true && realizaUrbanos == true) return "U/R";
        if (realizaRurais == true) return "R";
        if (realizaUrbanos == true) return "U";
        return "U/R";
    }

    /// <summary>
    /// Computa um hash determinístico do snapshot (§15, §18), usado para auditoria e
    /// revalidação na aprovação das equipes.
    /// </summary>
    private static string ComputarHashSnapshot(
        OptimizeToursRequest requestOriginal,
        OptimizeToursResponse responseOriginal,
        List<EquipeReotimizacao> equipesSelecionadas,
        DateTime dataCorteUtc)
    {
        var chave = string.Join('|',
            requestOriginal.Label,
            responseOriginal.Routes.Count,
            string.Join(',', equipesSelecionadas.Select(e => e.CodigoEquipe).OrderBy(c => c)),
            dataCorteUtc.Ticks);

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(chave));
        return Convert.ToHexString(bytes);
    }
}
