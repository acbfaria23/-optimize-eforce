using Energisa.Eforce.Otimizador.Models;
using Google.Maps.RouteOptimization.V1;
using System.Globalization;
using VisitRequest = Google.Maps.RouteOptimization.V1.Shipment.Types.VisitRequest;

namespace Energisa.Eforce.Otimizador.Helpers;

/// <summary>
/// Valida o <see cref="OptimizeToursRequest"/> antes do envio à GMPRO, replicando as regras
/// que a Google rejeita com INVALID_REQUEST (coordenadas, tags de transição, índices e janelas).
/// </summary>
public static class ValidadorRequestGmPro
{
    /// <summary>
    /// Retorna todas as inconsistências encontradas (a Google reporta apenas a primeira).
    /// </summary>
    /// <param name="request">Request a validar.</param>
    public static List<InconsistenciaRequestGmPro> Validar(OptimizeToursRequest request)
    {
        List<InconsistenciaRequestGmPro> resultado = [];

        var modelo = request?.Model;
        if (modelo is null)
        {
            resultado.Add(Critico("model", null, "Request sem model."));
            return resultado;
        }

        if (modelo.Vehicles.Count == 0)
            resultado.Add(Critico("model.vehicles", null, "Nenhum veículo no request."));
        if (modelo.Shipments.Count == 0)
            resultado.Add(Critico("model.shipments", null, "Nenhum shipment no request."));

        ValidarJanelaGlobal(modelo, resultado);
        ValidarVeiculos(modelo, resultado);
        ValidarShipments(modelo, resultado);
        ValidarTransitionAttributes(modelo, resultado);

        return resultado;
    }

    private static void ValidarJanelaGlobal(ShipmentModel modelo, List<InconsistenciaRequestGmPro> resultado)
    {
        if (modelo.GlobalEndTime is not null && modelo.GlobalStartTime is not null
            && modelo.GlobalEndTime.Seconds <= modelo.GlobalStartTime.Seconds)
        {
            resultado.Add(Critico(
                "model.globalEndTime", null,
                "globalEndTime deve ser posterior a globalStartTime."));
        }
    }

    private static void ValidarVeiculos(ShipmentModel modelo, List<InconsistenciaRequestGmPro> resultado)
    {
        HashSet<string> labels = [];

        for (var i = 0; i < modelo.Vehicles.Count; i++)
        {
            var veiculo = modelo.Vehicles[i];
            var caminho = $"model.vehicles[{i}]";

            if (!string.IsNullOrWhiteSpace(veiculo.Label) && !labels.Add(veiculo.Label))
                resultado.Add(Alerta(caminho, veiculo.Label, "Label de veículo duplicado."));

            ValidarCoordenada(veiculo.StartLocation, $"{caminho}.startLocation", veiculo.Label, resultado);
            ValidarCoordenada(veiculo.EndLocation, $"{caminho}.endLocation", veiculo.Label, resultado);
            ValidarCoordenada(veiculo.StartWaypoint?.Location?.LatLng, $"{caminho}.startWaypoint", veiculo.Label, resultado);
            ValidarCoordenada(veiculo.EndWaypoint?.Location?.LatLng, $"{caminho}.endWaypoint", veiculo.Label, resultado);

            ValidarJanelas(veiculo.StartTimeWindows, $"{caminho}.startTimeWindows", veiculo.Label, resultado);
            ValidarJanelas(veiculo.EndTimeWindows, $"{caminho}.endTimeWindows", veiculo.Label, resultado);
        }
    }

    private static void ValidarShipments(ShipmentModel modelo, List<InconsistenciaRequestGmPro> resultado)
    {
        var totalVeiculos = modelo.Vehicles.Count;

        for (var i = 0; i < modelo.Shipments.Count; i++)
        {
            var shipment = modelo.Shipments[i];
            var caminho = $"model.shipments[{i}]";

            if (shipment.Pickups.Count == 0 && shipment.Deliveries.Count == 0)
                resultado.Add(Critico(caminho, shipment.Label, "Shipment sem pickup e sem delivery."));

            for (var p = 0; p < shipment.Pickups.Count; p++)
                ValidarVisita(shipment.Pickups[p], $"{caminho}.pickups[{p}]", shipment.Label, resultado);
            for (var d = 0; d < shipment.Deliveries.Count; d++)
                ValidarVisita(shipment.Deliveries[d], $"{caminho}.deliveries[{d}]", shipment.Label, resultado);

            foreach (var indice in shipment.AllowedVehicleIndices)
            {
                if (indice < 0 || indice >= totalVeiculos)
                {
                    resultado.Add(Critico(
                        $"{caminho}.allowedVehicleIndices", shipment.Label,
                        $"Índice de veículo inexistente: {indice}. Total de veículos: {totalVeiculos}."));
                }
            }
        }
    }

    private static void ValidarVisita(
        VisitRequest visita, string caminho, string rotulo, List<InconsistenciaRequestGmPro> resultado)
    {
        ValidarCoordenada(visita.ArrivalLocation, $"{caminho}.arrivalLocation", rotulo, resultado);
        ValidarCoordenada(visita.ArrivalWaypoint?.Location?.LatLng, $"{caminho}.arrivalWaypoint", rotulo, resultado);
        ValidarCoordenada(visita.DepartureLocation, $"{caminho}.departureLocation", rotulo, resultado);
        ValidarCoordenada(visita.DepartureWaypoint?.Location?.LatLng, $"{caminho}.departureWaypoint", rotulo, resultado);
        ValidarJanelas(visita.TimeWindows, $"{caminho}.timeWindows", rotulo, resultado);
    }

