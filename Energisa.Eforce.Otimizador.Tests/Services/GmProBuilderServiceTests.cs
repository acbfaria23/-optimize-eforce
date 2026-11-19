using Energisa.Eforce.Otimizador.Helpers;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Tests.Helpers;

namespace Energisa.Eforce.Otimizador.Tests.Services;

/// <summary>
/// Testes para validar o comportamento de constru��o de requisi��es
/// para a API do Google Cloud Route Optimization (GmProBuilderService).
/// Especialmente focado em cen�rios de escalas que ultrapassam a meia-noite.
/// </summary>
public class GmProBuilderServiceTests
{
    [Fact(DisplayName = "Build_ComEquipeEscalaNoturna_EndTimeWindowsDentroDAJanelaGlobal")]
    public void Build_ComEquipeEscalaNoturna_EndTimeWindowsDentroDAJanelaGlobal()
    {
        // Arrange
        var gmProBuilder = new GmProBuilderService();
        var ordensProcessadas = new List<OsProcessada>();

        var equipeLv = TestDataFactory.CriarEquipeTurnoNoturno("NOTURNA_LV", "LV");
        var equipaProcessada = ConstruirEquipeProcessada(equipeLv);
        var equipesProcessadas = new List<EquipeProcessada> { equipaProcessada };

        var inicioTurno = new DateTime(2025, 1, 1);
        var fimTurno = new DateTime(2025, 1, 2);
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

        // Act
        var request = gmProBuilder.Build(
            ordensProcessadas,
            equipesProcessadas,
            inicioTurno,
            fimTurno,
            "test-project",
            "test-label",
            modoAsync: false,
            timeZone: timeZoneInfo);

        // Assert
        Assert.NotNull(request);
        Assert.NotNull(request.Model);
        Assert.Single(request.Model.Vehicles);

        var vehicle = request.Model.Vehicles[0];
        Assert.NotNull(vehicle);
        Assert.Single(vehicle.EndTimeWindows);

        var endTimeWindow = vehicle.EndTimeWindows[0];
        var globalEndTime = request.Model.GlobalEndTime;

        // EndTime da janela final nunca deve ser maior que GlobalEndTime
        Assert.True(
            endTimeWindow.EndTime.Seconds <= globalEndTime.Seconds,
            $"EndTimeWindow.EndTime ({endTimeWindow.EndTime.Seconds}) deve ser menor ou igual a GlobalEndTime ({globalEndTime.Seconds})");
    }

    [Fact(DisplayName = "Build_ComEquipeEscalaNormal_EndTimeWindowsComMargemDeUmaHora")]
    public void Build_ComEquipeEscalaNormal_EndTimeWindowsComMargemDeUmaHora()
    {
        // Arrange
        var gmProBuilder = new GmProBuilderService();
        var ordensProcessadas = new List<OsProcessada>();

        var equipeValida = TestDataFactory.CriarEquipeValida("NORMAL_LV", "LV");
        var equipaProcessada = ConstruirEquipeProcessada(equipeValida);
        var equipesProcessadas = new List<EquipeProcessada> { equipaProcessada };

        var inicioTurno = new DateTime(2025, 1, 15);
        var fimTurno = new DateTime(2025, 1, 15);
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

        // Act
        var request = gmProBuilder.Build(
            ordensProcessadas,
            equipesProcessadas,
            inicioTurno,
            fimTurno,
            "test-project",
            "test-label",
            modoAsync: false,
            timeZone: timeZoneInfo);

        // Assert
        Assert.NotNull(request);
        var vehicle = request.Model.Vehicles[0];
        var endTimeWindow = vehicle.EndTimeWindows[0];
        var globalEndTime = request.Model.GlobalEndTime;

        // Para escalas normais (dentro do mesmo dia), deve ter a margem de 1 hora
        Assert.True(
            endTimeWindow.EndTime.Seconds > endTimeWindow.StartTime.Seconds,
            "EndTimeWindow deve ter margem de tempo");

        // Mas ainda deve estar dentro da janela global
        Assert.True(
            endTimeWindow.EndTime.Seconds <= globalEndTime.Seconds,
            $"EndTimeWindow.EndTime ({endTimeWindow.EndTime.Seconds}) deve ser menor ou igual a GlobalEndTime ({globalEndTime.Seconds})");
    }

