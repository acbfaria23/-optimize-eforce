namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Resultado unificado do pipeline de otimização.
/// </summary>
public class ResultadoPipeline
{
    public string Label { get; set; } = string.Empty;
    public RelatorioOtimizacao Relatorio { get; set; } = null!;
    public string RequestJson { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
}
