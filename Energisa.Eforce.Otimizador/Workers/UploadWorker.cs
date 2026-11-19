using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Services;


namespace Energisa.Eforce.Otimizador.Workers;

public class UploadWorker : BackgroundService
{
    private readonly GoogleCloudStorageService _storageService;
    private readonly GcsSettings _settings;
    private readonly OtimizacaoSettings _otimizacaoSettings;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<UploadWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public UploadWorker(
        GoogleCloudStorageService storageService,
        IOptions<GcsSettings> settings,
        IOptions<OtimizacaoSettings> otimizacaoSettings,
        IHostEnvironment hostEnvironment,
        ILogger<UploadWorker> logger,
        IHostApplicationLifetime lifetime)
    {
        _storageService = storageService;
        _settings = settings.Value;
        _otimizacaoSettings = otimizacaoSettings.Value;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UploadWorker iniciado.");

        try
        {
            var scenarioPath = Path.Combine(AppContext.BaseDirectory, "scenario.json");

            if (!File.Exists(scenarioPath))
            {
                _logger.LogWarning("Arquivo scenario.json não encontrado em '{Path}'.", scenarioPath);
                return;
            }

            var json = await File.ReadAllTextAsync(scenarioPath, stoppingToken);
            var prefixo = ObterPrefixoArquivo();
            var objectName = $"input/{prefixo}{Guid.NewGuid()}.json";

            await _storageService.UploadJsonAsync(
                _settings.BucketName,
                objectName,
                json,
                stoppingToken);

            _logger.LogInformation("Upload concluído com sucesso. Objeto: '{ObjectName}'.", objectName);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("UploadWorker cancelado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar o upload.");
        }
        finally
        {
            _logger.LogInformation("UploadWorker finalizado. Encerrando aplicação.");
            _lifetime.StopApplication();
        }
    }

    private string ObterPrefixoArquivo()
    {
        if (_otimizacaoSettings.SqlDebug is { Habilitado: true })
            return "esp_";

        return _hostEnvironment.EnvironmentName switch
        {
            "Development" => "dev_",
            "Staging" => "hml_",
            "Production" => "prd_",
            _ => "dev_"
        };
    }
}
