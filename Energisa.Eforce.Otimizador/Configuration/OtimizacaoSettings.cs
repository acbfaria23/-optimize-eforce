namespace Energisa.Eforce.Otimizador.Configuration;

/// <summary>
/// Configurações do pipeline de otimização de rotas.
/// </summary>
public class OtimizacaoSettings
{
    public const string SectionName = "Otimizacao";

    /// <summary>
    /// ID do projeto no Google Cloud (usado na API Route Optimization).
    /// </summary>
    public string ProjectId { get; set; }

    /// <summary>
    /// Se verdadeiro, utiliza protocolo REST (HTTP/1.1) em vez de gRPC
    /// para comunicação com a API. Recomendado quando há bloqueios de firewall/proxy.
    /// </summary>
    public bool UsarRest { get; set; }

    /// <summary>
    /// Label de identificação do cenário para rastreabilidade.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Se verdadeiro, opera em modo assíncrono (fire-and-forget): dispara a requisição
    /// para a API do Google e encerra sem aguardar o resultado.
    /// Se falso, aguarda a conclusão da otimização e executa o pós-processamento.
    /// </summary>
    public bool ModoAsync { get; set; }

    /// <summary>
    /// Se verdadeiro, grava os JSONs de request/response em disco para debug.
    /// </summary>
    public bool DebugJsonFile { get; set; }

    /// <summary>
    /// Identificador externo da empresa (IdExterno) usado para filtrar dados no NoSQL.
    /// </summary>
    public string EmpresaId { get; set; }

    /// <summary>
    /// Configurações de debug para origem de dados via SQL (Oracle).
    /// Quando habilitado, substitui a busca no MongoDB por queries SQL diretas.
    /// </summary>
    public SqlDebugSettings? SqlDebug { get; set; }
}

/// <summary>
/// Configurações para carregamento alternativo de dados via SQL (somente debug).
/// </summary>
public class SqlDebugSettings
{
    /// <summary>
    /// Se verdadeiro, utiliza queries SQL como origem de dados ao invés do MongoDB.
    /// </summary>
    public bool Habilitado { get; set; }

    /// <summary>
    /// Caminho do arquivo .sql contendo a query de ordens de serviço.
    /// </summary>
    public string CaminhoSqlOrdensServico { get; set; } = string.Empty;

    /// <summary>
    /// Caminho do arquivo .sql contendo a query de equipes.
    /// </summary>
    public string CaminhoSqlEquipes { get; set; } = string.Empty;
}
