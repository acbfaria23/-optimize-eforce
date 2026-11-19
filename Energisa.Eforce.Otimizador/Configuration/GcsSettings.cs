using Google.Apis.Auth.OAuth2;
using System.Text.Json;

namespace Energisa.Eforce.Otimizador.Configuration;

public class GcsSettings
{
    public const string SectionName = "GoogleCloudStorage";

    /// <summary>
    /// The name of the Google Cloud Storage bucket.
    /// </summary>
    public string BucketName { get; set; }

    /// <summary>
    /// Objeto contendo as credenciais da service account inline.
    /// Se nulo ou vazio, serão usadas as credenciais padrão da aplicação (GOOGLE_APPLICATION_CREDENTIALS).
    /// </summary>
    public Dictionary<string, object>? ServiceAccountCredential { get; set; }

    /// <summary>
    /// Retorna o JSON serializado das credenciais, ou null se não configurado.
    /// Se o campo <c>private_key</c> estiver codificado em Base64, é decodificado antes da serialização.
    /// </summary>
    public string? GetCredentialJson()
    {
        if (ServiceAccountCredential is not { Count: > 0 })
            return null;

        var credentialDict = new Dictionary<string, object>(ServiceAccountCredential);

        if (credentialDict.TryGetValue("private_key", out var privateKeyRaw))
        {
            string? privateKeyStr = privateKeyRaw switch
            {
                JsonElement je => je.GetString(),
                string s => s,
                _ => privateKeyRaw?.ToString()
            };

            if (!string.IsNullOrEmpty(privateKeyStr) && !privateKeyStr.TrimStart().StartsWith("-----BEGIN"))
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(privateKeyStr));
                credentialDict["private_key"] = decoded;
            }
        }

        return JsonSerializer.Serialize(credentialDict);
    }

    /// <summary>
    /// Retorna um <see cref="GoogleCredential"/> pronto para uso.
    /// Se as credenciais inline estiverem configuradas, usa-as; caso contrário,
    /// recorre às Application Default Credentials (GOOGLE_APPLICATION_CREDENTIALS / Workload Identity).
    /// </summary>
    public GoogleCredential GetGoogleCredential()
    {
        var json = GetCredentialJson();
        return !string.IsNullOrEmpty(json)
            ? GoogleCredential.FromJson(json)
            : GoogleCredential.GetApplicationDefault();
    }
}
