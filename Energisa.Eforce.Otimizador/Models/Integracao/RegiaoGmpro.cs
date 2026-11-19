namespace Energisa.Eforce.Otimizador.Models.Eforce;

/// <summary>
/// Região de despacho do sistema E-force (§2.4).
/// Relaciona equipe a uma região de atuação.
/// </summary>
public class RegiaoGmpro
{
    /// <summary>Código da equipe.</summary>
    public string CodigoEquipe { get; set; } = string.Empty;

    /// <summary>Tipo da equipe.</summary>
    public string TipoEquipe { get; set; } = string.Empty;

    /// <summary>Código da região de despacho (formato: empresaId_polo-região no NoSQL; empresaId_região no SQL de debug).</summary>
    public string CodRegiao { get; set; } = string.Empty;
}
