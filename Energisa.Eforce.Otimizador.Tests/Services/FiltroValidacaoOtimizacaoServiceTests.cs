using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Energisa.Eforce.Otimizador.Tests.Services;

public class FiltroValidacaoOtimizacaoServiceTests
{
    private readonly Mock<ILogger<FiltroValidacaoOtimizacaoService>> _loggerMock;
    private readonly FiltroValidacaoOtimizacaoService _servico;

    public FiltroValidacaoOtimizacaoServiceTests()
    {
        _loggerMock = new Mock<ILogger<FiltroValidacaoOtimizacaoService>>();
        _servico = new FiltroValidacaoOtimizacaoService(_loggerMock.Object);
    }

    /// <summary>
    /// Testa se equipes com horário de almoço válido são mantidas.
    /// </summary>
    [Fact]
    public void AplicarFiltros_ComEquipeHorarioAlmocoValido_MantemaEquipe()
    {
        // Arrange
        var equipeProcesada = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida(),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        var os = TestDataFactory.CriarOsValida();

        var osProcesada = TestDataFactory.CriarOsProcessada(os);
        osProcesada.IndicesEquipesPermitidas = [0];

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [osProcesada],
            EquipesProcessadas = [equipeProcesada],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Single(resultadoFiltrado.EquipesProcessadas);
        Assert.Equal("CGRGA34", resultadoFiltrado.EquipesProcessadas[0].Equipe.CodigoEquipe);
    }

