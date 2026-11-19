using Energisa.Eforce.Otimizador.Services;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class RelatorioOtimizacaoTests
{
    [Fact]
    public void Default_TodosValoresZero()
    {
        var relatorio = new RelatorioOtimizacao();

        Assert.Equal(0, relatorio.ValorTotalOriginal);
        Assert.Equal(0, relatorio.ValorSkips);
        Assert.Equal(0, relatorio.ValorMultaRiscoVida);
        Assert.Equal(0, relatorio.ValorMultaClienteEssencial);
        Assert.Equal(0, relatorio.ValorTotalMulta);
        Assert.Equal(0, relatorio.ValorT0);
        Assert.Equal(0, relatorio.ValorFinal);
        Assert.Equal(0, relatorio.CustoKilometros);
        Assert.Equal(0, relatorio.CustoFixo);
    }

    [Fact]
    public void ToString_ContemValorOriginal()
    {
        var relatorio = new RelatorioOtimizacao
        {
            ValorTotalOriginal = 5000,
            ValorSkips = 100,
            ValorMultaRiscoVida = 200,
            ValorMultaClienteEssencial = 50,
            ValorTotalMulta = 300,
            ValorT0 = 150,
            ValorFinal = 4200,
            CustoKilometros = 1000,
            CustoFixo = 500
        };

        var resultado = relatorio.ToString();

        Assert.Contains("5000", resultado);
        Assert.Contains("4200", resultado);
        Assert.Contains("1500", resultado); // CustoKm + CustoFixo
    }

    [Fact]
    public void SetProperties_ValoresCorretos()
    {
        var relatorio = new RelatorioOtimizacao
        {
            ValorTotalOriginal = 10000,
            ValorFinal = 8000,
            CustoKilometros = 500,
            CustoFixo = 200
        };

        Assert.Equal(10000, relatorio.ValorTotalOriginal);
        Assert.Equal(8000, relatorio.ValorFinal);
        Assert.Equal(500, relatorio.CustoKilometros);
        Assert.Equal(200, relatorio.CustoFixo);
    }
}
