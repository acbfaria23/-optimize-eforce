using Microsoft.Extensions.Logging;
using Moq;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Tests.Helpers;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class EforceParaGmproOrchestratorTests
{
    private readonly EforceParaGmproOrchestrator _orchestrator;

    public EforceParaGmproOrchestratorTests()
    {
        var logger = new Mock<ILogger<EforceParaGmproOrchestrator>>();
        _orchestrator = new EforceParaGmproOrchestrator(logger.Object);
    }

    [Fact]
    public void ConverterComDetalhes_DadosCompletos_RetornaResultado()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.NotNull(resultado);
        Assert.NotEmpty(resultado.OrdensProcessadas);
        Assert.NotEmpty(resultado.EquipesProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_DadosCompletos_EquipesProcessadasPresentes()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.NotEmpty(resultado.EquipesProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_DadosCompletos_OrdensProcessadasPresentes()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.NotEmpty(resultado.OrdensProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_DadosVazios_RetornaResultadoVazio()
    {
        var dados = new DadosEforce();

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.NotNull(resultado);
        Assert.Empty(resultado.OrdensProcessadas);
        Assert.Empty(resultado.EquipesProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_OsSemCoordenadas_Filtrada()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();
        dados.OrdensServico.Add(TestDataFactory.CriarOsSemCoordenadas());

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_EquipeSemLatitude_Filtrada()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();
        dados.Equipes.Add(TestDataFactory.CriarEquipeComLatitudeNula("LATNULL"));

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.EquipesProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_SufixoAplicado_NumeroOsContemTipoOs()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();
        dados.OrdensServico[0].NumeroOs = "12345";
        dados.OrdensServico[0].TipoOs = "C";

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Contains("12345-C", resultado.OrdensProcessadas[0].Os.NumeroOs);
    }

    [Fact]
    public void ConverterComDetalhes_MultiplasOsEEquipes_ProcessaCorretamente()
    {
        var dados = new DadosEforce
        {
            OrdensServico =
            [
                TestDataFactory.CriarOsValida("OS1"),
                TestDataFactory.CriarOsRiscoVida("OS2"),
                TestDataFactory.CriarOsTecnica("OS3")
            ],
            Equipes =
            [
                TestDataFactory.CriarEquipeValida("EQ1"),
                TestDataFactory.CriarEquipeValida("EQ2")
            ],
            PerfisEquipe =
            [
                TestDataFactory.CriarPerfilEquipe(100, "Faz tudo")
            ],
            Regioes =
            [
                TestDataFactory.CriarRegiao("EQ1", "LV", "1"),
                TestDataFactory.CriarRegiao("EQ2", "LV", "1")
            ]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.NotNull(resultado);
        Assert.Equal(3, resultado.OrdensProcessadas.Count);
        Assert.Equal(2, resultado.EquipesProcessadas.Count);
    }

    [Fact]
    public void ConverterComDetalhes_TurnosCalculados_Presentes()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.True(resultado.InicioTurno < resultado.FimTurno);
    }

    // ?? Turnos noturnos ?????????????????????????????????????????????????????

    [Fact]
    public void ConverterComDetalhes_TurnoNoturno_RefeicaoDentroDoTurno()
    {
        var equipe = TestDataFactory.CriarEquipeTurnoNoturno();
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsValida()],
            Equipes = [equipe],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao(equipe.CodigoEquipe, equipe.TipoEquipe)]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        var ep = resultado.EquipesProcessadas[0];
        Assert.True(ep.InicioRefeicaoAjustado >= ep.InicioTurnoAjustado,
            $"InicioRefeicao ({ep.InicioRefeicaoAjustado}) deve ser >= InicioTurno ({ep.InicioTurnoAjustado})");
        Assert.True(ep.FimRefeicaoAjustado <= ep.FimTurnoAjustado,
            $"FimRefeicao ({ep.FimRefeicaoAjustado}) deve ser <= FimTurno ({ep.FimTurnoAjustado})");
    }

    [Fact]
    public void ConverterComDetalhes_TurnoNoturno_JanelaDoTurnoCoerente()
    {
        var equipe = TestDataFactory.CriarEquipeTurnoNoturno();
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsValida()],
            Equipes = [equipe],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao(equipe.CodigoEquipe, equipe.TipoEquipe)]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        var ep = resultado.EquipesProcessadas[0];
        Assert.True(ep.FimTurnoAjustado > ep.InicioTurnoAjustado,
            $"FimTurno ({ep.FimTurnoAjustado}) deve ser > InicioTurno ({ep.InicioTurnoAjustado})");
    }

    [Fact]
    public void ConverterComDetalhes_TurnoNoturnoComFlexibilizacao_RefeicaoPermaneceDentroDoTurno()
    {
        var equipe = TestDataFactory.CriarEquipeTurnoNoturno();
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsValida()],
            Equipes = [equipe],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao(equipe.CodigoEquipe, equipe.TipoEquipe)]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao, flexibilizarRefeicao: true);

        var ep = resultado.EquipesProcessadas[0];
        Assert.True(ep.InicioRefeicaoAjustado >= ep.InicioTurnoAjustado,
            $"InicioRefeicao ({ep.InicioRefeicaoAjustado}) deve ser >= InicioTurno ({ep.InicioTurnoAjustado}) mesmo com flexibilização");
    }

    [Fact]
    public void ConverterComDetalhes_OsSemEquipeCompativel_IndicesVazio()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();
        dados.OrdensServico[0].Localizacao = "R";
        dados.Equipes[0].AtuacaoEquipe = "U";

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.Empty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    // ?? Segundos residuais ???????????????????????????????????????????????????

    [Fact]
    public void ConverterComDetalhes_SegundosResiduaisNaRefeicao_DuracaoExataDeUmaHora()
    {
        var equipe = TestDataFactory.CriarEquipeComSegundosResiduaisNaRefeicao();
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsValida()],
            Equipes = [equipe],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao(equipe.CodigoEquipe, equipe.TipoEquipe)]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        var ep = resultado.EquipesProcessadas[0];
        var duracaoRefeicao = ep.FimRefeicaoAjustado - ep.InicioRefeicaoAjustado;
        Assert.Equal(TimeSpan.FromHours(1), duracaoRefeicao);
    }
}
