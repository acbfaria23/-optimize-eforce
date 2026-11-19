namespace Energisa.Eforce.Otimizador.Models.Eforce;

/// <summary>
/// OS com campos calculados após as regras de ajuste de datas (§9.1).
/// Carrega a OS original e os valores derivados necessários para o mapeamento.
/// </summary>
public class OsProcessada
{
    /// <summary>OS original do E-force.</summary>
    public OrdemServicoGmpro Os { get; set; } = null!;

    /// <summary>Data de solicitação ajustada (inicialmente = início do turno).</summary>
    public DateTime DataSolicitacaoAjustada { get; set; }

    /// <summary>Data de vencimento ajustada conforme regras de negócio.</summary>
    public DateTime DataVencimentoAjustada { get; set; }

    /// <summary>Multa por hora de atraso (calculada conforme tipo de OS).</summary>
    public double MultaAtraso { get; set; }

    /// <summary>Horas de atraso: int((inicio_turno - DATA_VENCIMENTO) / 1h).</summary>
    public int TempoVencida { get; set; }

    /// <summary>Horas até vencer: int((DATA_VENCIMENTO - inicio_turno) / 1h). Mínimo: 0.</summary>
    public int TempoAteVencer { get; set; }

    /// <summary>Horas que a OS está aberta: int((inicio_turno - DATA_SOLICITACAO) / 1h).</summary>
    public int TempoAberta { get; set; }

    /// <summary>True se DATA_VENCIMENTO &lt; inicio_turno.</summary>
    public bool Vencida { get; set; }

    /// <summary>True se DATA_VENCIMENTO está entre inicio_turno e fim_turno.</summary>
    public bool VencendoNoTurno { get; set; }

    /// <summary>True se o vencimento foi ajustado por vencer no dia da otimização (após o turno).</summary>
    public bool VencimentoAjustadoAposTurnoNoMesmoDia { get; set; }

    /// <summary>True se COMPENSACAO > 0 e RISCO_VIDA != 'S'.</summary>
    public bool IsCompensacao { get; set; }

    /// <summary>Peso final calculado pelo sistema de pesos (§8).</summary>
    public double PesoFinal { get; set; } = 10;

    /// <summary>Duração da OS em segundos (META_TME * 60 ou média).</summary>
    public int DuracaoSegundos { get; set; }

    /// <summary>Perfis de equipe compatíveis (lookup via CODSERV em df_perfis_).</summary>
    public List<string> PerfisEquipe { get; set; } = [];

    /// <summary>Índices dos vehicles compatíveis (allowedVehicleIndices).</summary>
    public List<int> IndicesEquipesPermitidas { get; set; } = [];

    /// <summary>Label composto do shipment (gerado pelo builder nativo).</summary>
    public string Label { get; set; } = string.Empty;
}
