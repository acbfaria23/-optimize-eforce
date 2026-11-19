namespace Energisa.Eforce.Otimizador.Configuration;

/// <summary>
/// Configurações de integração com a API EforceGeoAmbiente.
/// </summary>
public class EforceGeoAmbienteSettings
{
    public const string SectionName = "EforceGeoAmbiente";

    /// <summary>
    /// URL base da API EforceGeoAmbiente (ex: https://eforce-api-dev-xxx.us-central1.run.app).
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
}