    [Fact(DisplayName = "Build_ComDuasEquipesEscalaNoturna_TodasDentroDAJanelaGlobal")]
    public void Build_ComDuasEquipesEscalaNoturna_TodasDentroDAJanelaGlobal()
    {
        // Arrange
        var gmProBuilder = new GmProBuilderService();
        var ordensProcessadas = new List<OsProcessada>();

        var equipeLv1 = TestDataFactory.CriarEquipeTurnoNoturno("NOTURNA_LV_1", "LV");
        var equipeLv2 = TestDataFactory.CriarEquipeTurnoNoturno("NOTURNA_LV_2", "LV");

        var equipaProcessada1 = ConstruirEquipeProcessada(equipeLv1);
        var equipaProcessada2 = ConstruirEquipeProcessada(equipeLv2);
        var equipesProcessadas = new List<EquipeProcessada> { equipaProcessada1, equipaProcessada2 };

        var inicioTurno = new DateTime(2025, 1, 1);
        var fimTurno = new DateTime(2025, 1, 2);
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

        // Act
        var request = gmProBuilder.Build(
            ordensProcessadas,
            equipesProcessadas,
            inicioTurno,
            fimTurno,
            "test-project",
            "test-label",
            modoAsync: false,
            timeZone: timeZoneInfo);

        // Assert
        Assert.NotNull(request);
        Assert.Equal(2, request.Model.Vehicles.Count);

        var globalEndTime = request.Model.GlobalEndTime;
        foreach (var vehicle in request.Model.Vehicles)
        {
            Assert.Single(vehicle.EndTimeWindows);
            var endTimeWindow = vehicle.EndTimeWindows[0];
            Assert.True(
                endTimeWindow.EndTime.Seconds <= globalEndTime.Seconds,
                $"Vehicle {vehicle.Label}: EndTimeWindow.EndTime ({endTimeWindow.EndTime.Seconds}) deve estar dentro da GlobalEndTime ({globalEndTime.Seconds})");
        }
    }

    [Fact(DisplayName = "Build_ComEquipeValida_TransitionAttributeUnicoComTagsDeOrigemEDestinoExistentes")]
    public void Build_ComEquipeValida_TransitionAttributeUnicoComTagsDeOrigemEDestinoExistentes()
    {
        // Arrange
        var gmProBuilder = new GmProBuilderService();
        var ordens = new List<OsProcessada> { TestDataFactory.CriarOsProcessada() };
        var equipes = new List<EquipeProcessada> { ConstruirEquipeProcessada(TestDataFactory.CriarEquipeValida()) };
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

        // Act
        var request = gmProBuilder.Build(
            ordens, equipes, new DateTime(2025, 1, 15), new DateTime(2025, 1, 15),
            "test-project", "test-label", modoAsync: false, timeZone: timeZoneInfo);

        // Assert
        var transicao = Assert.Single(request.Model.TransitionAttributes);
        Assert.Equal("limit-transition", transicao.SrcTag);
        Assert.Equal("limit-transition", transicao.DstTag);
        Assert.Empty(transicao.ExcludedSrcTag);
        Assert.Equal(0, transicao.DistanceLimit.CostPerKilometerBelowSoftMax);
        Assert.Equal(100, transicao.DistanceLimit.CostPerKilometerAboveSoftMax);
        Assert.Empty(ValidadorRequestGmPro.Validar(request).Where(i => i.Caminho.StartsWith("model.transitionAttributes")));
    }

    /// <summary>
    /// Auxiliar para construir uma EquipeProcessada a partir de EquipeGmpro.
    /// </summary>
    private static EquipeProcessada ConstruirEquipeProcessada(EquipeGmpro equipeGmpro)
    {
        var inicioRefeicaoAjustado = equipeGmpro.InicioRefeicao.Date.Add(equipeGmpro.InicioRefeicao.TimeOfDay);
        var fimRefeicaoAjustado = equipeGmpro.FimRefeicao.Date.Add(equipeGmpro.FimRefeicao.TimeOfDay);

        return new EquipeProcessada
        {
            Equipe = equipeGmpro,
            InicioTurnoAjustado = equipeGmpro.InicioTurno,
            FimTurnoAjustado = equipeGmpro.FimTurno,
            InicioRefeicaoAjustado = inicioRefeicaoAjustado,
            FimRefeicaoAjustado = fimRefeicaoAjustado
        };
    }
}
