using Google.Maps.RouteOptimization.V1;

namespace Energisa.Eforce.Otimizador.Models.Reotimizacao;

/// <summary>
/// Comando de entrada da API para solicitar uma reotimização intradiária (§3).
/// Não deve conter dados que o backend é responsável por recuperar
/// (request/response originais, posições, status de OSs, cadastro de equipes,
/// data/hora da solicitação, timezone).
/// </summary>
public class SolicitarReotimizacaoCommand
{
    /// <summary>Identificador da primeira otimização do turno.</summary>
    public string OtimizacaoOriginalId { get; set; } = string.Empty;

    /// <summary>Códigos das equipes selecionadas para a reotimização.</summary>
    public List<string> Equipes { get; set; } = [];

    /// <summary>
    /// Chave de idempotência (§3, §16), recebida via header <c>Idempotency-Key</c> e propagada
    /// pelo controller — nunca enviada pelo corpo da requisição. Mesma chave + mesmo payload
    /// retorna a execução existente; mesma chave com payload diferente retorna 409.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Resultado da construção do novo request GMPRO para a reotimização (§9),
/// produzido pelo <c>ReotimizacaoRequestBuilderService</c> de forma pura (sem acesso a banco).
/// </summary>
public class ResultadoConstrucaoReotimizacao
{
    /// <summary>Novo request GMPRO construído a partir do request original.</summary>
    public OptimizeToursRequest NovoRequest { get; set; } = null!;

    /// <summary>Identificador do registro de reotimização persistido no NoSQL (§15, §17).</summary>
    public string ReotimizacaoId { get; set; } = string.Empty;

    /// <summary>Labels de OSs que não puderam ser atendidas por nenhuma equipe compatível (§13).</summary>
    public Dictionary<string, string> OrdensNaoAtendidas { get; set; } = [];

    /// <summary>Labels de OSs mantidas em execução (removidas dos shipments, tempo reservado no veículo).</summary>
    public List<string> OrdensEmExecucao { get; set; } = [];

    /// <summary>Labels de OSs mantidas em deslocamento (fixadas como primeira visita pendente).</summary>
    public List<string> OrdensEmDeslocamento { get; set; } = [];

    /// <summary>Avisos gerados durante a construção (ex: fallback de TME, fallback de refeição).</summary>
    public List<string> Avisos { get; set; } = [];
}
