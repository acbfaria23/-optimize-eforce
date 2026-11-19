using Energisa.Eforce.Otimizador.Configuration;

namespace Energisa.Eforce.Otimizador.Tests.Configuration;

public class GcsSettingsTests
{
    [Fact]
    public void SectionName_DeveSerGoogleCloudStorage()
    {
        Assert.Equal("GoogleCloudStorage", GcsSettings.SectionName);
    }

    [Fact]
    public void Default_BucketName_Null()
    {
        var settings = new GcsSettings();

        Assert.Null(settings.BucketName);
    }

    [Fact]
    public void Default_ServiceAccountCredential_Null()
    {
        var settings = new GcsSettings();

        Assert.Null(settings.ServiceAccountCredential);
        Assert.Null(settings.GetCredentialJson());
    }

    [Fact]
    public void SetProperties_ValoresAtribuidos()
    {
        var settings = new GcsSettings
        {
            BucketName = "meu-bucket",
            ServiceAccountCredential = new Dictionary<string, object>
            {
                ["type"] = "service_account",
                ["project_id"] = "my-project"
            }
        };

        Assert.Equal("meu-bucket", settings.BucketName);
        Assert.NotNull(settings.GetCredentialJson());
        Assert.Contains("service_account", settings.GetCredentialJson());
    }

    [Fact]
    public void GetCredentialJson_RetornaJsonComTodasAsChaves()
    {
        var settings = new GcsSettings
        {
            ServiceAccountCredential = new Dictionary<string, object>
            {
                ["type"] = "service_account",
                ["project_id"] = "my-project",
                ["client_email"] = "test@my-project.iam.gserviceaccount.com"
            }
        };

        var json = settings.GetCredentialJson();
        Assert.NotNull(json);
        Assert.Contains("service_account", json);
        Assert.Contains("my-project", json);
        Assert.Contains("test@my-project.iam.gserviceaccount.com", json);
    }

    [Fact]
    public void GetCredentialJson_RetornaNullQuandoDicionarioVazio()
    {
        var settings = new GcsSettings
        {
            ServiceAccountCredential = new Dictionary<string, object>()
        };

        Assert.Null(settings.GetCredentialJson());
    }
}
