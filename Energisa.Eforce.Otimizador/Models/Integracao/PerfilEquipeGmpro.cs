namespace Energisa.Eforce.Otimizador.Models.Eforce;

/// <summary>
/// Perfil de equipe do sistema E-force (§2.3).
/// Relaciona código de serviço ao perfil operacional.
/// </summary>
public class PerfilEquipeGmpro
{
    /// <summary>Código do serviço.</summary>
    public int Codserv { get; set; }

    /// <summary>Nome do perfil (join: OS ? Equipe via perfil).</summary>
    public string Perfil { get; set; } = string.Empty;

    /// <summary>Código alternativo do serviço.</summary>
    public int Codigo { get; set; }
}
