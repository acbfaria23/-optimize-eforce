using Energisa.Eforce.Otimizador.Configuration;

namespace Energisa.Eforce.Otimizador.Tests.Configuration;

public class ReotimizacaoSettingsTests
{
    private static Dictionary<string, string?> CriarValoresValidos() => new()
    {
        [nameof(ReotimizacaoSettings.Enabled)] = "true",
        [nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos)] = "15",
        [nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos)] = "30",
        [nameof(ReotimizacaoSettings.ModoBusca)] = "ReturnFast"
    };

    // ── Caminho feliz ────────────────────────────────────────────────────────

    [Fact]
    public void CarregarDeParametro_ValoresValidos_AplicaTodasAsPropriedades()
    {
        var settings = new ReotimizacaoSettings();

        settings.CarregarDeParametro(CriarValoresValidos());

        Assert.True(settings.Enabled);
        Assert.Equal(15, settings.MaxIdadePosicaoMinutos);
        Assert.Equal(30, settings.TempoMaximoProcessamentoMinutos);
        Assert.Equal("ReturnFast", settings.ModoBusca);
    }

    [Fact]
    public void Validar_ValoresValidos_NaoLancaExcecao()
    {
        var settings = new ReotimizacaoSettings();
        settings.CarregarDeParametro(CriarValoresValidos());

        var excecao = Record.Exception(settings.Validar);

        Assert.Null(excecao);
    }

    [Theory]
    [InlineData("ReturnFast")]
    [InlineData("ConsumeAllAvailableTime")]
    [InlineData("Unspecified")]
    [InlineData("returnfast")]
    public void Validar_ModoBuscaConhecido_NaoLancaExcecao(string modoBusca)
    {
        var settings = new ReotimizacaoSettings();
        var valores = CriarValoresValidos();
        valores[nameof(ReotimizacaoSettings.ModoBusca)] = modoBusca;
        settings.CarregarDeParametro(valores);

        var excecao = Record.Exception(settings.Validar);

        Assert.Null(excecao);
    }

    // ── Entrada vazia/nula (sem parametrização carregada) ───────────────────

    [Fact]
    public void CarregarDeParametro_DicionarioVazio_NaoAplicaNenhumaPropriedadeENaoAssumeValorPadrao()
    {
        var settings = new ReotimizacaoSettings();

        settings.CarregarDeParametro([]);

        // Sem carga vinda da parametrização, não há nenhum valor assumido — tudo permanece
        // no estado "zero value" do CLR (nunca um default de negócio implícito no código).
        Assert.False(settings.Enabled);
        Assert.Equal(0, settings.MaxIdadePosicaoMinutos);
        Assert.Equal(0, settings.TempoMaximoProcessamentoMinutos);
        Assert.Null(settings.ModoBusca);
    }

    [Fact]
    public void Validar_DicionarioVazio_LancaInvalidOperationExceptionListandoTodasAsChaves()
    {
        var settings = new ReotimizacaoSettings();
        settings.CarregarDeParametro([]);

        var excecao = Assert.Throws<InvalidOperationException>(settings.Validar);

        Assert.Contains(ReotimizacaoSettings.ChaveReotimizacaoSettings, excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.Enabled), excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos), excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos), excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.ModoBusca), excecao.Message);
    }

    [Fact]
    public void Validar_ValoresNulos_LancaInvalidOperationExceptionListandoTodasAsChaves()
    {
        var settings = new ReotimizacaoSettings();
        var valoresNulos = new Dictionary<string, string?>
        {
            [nameof(ReotimizacaoSettings.Enabled)] = null,
            [nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos)] = null,
            [nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos)] = null,
            [nameof(ReotimizacaoSettings.ModoBusca)] = null
        };

        settings.CarregarDeParametro(valoresNulos);

        var excecao = Assert.Throws<InvalidOperationException>(settings.Validar);

        Assert.Contains(nameof(ReotimizacaoSettings.Enabled), excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos), excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos), excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.ModoBusca), excecao.Message);
    }

    [Fact]
    public void Validar_ChaveObrigatoriaAusente_LancaInvalidOperationExceptionApenasParaAusente()
    {
        var settings = new ReotimizacaoSettings();
        var valores = CriarValoresValidos();
        valores.Remove(nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos));
        settings.CarregarDeParametro(valores);

        var excecao = Assert.Throws<InvalidOperationException>(settings.Validar);

        Assert.Contains(nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos), excecao.Message);
        Assert.DoesNotContain(nameof(ReotimizacaoSettings.Enabled), excecao.Message);
    }

    // ── Edge case de domínio ─────────────────────────────────────────────────

    [Fact]
    public void Validar_ModoBuscaDesconhecido_LancaInvalidOperationException()
    {
        var settings = new ReotimizacaoSettings();
        var valores = CriarValoresValidos();
        valores[nameof(ReotimizacaoSettings.ModoBusca)] = "ModoInexistente";
        settings.CarregarDeParametro(valores);

        var excecao = Assert.Throws<InvalidOperationException>(settings.Validar);

        Assert.Contains("ModoInexistente", excecao.Message);
        Assert.Contains(nameof(ReotimizacaoSettings.ModoBusca), excecao.Message);
    }

    [Fact]
    public void CarregarDeParametro_ValorInvalidoParaInt_NaoMarcaComoCarregado()
    {
        var settings = new ReotimizacaoSettings();
        var valores = CriarValoresValidos();
        valores[nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos)] = "nao-e-um-numero";
        settings.CarregarDeParametro(valores);

        // Como o parse falhou, a chave não foi marcada como carregada — Validar deve falhar.
        var excecao = Assert.Throws<InvalidOperationException>(settings.Validar);

        Assert.Contains(nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos), excecao.Message);
        Assert.Equal(0, settings.MaxIdadePosicaoMinutos);
    }

    [Fact]
    public void CarregarDeParametro_ChamadasSucessivas_LimpaEstadoAnterior()
    {
        var settings = new ReotimizacaoSettings();
        settings.CarregarDeParametro(CriarValoresValidos());

        // Segunda carga sem a chave ModoBusca — deve invalidar mesmo tendo sido carregada antes.
        var valoresIncompletos = CriarValoresValidos();
        valoresIncompletos.Remove(nameof(ReotimizacaoSettings.ModoBusca));
        settings.CarregarDeParametro(valoresIncompletos);

        var excecao = Assert.Throws<InvalidOperationException>(settings.Validar);

        Assert.Contains(nameof(ReotimizacaoSettings.ModoBusca), excecao.Message);
    }
}