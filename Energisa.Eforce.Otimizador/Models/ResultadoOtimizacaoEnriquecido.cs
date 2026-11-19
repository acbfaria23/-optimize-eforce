using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model._OrdemServico_.Enum;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Model.Cliente;
using Energisa.God.Domain.Model.Geral;
using Energisa.God.Domain.Model.Localizacao;
using Energisa.God.Domain.Model.Servico;

namespace Energisa.Eforce.Otimizador.Models;

/// <summary>
/// Resultado de otimização com dados de ordens de serviço enriquecidos.
/// </summary>
public class ResultadoOtimizacaoEnriquecido
{
    public string Id { get; set; } = string.Empty;
    public TipoOtimizacao? TipoOtimizacao { get; set; }
    public DateTime? DataOtimizacao { get; set; }
    public List<RotaEquipeEnriquecida> Equipes { get; set; } = [];
    public string? Solicitante { get; set; }

}

/// <summary>
/// Rota de equipe com ordens de serviço enriquecidas.
/// </summary>
public class RotaEquipeEnriquecida
{
    public string Equipe { get; set; } = string.Empty;
    public string TipoEquipe { get; set; } = string.Empty;
    public string PerfilEquipe { get; set; } = string.Empty;
    public double? LatitudeGaragem { get; set; }
    public double? LongitudeGaragem { get; set; }
    public double CustoKm { get; set; }
    public long MaxLoad { get; set; }
    public List<OrdemServicoAtendidaEnriquecida> OrdensServico { get; set; } = [];
    public string? Polyline { get; set; }
    public bool? Aprovada { get; set; }
    public string? Aprovador { get; set; }
    public DateTimeOffset DataAprovacao { get; set; }
}

/// <summary>
/// Ordem de serviço atendida com dados enriquecidos do repositório NoSQL.
/// </summary>
public class OrdemServicoAtendidaEnriquecida
{
    // Propriedades originais de OrdemServicoAtendida
    public string Id { get; set; } = string.Empty;
    public string TipoOs { get; set; } = string.Empty;
    public string Regulada { get; set; } = string.Empty;
    public string RiscoVida { get; set; } = string.Empty;
    public string RegiaoDespacho { get; set; } = string.Empty;
    public string Codserv { get; set; } = string.Empty;
    public string PesoFinal { get; set; } = string.Empty;
    public DateTime? DataVencimentoAjustada { get; set; }
    public string Prioridade { get; set; } = string.Empty;
    public string PerfilEquipe { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? DuracaoSegundos { get; set; }
    public double? PenaltyCost { get; set; }
    public DateTime? HorarioInicio { get; set; }
    public double? Detour { get; set; }
    public bool IsAlmoco { get; set; }

    // Propriedades enriquecidas
    public string? NumeroOs { get; set; }
    public Situacao? Situacao { get; set; }
    public DateTimeOffset? DataRegistro { get; set; }
    public DateTimeOffset? DataVencimento { get; set; }
    public string? GrupoId { get; set; }
    public List<Indenizacao>? Indenizacoes { get; set; }
    public Bloqueio? Bloqueio { get; set; }
    public CatalogoServico? Servico { get; set; }
    public UnidadeConsumidora? UnidadeConsumidoraOrigem { get; set; }
    public bool? Reincidencia { get; set; }
    public Localizacao? Localizacao { get; set; }
    public Abrangencia? Abrangencia { get; set; }
    public bool? Regulado { get; set; }
    public List<Callback>? Callbacks { get; set; }
    public List<Observacao>? Observacoes { get; set; }
    public List<OrdemServicoReference>? Children { get; set; }
    public RegiaoReference? RegiaoWfm { get; set; }
    public CategoriaPriorizacao? CategoriaPriorizacao { get; set; }
    public OrdemServicoReference? Parent { get; set; }
}
