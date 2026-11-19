using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Enums;
using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Google.Maps.RouteOptimization.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço puro (sem acesso a banco) responsável por construir o novo <see cref="OptimizeToursRequest"/>
/// de uma reotimização intradiária a partir do request/response originais e do
/// <see cref="ContextoReotimizacao"/> (snapshot) já carregado (§2, §9-§14).
/// Pode ser registrado como Singleton, pois não possui estado ou dependências de contexto.
/// </summary>
public class ReotimizacaoRequestBuilderService
{
    private readonly ILogger<ReotimizacaoRequestBuilderService> _logger;

    public ReotimizacaoRequestBuilderService(ILogger<ReotimizacaoRequestBuilderService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Constrói o novo request GMPRO para a reotimização.
    /// </summary>
    /// <param name="contexto">Snapshot operacional já validado e classificado (§5, §7).</param>
    /// <param name="novosShipments">Shipments já construídos para as novas OSs pendentes (§12), com pesos
    /// calculados sobre o conjunto completo de OSs (não apenas o novo lote).</param>
    /// <param name="searchMode">SearchMode a ser utilizado, resolvido a partir de <see cref="ReotimizacaoSettings.ModoBusca"/> (§14).</param>
    public ResultadoConstrucaoReotimizacao Build(
        ContextoReotimizacao contexto,
        List<Shipment> novosShipments,
        OptimizeToursRequest.Types.SearchMode searchMode)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var resultado = new ResultadoConstrucaoReotimizacao();

        _logger.LogInformation(
            "Iniciando construção do novo request de reotimização. OtimizacaoOriginalId: {OtimizacaoOriginalId}. " +
            "EquipesSelecionadas: {EquipeCount}. SearchMode: {SearchMode}.",
            contexto.OtimizacaoOriginalId, contexto.EquipesSelecionadas.Count, searchMode);

        // §2/§9: clonar o request original preservando ordem e índices dos veículos.
        var novoRequest = contexto.RequestOriginal.Clone();

        var equipesPorLabel = contexto.EquipesSelecionadas.ToDictionary(e => e.VehicleLabel);
        var indicesSelecionados = contexto.EquipesSelecionadas
            .Select(e => e.IndiceVehicleOriginal)
            .ToHashSet();

        // §2/§9: atualizar somente os veículos selecionados; os demais permanecem no array
        // original, mantendo seus índices, porém marcados como Ignore.
        var vehicles = novoRequest.Model.Vehicles;
        var vehiculosIgnorados = 0;
        for (var i = 0; i < vehicles.Count; i++)
        {
            var vehicleLabel = vehicles[i].Label;

            if (!equipesPorLabel.TryGetValue(vehicleLabel, out var equipe) || !indicesSelecionados.Contains(i))
            {
                vehicles[i].Ignore = true;
                vehiculosIgnorados++;
                continue;
            }

            AtualizarVeiculoSelecionado(vehicles[i], equipe);
        }

        _logger.LogInformation(
            "Veículos do request atualizado. Total: {Total}. Selecionados: {Selecionados}. Ignorados: {Ignorados}.",
            vehicles.Count, indicesSelecionados.Count, vehiculosIgnorados);

        // §7/§9/§12: reconstruir shipments roteirizáveis + adicionar novas OSs.
        var novosShipmentsModel = ReconstruirShipments(
            contexto,
            indicesSelecionados,
            resultado);

        novoRequest.Model.Shipments.Clear();
        novoRequest.Model.Shipments.AddRange(novosShipmentsModel);
        novoRequest.Model.Shipments.AddRange(novosShipments);

        _logger.LogInformation(
            "Shipments reconstruídos. ShipmentsReotimizados: {Reotimizados}. NovasOSs: {Novas}. Total: {Total}.",
            novosShipmentsModel.Count, novosShipments.Count, novoRequest.Model.Shipments.Count);

        // §14: solução inicial injetada construída do zero, somente com labels ainda válidos.
        var labelsShipmentsValidos = novoRequest.Model.Shipments.Select(s => s.Label).ToHashSet();
        novoRequest.InjectedFirstSolutionRoutes.Clear();
        novoRequest.InjectedFirstSolutionRoutes.AddRange(
            ConstruirRotasInjetadas(contexto, labelsShipmentsValidos));

        _logger.LogInformation(
            "Rotas injetadas construídas. RotasCount: {RotasCount}.",
            novoRequest.InjectedFirstSolutionRoutes.Count);

        novoRequest.InterpretInjectedSolutionsUsingLabels = true;
        novoRequest.SearchMode = searchMode;
        novoRequest.PopulatePolylines = true;
        novoRequest.PopulateTransitionPolylines = true;
        novoRequest.ConsiderRoadTraffic = true;

        resultado.NovoRequest = novoRequest;

        stopwatch.Stop();
        _logger.LogInformation(
            "Novo request de reotimização construído com sucesso. OtimizacaoOriginalId: {OtimizacaoOriginalId}. DuracaoMs: {DuracaoMs}.",
            contexto.OtimizacaoOriginalId, stopwatch.ElapsedMilliseconds);

        return resultado;
    }