    /// <summary>
    /// Testa se equipes com horário de almoço que excede o tempo global são removidas.
    /// </summary>
    [Fact]
    public void AplicarFiltros_ComEquipeHorarioAlmocoExcedeTempo_RemoveEquipe()
    {
        // Arrange
        var equipeProcesada = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EXCEDE"),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 6, 0, 0, DateTimeKind.Utc), // Antes do início do turno global
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 7, 0, 0, DateTimeKind.Utc)
        };

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [],
            EquipesProcessadas = [equipeProcesada],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Empty(resultadoFiltrado.EquipesProcessadas);
    }

    /// <summary>
    /// Testa se OSs sem equipe compatível são removidas.
    /// </summary>
    [Fact]
    public void AplicarFiltros_ComOsSemEquipeCompativel_RemoveOs()
    {
        // Arrange
        var equipeProcesada = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida(),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        var os = TestDataFactory.CriarOsValida("SEMEQUIPE");

        var osProcesada = TestDataFactory.CriarOsProcessada(os);
        osProcesada.IndicesEquipesPermitidas = [];

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [osProcesada],
            EquipesProcessadas = [equipeProcesada],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Empty(resultadoFiltrado.OrdensProcessadas);
    }

    /// <summary>
    /// Testa se equipes que não podem atender nenhuma OS são removidas.
    /// </summary>
    [Fact]
    public void AplicarFiltros_ComEquipeQueNaoAtendeNenhumaOs_RemoveEquipe()
    {
        // Arrange
        var equipeProcesada1 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE1"),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        var equipeProcesada2 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE2"),
            CodigosRegiao = ["2"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        var os = TestDataFactory.CriarOsValida();

        var osProcesada = TestDataFactory.CriarOsProcessada(os);
        osProcesada.IndicesEquipesPermitidas = [0];

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [osProcesada],
            EquipesProcessadas = [equipeProcesada1, equipeProcesada2],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Single(resultadoFiltrado.EquipesProcessadas);
        Assert.Equal("EQUIPE1", resultadoFiltrado.EquipesProcessadas[0].Equipe.CodigoEquipe);
    }

    /// <summary>
    /// Testa o cenário completo com múltiplas equipes e OSs, removendo as inválidas.
    /// </summary>
    [Fact]
    public void AplicarFiltros_CenarioCompleto_RemoveInvalidas()
    {
        // Arrange
        // Equipe 1: válida
        var equipeProcessada1 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE1"),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        // Equipe 2: almoço antes do turno global (será removida)
        var equipeProcessada2 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE2"),
            CodigosRegiao = ["2"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 6, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 7, 0, 0, DateTimeKind.Utc)
        };

        // Equipe 3: válida mas não atende nenhuma OS (será removida)
        var equipeProcessada3 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE3"),
            CodigosRegiao = ["3"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        // OS 1: pode ser atendida por EQUIPE1
        var os1 = TestDataFactory.CriarOsValida("OS001");
        var osProcessada1 = TestDataFactory.CriarOsProcessada(os1);
        osProcessada1.IndicesEquipesPermitidas = [0];

        // OS 2: sem equipe compatível (será removida)
        var os2 = TestDataFactory.CriarOsValida("OS002");
        var osProcessada2 = TestDataFactory.CriarOsProcessada(os2);
        osProcessada2.IndicesEquipesPermitidas = [];

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [osProcessada1, osProcessada2],
            EquipesProcessadas = [equipeProcessada1, equipeProcessada2, equipeProcessada3],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Single(resultadoFiltrado.EquipesProcessadas);
        Assert.Equal("EQUIPE1", resultadoFiltrado.EquipesProcessadas[0].Equipe.CodigoEquipe);
        Assert.Single(resultadoFiltrado.OrdensProcessadas);
        Assert.Equal("OS001", resultadoFiltrado.OrdensProcessadas[0].Os.NumeroOs);
    }

    /// <summary>
    /// Testa se dados vazios são tratados corretamente.
    /// </summary>
    [Fact]
    public void AplicarFiltros_ComDadosVazios_RetornaVazio()
    {
        // Arrange
        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [],
            EquipesProcessadas = [],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Empty(resultadoFiltrado.OrdensProcessadas);
        Assert.Empty(resultadoFiltrado.EquipesProcessadas);
    }

    /// <summary>
    /// Testa se o fim do almoço após o tempo global é rejeitado.
    /// </summary>
    [Fact]
    public void AplicarFiltros_ComFimAlmocoAposTempoGlobal_RemoveEquipe()
    {
        // Arrange
        var equipeProcesada = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EXCEDENTE"),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 20, 0, 0, DateTimeKind.Utc) // Após fim do turno global
        };

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [],
            EquipesProcessadas = [equipeProcesada],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Empty(resultadoFiltrado.EquipesProcessadas);
    }

    /// <summary>
    /// Testa se os índices das equipes nas OSs são remapeados corretamente após remoção de equipes.
    /// Este é o cenário do bug: quando equipes são removidas, os índices nas OSs devem ser ajustados
    /// para refletir as novas posições na lista filtrada.
    /// </summary>
    [Fact]
    public void AplicarFiltros_RemoveEquipesERemapeiaIndices_IndicesValidos()
    {
        // Arrange
        // Equipe 0: inválida (almoço fora do horário global) - será removida
        var equipeProcessada0 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE0"),
            CodigosRegiao = ["0"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 6, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 7, 0, 0, DateTimeKind.Utc)
        };

        // Equipe 1: válida
        var equipeProcessada1 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE1"),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        // Equipe 2: inválida (almoço fora do horário global) - será removida
        var equipeProcessada2 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE2"),
            CodigosRegiao = ["2"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 20, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 21, 0, 0, DateTimeKind.Utc)
        };

        // Equipe 3: válida
        var equipeProcessada3 = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPE3"),
            CodigosRegiao = ["3"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };

        // OS: referencia as equipes 0, 1, 2, 3
        var os = TestDataFactory.CriarOsValida();
        var osProcessada = TestDataFactory.CriarOsProcessada(os);
        osProcessada.IndicesEquipesPermitidas = [0, 1, 2, 3];

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [osProcessada],
            EquipesProcessadas = [equipeProcessada0, equipeProcessada1, equipeProcessada2, equipeProcessada3],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        // Esperado: 2 equipes (EQUIPE1 e EQUIPE3) e 1 OS
        Assert.Equal(2, resultadoFiltrado.EquipesProcessadas.Count);
        Assert.Single(resultadoFiltrado.OrdensProcessadas);

        // Verificar que as equipes mantidas são as corretas
        Assert.Equal("EQUIPE1", resultadoFiltrado.EquipesProcessadas[0].Equipe.CodigoEquipe);
        Assert.Equal("EQUIPE3", resultadoFiltrado.EquipesProcessadas[1].Equipe.CodigoEquipe);

        // Verificar que os índices foram remapeados: 0, 1 (antigos 1, 3)
        var osRemapeada = resultadoFiltrado.OrdensProcessadas[0];
        Assert.Equal([0, 1], osRemapeada.IndicesEquipesPermitidas);

        // Validar que os índices são válidos (não há out of bounds)
        foreach (var indice in osRemapeada.IndicesEquipesPermitidas)
        {
            Assert.InRange(indice, 0, resultadoFiltrado.EquipesProcessadas.Count - 1);
        }
    }

    /// <summary>
    /// Testa se o filtro retorna OSs vazias quando todos os dados são removidos.
    /// </summary>
    [Fact]
    public void AplicarFiltros_RemoveTodosOsDados_RetornaVazio()
    {
        // Arrange
        // Equipe inválida (almoço fora do horário global) - será removida
        var equipeProcesada = new EquipeProcessada
        {
            Equipe = TestDataFactory.CriarEquipeValida("EQUIPEINVALIDA"),
            CodigosRegiao = ["1"],
            InicioTurnoAjustado = TestDataFactory.InicioTurno,
            FimTurnoAjustado = TestDataFactory.FimTurno,
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 6, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 7, 0, 0, DateTimeKind.Utc)
        };

        // OS: referencia equipe que será removida
        var os = TestDataFactory.CriarOsValida();
        var osProcessada = TestDataFactory.CriarOsProcessada(os);
        osProcessada.IndicesEquipesPermitidas = [0];

        var resultado = new ResultadoConversao
        {
            OrdensProcessadas = [osProcessada],
            EquipesProcessadas = [equipeProcesada],
            InicioTurno = TestDataFactory.InicioTurno,
            FimTurno = TestDataFactory.FimTurno,
            TimeZone = TimeZoneInfo.Utc
        };

        // Act
        var resultadoFiltrado = _servico.AplicarFiltros(resultado, TestDataFactory.InicioTurno, TestDataFactory.FimTurno);

        // Assert
        Assert.Empty(resultadoFiltrado.EquipesProcessadas);
        Assert.Empty(resultadoFiltrado.OrdensProcessadas);
    }
}
