using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using Energisa.Eforce.Otimizador.Configuration;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Cliente HTTP responsável pela integração com a API EforceGeoAmbiente hospedada no Google Cloud Run.
/// Autentica as requisições com um token OIDC obtido via credenciais da service account.
/// </summary>
public class EforceGeoAmbienteApiClient(
    HttpClient httpClient,
    IOptions<EforceGeoAmbienteSettings> eforceGeoAmbienteSettings,
    IOptions<GcsSettings> gcsSettings,
    ILogger<EforceGeoAmbienteApiClient> logger)
{
    private const string StartOperationTrackingPath =
        "/api/start-operation-tracking";

    private readonly HttpClient _httpClient = httpClient;
    private readonly EforceGeoAmbienteSettings _settings =
        eforceGeoAmbienteSettings.Value;
    private readonly GcsSettings _gcsSettings = gcsSettings.Value;
    private readonly ILogger<EforceGeoAmbienteApiClient> _logger = logger;

    /// <summary>
    /// Notifica a API EforceGeoAmbiente para iniciar o rastreamento da operação assíncrona.
    /// Retorna normalmente somente após receber uma resposta HTTP de sucesso.
    /// Falhas são registradas e propagadas ao pipeline.
    /// </summary>
    /// <param name="operationName">Nome da operação LRO retornado pelo Google Cloud.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task NotificarInicioOperacaoAsync(
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var stopwatchTotal = Stopwatch.StartNew();
        var stopwatchEtapa = Stopwatch.StartNew();
        var etapaAtual = "ValidacaoConfiguracao";
        var statusRastreamento = "NaoIniciado";
        int? statusCode = null;

        try
        {
            _logger.LogInformation(
                "Iniciando notificação de rastreamento. OperationName: {OperationName}. " +
                "Endpoint: {Endpoint}.",
                operationName,
                StartOperationTrackingPath);

            if (string.IsNullOrWhiteSpace(_settings.ApiBaseUrl))
            {
                statusRastreamento = "ConfiguracaoAusente";

                throw new InvalidOperationException(
                    "EforceGeoAmbienteSettings.ApiBaseUrl não está configurado.");
            }

            var baseUrl = _settings.ApiBaseUrl.TrimEnd('/');
            var url = baseUrl + StartOperationTrackingPath;

            etapaAtual = "AutenticacaoOidc";
            statusRastreamento = "Autenticando";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando obtenção do token OIDC para rastreamento. " +
                "OperationName: {OperationName}.",
                operationName);

            var jwtToken = await ObterTokenOidcAsync(
                baseUrl,
                cancellationToken);

            _logger.LogInformation(
                "Token OIDC obtido para rastreamento. " +
                "OperationName: {OperationName}. " +
                "DuracaoEtapaMs: {DuracaoEtapaMs}.",
                operationName,
                stopwatchEtapa.ElapsedMilliseconds);

            etapaAtual = "PreparacaoRequisicao";
            statusRastreamento = "PreparandoEnvio";
            stopwatchEtapa.Restart();

            var payload = new { operationName };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", jwtToken);

            etapaAtual = "EnvioHttp";
            statusRastreamento = "RespostaNaoConfirmada";
            stopwatchEtapa.Restart();

            _logger.LogInformation(
                "Iniciando POST para rastreamento. " +
                "OperationName: {OperationName}. Endpoint: {Endpoint}. " +
                "TimeoutHttpMs: {TimeoutHttpMs}.",
                operationName,
                StartOperationTrackingPath,
                _httpClient.Timeout.TotalMilliseconds);

            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            statusCode = (int)response.StatusCode;
            etapaAtual = "ValidacaoRespostaHttp";

            if (!response.IsSuccessStatusCode)
            {
                statusRastreamento = "Recusado";

                // Não registrar corpo da resposta, token ou cabeçalhos de autenticação.
                throw new HttpRequestException(
                    "A API de rastreamento recusou o início da operação.",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            statusRastreamento = "Confirmado";

            _logger.LogInformation(
                "API aceitou a solicitação de rastreamento. " +
                "OperationName: {OperationName}. StatusCode: {StatusCode}. " +
                "StatusRastreamento: {StatusRastreamento}. " +
                "DuracaoHttpMs: {DuracaoHttpMs}. " +
                "DuracaoTotalMs: {DuracaoTotalMs}. " +
                "O aceite HTTP não confirma a conclusão da otimização " +
                "nem a execução futura do callback.",
                operationName,
                statusCode,
                statusRastreamento,
                stopwatchEtapa.ElapsedMilliseconds,
                stopwatchTotal.ElapsedMilliseconds);
        }
        catch (OperationCanceledException ex)
        {
            var cancelamentoSolicitado =
                cancellationToken.IsCancellationRequested;

            var motivo = cancelamentoSolicitado
                ? "CancelamentoSolicitado"
                : ex.InnerException is TimeoutException
                    ? "Timeout"
                    : "CancelamentoNaoSolicitado";

            _logger.Log(
                cancelamentoSolicitado
                    ? LogLevel.Warning
                    : LogLevel.Error,
                ex,
                "Notificação de rastreamento interrompida. " +
                "OperationName: {OperationName}. Etapa: {Etapa}. " +
                "Motivo: {Motivo}. StatusRastreamento: {StatusRastreamento}. " +
                "StatusCode: {StatusCode}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                "DuracaoTotalMs: {DuracaoTotalMs}. " +
                "Se o POST já tiver sido enviado, a ausência de confirmação " +
                "não prova que a API deixou de recebê-lo.",
                operationName,
                etapaAtual,
                motivo,
                statusRastreamento,
                statusCode,
                stopwatchEtapa.ElapsedMilliseconds,
                stopwatchTotal.ElapsedMilliseconds);

            throw;
        }
        catch (HttpRequestException ex)
        {
            var motivo = statusRastreamento == "Recusado"
                ? "RespostaHttpSemSucesso"
                : etapaAtual == "AutenticacaoOidc"
                    ? "FalhaHttpNaAutenticacao"
                    : "FalhaTransporteHttp";

            _logger.LogError(
                ex,
                "Falha HTTP na notificação de rastreamento. " +
                "OperationName: {OperationName}. Etapa: {Etapa}. " +
                "Motivo: {Motivo}. StatusRastreamento: {StatusRastreamento}. " +
                "StatusCode: {StatusCode}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                "DuracaoTotalMs: {DuracaoTotalMs}.",
                operationName,
                etapaAtual,
                motivo,
                statusRastreamento,
                statusCode ??
                    (ex.StatusCode.HasValue
                        ? (int)ex.StatusCode.Value
                        : (int?)null),
                stopwatchEtapa.ElapsedMilliseconds,
                stopwatchTotal.ElapsedMilliseconds);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha na notificação de rastreamento. " +
                "OperationName: {OperationName}. Etapa: {Etapa}. " +
                "StatusRastreamento: {StatusRastreamento}. " +
                "StatusCode: {StatusCode}. DuracaoEtapaMs: {DuracaoEtapaMs}. " +
                "DuracaoTotalMs: {DuracaoTotalMs}.",
                operationName,
                etapaAtual,
                statusRastreamento,
                statusCode,
                stopwatchEtapa.ElapsedMilliseconds,
                stopwatchTotal.ElapsedMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// Obtém um token OIDC assinado pela service account configurada em
    /// <see cref="GcsSettings"/>.
    /// O audience deve ser a URL base do Cloud Run sem o path.
    /// </summary>
    private async Task<string> ObterTokenOidcAsync(
        string audience,
        CancellationToken cancellationToken)
    {
        var credential = _gcsSettings.GetGoogleCredential();
        var tokenOptions =
            OidcTokenOptions.FromTargetAudience(audience);
        var oidcToken = await credential.GetOidcTokenAsync(
            tokenOptions,
            cancellationToken);

        return await oidcToken.GetAccessTokenAsync(cancellationToken);
    }
}