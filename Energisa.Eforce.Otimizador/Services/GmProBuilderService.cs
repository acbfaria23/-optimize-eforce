using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model.Modelo;
using Energisa.God.InfraData.Interfaces;
using Google.Maps.RouteOptimization.V1;
using Google.Protobuf.WellKnownTypes;
using Google.Type;
using DateTime = System.DateTime;
using VisitRequest = Google.Maps.RouteOptimization.V1.Shipment.Types.VisitRequest;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço responsável por transformar os objetos de domínio (OSs e Equipes processadas) em 
/// uma requisição estruturada (OptimizeToursRequest) para a API nativa do Google Cloud.
/// Utiliza tipos Protobuf diretamente para máxima compatibilidade com a API.
/// </summary>
public class GmProBuilderService
{
    private const string LimitTransitionTag = "limit-transition";

    public OptimizeToursRequest Build(
        List<OsProcessada> ordensProcessadas,
        List<EquipeProcessada> equipesProcessadas,
        DateTime inicioTurno,
        DateTime fimTurno,
        string projectId,
        string label,
        bool modoAsync,
        TimeZoneInfo? timeZone = null,
        bool? aplicarAvoidUTurns = true)
    {
        PrepararLabelsEDuracoes(ordensProcessadas, inicioTurno, fimTurno);

        var adjustedStartTime = SetLocalTimeToUtc(
                 dt: inicioTurno,
                 hour: 0, minute: 0, second: 0,
                 timeZone: timeZone);
        var adjustedEndTime = SetLocalTimeToUtc(
                dt: fimTurno,
                hour: 23, minute: 59, second: 59,
                timeZone: timeZone);

        var shipments = ConstruirShipments(ordensProcessadas, inicioTurno, fimTurno, aplicarAvoidUTurns);
        shipments.AddRange(ConstruirAlmocos(equipesProcessadas, adjustedStartTime, adjustedEndTime, aplicarAvoidUTurns));

        // Calcula o fim máximo de turno considerando escalas que ultrapassam a meia-noite.
        // Escalas noturnas podem ter FimTurnoAjustado além do adjustedEndTime (ex: 06:00 do dia seguinte).
        var maxEndTime = equipesProcessadas.Any()
            ? new[] { adjustedEndTime }.Concat(equipesProcessadas.Select(e => e.FimTurnoAjustado)).Max()
            : adjustedEndTime;

        var vehicles = ConstruirVeiculos(equipesProcessadas, maxEndTime);

        var distanciaMaximaMetros = ConversaoConstantes.DistanciaParaPenalidade * 1000;

        var model = new ShipmentModel
        {
            GlobalStartTime = ToTimestamp(adjustedStartTime),
            GlobalEndTime = ToTimestamp(maxEndTime),
            TransitionAttributes =
            {
                new TransitionAttributes
                {
                    SrcTag = LimitTransitionTag,
                    DstTag = LimitTransitionTag,
                    CostPerKilometer = 0,
                    DistanceLimit = new DistanceLimit
                    {
                        SoftMaxMeters = distanciaMaximaMetros == 0 ? ConversaoConstantes.MetrosDiametroCluster : distanciaMaximaMetros,
                        CostPerKilometerBelowSoftMax = 0,
                        CostPerKilometerAboveSoftMax = 100
                    }
                }
            }
        };
        model.Shipments.AddRange(shipments);
        model.Vehicles.AddRange(vehicles);

        return new OptimizeToursRequest
        {
            Parent = $"projects/{projectId}",
            Label = label,
            SearchMode = modoAsync ? OptimizeToursRequest.Types.SearchMode.ConsumeAllAvailableTime : OptimizeToursRequest.Types.SearchMode.ReturnFast,
            ConsiderRoadTraffic = true,
            Model = model,
            PopulatePolylines = true,
            PopulateTransitionPolylines = true,
            Timeout = Duration.FromTimeSpan(TimeSpan.FromMinutes(15)),
            AllowLargeDeadlineDespiteInterruptionRisk = true
        };
    }

