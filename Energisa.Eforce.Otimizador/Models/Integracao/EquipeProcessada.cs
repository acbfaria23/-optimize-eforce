namespace Energisa.Eforce.Otimizador.Models.Eforce;

/// <summary>
/// Equipe com datas de turno ajustadas para o dia de execução corrente (§5.1, §5.2).
/// </summary>
public class EquipeProcessada
{
    /// <summary>Equipe original do E-force.</summary>
    public EquipeGmpro Equipe { get; set; } = null!;

    /// <summary>Códigos das regiões de despacho (vindo do join com Regiões).</summary>
    public List<string> CodigosRegiao { get; set; } = [];

    /// <summary>Início do turno ajustado para o dia corrente.</summary>
    public DateTime InicioTurnoAjustado { get; set; }

    /// <summary>Fim do turno ajustado por carga de trabalho e dia corrente.</summary>
    public DateTime FimTurnoAjustado { get; set; }

    /// <summary>Início da refeição ajustado para o dia corrente.</summary>
    public DateTime InicioRefeicaoAjustado { get; set; }

    /// <summary>Fim da refeição ajustado para o dia corrente.</summary>
    public DateTime FimRefeicaoAjustado { get; set; }
}
