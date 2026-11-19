using Energisa.Eforce.Otimizador.Models;

namespace Energisa.Eforce.Otimizador.Services.Interface;

/// <summary>
/// Contrato para o serviço de enriquecimento de resultados de otimização.
/// </summary>
public interface IResultadoOtimizacaoEnriquecimentoService
{
    /// <summary>
    /// Obtém os resultados de otimização das últimas 12 horas com dados de OS enriquecidos.
    /// </summary>
    Task<List<ResultadoOtimizacaoEnriquecido>> ObterResultadosEnriquecidosAsync(string login, string empresa);
}
