using Energisa.Eforce.Otimizador.Models.Eforce;

namespace Energisa.Eforce.Otimizador.Tests.Models;

public class EforceModelsTests
{
    // ?? OrdemServico ????????????????????????????????????????????????????????

    [Fact]
    public void OrdemServico_DefaultValues_CorretoS()
    {
        var os = new OrdemServicoGmpro();

        Assert.Equal(string.Empty, os.NumeroOs);
        Assert.Null(os.NumcdcLote);
        Assert.Equal(0, os.Codserv);
        Assert.Null(os.Latitude);
        Assert.Null(os.Longitude);
        Assert.Null(os.RegiaoDespacho);
        Assert.Equal(string.Empty, os.TipoOs);
        Assert.Equal("N", os.RiscoVida);
        Assert.Equal("N", os.OsRegulada);
        Assert.Equal("N", os.Reicidencia);
        Assert.Equal(0, os.PessoasAtingidas);
        Assert.Equal(0, os.Compensacao);
        Assert.Equal(0, os.CompensacaoPorHora);
        Assert.Equal("NORMAL", os.Prioridade);
        Assert.Null(os.MetaTme);
        Assert.Equal("U", os.Localizacao);
        Assert.Null(os.TipoCorte);
    }

    [Fact]
    public void OrdemServico_SetProperties_ValoresCorretos()
    {
        var os = new OrdemServicoGmpro
        {
            NumeroOs = "12345",
            TipoOs = "C",
            Codserv = 100,
            Latitude = -23.5,
            Longitude = -46.6,
            RegiaoDespacho = "1",
            RiscoVida = "S",
            PessoasAtingidas = 50,
            Compensacao = 500,
            CompensacaoPorHora = 100,
            Prioridade = "URGENTE",
            MetaTme = 30,
            Localizacao = "R",
            TipoCorte = "3 - Corte no Poste"
        };

        Assert.Equal("12345", os.NumeroOs);
        Assert.Equal("S", os.RiscoVida);
        Assert.Equal(50, os.PessoasAtingidas);
        Assert.Equal("R", os.Localizacao);
    }

    // ?? Equipe ??????????????????????????????????????????????????????????????

    [Fact]
    public void Equipe_DefaultValues_Corretos()
    {
        var equipe = new EquipeGmpro();

        Assert.Equal(string.Empty, equipe.CodigoEquipe);
        Assert.Equal(string.Empty, equipe.TipoEquipe);
        Assert.Equal(string.Empty, equipe.AtuacaoEquipe);
        Assert.Equal(0, equipe.CustoKm);
        Assert.Null(equipe.LatitudeGaragem);
        Assert.Null(equipe.LongitudeGaragem);
        Assert.Null(equipe.LatitudeAlmoco);
        Assert.Null(equipe.LongitudeAlmoco);
        Assert.Null(equipe.DuracaoAlmoco);
        Assert.Equal(0, equipe.PorcentagemCargaTrabalho);
        Assert.Equal(0, equipe.MaxLoad);
        Assert.Equal(string.Empty, equipe.PerfilEquipe);
        Assert.Equal(0, equipe.NPessoas);
    }

    // ?? PerfilEquipe ????????????????????????????????????????????????????????

    [Fact]
    public void PerfilEquipe_DefaultValues_Corretos()
    {
        var perfil = new PerfilEquipeGmpro();

        Assert.Equal(0, perfil.Codserv);
        Assert.Equal(string.Empty, perfil.Perfil);
        Assert.Equal(0, perfil.Codigo);
    }

    [Fact]
    public void PerfilEquipe_SetProperties_Corretos()
    {
        var perfil = new PerfilEquipeGmpro
        {
            Codserv = 100,
            Perfil = "Faz tudo",
            Codigo = 200
        };

        Assert.Equal(100, perfil.Codserv);
        Assert.Equal("Faz tudo", perfil.Perfil);
        Assert.Equal(200, perfil.Codigo);
    }

    // ?? Regiao ??????????????????????????????????????????????????????????????

    [Fact]
    public void Regiao_DefaultValues_Corretos()
    {
        var regiao = new RegiaoGmpro();

        Assert.Equal(string.Empty, regiao.CodigoEquipe);
        Assert.Equal(string.Empty, regiao.TipoEquipe);
        Assert.Equal(string.Empty, regiao.CodRegiao);
    }

    // ?? OsProcessada ????????????????????????????????????????????????????????

    [Fact]
    public void OsProcessada_DefaultValues_Corretos()
    {
        var osProc = new OsProcessada();

        Assert.Equal(0, osProc.MultaAtraso);
        Assert.Equal(0, osProc.TempoVencida);
        Assert.Equal(0, osProc.TempoAteVencer);
        Assert.Equal(0, osProc.TempoAberta);
        Assert.False(osProc.Vencida);
        Assert.False(osProc.VencendoNoTurno);
        Assert.False(osProc.IsCompensacao);
        Assert.Equal(10, osProc.PesoFinal);
        Assert.Equal(0, osProc.DuracaoSegundos);
        Assert.Empty(osProc.PerfisEquipe);
        Assert.Empty(osProc.IndicesEquipesPermitidas);
    }

    // ?? EquipeProcessada ????????????????????????????????????????????????????

    [Fact]
    public void EquipeProcessada_DefaultValues_Corretos()
    {
        var ep = new EquipeProcessada();

        Assert.Empty(ep.CodigosRegiao);
    }

    [Fact]
    public void EquipeProcessada_SetProperties_Corretos()
    {
        var equipe = new EquipeGmpro { CodigoEquipe = "EQ1" };
        var ep = new EquipeProcessada
        {
            Equipe = equipe,
            CodigosRegiao = ["5"],
            InicioTurnoAjustado = new DateTime(2025, 1, 15, 8, 0, 0, DateTimeKind.Utc),
            FimTurnoAjustado = new DateTime(2025, 1, 15, 17, 0, 0, DateTimeKind.Utc)
        };

        Assert.Same(equipe, ep.Equipe);
        Assert.Equal(["5"], ep.CodigosRegiao);
    }
}
