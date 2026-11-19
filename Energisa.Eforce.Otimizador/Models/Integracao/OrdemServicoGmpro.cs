namespace Energisa.Eforce.Otimizador.Models.Eforce;

/// <summary>
/// Ordem de Serviço do sistema E-force (§2.1).
/// </summary>
public class OrdemServicoGmpro
{
    /// <summary>Identificador único da OS.</summary>
    public string NumeroOs { get; set; } = string.Empty;

    /// <summary>Número do CDC do lote (para TIPO_OS = 'X').</summary>
    public string? NumcdcLote { get; set; }

    /// <summary>Código do serviço (chave para perfil de equipe).</summary>
    public int Codserv { get; set; }

    /// <summary>Latitude da OS (negativa no Brasil).</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude da OS (negativa no Brasil).</summary>
    public double? Longitude { get; set; }

    /// <summary>Data/hora limite para execução.</summary>
    public DateTime DataVencimento { get; set; }

    /// <summary>Data/hora em que a OS foi criada.</summary>
    public DateTime DataSolicitacao { get; set; }

    /// <summary>Código da região de despacho.</summary>
    public string? RegiaoDespacho { get; set; }

    /// <summary>Tipo da OS: C=Comercial, T=Técnica, X=Lote, P=Projeto.</summary>
    public string TipoOs { get; set; } = string.Empty;

    /// <summary>Indicador de risco à vida: S / N.</summary>
    public string RiscoVida { get; set; } = "N";

    /// <summary>Indicador de OS regulada (ANEEL): S / N.</summary>
    public string OsRegulada { get; set; } = "N";

    /// <summary>Indicador de reincidência: S / N.</summary>
    public string Reicidencia { get; set; } = "N";

    /// <summary>Número de pessoas afetadas (usado para OS Técnica).</summary>
    public int PessoasAtingidas { get; set; }

    /// <summary>Valor base de compensação (T0).</summary>
    public double Compensacao { get; set; }

    /// <summary>Valor de compensação por hora de atraso.</summary>
    public double CompensacaoPorHora { get; set; }

    /// <summary>Nível de prioridade: URGENTE, ALTA, NORMAL.</summary>
    public string Prioridade { get; set; } = "NORMAL";

    /// <summary>Tempo médio de execução em minutos.</summary>
    public double? MetaTme { get; set; }

    /// <summary>Localização: U=Urbano, R=Rural.</summary>
    public string Localizacao { get; set; } = "U";

    /// <summary>Indicador de cliente essencial: 1=Sim, 0=Não.</summary>
    public int ClienteEssencial { get; set; }

    /// <summary>Tipo de corte (ex: "3 - Corte no Poste").</summary>
    public string? TipoCorte { get; set; }

    /// <summary>Descrição do tipo de corte (usado em lotes para filtro por NPessoas).</summary>
    public string? DescricaoTipoCorte { get; set; }

    /// <summary>Número da OS no sistema Energisa.</summary>
    public string? NumeroOsEnergisa { get; set; }

    public DateTime? DataVencimentoReal { get; set; }
}
