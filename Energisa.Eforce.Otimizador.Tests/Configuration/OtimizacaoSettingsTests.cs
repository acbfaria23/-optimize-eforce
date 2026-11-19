using Energisa.Eforce.Otimizador.Configuration;

namespace Energisa.Eforce.Otimizador.Tests.Configuration;

public class OtimizacaoSettingsTests
{
    [Fact]
    public void SectionName_DeveSerOtimizacao()
    {
        Assert.Equal("Otimizacao", OtimizacaoSettings.SectionName);
    }

    [Fact]
    public void SetProperties_ValoresAtribuidos()
    {
        var settings = new OtimizacaoSettings
        {
            ProjectId = "meu-projeto",
            UsarRest = true,
            Label = "cenario-teste",
            ModoAsync = true,
            DebugJsonFile = true,
            EmpresaId = "EMP001"
        };

        Assert.Equal("meu-projeto", settings.ProjectId);
        Assert.True(settings.UsarRest);
        Assert.Equal("cenario-teste", settings.Label);
        Assert.True(settings.ModoAsync);
        Assert.True(settings.DebugJsonFile);
        Assert.Equal("EMP001", settings.EmpresaId);
    }

    [Fact]
    public void Default_BoolProperties_False()
    {
        var settings = new OtimizacaoSettings();

        Assert.False(settings.UsarRest);
        Assert.False(settings.ModoAsync);
        Assert.False(settings.DebugJsonFile);
    }
}
