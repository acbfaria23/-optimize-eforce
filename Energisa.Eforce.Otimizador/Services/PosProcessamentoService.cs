using System.Text.RegularExpressions;
using Google.Maps.RouteOptimization.V1;
using Microsoft.Extensions.Logging;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Eforce;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Responsável por processar a resposta da API do Google, realizar os descontos
/// de multas recalculadas e gerar o relatório final de conferência.
/// </summary>
public class PosProcessamentoService
{
    private static int ShiftRiscoVidaMin => ConversaoConstantes.TempoParaExecucaoRiscoVidaMinutos;

    private readonly double _multaRiscoVida = ConversaoConstantes.MultaAtrasoRiscoVidaHora;
    private readonly double _multaClienteEssencial = ConversaoConstantes.MultaAtrasoRiscoVidaHora;
    private readonly ILogger<PosProcessamentoService> _logger;

    public PosProcessamentoService(ILogger<PosProcessamentoService> logger)
    {
        _logger = logger;
    }

    public RelatorioOtimizacao Processar(OptimizeToursResponse response, List<OsProcessada> ordensOriginais)
    {
        var relatorio = new RelatorioOtimizacao
        {
            ValorTotalOriginal = ordensOriginais.Sum(o => o.PesoFinal)
        };

        if (response.Metrics == null) return relatorio;

        double skipValue = 0;
        double multaRV = 0;
        double multaCE = 0;
        double valorResidualMulta = 0;
        double valorResidualT0 = 0;

        var costs = response.Metrics?.Costs;

        var idsAtendidos = response.Routes
            .SelectMany(r => r.Visits ?? Enumerable.Empty<ShipmentRoute.Types.Visit>())
            .Select(v => ExtrairIDUnico(v.ShipmentLabel))
            .ToHashSet();

        var ordensSkiped = ordensOriginais.Where(o => !idsAtendidos.Contains(o.Os.NumeroOs)).ToList();
        skipValue = ordensSkiped.Sum(o => o.PesoFinal);

        foreach (var route in response.Routes)
        {
            if (route.Visits == null) continue;

            foreach (var visit in route.Visits)
            {
                string label = visit.ShipmentLabel;
                var startTime = visit.StartTime.ToDateTime();

                try
                {
                    bool isRiscoVida = label.Contains("SIM RISCO A VIDA");
                    bool isClienteEssencial = label.Contains("SIM CLIENTE ESSENCIAL");



                    string inicioTurnoStr = ExtrairCampoLabel(label, "Inicio Turno");
                    DateTime dtInicio = DateTime.Parse(inicioTurnoStr.Replace("Z", ""));

                    string vencimentoAjustadoStr = ExtrairCampoLabel(label, "Data Vencimento Ajustada");
                    DateTime dtVencimentoAjustado = DateTime.Parse(vencimentoAjustadoStr.Replace("Z", ""));

                    double difMinutos = (startTime - dtVencimentoAjustado).TotalMinutes;
                    bool atrasou = difMinutos > 0;

                    if (isRiscoVida)
                    {
                        var dtShift = dtInicio.AddMinutes(ShiftRiscoVidaMin);
                        double minutosAtraso = Math.Round(Math.Max(0, (startTime - dtShift).TotalMinutes), 2);
                        multaRV += AplicarLag(_multaRiscoVida, minutosAtraso);
                    }
                    else if (isClienteEssencial)
                    {
                        var dtShift = dtInicio.AddMinutes(ShiftRiscoVidaMin);
                        double minutosAtraso = Math.Round(Math.Max(0, (startTime - dtShift).TotalMinutes), 2);
                        multaCE += AplicarLag(_multaClienteEssencial, minutosAtraso);
                    }
                    else if (atrasou && label.Contains("Valor de Compensação"))
                    {
                        double compensacaoHora = ExtrairValorNumerico(label, "Valor de Compensação Por Hora");
                        double compensacaoT0 = ExtrairValorNumerico(label, "Valor de Compensação");

                        valorResidualMulta += AplicarLag(compensacaoHora, Math.Round(difMinutos, 2));
                        valorResidualT0 += compensacaoT0;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Falha ao processar label da visita: {Label}", visit.ShipmentLabel);
                }
            }
        }

        relatorio.ValorSkips = Math.Round(skipValue, 2);
        relatorio.ValorMultaRiscoVida = Math.Round(multaRV, 2);
        relatorio.ValorMultaClienteEssencial = Math.Round(multaCE, 2);
        relatorio.ValorTotalMulta = Math.Round(valorResidualMulta, 2);
        relatorio.ValorT0 = Math.Round(valorResidualT0, 2);

        relatorio.ValorFinal = Math.Round(
            relatorio.ValorTotalOriginal
            - relatorio.ValorSkips
            - relatorio.ValorMultaRiscoVida
            - relatorio.ValorMultaClienteEssencial
            - relatorio.ValorTotalMulta
            - relatorio.ValorT0, 2);

        if (costs != null)
        {
            relatorio.CustoKilometros = costs.TryGetValue("model.vehicles.cost_per_kilometer", out double ck) ? ck : 0;
            relatorio.CustoFixo = costs.TryGetValue("model.vehicles.fixed_cost", out double cf) ? cf : 0;
        }

        return relatorio;
    }

    private static double AplicarLag(double multa, double minutosAtraso) => (multa * minutosAtraso) / 60;

    private static string ExtrairCampoLabel(string label, string campo)
    {
        var match = Regex.Match(label, $@"{campo}:\s*([^,\n]*)");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static double ExtrairValorNumerico(string label, string campo)
    {
        var strValue = ExtrairCampoLabel(label, campo);
        return double.TryParse(strValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) ? val : 0;
    }

    private static string ExtrairIDUnico(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;
        return label.Split(' ')[0];
    }
}

public class RelatorioOtimizacao
{
    public double ValorTotalOriginal { get; set; }
    public double ValorSkips { get; set; }
    public double ValorMultaRiscoVida { get; set; }
    public double ValorMultaClienteEssencial { get; set; }
    public double ValorTotalMulta { get; set; }
    public double ValorT0 { get; set; }
    public double ValorFinal { get; set; }
    public double CustoKilometros { get; set; }
    public double CustoFixo { get; set; }

    public override string ToString()
    {
        return ($"""

  VALOR OPERACIONAL ORIGINAL: {Math.Round(ValorTotalOriginal, 2)}
  VALOR TAREFAS DESCARTADAS (SKIPS): {Math.Round(ValorSkips, 2)}
  MULTA ESTIMADA RISCO VIDA: {Math.Round(ValorMultaRiscoVida, 2)}
  MULTA ESTIMADA CLIENTE ESSENCIAL: {Math.Round(ValorMultaClienteEssencial, 2)}
  MULTA OPERACIONAL (OUTROS): {Math.Round(ValorTotalMulta, 2)}
  VALOR T0 (RESIDUAL): {Math.Round(ValorT0, 2)}

  VALOR LÍQUIDO FINAL: {Math.Round(ValorFinal, 2)}

CONFERÊNCIA OPERACIONAL (KM + FIXO): {Math.Round(CustoKilometros + CustoFixo, 2)}
DIFERENÇA: {Math.Round(Math.Round(ValorFinal, 2) - Math.Round(CustoKilometros + CustoFixo, 2), 2)}
""");
    }

    public void Imprimir() => Console.WriteLine(this.ToString());
}