    /// <summary>
    /// Atualiza somente os campos permitidos de um veículo selecionado (§2):
    /// posição inicial, disponibilidade, capacidade restante, refeição e Ignore.
    /// Preserva StartTimeWindows/EndTimeWindows originais como base, custos e limites.
    /// </summary>
    private void AtualizarVeiculoSelecionado(Vehicle vehicle, EquipeReotimizacao equipe)
    {
        vehicle.Ignore = equipe.TurnoEncerrado;

        if (equipe.TurnoEncerrado)
        {
            _logger.LogInformation(
                "Equipe {Equipe} com turno encerrado (disponibilidade {Disponibilidade:u}). Marcada como Ignore na reotimização.",
                equipe.CodigoEquipe, equipe.DisponibilidadeUtc);
            return;
        }

        // §2/§6: atualizar posição inicial (StartWaypoint substituído por StartLocation).
        if (equipe.Posicao is not null)
        {
            vehicle.StartWaypoint = null;
            vehicle.StartLocation = equipe.Posicao;
        }

        // §2: atualizar horário em que a equipe estará disponível. A janela final (EndTimeWindows)
        // é preservada exatamente como enviada na otimização original, incluindo a margem já existente.
        var disponibilidade = ToTimestamp(equipe.DisponibilidadeUtc);
        vehicle.StartTimeWindows.Clear();
        vehicle.StartTimeWindows.Add(new TimeWindow { StartTime = disponibilidade, EndTime = disponibilidade });

        // §10: capacidade restante = capacidade original - concluídas - em execução, mínimo zero.
        var maxLoadOriginal = vehicle.LoadLimits.TryGetValue(ConversaoConstantes.ChaveCapacidadeVisitas, out var limiteOriginal)
            ? limiteOriginal.MaxLoad
            : 0;
        var capacidadeRestante = Math.Max(0, Math.Min(maxLoadOriginal, equipe.CapacidadeRestante));
        vehicle.LoadLimits[ConversaoConstantes.ChaveCapacidadeVisitas] = new Vehicle.Types.LoadLimit { MaxLoad = capacidadeRestante };

        // §11: tratamento da refeição.
        switch (equipe.SituacaoRefeicao)
        {
            case SituacaoRefeicaoReotimizacao.Concluida:
                vehicle.BreakRule = null;
                break;
            case SituacaoRefeicaoReotimizacao.EmAndamento:
                // A disponibilidade já foi calculada considerando o fim da refeição em andamento (§2);
                // a regra já consumida deve ser removida.
                vehicle.BreakRule = null;
                break;
            case SituacaoRefeicaoReotimizacao.Futura:
                // Preservar a regra original (nada a fazer).
                break;
            case SituacaoRefeicaoReotimizacao.NaoAplicavel:
                vehicle.BreakRule = null;
                break;
        }

        // RouteDistanceLimit, custos e demais limites são preservados como no request original (§10).
    }

