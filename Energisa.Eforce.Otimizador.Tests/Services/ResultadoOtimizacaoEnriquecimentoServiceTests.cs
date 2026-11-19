using Energisa.Eforce.Otimizador.Models;
using Energisa.Eforce.Otimizador.Services;
using Energisa.God.Domain.Model._OrdemServico_.Interface;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Repository._OrdemServico_.Interface;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Microsoft.Extensions.Logging;
using Moq;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class ResultadoOtimizacaoEnriquecimentoServiceTests
{
    private readonly Mock<IOtimizadorRotasRepository> _otimizadorRepositoryMock;
    private readonly Mock<IOrdemServicoRepository> _osRepositoryMock;
    private readonly Mock<ILogger<ResultadoOtimizacaoEnriquecimentoService>> _loggerMock;
    private readonly ResultadoOtimizacaoEnriquecimentoService _service;

    public ResultadoOtimizacaoEnriquecimentoServiceTests()
    {
        _otimizadorRepositoryMock = new Mock<IOtimizadorRotasRepository>();
        _osRepositoryMock = new Mock<IOrdemServicoRepository>();
        _loggerMock = new Mock<ILogger<ResultadoOtimizacaoEnriquecimentoService>>();
        _service = new ResultadoOtimizacaoEnriquecimentoService(
            _otimizadorRepositoryMock.Object,
            _osRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_SemResultados_RetornaListaVazia()
    {
        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        var resultado = await _service.ObterResultadosEnriquecidosAsync("user", "1");

        Assert.Empty(resultado);
        _osRepositoryMock.Verify(
            r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()),
            Times.Never);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_ComResultados_RetornaEnriquecidos()
    {
        var resultadosRaw = CriarResultadosOtimizacao(["OS-001", "OS-002"]);
        var osEnriquecidas = CriarOsMock(["OS-001", "OS-002"]);

        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync("user", "1"))
            .ReturnsAsync(resultadosRaw);
        _osRepositoryMock
            .Setup(r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()))
            .ReturnsAsync(osEnriquecidas);

        var resultado = await _service.ObterResultadosEnriquecidosAsync("user", "1");

        Assert.Single(resultado);
        var os = resultado[0].Equipes[0].OrdensServico.First(o => o.Id == "OS-001");
        Assert.Equal("NUM-OS-001", os.NumeroOs);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_OsAlmoco_NaoBuscaEnriquecimento()
    {
        var resultadosRaw = CriarResultadosOtimizacao(["OS-001"], incluirAlmoco: true);

        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultadosRaw);
        _osRepositoryMock
            .Setup(r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()))
            .ReturnsAsync([]);

        await _service.ObterResultadosEnriquecidosAsync("user", "1");

        _osRepositoryMock.Verify(
            r => r.GetOrdemServicoByIdWithProjectionAsync(
                It.Is<string[]>(ids => ids.Length == 1 && ids[0] == "OS-001"),
                It.IsAny<string[]>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_OsIdVazia_NaoBuscaEnriquecimento()
    {
        var resultadosRaw = CriarResultadosOtimizacao([""]);

        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultadosRaw);

        await _service.ObterResultadosEnriquecidosAsync("user", "1");

        _osRepositoryMock.Verify(
            r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()),
            Times.Never);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_ErroNoRepositorioOs_LoteIgnoradoSemExcecao()
    {
        var resultadosRaw = CriarResultadosOtimizacao(["OS-001"]);

        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultadosRaw);
        _osRepositoryMock
            .Setup(r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()))
            .ThrowsAsync(new Exception("Erro simulado de repositório"));

        var resultado = await _service.ObterResultadosEnriquecidosAsync("user", "1");

        Assert.Single(resultado);
        Assert.Null(resultado[0].Equipes[0].OrdensServico[0].NumeroOs);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_OsIdsDistintos_BuscaSemDuplicatas()
    {
        var resultadosRaw = CriarResultadosOtimizacao(["OS-001", "OS-001", "OS-002"]);

        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultadosRaw);
        _osRepositoryMock
            .Setup(r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()))
            .ReturnsAsync([]);

        await _service.ObterResultadosEnriquecidosAsync("user", "1");

        _osRepositoryMock.Verify(
            r => r.GetOrdemServicoByIdWithProjectionAsync(
                It.Is<string[]>(ids => ids.Length == 2),
                It.IsAny<string[]>()),
            Times.Once);
    }

    [Fact]
    public async Task ObterResultadosEnriquecidosAsync_OsNaoEncontradaNoLookup_MantemPropriedadesOriginais()
    {
        var resultadosRaw = CriarResultadosOtimizacao(["OS-DESCONHECIDA"]);

        _otimizadorRepositoryMock
            .Setup(r => r.ObterResultadosOtimizacaoUltimas12HorasAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultadosRaw);
        _osRepositoryMock
            .Setup(r => r.GetOrdemServicoByIdWithProjectionAsync(It.IsAny<string[]>(), It.IsAny<string[]>()))
            .ReturnsAsync([]);

        var resultado = await _service.ObterResultadosEnriquecidosAsync("user", "1");

        var os = resultado[0].Equipes[0].OrdensServico[0];
        Assert.Equal("OS-DESCONHECIDA", os.Id);
        Assert.Null(os.NumeroOs);
    }

    // Helpers

    private static List<ResultadoOtimizacaoEquipes> CriarResultadosOtimizacao(
        string[] idsOs, bool incluirAlmoco = false)
    {
        var ordensServico = idsOs
            .Select(id => new OrdemServicoAtendida { Id = id })
            .ToList();

        if (incluirAlmoco)
            ordensServico.Add(new OrdemServicoAtendida { Id = "ALMOCO", IsAlmoco = true });

        return
        [
            new ResultadoOtimizacaoEquipes
            {
                Id = "resultado-1",
                Equipes =
                [
                    new RotaEquipe
                    {
                        Equipe = "EQ-01",
                        OrdensServico = ordensServico
                    }
                ]
            }
        ];
    }

    private static List<IOrdemServico> CriarOsMock(string[] ids)
    {
        return ids.Select(id =>
        {
            var mock = new Mock<IOrdemServico>();
            mock.Setup(o => o.Id).Returns(id);
            mock.Setup(o => o.NumeroOs).Returns($"NUM-{id}");
            return mock.Object;
        }).Cast<IOrdemServico>().ToList();
    }
}
