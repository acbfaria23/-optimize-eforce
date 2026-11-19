using Google.Api.Gax.Grpc;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Maps.RouteOptimization.V1;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Provedor de serviços para interação com a API do Google Cloud Route Optimization.
/// </summary>
public class GmProApiClient : IAsyncDisposable
{
    private readonly RouteOptimizationClient _client;
    private readonly string _projectId;

    /// <summary>
    /// Instancia o cliente usando Application Default Credentials (ADC)
    /// ou um arquivo de credenciais explícito.
    /// </summary>
    public static async Task<GmProApiClient> CriarAsync(string projectId, string? credentialJson = null)
    {
        var builder = new RouteOptimizationClientBuilder();

        if (!string.IsNullOrWhiteSpace(credentialJson))
        {
            builder.JsonCredentials = credentialJson;
        }

        var client = await builder.BuildAsync();
        return new GmProApiClient(client, projectId);
    }

    /// <summary>
    /// Instancia o cliente usando o adaptador REST (HTTP/1.1), 
    /// recomendado caso o ambiente de rede bloqueie gRPC/HTTP2.
    /// </summary>
    public static async Task<GmProApiClient> CriarComRestAsync(string projectId, string? credentialJson = null)
    {
        var builder = new RouteOptimizationClientBuilder
        {
            GrpcAdapter = Google.Api.Gax.Grpc.Rest.RestGrpcAdapter.Default
        };

        if (!string.IsNullOrWhiteSpace(credentialJson))
        {
            builder.JsonCredentials = credentialJson;
        }

        var client = await builder.BuildAsync();
        return new GmProApiClient(client, projectId);
    }

    /// <summary>Timeout para chamadas síncronas à API (OptimizeTours).</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Timeout para o disparo inicial de uma LRO (OptimizeToursUri).</summary>
    private static readonly TimeSpan LroDispatchTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Configuração de polling para LROs: intervalo entre checks e timeout total.</summary>
    private static readonly PollSettings LroPollSettings = new(
        expiration: Expiration.FromTimeout(TimeSpan.FromMinutes(60)),
        delay: TimeSpan.FromSeconds(60));

    private GmProApiClient(RouteOptimizationClient client, string projectId)
    {
        _client    = client;
        _projectId = projectId;
    }

    private static CallSettings CreateCallSettings(TimeSpan timeout, CancellationToken cancellationToken) =>
        CallSettings.FromExpiration(Expiration.FromTimeout(timeout))
            .WithCancellationToken(cancellationToken);

    /// <summary>
    /// Envia uma requisição de otimização de rotas para o Google Cloud.
    /// </summary>
    public async Task<OptimizeToursResponse> OtimizarAsync(
        OptimizeToursRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Parent = $"projects/{_projectId}";

        try
        {
            return await _client.OptimizeToursAsync(
                request,
                CreateCallSettings(RequestTimeout, cancellationToken));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Falha na chamada à API de Otimização: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Envia uma requisição de otimização de rotas via URI (GCS) para o Google Cloud.
    /// Aguarda a conclusão da operação de longa duração (LRO).
    /// </summary>
    public async Task<OptimizeToursUriResponse> OtimizarPorUriAsync(
        OptimizeToursUriRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Parent = $"projects/{_projectId}";

        try
        {
            var operation = await _client.OptimizeToursUriAsync(
                request,
                CreateCallSettings(LroDispatchTimeout, cancellationToken));

            var completed = await operation.PollUntilCompletedAsync(
                pollSettings: LroPollSettings,
                callSettings: CreateCallSettings(LroDispatchTimeout, cancellationToken));

            return completed.Result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Falha na chamada à API de Otimização por URI: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Dispara uma requisição de otimização de rotas via URI (GCS) para o Google Cloud
    /// sem aguardar a conclusão (fire-and-forget). Retorna o nome da operação LRO
    /// criada no servidor, que pode ser usado para consultar o status posteriormente.
    /// Retorna null em caso de falha.
    /// </summary>
    public async Task<string?> DispararOtimizacaoPorUriAsync(
        OptimizeToursUriRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Parent = $"projects/{_projectId}";

        try
        {
            var operation = await _client.OptimizeToursUriAsync(
                request,
                CreateCallSettings(LroDispatchTimeout, cancellationToken));

            return operation.Name;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Falha ao disparar otimização por URI (fire-and-forget): {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