    /// <summary>
    /// Reconstrói os shipments ainda roteirizáveis conforme a classificação semântica (§7, §9, §13):
    /// OS já despachada permanece fixada na equipe atual; OS original não despachada é redistribuída
    /// entre equipes selecionadas e compatíveis; OS removida/execução ativa não é incluída.
    /// </summary>
    private List<Shipment> ReconstruirShipments(
        ContextoReotimizacao contexto,
        HashSet<int> indicesSelecionados,
        ResultadoConstrucaoReotimizacao resultado)
    {
        var shipmentsOriginais = contexto.RequestOriginal.Model.Shipments.ToDictionary(s => s.Label);
        var indicesVeiculoPorLabel = ConstruirIndicesVeiculoPorLabel(contexto.RequestOriginal);
        var novosShipments = new List<Shipment>();

        foreach (var (labelOs, classificacao) in contexto.OrdensClassificadas)
        {
            if (!shipmentsOriginais.TryGetValue(labelOs, out var shipmentOriginal))
                continue;

            switch (classificacao.Estado)
            {
                case EstadoOsReotimizacao.Removida:
                case EstadoOsReotimizacao.ExecucaoAtiva:
                    if (classificacao.Estado == EstadoOsReotimizacao.ExecucaoAtiva)
                        resultado.OrdensEmExecucao.Add(labelOs);
                    continue; // não incluir no novo modelo (§7).

                case EstadoOsReotimizacao.Deslocamento:
                    resultado.OrdensEmDeslocamento.Add(labelOs);
                    novosShipments.Add(FixarNaEquipeAtual(shipmentOriginal, classificacao, indicesVeiculoPorLabel, labelOs));
                    break;

                case EstadoOsReotimizacao.DespachadaPendente:
                    // §13: manter obrigatoriamente na equipe atual, mesmo com incompatibilidade atual.
                    novosShipments.Add(FixarNaEquipeAtual(shipmentOriginal, classificacao, indicesVeiculoPorLabel, labelOs));
                    break;

                case EstadoOsReotimizacao.OriginalNaoDespachada:
                case EstadoOsReotimizacao.NovaPendente:
                    var shipmentRedistribuido = RedistribuirEntreEquipesCompativeis(
                        shipmentOriginal, indicesSelecionados, labelOs, resultado);
                    if (shipmentRedistribuido is not null)
                        novosShipments.Add(shipmentRedistribuido);
                    break;
            }
        }

        return novosShipments;
    }

    /// <summary>
    /// Constrói o mapa Label do veículo ? índice no array original de Vehicles,
    /// utilizado para restringir AllowedVehicleIndices das OSs fixadas na equipe atual (§9).
    /// </summary>
    private static Dictionary<string, int> ConstruirIndicesVeiculoPorLabel(OptimizeToursRequest requestOriginal)
    {
        var indices = new Dictionary<string, int>();
        var vehicles = requestOriginal.Model.Vehicles;
        for (var i = 0; i < vehicles.Count; i++)
            indices[vehicles[i].Label] = i;
        return indices;
    }