    /// <summary>
    /// Prepara labels/durações e constrói os shipments para um conjunto de OSs já processadas
    /// (pesos e atribuição de equipes já calculados), reaproveitando exatamente a mesma lógica
    /// de construção de shipments usada na otimização completa. Utilizado pelo
    /// <c>ReotimizacaoDataService</c> para as novas OSs pendentes (§12).
    /// </summary>
    public List<Shipment> ConstruirShipmentsParaReotimizacao(
        List<OsProcessada> ordensProcessadas, DateTime inicioTurno, DateTime fimTurno)
    {
        PrepararLabelsEDuracoes(ordensProcessadas, inicioTurno, fimTurno);
        return ConstruirShipments(ordensProcessadas, inicioTurno, fimTurno, aplicarAvoidUTurns: false);
    }

    private static void PrepararLabelsEDuracoes(
        List<OsProcessada> ordensProcessadas,
        DateTime inicioTurno,
        DateTime fimTurno)
    {
        foreach (var p in ordensProcessadas)
        {
            var os = p.Os;

            string baseLabel = os.TipoOs == "X" ? (os.NumeroOs ?? os.NumcdcLote) : os.NumeroOs;

            var regulada = os.OsRegulada == "S" ? "REGULADA" : "NAO REGULADA";
            var riscoVida = os.RiscoVida == "S" ? "SIM RISCO A VIDA" : "NAO RISCO A VIDA";
            var vencidaStr = p.Vencida ? $"VENCIDA {p.TempoVencida} HORAS" : "NO PRAZO";
            var tipo = os.TipoOs switch
            {
                "X" => "LOTE",
                "C" => "COMERCIAL",
                "T" => "TECNICA",
                "P" => "PROJETO",
                _ => os.TipoOs
            };

            var perfisStr = p.PerfisEquipe.Count > 0
                ? string.Join(", ", p.PerfisEquipe)
                : "N/A";

            var localizacaoStr = os.Localizacao switch
            {
                "U" => "URBANA",
                "R" => "RURAL",
                _ => "DESCONHECIDA"
            };

            var infoAdicional = string.Join(" , ",
                regulada,
                riscoVida,
                vencidaStr,
                tipo,
                $"Localizacao: {localizacaoStr}",
                $"Afeta {os.PessoasAtingidas} pessoas",
                $"Reincidente: {os.Reicidencia}",
                $"Aberta a: {p.TempoAberta} horas",
                $"Valor de Compensacao: {os.Compensacao}",
                $"Valor de Compensacao Por Hora: {os.CompensacaoPorHora}",
                $"Prioridade: {os.Prioridade}",
                $"Tempo ate vencer: {p.TempoAteVencer}",
                $"Inicio Turno: {inicioTurno:yyyy-MM-ddTHH:mm:ssZ}",
                $"Fim Turno: {fimTurno:yyyy-MM-ddTHH:mm:ssZ}",
                $"PERFIL EQUIPE: {perfisStr}");

            p.Label = $"{baseLabel} {infoAdicional}" +
                      $", Regiao Despacho {os.RegiaoDespacho}" +
                      $" , CODSERV {os.Codserv}" +
                      $" , PESO FINAL: {(p.PesoFinal == 0 ? 1 : p.PesoFinal)}" +
                      $" , Data Vencimento Ajustada: {p.DataVencimentoAjustada:yyyy-MM-ddTHH:mm:ssZ}" +
                      $" , Data Vencimento Real: {os.DataVencimentoReal:yyyy-MM-ddTHH:mm:ssZ}" +
                      $" , Numero Os Real: {os.NumeroOsEnergisa}" +
                      $" , Tipo OS: {tipo}";
        }
    }

