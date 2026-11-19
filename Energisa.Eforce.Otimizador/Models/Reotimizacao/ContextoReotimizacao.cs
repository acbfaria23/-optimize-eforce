using Energisa.Eforce.Otimizador.Models.Enums;
using Google.Maps.RouteOptimization.V1;

namespace Energisa.Eforce.Otimizador.Models.Reotimizacao;

/// <summary>
/// Snapshot operacional completo capturado pelo <c>ReotimizacaoDataService</c> (§5),
/// contendo todos os dados necessários para classificar as OSs e construir o novo
/// request GMPRO, sem necessidade de novos acessos a banco durante o build.
/// </summary>
public class ContextoReotimizacao
{
    /// <summary>Identificador da otimização original (primeira otimização do turno).</summary>
    public string OtimizacaoOriginalId { get; set; } = string.Empty;

    /// <summary>Request original persistido (base para reconstrução, §2).</summary>
    public OptimizeToursRequest RequestOriginal { get; set; } = null!;

    /// <summary>Response original persistida (base para solução injetada, §14).</summary>
    public OptimizeToursResponse ResponseOriginal { get; set; } = null!;

    /// <summary>Equipes selecionadas para a reotimização, com estado atual (§5, §6).</summary>
    public List<EquipeReotimizacao> EquipesSelecionadas { get; set; } = [];

    /// <summary>Classificação semântica de cada OS presente no cenário (§7). Chave: label do shipment/OS.</summary>
    public Dictionary<string, EstadoOsClassificada> OrdensClassificadas { get; set; } = [];

    /// <summary>Novas OSs pendentes (não presentes nos shipments do request original), já convertidas.</summary>
    public List<Models.Eforce.OrdemServicoGmpro> NovasOrdensPendentes { get; set; } = [];

    /// <summary>Data/hora de corte (UTC) capturada uma única vez pela API no início da solicitação.</summary>
    public DateTime DataCorteUtc { get; set; }

    /// <summary>Fuso horário resolvido da empresa.</summary>
    public TimeZoneInfo FusoHorario { get; set; } = TimeZoneInfo.Utc;

    /// <summary>Chave de idempotência informada no header da requisição (§3, §16).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Versão/hash do snapshot, para auditoria e revalidação na aprovação (§15, §18).</summary>
    public string HashSnapshot { get; set; } = string.Empty;

    /// <summary>Indica se houve fallback de estado operacional de refeição inferido pela programação original (§11).</summary>
    public bool UsouFallbackRefeicao { get; set; }
}

/// <summary>
/// Classificação semântica de uma OS específica dentro do contexto de reotimização (§7).
/// </summary>
public class EstadoOsClassificada
{
    /// <summary>Label/identificador da OS (correlação exata, nunca por prefixo, §4).</summary>
    public string LabelOs { get; set; } = string.Empty;

    /// <summary>Categoria atribuída à OS.</summary>
    public EstadoOsReotimizacao Estado { get; set; }

    /// <summary>Label do veículo/equipe atualmente associado à OS (quando fixada).</summary>
    public string? VehicleLabelAtual { get; set; }

    /// <summary>Motivo pelo qual a OS não pôde ser atendida por nenhuma equipe compatível (§13), se aplicável.</summary>
    public string? MotivoNaoAtendida { get; set; }
}