    /// <summary>
    /// Fixa um shipment na equipe atualmente associada (§9, §13): AllowedVehicleIndices passa a
    /// conter somente o índice da equipe atual, garantindo que a OS despachada/em deslocamento
    /// permaneça obrigatoriamente com a mesma equipe, mesmo diante de incompatibilidade atual.
    /// </summary>
    private static Shipment FixarNaEquipeAtual(
        Shipment shipmentOriginal,
        EstadoOsClassificada classificacao,
        Dictionary<string, int> indicesVeiculoPorLabel,
        string labelOs)
    {
        var clone = shipmentOriginal.Clone();
        clone.Ignore = false;

        if (classificacao.VehicleLabelAtual is null ||
            !indicesVeiculoPorLabel.TryGetValue(classificacao.VehicleLabelAtual, out var indiceEquipeAtual))
        {
            throw new InvalidOperationException(
                $"Não foi possível determinar a equipe atual da OS '{labelOs}' para fixação (§9, §13). " +
                "Snapshot inconsistente: VehicleLabelAtual ausente ou não encontrado no request original.");
        }

        clone.AllowedVehicleIndices.Clear();
        clone.AllowedVehicleIndices.Add(indiceEquipeAtual);
        return clone;
    }

    /// <summary>
    /// Recalcula AllowedVehicleIndices considerando somente as equipes selecionadas (§9, §13).
    /// Quando não houver equipe compatível selecionada, a OS não é incluída no request e é
    /// registrada como não atendida.
    /// </summary>
    private static Shipment? RedistribuirEntreEquipesCompativeis(
        Shipment shipmentOriginal,
        HashSet<int> indicesSelecionados,
        string labelOs,
        ResultadoConstrucaoReotimizacao resultado)
    {
        var indicesCompativeis = shipmentOriginal.AllowedVehicleIndices
            .Where(indicesSelecionados.Contains)
            .ToList();

        if (indicesCompativeis.Count == 0)
        {
            resultado.OrdensNaoAtendidas[labelOs] =
                "Nenhuma equipe selecionada é compatível com a OS (perfil/região/urbano-rural).";
            return null;
        }

        var clone = shipmentOriginal.Clone();
        clone.Ignore = false;
        clone.AllowedVehicleIndices.Clear();
        clone.AllowedVehicleIndices.AddRange(indicesCompativeis);
        return clone;
    }

    /// <summary>
    /// Constrói as rotas injetadas do zero (§14): somente VehicleLabel e visitas cujos
    /// shipments continuam no novo modelo, na ordem original das visitas ainda pendentes.
    /// Não copia transitions, métricas, custos, horários, polylines ou breaks consumidos.
    /// </summary>
    private static List<ShipmentRoute> ConstruirRotasInjetadas(
        ContextoReotimizacao contexto,
        HashSet<string> labelsShipmentsValidos)
    {
        var rotas = new List<ShipmentRoute>();

        // §7: labels em deslocamento devem ser inseridas como primeira visita pendente da equipe.
        var labelsEmDeslocamento = contexto.OrdensClassificadas.Values
            .Where(c => c.Estado == EstadoOsReotimizacao.Deslocamento)
            .Select(c => c.LabelOs)
            .ToHashSet();

        foreach (var rotaOriginal in contexto.ResponseOriginal.Routes)
        {
            var visitasValidas = rotaOriginal.Visits
                .Where(v => labelsShipmentsValidos.Contains(v.ShipmentLabel))
                .Select(v => new ShipmentRoute.Types.Visit
                {
                    ShipmentLabel = v.ShipmentLabel,
                    InjectedSolutionLocationToken = v.InjectedSolutionLocationToken
                })
                .OrderByDescending(v => labelsEmDeslocamento.Contains(v.ShipmentLabel)) // deslocamento primeiro (§7)
                .ToList();

            if (visitasValidas.Count == 0)
                continue;

            rotas.Add(new ShipmentRoute
            {
                VehicleLabel = rotaOriginal.VehicleLabel,
                Visits = { visitasValidas }
            });
        }

        return rotas;
    }

    private static Timestamp ToTimestamp(DateTime dt)
    {
        var utcDt = dt.Kind == DateTimeKind.Utc
            ? dt
            : dt.Kind == DateTimeKind.Local
                ? dt.ToUniversalTime()
                : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        var ts = Timestamp.FromDateTime(utcDt);
        ts.Nanos = 0;
        return ts;
    }
}
