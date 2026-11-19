using Energisa.Eforce.Otimizador.Configuration;

namespace Energisa.Eforce.Otimizador.Tests.Configuration;

public class ElegibilidadeOsSettingsTests
{
    [Fact]
    public void CarregarDeParametro_ValoresValidos_CarregaComSucesso()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "30",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "2",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "3"
        };

        // Act
        ElegibilidadeOsSettings.CarregarDeParametro(parametros);
        ElegibilidadeOsSettings.Validar();
        var settings = new ElegibilidadeOsSettings();

        // Assert
        Assert.Equal(30, settings.IdadeMaximaOsComercialTecnicaDias);
        Assert.Equal(2, settings.IdadeMaximaLotesDias);
        Assert.Equal(3, settings.PrazoMaximoVencimentoOsComercialDias);
    }

    [Fact]
    public void CarregarDeParametro_Zero_Aceita()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "0",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "0",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "0"
        };

        // Act
        ElegibilidadeOsSettings.CarregarDeParametro(parametros);
        ElegibilidadeOsSettings.Validar();
        var settings = new ElegibilidadeOsSettings();

        // Assert
        Assert.Equal(0, settings.IdadeMaximaOsComercialTecnicaDias);
        Assert.Equal(0, settings.IdadeMaximaLotesDias);
        Assert.Equal(0, settings.PrazoMaximoVencimentoOsComercialDias);
    }

    [Fact]
    public void CarregarDeParametro_ValoresNegativos_RejectaComExcecao()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "-5",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "2",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "3"
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => ElegibilidadeOsSettings.CarregarDeParametro(parametros));

        Assert.Contains("negativo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CarregarDeParametro_ValorNaoInteiro_RejectaComExcecao()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "abc",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "2",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "3"
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => ElegibilidadeOsSettings.CarregarDeParametro(parametros));

        Assert.Contains("não é um número inteiro", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validar_ComChavesIncompletas_RejectaComExcecao()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "30"
            // Faltam outras chaves
        };

        ElegibilidadeOsSettings.CarregarDeParametro(parametros);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => ElegibilidadeOsSettings.Validar());

        Assert.Contains("incompleta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CarregarDeParametro_GrandesValores_Aceita()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "999",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "999",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "999"
        };

        // Act
        ElegibilidadeOsSettings.CarregarDeParametro(parametros);
        ElegibilidadeOsSettings.Validar();
        var settings = new ElegibilidadeOsSettings();

        // Assert
        Assert.Equal(999, settings.IdadeMaximaOsComercialTecnicaDias);
    }

    [Fact]
    public void CarregarDeParametro_ValorNull_Rejeita()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = null,
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "2",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "3"
        };

        ElegibilidadeOsSettings.CarregarDeParametro(parametros);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => ElegibilidadeOsSettings.Validar());

        Assert.Contains("incompleta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CarregarDeParametro_ValoresComEspacos_Rejeita()
    {
        // Arrange
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = " 30 ",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = " 2 ",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = " 3 "
        };

        // Act & Assert - Tentará fazer parse, se houver espaços em branco pode falhar
        // Dependendo da implementação de int.TryParse, isso pode funcionar ou não
        // No .NET, int.TryParse com espaços geralmente falha
        var ex = Assert.Throws<InvalidOperationException>(
            () => ElegibilidadeOsSettings.CarregarDeParametro(parametros));

        Assert.Contains("não é um número inteiro", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
