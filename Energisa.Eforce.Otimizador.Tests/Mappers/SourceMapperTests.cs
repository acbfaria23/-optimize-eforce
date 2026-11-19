using Energisa.Eforce.Otimizador.Mappers;

namespace Energisa.Eforce.Otimizador.Tests.Mappers;

public class SourceMapperTests
{
    [Fact]
    public void FormatCodRegiao_IdExternoValido_RetornaEmpresaPoloRegiao()
    {
        var resultado = SourceMapper.FormatCodRegiao("20", "1-10-1");

        Assert.Equal("20_10-1", resultado);
    }

    [Fact]
    public void FormatCodRegiao_MesmaRegiaoEmPolosDiferentes_RetornaCodigosDistintos()
    {
        var polo10 = SourceMapper.FormatCodRegiao("20", "1-10-1");
        var polo80 = SourceMapper.FormatCodRegiao("20", "1-80-1");

        Assert.NotEqual(polo10, polo80);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FormatCodRegiao_IdExternoNuloOuVazio_RetornaVazio(string? idExterno)
    {
        Assert.Equal(string.Empty, SourceMapper.FormatCodRegiao("20", idExterno));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1-10")]
    [InlineData("1--1")]
    [InlineData("1-10-")]
    public void FormatCodRegiao_IdExternoIncompleto_RetornaVazio(string idExterno)
    {
        Assert.Equal(string.Empty, SourceMapper.FormatCodRegiao("20", idExterno));
    }
}
