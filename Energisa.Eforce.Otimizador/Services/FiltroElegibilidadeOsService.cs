using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço responsável por aplicar filtros de elegibilidade nas Ordens de Serviço,
/// removendo OSs que não atendem aos critérios de idade e vencimento.
/// </summary>
public class FiltroElegibilidadeOsService(
    IOptions<ElegibilidadeOsSettings> elegibilidadeSettings,
    ILogger<FiltroElegibilidadeOsService> logger)
{
    private readonly ElegibilidadeOsSettings _elegibilidadeSettings = elegibilidadeSettings.Value;
    private readonly ILogger<FiltroElegibilidadeOsService> _logger = logger;

    /// <summary>
    /// Filtra OSs Comerciais e Técnicas cuja idade seja maior que o valor parametrizado.
    /// A idade é calculada como: dataExecucao - DataSolicitacao (em dias).
    /// OSs com idade maior que IdadeMaximaOsComercialTecnicaDias são removidas.
    /// 
    /// Quando IdadeMaximaOsComercialTecnicaDias = 0, apenas OSs do dia corrente são consideradas.
    /// </summary>
    public List<OrdemServicoGmpro> FiltrarPorIdadeOsComercialTecnica(
        List<OrdemServicoGmpro> ordens,
        DateTime dataExecucao)
    {
        if (ordens.Count == 0)
        {
            return ordens;
        }

        var idadeMaximaDias = _elegibilidadeSettings.IdadeMaximaOsComercialTecnicaDias;
        var totalOriginal = ordens.Count;

        var resultado = ordens
            .Where(os =>
            {
                // Filtro aplicável apenas a OSs Comerciais (C) e Técnicas (T)
                if (os.TipoOs != "C" && os.TipoOs != "T")
                {
                    return true;
                }

                var idade = (dataExecucao.Date - os.DataSolicitacao.Date).TotalDays;

                if (idade > idadeMaximaDias)
                {
                    _logger.LogWarning(
                        "OS '{NumeroOs}' removida por idade excedida. TipoOs: {TipoOs}. " +
                        "Idade: {Idade} dias. IdadeMaxima: {IdadeMaxima} dias.",
                        os.NumeroOs, os.TipoOs, (int)idade, idadeMaximaDias);
                    return false;
                }

                return true;
            })
            .ToList();

        var removidas = totalOriginal - resultado.Count;
        if (removidas > 0)
        {
            _logger.LogInformation(
                "Filtro de idade OSs Comerciais/Técnicas aplicado. IdadeMaxima: {IdadeMaxima} dias. " +
                "Total: {Total}. Removidas: {Removidas}. Mantidas: {Mantidas}.",
                idadeMaximaDias, totalOriginal, removidas, resultado.Count);
        }

        return resultado;
    }

    /// <summary>
    /// Filtra OSs Comerciais cujo vencimento seja maior que o prazo parametrizado.
    /// A elegibilidade é calculada como: DataVencimento - dataExecucao (em dias).
    /// 
    /// Inclui: OSs vencidas, OSs que vencem hoje, OSs com vencimento nos próximos N dias.
    /// Remove: OSs cujo vencimento seja superior ao período parametrizado.
    /// 
    /// Este filtro aplica-se apenas a OSs Comerciais (TipoOs = 'C').
    /// OSs Técnicas e Lotes não são filtrados.
    /// 
    /// Quando PrazoMaximoVencimentoOsComercialDias = 0, apenas OSs vencidas ou vencendo hoje são consideradas.
    /// </summary>
    public List<OrdemServicoGmpro> FiltrarPorVencimentoOsComercial(
        List<OrdemServicoGmpro> ordens,
        DateTime dataExecucao)
    {
        if (ordens.Count == 0)
        {
            return ordens;
        }

        var prazoMaximoDias = _elegibilidadeSettings.PrazoMaximoVencimentoOsComercialDias;
        var totalOriginal = ordens.Count;

        var resultado = ordens
            .Where(os =>
            {
                // Filtro aplicável apenas a OSs Comerciais (C)
                if (os.TipoOs != "C")
                {
                    return true;
                }

                var diasParaVencimento = (os.DataVencimento.Date - dataExecucao.Date).TotalDays;

                if (diasParaVencimento > prazoMaximoDias)
                {
                    _logger.LogWarning(
                        "OS '{NumeroOs}' removida por vencimento distante. " +
                        "DiasParaVencimento: {DiasParaVencimento}. PrazoMaximo: {PrazoMaximo} dias.",
                        os.NumeroOs, (int)diasParaVencimento, prazoMaximoDias);
                    return false;
                }

                return true;
            })
            .ToList();

        var removidas = totalOriginal - resultado.Count;
        if (removidas > 0)
        {
            _logger.LogInformation(
                "Filtro de vencimento OSs Comerciais aplicado. PrazoMaximo: {PrazoMaximo} dias. " +
                "Total: {Total}. Removidas: {Removidas}. Mantidas: {Mantidas}.",
                prazoMaximoDias, totalOriginal, removidas, resultado.Count);
        }

        return resultado;
    }

    /// <summary>
    /// Filtra Lotes (TipoOs = 'X') cuja idade seja maior que o valor parametrizado.
    /// A idade é calculada como: dataExecucao - DataSolicitacao (em dias).
    /// Lotes com idade maior que IdadeMaximaLotesDias são removidos.
    /// 
    /// Este filtro é completamente independente de <see cref="FiltrarPorIdadeOsComercialTecnica"/>.
    /// O parâmetro de vencimento (<see cref="FiltrarPorVencimentoOsComercial"/>) NUNCA é aplicado a Lotes.
    /// 
    /// Quando IdadeMaximaLotesDias = 0, apenas lotes criados no dia corrente são considerados.
    /// </summary>
    public List<OrdemServicoGmpro> FiltrarPorIdadeLotes(
        List<OrdemServicoGmpro> ordens,
        DateTime dataExecucao)
    {
        if (ordens.Count == 0)
        {
            return ordens;
        }

        var idadeMaximaDias = _elegibilidadeSettings.IdadeMaximaLotesDias;
        var totalOriginal = ordens.Count;

        var resultado = ordens
            .Where(os =>
            {
                // Filtro aplicável apenas a Lotes (X)
                if (os.TipoOs != "X")
                {
                    return true;
                }

                var idade = (dataExecucao.Date - os.DataSolicitacao.Date).TotalDays;

                if (idade > idadeMaximaDias)
                {
                    _logger.LogWarning(
                        "Lote '{NumcdcLote}' removido por idade excedida. " +
                        "Idade: {Idade} dias. IdadeMaxima: {IdadeMaxima} dias.",
                        os.NumcdcLote, (int)idade, idadeMaximaDias);
                    return false;
                }

                return true;
            })
            .ToList();

        var removidas = totalOriginal - resultado.Count;
        if (removidas > 0)
        {
            _logger.LogInformation(
                "Filtro de idade Lotes aplicado. IdadeMaxima: {IdadeMaxima} dias. " +
                "Total: {Total}. Removidas: {Removidas}. Mantidos: {Mantidos}.",
                idadeMaximaDias, totalOriginal, removidas, resultado.Count);
        }

        return resultado;
    }
}