    private static List<Shipment> ConstruirShipments(
        List<OsProcessada> ordensProcessadas,
        DateTime inicioTurno,
        DateTime fimTurno,
        bool? aplicarAvoidUTurns)
    {
        var shipments = new List<Shipment>();

        foreach (var p in ordensProcessadas)
        {
            if (!(p.IndicesEquipesPermitidas.Count != 0)) continue; // Ignorar OSs que não podem ser atendidas por nenhuma equipe

            var os = p.Os;
            var shipment = new Shipment
            {
                Label = p.Label,
                PenaltyCost = (long)Math.Floor(p.PesoFinal) == 0 ? 1 : (long)Math.Floor(p.PesoFinal),
            };

            shipment.LoadDemands[ConversaoConstantes.ChaveCapacidadeVisitas] = new Shipment.Types.Load { Amount = 1 };

            foreach (var idx in p.IndicesEquipesPermitidas)
                shipment.AllowedVehicleIndices.Add(idx);

            bool isLateCompensacao = p.IsCompensacao && p.Vencida && os.RiscoVida == "N";

            DateTime startTimePrincipal = inicioTurno;
            DateTime endTimePrincipal = isLateCompensacao
                ? (p.DataVencimentoAjustada > inicioTurno ? p.DataVencimentoAjustada : inicioTurno)
                : (p.IsCompensacao && os.RiscoVida != "S" ? p.DataVencimentoAjustada : fimTurno);

            shipment.Deliveries.Add(MontarVisitRequest(
                lat: os.Latitude!.Value,
                lon: os.Longitude!.Value,
                duration: p.DuracaoSegundos,
                startTime: startTimePrincipal,
                endTime: endTimePrincipal,
                softStartTime: p.DataSolicitacaoAjustada,
                softEndTime: p.DataVencimentoAjustada,
                costBeforeSoftStart: 0,
                costAfterSoftEnd: p.MultaAtraso,
                cost: null));

            if (p.IsCompensacao && p.Vencida && os.RiscoVida == "N")
            {
                shipment.Deliveries.Add(MontarVisitRequest(
                    lat: os.Latitude!.Value,
                    lon: os.Longitude!.Value,
                    duration: p.DuracaoSegundos,
                    startTime: inicioTurno,
                    endTime: fimTurno,
                    softStartTime: p.DataSolicitacaoAjustada,
                    softEndTime: p.DataVencimentoAjustada,
                    costBeforeSoftStart: 0,
                    costAfterSoftEnd: os.CompensacaoPorHora,
                    cost: os.Compensacao));
            }
            else if (p.IsCompensacao && !p.Vencida && os.RiscoVida == "N")
            {
                DateTime softTimeAlt =
                    p.VencendoNoTurno ||
                    p.VencimentoAjustadoAposTurnoNoMesmoDia
                        ? p.DataVencimentoAjustada
                        : fimTurno;

                shipment.Deliveries.Add(MontarVisitRequest(
                    lat: os.Latitude!.Value,
                    lon: os.Longitude!.Value,
                    duration: p.DuracaoSegundos,
                    startTime: inicioTurno,
                    endTime: fimTurno,
                    softStartTime: softTimeAlt,
                    softEndTime: softTimeAlt,
                    costBeforeSoftStart: 0,
                    costAfterSoftEnd: os.CompensacaoPorHora,
                    cost: os.Compensacao));
            }


            shipments.Add(shipment);
        }

        return shipments;
    }

