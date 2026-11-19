using Energisa.Eforce.Otimizador.Services;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class ResultDeserializerTests
{
    [Fact]
    public void DeserializeFromJson_JsonVazio_RetornaResponseVazio()
    {
        var json = "{}";

        var result = ResultDeserializer.DeserializeFromJson(json);

        Assert.NotNull(result);
    }

    [Fact]
    public void DeserializeFromJson_ComRoutes_RetornaRoutes()
    {
        var json = """{"routes":[{"vehicleLabel":"EQ1"}]}""";

        var result = ResultDeserializer.DeserializeFromJson(json);

        Assert.NotNull(result);
        Assert.Single(result.Routes);
        Assert.Equal("EQ1", result.Routes[0].VehicleLabel);
    }

    [Fact]
    public void DeserializeFromJson_CamposDesconhecidos_IgnoraESemErro()
    {
        var json = """{"campoDesconhecido":"valor","routes":[]}""";

        var result = ResultDeserializer.DeserializeFromJson(json);

        Assert.NotNull(result);
        Assert.Empty(result.Routes);
    }
}
