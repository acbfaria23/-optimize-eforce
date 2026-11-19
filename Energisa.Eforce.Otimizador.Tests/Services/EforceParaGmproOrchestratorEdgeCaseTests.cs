using Microsoft.Extensions.Logging;
using Moq;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Tests.Helpers;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class EforceParaGmproOrchestratorEdgeCaseTests
{
    private readonly EforceParaGmproOrchestrator _orchestrator;

    public EforceParaGmproOrchestratorEdgeCaseTests()
    {
        var logger = new Mock<ILogger<EforceParaGmproOrchestrator>>();
        _orchestrator = new EforceParaGmproOrchestrator(logger.Object);
    }

    [Fact]
    public void ConverterComDetalhes_OsLote_AtribuidaEquipeMaisProxima()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsLote("L1", "CDC001")],
            Equipes =
            [
                TestDataFactory.CriarEquipeValida("EQ1"),
                TestDataFactory.CriarEquipeValida("EQ2")
            ],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe(100, "Faz tudo")],
            Regioes =
            [
                TestDataFactory.CriarRegiao("EQ1", "LV", "1"),
                TestDataFactory.CriarRegiao("EQ2", "LV", "1")
            ]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.Single(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    [Fact]
    public void ConverterComDetalhes_OsRiscoVida_PesoMaiorQueNormal()
    {
        var dados = new DadosEforce
        {
            OrdensServico =
            [
                TestDataFactory.CriarOsValida("NORMAL1"),
                TestDataFactory.CriarOsRiscoVida("RV1")
            ],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        var osNormal = resultado.OrdensProcessadas.First(o => o.Os.NumeroOs.StartsWith("NORMAL1"));
        var osRV = resultado.OrdensProcessadas.First(o => o.Os.NumeroOs.StartsWith("RV1"));
        Assert.True(osRV.PesoFinal > osNormal.PesoFinal);
    }

    [Fact]
    public void ConverterComDetalhes_OsComCompensacao_IsCompensacaoTrue()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsComCompensacao("COMP1")],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.True(resultado.OrdensProcessadas[0].IsCompensacao);
    }

    [Fact]
    public void ConverterComDetalhes_OsVencida_VencidaTrue()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsVencida("VENCIDA1")],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.True(resultado.OrdensProcessadas[0].Vencida);
    }

    [Fact]
    public void ConverterComDetalhes_OsSemRegiao_Filtrada()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();
        dados.OrdensServico.Add(TestDataFactory.CriarOsSemRegiao());

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_EquipeComLatitudePositiva_Filtrada()
    {
        var dados = TestDataFactory.CriarDadosEforceCompletos();
        dados.Equipes.Add(TestDataFactory.CriarEquipeComLatitudeInvalida("INVALIDA"));

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.EquipesProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_EquipeSemRegiao_Ignorada()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsValida()],
            Equipes = [TestDataFactory.CriarEquipeValida("SEM_REGIAO")],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [] // nenhuma regiao
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Empty(resultado.EquipesProcessadas);
    }

    [Fact]
    public void ConverterComDetalhes_SufixoLote_NumcdcLoteContemTipoOs()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsLote("66666", "CDC001")],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Contains("-X", resultado.OrdensProcessadas[0].Os.NumcdcLote);
    }

    [Fact]
    public void ConverterComDetalhes_TipoCorteRestritoEquipe1Pessoa_NaoCompativel()
    {
        var equipe1Pessoa = TestDataFactory.CriarEquipeValida("EQ_1P");
        equipe1Pessoa.NPessoas = 1;

        var osCortePoste = TestDataFactory.CriarOsValida("OS_CORTE");
        osCortePoste.TipoCorte = "3 - Corte no Poste";

        var dados = new DadosEforce
        {
            OrdensServico = [osCortePoste],
            Equipes = [equipe1Pessoa],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao("EQ_1P", "LV", "1")]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.Empty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    [Fact]
    public void ConverterComDetalhes_MesmaRegiaoEmPoloDiferente_NaoCompativel()
    {
        var os = TestDataFactory.CriarOsValida("OS_POLO");
        os.RegiaoDespacho = "20_10-1";

        var dados = new DadosEforce
        {
            OrdensServico = [os],
            Equipes = [TestDataFactory.CriarEquipeValida("EQ_POLO80")],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao("EQ_POLO80", "LV", "20_80-1")]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.Empty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    [Fact]
    public void ConverterComDetalhes_MesmaRegiaoEMesmoPolo_Compativel()
    {
        var os = TestDataFactory.CriarOsValida("OS_POLO");
        os.RegiaoDespacho = "20_10-1";

        var dados = new DadosEforce
        {
            OrdensServico = [os],
            Equipes = [TestDataFactory.CriarEquipeValida("EQ_POLO10")],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao("EQ_POLO10", "LV", "20_10-1")]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.NotEmpty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    [Fact]
    public void ConverterComDetalhes_TipoCorteRestritoEquipe2Pessoas_Compativel()
    {
        var equipe2Pessoas = TestDataFactory.CriarEquipeValida("EQ_2P");
        equipe2Pessoas.NPessoas = 2;

        var osCortePoste = TestDataFactory.CriarOsValida("OS_CORTE");
        osCortePoste.TipoCorte = "3 - Corte no Poste";

        var dados = new DadosEforce
        {
            OrdensServico = [osCortePoste],
            Equipes = [equipe2Pessoas],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao("EQ_2P", "LV", "1")]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.NotEmpty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    [Fact]
    public void ConverterComDetalhes_EquipeAtuacaoUR_CompativelComQualquerLocalizacao()
    {
        var equipeUR = TestDataFactory.CriarEquipeValida("EQ_UR");
        equipeUR.AtuacaoEquipe = "U/R";

        var osRural = TestDataFactory.CriarOsValida("OS_RURAL");
        osRural.Localizacao = "R";

        var dados = new DadosEforce
        {
            OrdensServico = [osRural],
            Equipes = [equipeUR],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao("EQ_UR", "LV", "1")]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.NotEmpty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
    }

    [Fact]
    public void ConverterComDetalhes_OsRegulada_MultaAtrasoReguladaHora()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsRegulada("REG1")],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Single(resultado.OrdensProcessadas);
        Assert.Equal(
            ConversaoConstantes.MultaAtrasoReguladaHora,
            resultado.OrdensProcessadas[0].MultaAtraso);
    }

    [Fact]
    public void ConverterComDetalhes_OsRiscoVida_MultaAtrasoAlta()
    {
        var dados = new DadosEforce
        {
            OrdensServico = [TestDataFactory.CriarOsRiscoVida("RV1")],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        Assert.Equal(ConversaoConstantes.MultaAtrasoRiscoVidaHora, resultado.OrdensProcessadas[0].MultaAtraso);
    }

    [Fact]
    public void ConverterComDetalhes_OsSemMetaTme_UsaMediaGeral()
    {
        var osComTme = TestDataFactory.CriarOsValida("COM_TME");
        osComTme.MetaTme = 60; // 60 min = 3600s

        var osSemTme = TestDataFactory.CriarOsValida("SEM_TME");
        osSemTme.MetaTme = null;

        var dados = new DadosEforce
        {
            OrdensServico = [osComTme, osSemTme],
            Equipes = [TestDataFactory.CriarEquipeValida()],
            PerfisEquipe = [TestDataFactory.CriarPerfilEquipe()],
            Regioes = [TestDataFactory.CriarRegiao()]
        };

        var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

        var osSemTmeProc = resultado.OrdensProcessadas.First(o => o.Os.NumeroOs.StartsWith("SEM_TME"));
         Assert.Equal(3600, osSemTmeProc.DuracaoSegundos); // media = 60min * 60 = 3600s
     }

     [Fact]
     public void ConverterComDetalhes_OsSemPerfilMapeado_IncompatívelComEquipeComPerfil()
     {
         var osOrf = TestDataFactory.CriarOsValida("OS_ORF");
         osOrf.Codserv = 999; // Codserv não presente em PerfisEquipe

         var equipeComPerfil = TestDataFactory.CriarEquipeValida("EQ_PERFIL");
         equipeComPerfil.PerfilEquipe = "Instalação"; // Equipe exige perfil específico

         var dados = new DadosEforce
         {
             OrdensServico = [osOrf],
             Equipes = [equipeComPerfil],
             PerfisEquipe = [TestDataFactory.CriarPerfilEquipe(100, "Instalação")], // Codserv=100, não 999
             Regioes = [TestDataFactory.CriarRegiao(equipeComPerfil.CodigoEquipe, equipeComPerfil.TipoEquipe)]
         };

         var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

         Assert.Single(resultado.OrdensProcessadas);
         Assert.Empty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
     }

     [Fact]
     public void ConverterComDetalhes_OsComPerfilMapeado_CompatívelComEquipeComPerfil()
     {
         var osComPerfil = TestDataFactory.CriarOsValida("OS_COM_PERFIL");
         osComPerfil.Codserv = 100; // Codserv presente em PerfisEquipe

         var equipeComPerfil = TestDataFactory.CriarEquipeValida("EQ_PERFIL");
         equipeComPerfil.PerfilEquipe = "Instalação"; // Equipe exige perfil "Instalação"

         var dados = new DadosEforce
         {
             OrdensServico = [osComPerfil],
             Equipes = [equipeComPerfil],
             PerfisEquipe = [TestDataFactory.CriarPerfilEquipe(100, "Instalação")], // Codserv=100 mapeia para "Instalação"
             Regioes = [TestDataFactory.CriarRegiao(equipeComPerfil.CodigoEquipe, equipeComPerfil.TipoEquipe)]
         };

         var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

         Assert.Single(resultado.OrdensProcessadas);
         Assert.NotEmpty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
     }

     [Fact]
     public void ConverterComDetalhes_OsSemPerfilMapeado_CompatívelComEquipeSemPerfil()
     {
         var osOrf = TestDataFactory.CriarOsValida("OS_ORF");
         osOrf.Codserv = 999; // Codserv não presente em PerfisEquipe

         var equipeSemPerfil = TestDataFactory.CriarEquipeValida("EQ_GENERICO");
         equipeSemPerfil.PerfilEquipe = string.Empty; // Sem perfil específico

         var dados = new DadosEforce
         {
             OrdensServico = [osOrf],
             Equipes = [equipeSemPerfil],
             PerfisEquipe = [TestDataFactory.CriarPerfilEquipe(100, "Instalação")],
             Regioes = [TestDataFactory.CriarRegiao(equipeSemPerfil.CodigoEquipe, equipeSemPerfil.TipoEquipe)]
         };

         var resultado = _orchestrator.ConverterComDetalhes(dados, TestDataFactory.DataExecucao);

         Assert.Single(resultado.OrdensProcessadas);
         Assert.NotEmpty(resultado.OrdensProcessadas[0].IndicesEquipesPermitidas);
     }
}