    private static List<Shipment> ConstruirAlmocos(List<EquipeProcessada> equipesProcessadas, DateTime globalStartTime, DateTime globalEndTime, bool? aplicarAvoidUTurns)
    {
        var almocos = new List<Shipment>();

        for (int i = 0; i < equipesProcessadas.Count; i++)
        {
            var ep = equipesProcessadas[i];
            var eq = ep.Equipe;

            if (!eq.RetornaGaragemAlmoco) continue;
            if (eq.PorcentagemCargaTrabalho <= 50) continue;

            double penaltyAlmoco = ConversaoConstantes.PenalidadeAlmoco;
            var shipment = new Shipment
            {
                Label = $"REFEICAO {eq.CodigoEquipe} {eq.TipoEquipe}," +
                              $" PERFIL: {eq.PerfilEquipe}," +
                              $" PESO: {penaltyAlmoco}",
                PenaltyCost = penaltyAlmoco,
            };
            shipment.LoadDemands[ConversaoConstantes.ChaveCapacidadeVisitas] = new Shipment.Types.Load { Amount = 1 };
            shipment.AllowedVehicleIndices.Add(i);

            var refeicaoStart = ep.InicioRefeicaoAjustado;
            var refeicaoEnd = ep.FimRefeicaoAjustado;
            if (refeicaoStart < globalStartTime) refeicaoStart = globalStartTime;
            if (refeicaoStart > globalEndTime) refeicaoStart = globalEndTime;
            if (refeicaoEnd < globalStartTime) refeicaoEnd = globalStartTime;
            if (refeicaoEnd > globalEndTime) refeicaoEnd = globalEndTime;
            if (refeicaoStart >= refeicaoEnd) continue;

            shipment.Deliveries.Add(MontarVisitRequest(
                lat: eq.LatitudeGaragem!.Value,
                lon: eq.LongitudeGaragem!.Value,
                duration: ConversaoConstantes.DuracaoAlmocoSegundos,
                startTime: refeicaoStart,
                endTime: refeicaoEnd,
                softStartTime: ep.InicioRefeicaoAjustado,
                softEndTime: ep.FimRefeicaoAjustado,
                costBeforeSoftStart: 0,
                costAfterSoftEnd: 0,
                cost: null));

            almocos.Add(shipment);
        }

        return almocos;
    }

    private static List<Vehicle> ConstruirVeiculos(List<EquipeProcessada> equipesProcessadas, DateTime globalEndTime)
    {
        var vehicles = new List<Vehicle>();

        foreach (var ep in equipesProcessadas)
        {
            var eq = ep.Equipe;

            var vehicle = new Vehicle
            {
                Label = $"{eq.TipoEquipe}-{eq.CodigoEquipe}," +
                                   $" PERFIL: {eq.PerfilEquipe}",
                TravelMode = Vehicle.Types.TravelMode.Driving,
                CostPerKilometer = eq.CustoKm,
                FixedCost = eq.CustoFixo,
                StartWaypoint = MontarWaypoint(eq.LatitudeGaragem!.Value, eq.LongitudeGaragem!.Value),
                TravelDurationMultiple = eq.TravelDurationMultiple,
                RouteDistanceLimit = new DistanceLimit
                {
                    MaxMeters = ConversaoConstantes.MetrosLimiteRotaPorEquipe,
                    SoftMaxMeters = ConversaoConstantes.MetrosLimiteRotaPorEquipeSoft,
                    CostPerKilometerAboveSoftMax = 30,

                    // (cost below é incompatível com routedistancelimit, nao incluir)
                    //CostPerKilometerBelowSoftMax
                },
            };

            vehicle.LoadLimits[ConversaoConstantes.ChaveCapacidadeVisitas] = new Vehicle.Types.LoadLimit { MaxLoad = eq.MaxLoad };

            vehicle.StartTimeWindows.Add(new TimeWindow { StartTime = ToTimestamp(ep.InicioTurnoAjustado), EndTime = ToTimestamp(ep.InicioTurnoAjustado) });
            // § Escala que ultrapassa meia-noite: end_time_windows nunca deve ultrapassar globalEndTime.
            // Caso FimTurnoAjustado + 1h saia da janela global, limitar ao próprio FimTurnoAjustado.
            var endTimeMargin = ep.FimTurnoAjustado.AddHours(1);
            var endTimeWindow = endTimeMargin <= globalEndTime ? endTimeMargin : ep.FimTurnoAjustado;
            vehicle.EndTimeWindows.Add(new TimeWindow { StartTime = ToTimestamp(ep.FimTurnoAjustado), EndTime = ToTimestamp(endTimeWindow) });

            if (eq.RetornaGaragem)
                vehicle.EndWaypoint = MontarWaypoint(eq.LatitudeGaragem!.Value, eq.LongitudeGaragem!.Value);

            var temRefeicao = eq.PorcentagemCargaTrabalho > 50;
            var refeicaoNaRua = !eq.RetornaGaragemAlmoco;
            if (temRefeicao && refeicaoNaRua)
            {
                vehicle.BreakRule = new BreakRule();
                vehicle.BreakRule.BreakRequests.Add(new BreakRule.Types.BreakRequest
                {
                    EarliestStartTime = ToTimestamp(ep.InicioRefeicaoAjustado),
                    LatestStartTime = ToTimestamp(ep.FimRefeicaoAjustado), // Alterado para garantir que o almoço seja iniciado no horário definido, evitando que a otimização adie o início do almoço para mais tarde, o que poderia causar conflitos com outras visitas ou com o fim do turno.
                    MinDuration = Duration.FromTimeSpan(TimeSpan.FromSeconds(ConversaoConstantes.DuracaoAlmocoSegundos)),
                });
            }

            vehicles.Add(vehicle);
        }

        return vehicles;
    }

