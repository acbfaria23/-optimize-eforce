namespace Energisa.Eforce.Otimizador.Models.Enums;

/// <summary>
/// Categoria semântica de uma OS no contexto da reotimização intradiária (§7).
/// Cada OS deve pertencer a exatamente uma categoria, respeitando a seguinte
/// ordem de precedência: ExecucaoAtiva, Deslocamento, DespachadaPendente,
/// OriginalNaoDespachada, NovaPendente, Removida.
/// </summary>
public enum EstadoOsReotimizacao
{
    /// <summary>OS em execução: removida dos shipments, tempo restante reservado no veículo.</summary>
    ExecucaoAtiva = 0,

    /// <summary>OS em deslocamento: mantida como shipment, fixada como primeira visita pendente da equipe.</summary>
    Deslocamento = 1,

    /// <summary>OS enviada/reconhecida/lida/interrompida ou despachada após a otimização original: fixada na equipe atual.</summary>
    DespachadaPendente = 2,

    /// <summary>OS original ainda pendente e não despachada: pode ser redistribuída entre equipes compatíveis.</summary>
    OriginalNaoDespachada = 3,

    /// <summary>Nova OS pendente (não presente no request original): pode ser distribuída entre equipes compatíveis.</summary>
    NovaPendente = 4,

    /// <summary>OS concluída, cancelada, retirada ou redirecionada: excluída do modelo, não publicada.</summary>
    Removida = 5
}
