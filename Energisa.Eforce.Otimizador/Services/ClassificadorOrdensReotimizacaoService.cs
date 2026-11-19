using Energisa.Eforce.Otimizador.Models.Enums;
using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Microsoft.Extensions.Logging;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Ordem de serviço com sua situação semântica atual, utilizada como entrada
/// para o <see cref="ClassificadorOrdensReotimizacaoService"/> (§7).
/// </summary>
public class OsSituacaoAtual
{
    /// <summary>Label/identificador exato da OS (correlação exata, nunca por prefixo, §4).</summary>
    public string LabelOs { get; set; } = string.Empty;

    /// <summary>Situação semântica atual da OS no domínio E-force.</summary>
    public Models.Enums.StatusOs? Status { get; set; }

    /// <summary>Label do veículo/equipe atualmente ativo para esta OS, se houver.</summary>
    public string? VehicleLabelAtivo { get; set; }

    /// <summary>Indica se a OS existia nos shipments do request original.</summary>
    public bool ExistiaNoRequestOriginal { get; set; }

    /// <summary>Horário real de início da execução, quando disponível (§8).</summary>
    public DateTime? InicioRealExecucaoUtc { get; set; }

    /// <summary>TME total (segundos) planejado para a OS.</summary>
    public int TmeTotalSegundos { get; set; }
}

/// <summary>
/// Serviço responsável por classificar cada OS do cenário em exatamente uma categoria
/// semântica (§7), aplicando a precedência: Execução ativa, Deslocamento, Despachada
/// pendente, Original não despachada, Nova pendente, Removida.
/// Também calcula o TME restante das OSs em execução (§8).
/// Serviço puro (sem acesso a banco), pode ser registrado como Singleton.
/// </summary>
public class ClassificadorOrdensReotimizacaoService
{
    private readonly ILogger<ClassificadorOrdensReotimizacaoService> _logger;

