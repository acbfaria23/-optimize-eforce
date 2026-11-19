namespace Energisa.Eforce.Otimizador.Models.Eforce;

/// <summary>
/// Equipe de campo do sistema E-force (§2.2).
/// </summary>
public class EquipeGmpro
{
    /// <summary>Código da equipe (ex: CGRGA34).</summary>
    public string CodigoEquipe { get; set; } = string.Empty;

    /// <summary>Tipo/subtipo da equipe.</summary>
    public string TipoEquipe { get; set; } = string.Empty;

    /// <summary>Área de atuação: U, R, U/R.</summary>
    public string AtuacaoEquipe { get; set; } = string.Empty;

    /// <summary>Custo por quilômetro rodado.</summary>
    public double CustoKm { get; set; }
    
    /// <summary>Custo fixo da equipe.</summary>
    public double CustoFixo { get; set; }

    /// <summary>Horário de início do turno.</summary>
    public DateTime InicioTurno { get; set; }

    /// <summary>Horário de fim do turno.</summary>
    public DateTime FimTurno { get; set; }

    /// <summary>Horário de início da refeição.</summary>
    public DateTime InicioRefeicao { get; set; }

    /// <summary>Horário de fim da refeição.</summary>
    public DateTime FimRefeicao { get; set; }

    /// <summary>Duração do almoço em minutos.</summary>
    public double? DuracaoAlmoco { get; set; }

    /// <summary>Latitude da garagem/base da equipe.</summary>
    public double? LatitudeGaragem { get; set; }

    /// <summary>Longitude da garagem/base da equipe.</summary>
    public double? LongitudeGaragem { get; set; }

    /// <summary>Latitude do local de almoço.</summary>
    public double? LatitudeAlmoco { get; set; }

    /// <summary>Longitude do local de almoço.</summary>
    public double? LongitudeAlmoco { get; set; }

    /// <summary>Percentual de carga de trabalho (ajusta FIM_TURNO). Originado de <c>PerfilEquipe.PercentualAlocacaoEquipe</c>.</summary>
    public double PorcentagemCargaTrabalho { get; set; }

    /// <summary>Tempo adicional de checklist em minutos. Originado de <c>PerfilEquipe.TempoAdicionalChecklist</c>.</summary>
    public int? TempoAdicionalChecklist { get; set; }

    /// <summary>Tolerância em minutos antes do início da refeição. Originado de <c>Equipe.ToleranciaInicioAlmoco</c>.</summary>
    public int? ToleranciaInicioAlmoco { get; set; }

    /// <summary>Tolerância em minutos após o fim da refeição. Originado de <c>Equipe.ToleranciaFimAlmoco</c>.</summary>
    public int? ToleranciaFimAlmoco { get; set; }

    /// <summary>Indica se a equipe retorna à base ao fim do expediente. Originado de <c>Equipe.RetornaBaseFimExpediente</c>.</summary>
    public bool? RetornaBaseFimExpediente { get; set; }

    /// <summary>Indica se a equipe retorna à base para a refeição. Originado de <c>Equipe.RetornaAlmocoBase</c>.</summary>
    public bool? RetornaAlmocoBase { get; set; }

    /// <summary>Capacidade máxima de visitas da equipe.</summary>
    public int MaxLoad { get; set; }

    /// <summary>Perfil operacional (ex: Faz tudo).</summary>
    public string PerfilEquipe { get; set; } = string.Empty;

    /// <summary>Número de pessoas na equipe.</summary>
    public string EquipeNome { get; set; } = string.Empty;
    public string EquipeFull { get => EquipeNome; set => EquipeNome = value; }
    public string CodRegiao { get; set; } = string.Empty;
    public string CodServ { get; set; } = string.Empty;
    public double LatitudeLocalPartida { get; set; }
    public double LongitudeLocalPartida { get; set; }
    public double? LatitudeLocalRetorno { get; set; }
    public double? LongitudeLocalRetorno { get; set; }
    public double? LatitudeLocalRefeicao { get; set; }
    public double? LongitudeLocalRefeicao { get; set; }
    public int TempoRefeicao { get; set; }
    public int NPessoas { get; set; }
    /// <summary>Indica se a equipe retorna à garagem ao fim do expediente, com base em <see cref="RetornaBaseFimExpediente"/>.</summary>
    public bool RetornaGaragem { get => RetornaBaseFimExpediente == true; }

    /// <summary>Indica se a equipe retorna à garagem para a refeição, com base em <see cref="RetornaAlmocoBase"/>.</summary>
    public bool RetornaGaragemAlmoco { get => RetornaAlmocoBase == true; }
    public double TravelDurationMultiple { get; set; } = 1.0;
}
