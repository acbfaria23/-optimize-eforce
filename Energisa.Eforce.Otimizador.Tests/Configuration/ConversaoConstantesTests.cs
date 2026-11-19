namespace Energisa.Eforce.Otimizador.Tests.Configuration;

public class ConversaoConstantesTests
{
    // ?? Constantes de peso ??????????????????????????????????????????????????

    [Fact]
    public void PesoRiscoVida_DeveSerCemMil()
    {
        Assert.Equal(100_000, ConversaoConstantes.PesoRiscoVida);
    }

    [Fact]
    public void PesoBase_DeveSerUm()
    {
        Assert.Equal(1, ConversaoConstantes.PesoBase);
    }

    [Fact]
    public void PesoRegulada_DeveSerUm()
    {
        Assert.Equal(1, ConversaoConstantes.PesoRegulada);
    }

    [Fact]
    public void PesoReicidente_DeveSerUm()
    {
        Assert.Equal(1, ConversaoConstantes.PesoReicidente);
    }

    // ?? Hierarquia de importância ???????????????????????????????????????????

    [Fact]
    public void RiscoVidaImportancia_DeveSerOnze()
    {
        Assert.Equal(11, ConversaoConstantes.RiscoVidaImportancia);
    }

    [Fact]
    public void HierarquiaDecrescente_OrdemCorreta()
    {
        Assert.True(ConversaoConstantes.RiscoVidaImportancia >= ConversaoConstantes.ClienteEssencialImportancia);
        Assert.True(ConversaoConstantes.ClienteEssencialImportancia > ConversaoConstantes.PessoasAtingidasImportancia);
        Assert.True(ConversaoConstantes.PessoasAtingidasImportancia > ConversaoConstantes.CompensacaoImportancia);
        Assert.True(ConversaoConstantes.CompensacaoImportancia > ConversaoConstantes.PrioridadeEReguladaImportancia);
        Assert.True(ConversaoConstantes.PrioridadeEReguladaImportancia > ConversaoConstantes.ReicidenciaImportancia);
        Assert.True(ConversaoConstantes.ReicidenciaImportancia > ConversaoConstantes.TempoTecnicaImportancia);
        Assert.True(ConversaoConstantes.TempoTecnicaImportancia > ConversaoConstantes.PrioridadeImportancia);
        Assert.True(ConversaoConstantes.PrioridadeImportancia > ConversaoConstantes.EDoLoteImportancia);
        Assert.True(ConversaoConstantes.EDoLoteImportancia > ConversaoConstantes.OsPrazoImportancia);
    }

    // ?? Parâmetros operacionais ?????????????????????????????????????????????

    [Fact]
    public void CapacidadeEquipePadrao_DeveSer60()
    {
        Assert.Equal(60, ConversaoConstantes.CapacidadeEquipePadrao);
    }

    [Fact]
    public void DuracaoAlmocoSegundos_DeveSer3600()
    {
        Assert.Equal(3600, ConversaoConstantes.DuracaoAlmocoSegundos);
    }

    [Fact]
    public void CustoFixoVeiculo_DeveSer200()
    {
        Assert.Equal(200, ConversaoConstantes.CustoFixoVeiculo);
    }

    [Fact]
    public void PenalidadeAlmoco_DeveSerAlta()
    {
        Assert.Equal(1_000_000_000, ConversaoConstantes.PenalidadeAlmoco);
    }

    [Fact]
    public void MargemAlmocoHoras_DeveSerUm()
    {
        Assert.Equal(1, ConversaoConstantes.MargemAlmocoHoras);
    }

    // ?? ServicosRestritos1Pessoa ?????????????????????????????????????????????

    [Fact]
    public void ServicosRestritos1Pessoa_ContemCorteNoPoste()
    {
        Assert.Contains("3 - Corte no Poste", ConversaoConstantes.ServicosRestritos1Pessoa);
    }

    [Fact]
    public void ServicosRestritos1Pessoa_NaoEhVazio()
    {
        Assert.NotEmpty(ConversaoConstantes.ServicosRestritos1Pessoa);
    }

    // ?? ObterValorPrioridade ????????????????????????????????????????????????

    [Theory]
    [InlineData("URGENTE", 4)]
    [InlineData("urgente", 4)]
    [InlineData("Urgente", 4)]
    [InlineData("ALTA", 3)]
    [InlineData("alta", 3)]
    [InlineData("NORMAL", 2)]
    [InlineData("normal", 2)]
    public void ObterValorPrioridade_PrioridadesConhecidas_RetornaValorCorreto(string prioridade, int esperado)
    {
        var resultado = ConversaoConstantes.ObterValorPrioridade(prioridade);

        Assert.Equal(esperado, resultado);
    }

    [Theory]
    [InlineData("BAIXA")]
    [InlineData("")]
    [InlineData("DESCONHECIDA")]
    public void ObterValorPrioridade_PrioridadeDesconhecida_RetornaZero(string prioridade)
    {
        var resultado = ConversaoConstantes.ObterValorPrioridade(prioridade);

        Assert.Equal(0, resultado);
    }

    [Fact]
    public void ObterValorPrioridade_Null_RetornaZero()
    {
        var resultado = ConversaoConstantes.ObterValorPrioridade(null!);

        Assert.Equal(0, resultado);
    }

    // ?? Multas ??????????????????????????????????????????????????????????????

    [Fact]
    public void MultaAtrasoRiscoVidaHora_DeveSerAlta()
    {
        Assert.Equal(1_000_000, ConversaoConstantes.MultaAtrasoRiscoVidaHora);
    }

    // ?? Prioridades ?????????????????????????????????????????????????????????

    [Fact]
    public void PrioridadeUrgente_MaiorQueAlta()
    {
        Assert.True(ConversaoConstantes.PrioridadeUrgente > ConversaoConstantes.PrioridadeAlta);
    }

    [Fact]
    public void PrioridadeAlta_MaiorQueNormal()
    {
        Assert.True(ConversaoConstantes.PrioridadeAlta > ConversaoConstantes.PrioridadeNormal);
    }

    // ?? Parâmetros de custo e penalidade ?????????????????????????????????????

    [Fact]
    public void DistanciaParaPenalidade_DeveSerDez()
    {
        Assert.Equal(10, ConversaoConstantes.DistanciaParaPenalidade);
    }

    [Fact]
    public void CustoKmEquipe_DeveSerPositivo()
    {
        Assert.True(ConversaoConstantes.CustoKmEquipe >= 0);
    }

    [Fact]
    public void CustoFixoEquipe_DeveSerNaoNegativo()
    {
        Assert.True(ConversaoConstantes.CustoFixoEquipe >= 0);
    }
}