    public ClassificadorOrdensReotimizacaoService(ILogger<ClassificadorOrdensReotimizacaoService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Classifica uma lista de OSs conforme sua situação semântica atual.
    /// Lança exceção caso uma OS tenha situação desconhecida (não deve ser removida
    /// silenciosamente) ou caso haja mais de uma OS em execução para a mesma equipe.
    /// </summary>
    public Dictionary<string, EstadoOsClassificada> Classificar(
        IEnumerable<OsSituacaoAtual> ordens,
        DateTime dataCorteUtc)
    {
        var resultado = new Dictionary<string, EstadoOsClassificada>();
        var execucaoAtivaPorEquipe = new Dictionary<string, string>();
        var contadoresEstado = new Dictionary<EstadoOsReotimizacao, int>();

        _logger.LogInformation(
            "Iniciando classificação de ordens para reotimização. DataCorte: {DataCorte:u}.",
            dataCorteUtc);

        foreach (var os in ordens)
        {
            var estado = MapearEstado(os);

            if (estado == EstadoOsReotimizacao.ExecucaoAtiva)
            {
                if (os.VehicleLabelAtivo is null)
                {
                    throw new InvalidOperationException(
                        $"OS '{os.LabelOs}' está em execução ativa, mas não possui equipe associada. Snapshot inconsistente.");
                }

                if (execucaoAtivaPorEquipe.TryGetValue(os.VehicleLabelAtivo, out var existente))
                {
                    throw new InvalidOperationException(
                        $"Equipe '{os.VehicleLabelAtivo}' possui mais de uma OS em execução ativa ('{existente}' e '{os.LabelOs}'). Snapshot rejeitado por inconsistência (§8).");
                }

                execucaoAtivaPorEquipe[os.VehicleLabelAtivo] = os.LabelOs;

                _logger.LogInformation(
                    "OS em execução ativa classificada. LabelOs: {LabelOs}. Equipe: {Equipe}.",
                    os.LabelOs, os.VehicleLabelAtivo);
            }

            if (resultado.ContainsKey(os.LabelOs))
            {
                throw new InvalidOperationException(
                    $"OS '{os.LabelOs}' foi classificada mais de uma vez. Conflito de categoria/equipe (§7).");
            }

            // Contar estado
            if (!contadoresEstado.ContainsKey(estado))
                contadoresEstado[estado] = 0;
            contadoresEstado[estado]++;

            resultado[os.LabelOs] = new EstadoOsClassificada
            {
                LabelOs = os.LabelOs,
                Estado = estado,
                VehicleLabelAtual = os.VehicleLabelAtivo
            };
        }

        // Logar resumo de classificação por estado
        var resumoEstados = string.Join(", ", contadoresEstado.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {kv.Value}"));
        _logger.LogInformation(
            "Classificação de ordens concluída. Total: {Total}. Resumo por Estado: {ResumoEstados}.",
            resultado.Count, resumoEstados);

        return resultado;
    }

    /// <summary>
    /// Calcula o TME restante de uma OS em execução (§8):
    /// TME restante = TME total - tempo transcorrido desde o início real da execução.
    /// Nunca negativo. Se o horário real de início não estiver disponível, utiliza o
    /// TME integral como fallback conservador e registra warning.
    /// </summary>
    public (int TmeRestanteSegundos, bool UsouFallback) CalcularTmeRestante(
        OsSituacaoAtual osEmExecucao,
        DateTime dataCorteUtc)
    {
        if (osEmExecucao.InicioRealExecucaoUtc is null)
        {
            _logger.LogWarning(
                "OS {LabelOs} em execução sem horário real de início disponível. Utilizando TME integral como fallback conservador.",
                osEmExecucao.LabelOs);
            return (osEmExecucao.TmeTotalSegundos, true);
        }

        var decorrido = (dataCorteUtc - osEmExecucao.InicioRealExecucaoUtc.Value).TotalSeconds;
        var restante = osEmExecucao.TmeTotalSegundos - decorrido;

        return ((int)Math.Max(0, restante), false);
    }

    /// <summary>
    /// Aplica o mapeamento explícito de situação semântica para categoria de reotimização.
    /// Rejeita (lança exceção) situações desconhecidas, ao invés de removê-las silenciosamente.
    /// </summary>
    private static EstadoOsReotimizacao MapearEstado(OsSituacaoAtual os)
    {
        // OS nova: não existia no request original e ainda não foi despachada.
        if (!os.ExistiaNoRequestOriginal && os.Status is null)
        {
            return EstadoOsReotimizacao.NovaPendente;
        }

        return os.Status switch
        {
            null => os.ExistiaNoRequestOriginal
                ? EstadoOsReotimizacao.OriginalNaoDespachada
                : EstadoOsReotimizacao.NovaPendente,

            Models.Enums.StatusOs.Deslocamento => EstadoOsReotimizacao.Deslocamento,

            // Chegada, Localização e Execução representam serviço já iniciado no local (§7, §8).
            Models.Enums.StatusOs.Chegada => EstadoOsReotimizacao.ExecucaoAtiva,
            Models.Enums.StatusOs.Localizacao => EstadoOsReotimizacao.ExecucaoAtiva,
            Models.Enums.StatusOs.Execucao => EstadoOsReotimizacao.ExecucaoAtiva,

            // Enviada/reconhecida/lida/interrompida ou despachada após a otimização original:
            // mantida como shipment, fixada na equipe atual.
            Models.Enums.StatusOs.Despachado => EstadoOsReotimizacao.DespachadaPendente,
            Models.Enums.StatusOs.Reconhecido => EstadoOsReotimizacao.DespachadaPendente,
            Models.Enums.StatusOs.Lido => EstadoOsReotimizacao.DespachadaPendente,
            Models.Enums.StatusOs.Interrompida => EstadoOsReotimizacao.DespachadaPendente,
            Models.Enums.StatusOs.DespachoAutomatico => EstadoOsReotimizacao.DespachadaPendente,

            // Finalização e Encerramento representam serviço concluído: excluir do modelo.
            Models.Enums.StatusOs.Finalizacao => EstadoOsReotimizacao.Removida,
            Models.Enums.StatusOs.Encerramento => EstadoOsReotimizacao.Removida,
            Models.Enums.StatusOs.Retirada => EstadoOsReotimizacao.Removida,
            Models.Enums.StatusOs.Redirecionada => EstadoOsReotimizacao.Removida,

            _ => throw new InvalidOperationException(
                $"OS '{os.LabelOs}' possui situação desconhecida ('{os.Status}') para reotimização. Snapshot rejeitado (§7).")
        };
    }
}
