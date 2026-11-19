using Energisa.Context.Interface;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Energisa.Eforce.Otimizador.Services;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Model.Geral;
using Energisa.God.Domain.Repository._OrdemServico_.Interface;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Energisa.God.Domain.Repository.Config;
using Energisa.God.Domain.Repository.Modelo.Interface;
using Energisa.God.Domain.Service.Alocacao.Interface;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class ReotimizacaoDataServiceTests
{
    private readonly Mock<IOtimizadorRotasRepository> _otimizadorRotasRepositoryMock = new();
    private readonly Mock<IOrdemServicoRepository> _osRepositoryMock = new();
    private readonly Mock<IEquipeRepository> _equipeRepositoryMock = new();
    private readonly Mock<IPerfilEquipeRepository> _perfilEquipeRepositoryMock = new();
    private readonly Mock<IParametroConfigRepository> _parametroConfigRepositoryMock = new();
    private readonly Mock<ITempoMedioExecucaoService> _tempoMedioExecucaoServiceMock = new();
    private readonly Mock<IContextRepository> _contextMock = new();

    /// <summary>
    /// Configura o mock de <see cref="IParametroConfigRepository"/> para retornar valores válidos
    /// para o parâmetro "Eforce:ReotimizacaoSettings", simulando o cenário em que a parametrização
    /// dinâmica (MongoDB) está corretamente configurada.
    /// </summary>
    private void ConfigurarParametroReotimizacaoValido(bool enabled = true, string modoBusca = "ReturnFast")
    {
        _parametroConfigRepositoryMock
            .Setup(r => r.ObterPorChaveAsync(ReotimizacaoSettings.ChaveReotimizacaoSettings))
            .ReturnsAsync(new ParametroConfig
            {
                Chave = ReotimizacaoSettings.ChaveReotimizacaoSettings,
                Valores = new Dictionary<string, string?>
                {
                    [nameof(ReotimizacaoSettings.Enabled)] = enabled.ToString(),
                    [nameof(ReotimizacaoSettings.MaxIdadePosicaoMinutos)] = "15",
                    [nameof(ReotimizacaoSettings.TempoMaximoProcessamentoMinutos)] = "30",
                    [nameof(ReotimizacaoSettings.ModoBusca)] = modoBusca
                }
            });
    }

    private ReotimizacaoDataService CriarService()
    {
        var googleCloudStorageService = new GoogleCloudStorageService(
            StorageClient.Create(Google.Apis.Auth.OAuth2.GoogleCredential.FromAccessToken("fake-token")),
            new Mock<ILogger<GoogleCloudStorageService>>().Object);

        var orchestrator = new EforceParaGmproOrchestrator(new Mock<ILogger<EforceParaGmproOrchestrator>>().Object);
        var builderService = new GmProBuilderService();
        var classificador = new ClassificadorOrdensReotimizacaoService(new Mock<ILogger<ClassificadorOrdensReotimizacaoService>>().Object);

        var reotimizacaoSettings = Options.Create(new ReotimizacaoSettings());

        _contextMock.SetupGet(c => c.Empresa).Returns(1);
        _contextMock.SetupGet(c => c.User).Returns("user-teste");

        return new ReotimizacaoDataService(
            _otimizadorRotasRepositoryMock.Object,
            _osRepositoryMock.Object,
            _equipeRepositoryMock.Object,
            _perfilEquipeRepositoryMock.Object,
            _parametroConfigRepositoryMock.Object,
            _tempoMedioExecucaoServiceMock.Object,
            googleCloudStorageService,
            orchestrator,
            builderService,
            classificador,
            reotimizacaoSettings,
            _contextMock.Object,
            new Mock<ILogger<ReotimizacaoDataService>>().Object);
    }

    [Fact]
    public async Task MontarContextoAsync_ParametroReotimizacaoNaoEncontrado_LancaInvalidOperationException()
    {
        var service = CriarService();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-1",
            Equipes = ["EQ-01"]
        };

        _parametroConfigRepositoryMock
            .Setup(r => r.ObterPorChaveAsync(ReotimizacaoSettings.ChaveReotimizacaoSettings))
            .ReturnsAsync((ParametroConfig?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains(ReotimizacaoSettings.ChaveReotimizacaoSettings, ex.Message);
        _otimizadorRotasRepositoryMock.Verify(
            r => r.ObterCenarioCompletoParaReotimizacaoAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task MontarContextoAsync_ParametroReotimizacaoIncompleto_LancaInvalidOperationException()
    {
        var service = CriarService();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-1",
            Equipes = ["EQ-01"]
        };

        _parametroConfigRepositoryMock
            .Setup(r => r.ObterPorChaveAsync(ReotimizacaoSettings.ChaveReotimizacaoSettings))
            .ReturnsAsync(new ParametroConfig
            {
                Chave = ReotimizacaoSettings.ChaveReotimizacaoSettings,
                Valores = new Dictionary<string, string?>
                {
                    [nameof(ReotimizacaoSettings.Enabled)] = "true"
                    // MaxIdadePosicaoMinutos, TempoMaximoProcessamentoMinutos e ModoBusca ausentes.
                }
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains(nameof(ReotimizacaoSettings.ModoBusca), ex.Message);
    }

    [Fact]
    public async Task MontarContextoAsync_FuncionalidadeDesabilitada_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido(enabled: false);
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-1",
            Equipes = ["EQ-01"]
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("desabilitada", ex.Message, StringComparison.OrdinalIgnoreCase);
        _otimizadorRotasRepositoryMock.Verify(
            r => r.ObterCenarioCompletoParaReotimizacaoAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task MontarContextoAsync_OtimizacaoOriginalIdVazio_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "",
            Equipes = ["EQ-01"]
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("OtimizacaoOriginalId", ex.Message);
    }

    [Fact]
    public async Task MontarContextoAsync_ListaDeEquipesNulaOuVazia_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-1",
            Equipes = []
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("equipe", ex.Message, StringComparison.OrdinalIgnoreCase);
        _otimizadorRotasRepositoryMock.Verify(
            r => r.ObterCenarioCompletoParaReotimizacaoAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task MontarContextoAsync_IdempotencyKeyVazia_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-1",
            Equipes = ["EQ-01"],
            IdempotencyKey = ""
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("Idempotency-Key", ex.Message);
        _otimizadorRotasRepositoryMock.Verify(
            r => r.ObterCenarioCompletoParaReotimizacaoAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task MontarContextoAsync_OtimizacaoOriginalNaoEncontrada_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-INEXISTENTE",
            Equipes = ["EQ-01"],
            IdempotencyKey = "idem-1"
        };

        _otimizadorRotasRepositoryMock
            .Setup(r => r.ObterCenarioCompletoParaReotimizacaoAsync("OTIM-INEXISTENTE"))
            .ReturnsAsync((ResultadoOtimizacao?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("não encontrada", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MontarContextoAsync_OtimizacaoOriginalEhElaMesmaReotimizacao_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-REOTIMIZACAO",
            Equipes = ["EQ-01"],
            IdempotencyKey = "idem-reotimizacao"
        };

        _otimizadorRotasRepositoryMock
            .Setup(r => r.ObterCenarioCompletoParaReotimizacaoAsync("OTIM-REOTIMIZACAO"))
            .ReturnsAsync(new ResultadoOtimizacao
            {
                TipoOtimizacao = TipoOtimizacao.Reotimizacao,
                ReferenciaInput = new ReferenciaArmazenamentoGcs(),
                ReferenciaOutput = new ReferenciaArmazenamentoGcs()
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("reotimização", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("§4", ex.Message);
    }

    [Fact]
    public async Task MontarContextoAsync_CenarioOriginalSemReferenciasGcs_LancaInvalidOperationException()
    {
        var service = CriarService();
        ConfigurarParametroReotimizacaoValido();
        var command = new SolicitarReotimizacaoCommand
        {
            OtimizacaoOriginalId = "OTIM-SEM-REFERENCIA",
            Equipes = ["EQ-01"],
            IdempotencyKey = "idem-2"
        };

        _otimizadorRotasRepositoryMock
            .Setup(r => r.ObterCenarioCompletoParaReotimizacaoAsync("OTIM-SEM-REFERENCIA"))
            .ReturnsAsync(new ResultadoOtimizacao
            {
                ReferenciaInput = null,
                ReferenciaOutput = null
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MontarContextoAsync(command, CancellationToken.None));

        Assert.Contains("referências GCS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
