using Google.Maps.RouteOptimization.V1;
using Google.Type;
using DateTime = System.DateTime;

namespace Energisa.Eforce.Otimizador.Models.Reotimizacao;

/// <summary>
/// Representa o estado atual de uma equipe selecionada para reotimização (§5, §6).
/// </summary>
public class EquipeReotimizacao
{
    /// <summary>Label do veículo no request original (chave de correlação).</summary>
    public string VehicleLabel { get; set; } = string.Empty;

    /// <summary>Índice do veículo no array original (preservado para AllowedVehicleIndices, §9).</summary>
    public int IndiceVehicleOriginal { get; set; }

    /// <summary>Código da equipe (negócio).</summary>
    public string CodigoEquipe { get; set; } = string.Empty;

    /// <summary>Indica se a equipe está apresentada (possui posição de GPS atual válida).</summary>
    public bool Apresentada { get; set; }

    /// <summary>Posição atual da equipe (GPS, quando apresentada) ou ponto de partida atual (quando não apresentada).</summary>
    public LatLng? Posicao { get; set; }

    /// <summary>Data/hora UTC da coordenada de GPS utilizada (quando apresentada).</summary>
    public DateTime? DataCoordenadaUtc { get; set; }

    /// <summary>Horário em que a equipe estará disponível para novas visitas (§2).</summary>
    public DateTime DisponibilidadeUtc { get; set; }

    /// <summary>Indica se o turno original da equipe já se encerrou (disponibilidade >= fim original do turno).</summary>
    public bool TurnoEncerrado { get; set; }

    /// <summary>Capacidade restante (capacidade original - concluídas - em execução), nunca negativa (§10).</summary>
    public int CapacidadeRestante { get; set; }

    /// <summary>Situação atual da refeição da equipe (§11).</summary>
    public SituacaoRefeicaoReotimizacao SituacaoRefeicao { get; set; } = SituacaoRefeicaoReotimizacao.Futura;

    /// <summary>Perfil operacional atual da equipe.</summary>
    public string? PerfilAtual { get; set; }

    /// <summary>Regiões atuais de atuação da equipe.</summary>
    public List<string> RegioesAtuais { get; set; } = [];

    /// <summary>Atuação Urbana/Rural atual.</summary>
    public string? AtuacaoAtual { get; set; }

    /// <summary>Quantidade/composição atual de colaboradores.</summary>
    public int ColaboradoresAtuais { get; set; }

    /// <summary>Label da OS atualmente em execução por esta equipe, se houver.</summary>
    public string? OsEmExecucaoLabel { get; set; }

    /// <summary>TME restante (em segundos) da OS em execução, calculado conforme §8.</summary>
    public int? TmeRestanteSegundos { get; set; }

    /// <summary>Indica se foi necessário aplicar o fallback conservador de TME integral (§8).</summary>
    public bool UsouFallbackTme { get; set; }

    /// <summary>Label da OS atualmente em deslocamento por esta equipe, se houver.</summary>
    public string? OsEmDeslocamentoLabel { get; set; }
}

/// <summary>
/// Situação operacional da refeição de uma equipe no momento da reotimização (§11).
/// </summary>
public enum SituacaoRefeicaoReotimizacao
{
    /// <summary>Refeição já concluída: a regra/shipment correspondente deve ser removida.</summary>
    Concluida = 0,

    /// <summary>Refeição em andamento: a disponibilidade da equipe inicia no fim da refeição.</summary>
    EmAndamento = 1,

    /// <summary>Refeição futura: a regra original deve ser preservada.</summary>
    Futura = 2,

    /// <summary>Equipe não possui refeição programada.</summary>
    NaoAplicavel = 3
}
