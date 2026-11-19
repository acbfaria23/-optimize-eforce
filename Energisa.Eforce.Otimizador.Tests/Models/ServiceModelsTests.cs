using Energisa.Eforce.Otimizador.Services;

namespace Energisa.Eforce.Otimizador.Tests.Models;

public class ServiceModelsTests
{
    [Fact]
    public void DadosEforce_DefaultValues_ListasVazias()
    {
        var dados = new DadosEforce();

        Assert.Empty(dados.OrdensServico);
        Assert.Empty(dados.Equipes);
        Assert.Empty(dados.PerfisEquipe);
        Assert.Empty(dados.Regioes);
    }

    [Fact]
    public void EquipeIndexada_SetProperties_Corretos()
    {
        var equipe = new Energisa.Eforce.Otimizador.Models.Eforce.EquipeGmpro { CodigoEquipe = "EQ1" };
        var idx = new EquipeIndexada
        {
            IndiceVehicle = 3,
            Equipe = equipe,
            CodigosRegiao = ["5"]
        };

        Assert.Equal(3, idx.IndiceVehicle);
        Assert.Same(equipe, idx.Equipe);
        Assert.Equal(["5"], idx.CodigosRegiao);
    }

    [Fact]
    public void ResultadoConversao_DefaultValues_ListasVazias()
    {
        var resultado = new ResultadoConversao();

        Assert.Empty(resultado.OrdensProcessadas);
        Assert.Empty(resultado.EquipesProcessadas);
    }
}