    private static void ValidarTransitionAttributes(ShipmentModel modelo, List<InconsistenciaRequestGmPro> resultado)
    {
        var tagsOrigem = modelo.Shipments
            .SelectMany(s => s.Pickups.Concat(s.Deliveries))
            .SelectMany(v => v.Tags)
            .Concat(modelo.Vehicles.SelectMany(v => v.StartTags))
            .ToHashSet();
        var tagsDestino = modelo.Shipments
            .SelectMany(s => s.Pickups.Concat(s.Deliveries))
            .SelectMany(v => v.Tags)
            .Concat(modelo.Vehicles.SelectMany(v => v.EndTags))
            .ToHashSet();

        for (var i = 0; i < modelo.TransitionAttributes.Count; i++)
        {
            var transicao = modelo.TransitionAttributes[i];
            var caminho = $"model.transitionAttributes[{i}]";

            var temSrc = !string.IsNullOrEmpty(transicao.SrcTag);
            var temSrcExcluida = !string.IsNullOrEmpty(transicao.ExcludedSrcTag);
            if (temSrc == temSrcExcluida)
            {
                resultado.Add(Critico(
                    caminho, null,
                    temSrc
                        ? "srcTag e excludedSrcTag preenchidos; exatamente um deve ser informado."
                        : "srcTag e excludedSrcTag ausentes; exatamente um deve ser informado."));
            }

            var temDst = !string.IsNullOrEmpty(transicao.DstTag);
            var temDstExcluida = !string.IsNullOrEmpty(transicao.ExcludedDstTag);
            if (temDst == temDstExcluida)
            {
                resultado.Add(Critico(
                    caminho, null,
                    temDst
                        ? "dstTag e excludedDstTag preenchidos; exatamente um deve ser informado."
                        : "dstTag e excludedDstTag ausentes; exatamente um deve ser informado."));
            }

            if (temSrc && !tagsOrigem.Contains(transicao.SrcTag))
                resultado.Add(Alerta(caminho, null, $"srcTag '{transicao.SrcTag}' não existe em nenhuma visita ou veículo."));
            if (temDst && !tagsDestino.Contains(transicao.DstTag))
                resultado.Add(Alerta(caminho, null, $"dstTag '{transicao.DstTag}' não existe em nenhuma visita ou veículo."));
        }
    }

    private static void ValidarJanelas(
        IEnumerable<TimeWindow> janelas, string caminho, string? rotulo, List<InconsistenciaRequestGmPro> resultado)
    {
        var indice = 0;
        foreach (var janela in janelas)
        {
            if (janela.StartTime is not null && janela.EndTime is not null
                && janela.EndTime.Seconds < janela.StartTime.Seconds)
            {
                resultado.Add(Critico(
                    $"{caminho}[{indice}]", rotulo,
                    $"Janela com fim anterior ao início. Início: {janela.StartTime.Seconds}. Fim: {janela.EndTime.Seconds}."));
            }
            indice++;
        }
    }

    private static void ValidarCoordenada(
        Google.Type.LatLng? coordenada, string caminho, string? rotulo, List<InconsistenciaRequestGmPro> resultado)
    {
        if (coordenada is null)
            return;

        var latitude = coordenada.Latitude;
        var longitude = coordenada.Longitude;

        if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90)
        {
            resultado.Add(Critico(
                $"{caminho}.latitude", rotulo,
                $"Latitude inválida: {Formatar(latitude)}.{SugerirCorrecao(latitude)}"));
        }

        if (!double.IsFinite(longitude) || longitude < -180 || longitude > 180)
        {
            resultado.Add(Critico(
                $"{caminho}.longitude", rotulo,
                $"Longitude inválida: {Formatar(longitude)}.{SugerirCorrecao(longitude)}"));
        }
    }

    /// <summary>Sugere o valor provável quando o separador decimal foi perdido (ex.: -3792820 → -37.9282).</summary>
    private static string SugerirCorrecao(double valor)
    {
        if (!double.IsFinite(valor) || Math.Abs(valor) < 1000 || valor != Math.Truncate(valor))
            return string.Empty;

        // Inserir o ponto após os 2 primeiros dígitos (padrão das coordenadas do Brasil).
        var digitos = Math.Abs(valor).ToString("0", CultureInfo.InvariantCulture);
        var texto = digitos.Insert(2, ".");
        var sinal = valor < 0 ? "-" : string.Empty;
        return $" Possível perda do separador decimal; valor provável: {sinal}{texto}.";
    }

    private static string Formatar(double valor) => valor.ToString("R", CultureInfo.InvariantCulture);

    private static InconsistenciaRequestGmPro Critico(string caminho, string? rotulo, string mensagem) =>
        new(SeveridadeInconsistencia.Critico, caminho, rotulo, mensagem);

    private static InconsistenciaRequestGmPro Alerta(string caminho, string? rotulo, string mensagem) =>
        new(SeveridadeInconsistencia.Alerta, caminho, rotulo, mensagem);
}
