using Energisa.Eforce.Otimizador.Helpers;

namespace Energisa.Eforce.Otimizador.Tests.Helpers;

public class ConversaoHelperTests
{
    // ?? FormatToGoogleDate ??????????????????????????????????????????????????

    [Fact]
    public void FormatToGoogleDate_ComDataValida_RetornaFormatoIso8601()
    {
        var dt = new DateTime(2025, 3, 15, 14, 30, 0, DateTimeKind.Utc);

        var result = ConversaoHelper.FormatToGoogleDate(dt);

        Assert.Equal("2025-03-15T14:30:00Z", result);
    }

    [Fact]
    public void FormatToGoogleDate_ComNull_RetornaNull()
    {
        var result = ConversaoHelper.FormatToGoogleDate(null);

        Assert.Null(result);
    }

    [Fact]
    public void FormatToGoogleDate_ComDataLocal_ConverteParaUtc()
    {
        var dt = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = ConversaoHelper.FormatToGoogleDate(dt);

        Assert.NotNull(result);
        Assert.EndsWith("Z", result);
    }

    // ?? Haversine ???????????????????????????????????????????????????????????

    [Fact]
    public void Haversine_MesmoPonto_RetornaZero()
    {
        var result = ConversaoHelper.Haversine(-23.55, -46.63, -23.55, -46.63);

        Assert.Equal(0, result, 1);
    }

    [Fact]
    public void Haversine_PontosDiferentes_RetornaDistanciaPositiva()
    {
        // São Paulo ? Rio de Janeiro (~350-360 km)
        var result = ConversaoHelper.Haversine(-23.5505, -46.6333, -22.9068, -43.1729);

        Assert.True(result > 300);
        Assert.True(result < 400);
    }

    [Fact]
    public void Haversine_PontosProximos_RetornaDistanciaPequena()
    {
        var result = ConversaoHelper.Haversine(-23.5505, -46.6333, -23.5510, -46.6340);

        Assert.True(result < 1.0);
    }

    // ?? MinMaxScale ?????????????????????????????????????????????????????????

    [Fact]
    public void MinMaxScale_ArrayVazio_RetornaArrayVazio()
    {
        var result = ConversaoHelper.MinMaxScale([]);

        Assert.Empty(result);
    }

    [Fact]
    public void MinMaxScale_ValoresIguais_RetornaTodosZero()
    {
        var result = ConversaoHelper.MinMaxScale([5, 5, 5]);

        Assert.All(result, v => Assert.Equal(0, v));
    }

    [Fact]
    public void MinMaxScale_ValoresVariados_RetornaIntervaloZeroUm()
    {
        var result = ConversaoHelper.MinMaxScale([0, 50, 100]);

        Assert.Equal(3, result.Length);
        Assert.Equal(0, result[0], 5);
        Assert.Equal(0.5, result[1], 5);
        Assert.Equal(1, result[2], 5);
    }

    [Fact]
    public void MinMaxScale_UmElemento_RetornaZero()
    {
        var result = ConversaoHelper.MinMaxScale([42]);

        Assert.Single(result);
        Assert.Equal(0, result[0]);
    }

    [Fact]
    public void MinMaxScale_ValoresNegativos_NormalizaCorretamente()
    {
        var result = ConversaoHelper.MinMaxScale([-10, 0, 10]);

        Assert.Equal(0, result[0], 5);
        Assert.Equal(0.5, result[1], 5);
        Assert.Equal(1, result[2], 5);
    }
}