    private static VisitRequest MontarVisitRequest(
        double lat, double lon, int duration,
        DateTime startTime, DateTime endTime,
        DateTime? softStartTime, DateTime? softEndTime,
        double costBeforeSoftStart, double costAfterSoftEnd,
        double? cost,
        bool? aplicarAvoidUTurns = true)
    {
        var effectiveCostAfterSoftEnd = costAfterSoftEnd > 0 && costAfterSoftEnd < ConversaoConstantes.MinCostPerHourAfterSoftEnd
            ? ConversaoConstantes.MinCostPerHourAfterSoftEnd
            : costAfterSoftEnd;

        var tw = new TimeWindow
        {
            StartTime = ToTimestamp(startTime),
            EndTime = ToTimestamp(endTime),
            CostPerHourBeforeSoftStartTime = costBeforeSoftStart,
            CostPerHourAfterSoftEndTime = effectiveCostAfterSoftEnd,
        };

        if (softStartTime is not null) tw.SoftStartTime = ToTimestamp(softStartTime.Value);
        if (softEndTime is not null) tw.SoftEndTime = ToTimestamp(softEndTime.Value);

        var visit = new VisitRequest
        {
            ArrivalWaypoint = MontarWaypoint(lat, lon),
            Duration = Duration.FromTimeSpan(TimeSpan.FromSeconds(duration)),
            AvoidUTurns = aplicarAvoidUTurns.HasValue ? aplicarAvoidUTurns.Value : false,
            Tags = { LimitTransitionTag },
        };
        visit.TimeWindows.Add(tw);

        if (cost.HasValue) visit.Cost = cost.Value;

        return visit;
    }

    private static Waypoint MontarWaypoint(double lat, double lon) => new()
    {
        Location = new Location { LatLng = new LatLng { Latitude = lat, Longitude = lon } }
    };

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
    private static DateTime SetLocalTimeToUtc(DateTime dt, int hour, int minute, int second, TimeZoneInfo timeZone)
    {

        // define entrada para ser interpretada como UTC (sem conversão automática)
        var forcedUtcDt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        // converte para horario local
        var localDt = TimeZoneInfo.ConvertTimeFromUtc(forcedUtcDt, timeZone);
        // ajusta hora, minuto e segundo conforme parâmetros (mantendo data original)
        var adjustedLocalDt = new DateTime(
            year: localDt.Year, month: localDt.Month, day: localDt.Day,
            hour: hour, minute: minute, second: second,
            kind: DateTimeKind.Unspecified);
        // converte de volta para UTC
        return TimeZoneInfo.ConvertTimeToUtc(adjustedLocalDt, timeZone);
    }

    /* public static List<Shipment> PrepararShipments(
         List<OrdemServicoGmpro> ordensAtribuidas,
         List<EquipeGmpro> equipes,
         DateTime inicioTurno,
         DateTime fimTurno,
         bool flexibilizarRefeicao)      
     {
         PrepararLabelsEDuracoes(ordensAtribuidas, inicioTurno, fimTurno);
         var shipments = ConstruirShipments(ordensAtribuidas, inicioTurno, fimTurno);
         shipments.AddRange(ConstruirAlmocos(equipes: equipes, flexibilizarRefeicao: flexibilizarRefeicao));
         return shipments;
     }*/
}
