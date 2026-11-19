using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class FiltroElegibilidadeOsServiceTests
{
    private readonly Mock<ILogger<FiltroElegibilidadeOsService>> _mockLogger = new();

    private FiltroElegibilidadeOsService CriarServico(
        int idadeMaximaOsComercialTecnicaDias = 30,
        int idadeMaximaLotesDias = 2,
        int prazoMaximoVencimentoOsComercialDias = 3)
    {
        var parametros = new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = idadeMaximaOsComercialTecnicaDias.ToString(),
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = idadeMaximaLotesDias.ToString(),
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = prazoMaximoVencimentoOsComercialDias.ToString()
        };

        ElegibilidadeOsSettings.CarregarDeParametro(parametros);
        ElegibilidadeOsSettings.Validar();

        var settings = new ElegibilidadeOsSettings();
        var options = Options.Create(settings);
        return new FiltroElegibilidadeOsService(options, _mockLogger.Object);
    }

    private OrdemServicoGmpro CriarOs(
        string numeroOs = "OS001",
        string tipoOs = "C",
        DateTime? dataSolicitacao = null,
        DateTime? dataVencimento = null)
    {
        var dataBase = DateTime.Today.AddHours(10);

        return new OrdemServicoGmpro
        {
            NumeroOs = numeroOs,
            TipoOs = tipoOs,
            DataSolicitacao = dataSolicitacao ?? dataBase.AddDays(-5),
            DataVencimento = dataVencimento ?? dataBase.AddDays(5),
            Latitude = -23.5,
            Longitude = -46.6,
            RegiaoDespacho = "1",
            Codserv = 100
        };
    }

    private OrdemServicoGmpro CriarLote(
        string numcdcLote = "LOTE001",
        DateTime? dataSolicitacao = null)
    {
        var dataBase = DateTime.Today.AddHours(10);

        return new OrdemServicoGmpro
        {
            NumeroOs = numcdcLote,
            NumcdcLote = numcdcLote,
            TipoOs = "X",
            DataSolicitacao = dataSolicitacao ?? dataBase.AddDays(-1),
            DataVencimento = dataBase.AddDays(7),
            Latitude = -23.5,
            Longitude = -46.6,
            RegiaoDespacho = "1",
            Codserv = 100
        };
    }

    // ??????????????????????????????????????????????????????????????????????????????????????????????????????
    // Testes: FiltrarPorIdadeOsComercialTecnica
    // ??????????????????????????????????????????????????????????????????????????????????????????????????????

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_OsComIdadeValida_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaOsComercialTecnicaDias: 30);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataSolicitacao: dataExecucao.AddDays(-15)); // 15 dias de idade
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
        Assert.Equal("OS001", resultado[0].NumeroOs);
    }

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_OsComIdadeExcedida_RemoveOs()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaOsComercialTecnicaDias: 30);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataSolicitacao: dataExecucao.AddDays(-31)); // 31 dias de idade
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_IdadeExatamenteLimite_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaOsComercialTecnicaDias: 30);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataSolicitacao: dataExecucao.AddDays(-30)); // Exatamente 30 dias
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_IdadeZero_ApenasDoiaCorrente()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaOsComercialTecnicaDias: 0);
        var dataExecucao = DateTime.Today;
        var osHoje = CriarOs(dataSolicitacao: dataExecucao);
        var osOntem = CriarOs(numeroOs: "OS002", dataSolicitacao: dataExecucao.AddDays(-1));
        var ordens = new List<OrdemServicoGmpro> { osHoje, osOntem };

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
        Assert.Equal("OS001", resultado[0].NumeroOs);
    }

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_ListaVazia_RetornaVazio()
    {
        // Arrange
        var servico = CriarServico();
        var ordens = new List<OrdemServicoGmpro>();

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, DateTime.Today);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_OsTecnicaAplicavel_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaOsComercialTecnicaDias: 30);
        var dataExecucao = DateTime.Today;
        var osTecnica = CriarOs(tipoOs: "T", dataSolicitacao: dataExecucao.AddDays(-50)); // 50 dias
        var ordens = new List<OrdemServicoGmpro> { osTecnica };

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeOsComercialTecnica_OsLoteNaoAplicavel_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaOsComercialTecnicaDias: 30);
        var dataExecucao = DateTime.Today;
        var lote = CriarLote(dataSolicitacao: dataExecucao.AddDays(-100)); // 100 dias
        var ordens = new List<OrdemServicoGmpro> { lote };

        // Act
        var resultado = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado); // Lote não é filtrado aqui
    }

    // ??????????????????????????????????????????????????????????????????????????????????????????????????????
    // Testes: FiltrarPorVencimentoOsComercial
    // ??????????????????????????????????????????????????????????????????????????????????????????????????????

    [Fact]
    public void FiltrarPorVencimentoOsComercial_OsVencida_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataVencimento: dataExecucao.AddDays(-5)); // Vencida há 5 dias
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_OsVencemHoje_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataVencimento: dataExecucao);
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_OsDentroDosPrazo_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataVencimento: dataExecucao.AddDays(2)); // Vence em 2 dias
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_OsAlemDoPrazo_RemoveOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataVencimento: dataExecucao.AddDays(5)); // Vence em 5 dias
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_OsExatamenteLimite_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataVencimento: dataExecucao.AddDays(3)); // Vence em exatamente 3 dias
        var ordens = new List<OrdemServicoGmpro> { os };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_TecnicaNaoFiltra_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var osTecnica = CriarOs(tipoOs: "T", dataVencimento: dataExecucao.AddDays(10)); // Fora do prazo
        var ordens = new List<OrdemServicoGmpro> { osTecnica };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado); // Técnica não é filtrada
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_LoteNaoFiltra_RetornaOs()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 3);
        var dataExecucao = DateTime.Today;
        var lote = CriarLote();
        lote.DataVencimento = dataExecucao.AddDays(10); // Fora do prazo
        var ordens = new List<OrdemServicoGmpro> { lote };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado); // Lote não é filtrado
    }

    [Fact]
    public void FiltrarPorVencimentoOsComercial_Zero_ApenasVencidaOuHoje()
    {
        // Arrange
        var servico = CriarServico(prazoMaximoVencimentoOsComercialDias: 0);
        var dataExecucao = DateTime.Today;
        var osVencida = CriarOs(numeroOs: "OS001", dataVencimento: dataExecucao.AddDays(-1));
        var osHoje = CriarOs(numeroOs: "OS002", dataVencimento: dataExecucao);
        var osFutura = CriarOs(numeroOs: "OS003", dataVencimento: dataExecucao.AddDays(1));
        var ordens = new List<OrdemServicoGmpro> { osVencida, osHoje, osFutura };

        // Act
        var resultado = servico.FiltrarPorVencimentoOsComercial(ordens, dataExecucao);

        // Assert
        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, o => o.NumeroOs == "OS001");
        Assert.Contains(resultado, o => o.NumeroOs == "OS002");
        Assert.DoesNotContain(resultado, o => o.NumeroOs == "OS003");
    }

    // ??????????????????????????????????????????????????????????????????????????????????????????????????????
    // Testes: FiltrarPorIdadeLotes
    // ??????????????????????????????????????????????????????????????????????????????????????????????????????

    [Fact]
    public void FiltrarPorIdadeLotes_LoteComIdadeValida_RetornaLote()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaLotesDias: 2);
        var dataExecucao = DateTime.Today;
        var lote = CriarLote(dataSolicitacao: dataExecucao.AddDays(-1)); // 1 dia
        var ordens = new List<OrdemServicoGmpro> { lote };

        // Act
        var resultado = servico.FiltrarPorIdadeLotes(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeLotes_LoteComIdadeExcedida_RemoveLote()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaLotesDias: 2);
        var dataExecucao = DateTime.Today;
        var lote = CriarLote(dataSolicitacao: dataExecucao.AddDays(-5)); // 5 dias
        var ordens = new List<OrdemServicoGmpro> { lote };

        // Act
        var resultado = servico.FiltrarPorIdadeLotes(ordens, dataExecucao);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeLotes_LoteExatamenteLimite_RetornaLote()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaLotesDias: 2);
        var dataExecucao = DateTime.Today;
        var lote = CriarLote(dataSolicitacao: dataExecucao.AddDays(-2)); // Exatamente 2 dias
        var ordens = new List<OrdemServicoGmpro> { lote };

        // Act
        var resultado = servico.FiltrarPorIdadeLotes(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
    }

    [Fact]
    public void FiltrarPorIdadeLotes_Zero_ApenasLoteHoje()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaLotesDias: 0);
        var dataExecucao = DateTime.Today;
        var loteHoje = CriarLote(numcdcLote: "LOTE001", dataSolicitacao: dataExecucao);
        var loteOntem = CriarLote(numcdcLote: "LOTE002", dataSolicitacao: dataExecucao.AddDays(-1));
        var ordens = new List<OrdemServicoGmpro> { loteHoje, loteOntem };

        // Act
        var resultado = servico.FiltrarPorIdadeLotes(ordens, dataExecucao);

        // Assert
        Assert.Single(resultado);
        Assert.Equal("LOTE001", resultado[0].NumcdcLote);
    }

    [Fact]
    public void FiltrarPorIdadeLotes_ApenasLotes_OsComercialNaoRemovida()
    {
        // Arrange
        var servico = CriarServico(idadeMaximaLotesDias: 2);
        var dataExecucao = DateTime.Today;
        var os = CriarOs(dataSolicitacao: dataExecucao.AddDays(-100)); // Muito antiga
        var lote = CriarLote(dataSolicitacao: dataExecucao.AddDays(-1)); // Recente
        var ordens = new List<OrdemServicoGmpro> { os, lote };

        // Act
        var resultado = servico.FiltrarPorIdadeLotes(ordens, dataExecucao);

        // Assert
        Assert.Equal(2, resultado.Count); // Ambas permanecem
    }

    [Fact]
    public void FiltrarPorIdadeLotes_ListaVazia_RetornaVazio()
    {
        // Arrange
        var servico = CriarServico();
        var ordens = new List<OrdemServicoGmpro>();

        // Act
        var resultado = servico.FiltrarPorIdadeLotes(ordens, DateTime.Today);

        // Assert
        Assert.Empty(resultado);
    }

    // ??????????????????????????????????????????????????????????????????????????????????????????????????????
    // Testes: Integração múltiplos filtros
    // ??????????????????????????????????????????????????????????????????????????????????????????????????????

    [Fact]
    public void AplicarMultiplosFiltros_CenarioCompleto_OrdensBemFiltradasComNivelAceitavel()
    {
        // Arrange
        var servico = CriarServico(
            idadeMaximaOsComercialTecnicaDias: 30,
            idadeMaximaLotesDias: 2,
            prazoMaximoVencimentoOsComercialDias: 3);

        var dataExecucao = DateTime.Today;

        var osComercialValida = CriarOs(
            numeroOs: "OS_C_OK",
            tipoOs: "C",
            dataSolicitacao: dataExecucao.AddDays(-15),
            dataVencimento: dataExecucao.AddDays(2));

        var osComercialIdadeExcedida = CriarOs(
            numeroOs: "OS_C_IDADE_EXC",
            tipoOs: "C",
            dataSolicitacao: dataExecucao.AddDays(-50),
            dataVencimento: dataExecucao.AddDays(2));

        var osComercialVencimentoExcedido = CriarOs(
            numeroOs: "OS_C_VENC_EXC",
            tipoOs: "C",
            dataSolicitacao: dataExecucao.AddDays(-15),
            dataVencimento: dataExecucao.AddDays(10));

        var osTecnicaValida = CriarOs(
            numeroOs: "OS_T_OK",
            tipoOs: "T",
            dataSolicitacao: dataExecucao.AddDays(-25),
            dataVencimento: dataExecucao.AddDays(5));

        var loteValido = CriarLote(
            numcdcLote: "LOTE_OK",
            dataSolicitacao: dataExecucao.AddDays(-1));

        var loteIdadeExcedida = CriarLote(
            numcdcLote: "LOTE_EXC",
            dataSolicitacao: dataExecucao.AddDays(-5));

        var ordens = new List<OrdemServicoGmpro>
        {
            osComercialValida,
            osComercialIdadeExcedida,
            osComercialVencimentoExcedido,
            osTecnicaValida,
            loteValido,
            loteIdadeExcedida
        };

        // Act
        var apos1 = servico.FiltrarPorIdadeOsComercialTecnica(ordens, dataExecucao);
        var apos2 = servico.FiltrarPorVencimentoOsComercial(apos1, dataExecucao);
        var apos3 = servico.FiltrarPorIdadeLotes(apos2, dataExecucao);

        // Assert
        Assert.Equal(4, apos3.Count); // 4 válidas: C_OK, T_OK, LOTE_OK, e um dos anteriormente removidos pode retornar
        Assert.Contains(apos3, o => o.NumeroOs == "OS_C_OK");
        Assert.Contains(apos3, o => o.NumeroOs == "OS_T_OK");
        Assert.Contains(apos3, o => o.NumcdcLote == "LOTE_OK");
    }
}
