using Energisa.Eforce.Otimizador.Helpers;
using Energisa.Eforce.Otimizador.Models;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;
using Google.Maps.RouteOptimization.V1;
using Google.Protobuf.WellKnownTypes;
using Google.Type;
using DateTime = System.DateTime;
using VisitRequest = Google.Maps.RouteOptimization.V1.Shipment.Types.VisitRequest;

namespace Energisa.Eforce.Otimizador.Tests.Helpers;

/// <summary>
/// Testes de contrato do request enviado à GMPRO: reproduzem os erros reais de INVALID_REQUEST.
/// </summary>
public class ValidadorRequestGmProTests
{
    [Fact(DisplayName = "Validar_RequestValido_RetornaListaVazia")]
    public void Validar_RequestValido_RetornaListaVazia()
    {
        var request = CriarRequestValido();

        var resultado = ValidadorRequestGmPro.Validar(request);

        Assert.Empty(resultado);
    }

    [Fact(DisplayName = "Validar_RequestNulo_RetornaCritico")]
    public void Validar_RequestNulo_RetornaCritico()
    {
        var resultado = ValidadorRequestGmPro.Validar(null!);

        Assert.Single(resultado);
        Assert.Equal(SeveridadeInconsistencia.Critico, resultado[0].Severidade);
    }

    [Fact(DisplayName = "Validar_SemVeiculosESemShipments_RetornaDoisCriticos")]
    public void Validar_SemVeiculosESemShipments_RetornaDoisCriticos()
    {
        var request = new OptimizeToursRequest { Model = new ShipmentModel() };

        var resultado = ValidadorRequestGmPro.Validar(request);

        Assert.Contains(resultado, i => i.Caminho == "model.vehicles");
        Assert.Contains(resultado, i => i.Caminho == "model.shipments");
    }

    [Fact(DisplayName = "Validar_LongitudeSemSeparadorDecimal_RetornaCriticoComSugestao")]
    public void Validar_LongitudeSemSeparadorDecimal_RetornaCriticoComSugestao()
    {
        var request = CriarRequestValido();
        request.Model.Vehicles[0].StartWaypoint = MontarWaypoint(-7.19695, -3792820);

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal(SeveridadeInconsistencia.Critico, inconsistencia.Severidade);
        Assert.Equal("model.vehicles[0].startWaypoint.longitude", inconsistencia.Caminho);
        Assert.Contains("-37.92820", inconsistencia.Mensagem);
    }

    [Fact(DisplayName = "Validar_LatitudeForaDaFaixaEmShipment_RetornaCritico")]
    public void Validar_LatitudeForaDaFaixaEmShipment_RetornaCritico()
    {
        var request = CriarRequestValido();
        request.Model.Shipments[0].Pickups[0].ArrivalWaypoint = MontarWaypoint(95, -46.6);

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal("model.shipments[0].pickups[0].arrivalWaypoint.latitude", inconsistencia.Caminho);
    }

    [Fact(DisplayName = "Validar_TransitionAttributeSemSrcTag_RetornaCritico")]
    public void Validar_TransitionAttributeSemSrcTag_RetornaCritico()
    {
        var request = CriarRequestValido();
        request.Model.TransitionAttributes.Add(new TransitionAttributes
        {
            ExcludedDstTag = "x",
            DistanceLimit = new DistanceLimit { SoftMaxMeters = 100_000, CostPerKilometerAboveSoftMax = 100 }
        });

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal("model.transitionAttributes[0]", inconsistencia.Caminho);
        Assert.Contains("srcTag", inconsistencia.Mensagem);
    }

    [Fact(DisplayName = "Validar_TransitionAttributeComSrcTagEExcludedSrcTag_RetornaCritico")]
    public void Validar_TransitionAttributeComSrcTagEExcludedSrcTag_RetornaCritico()
    {
        var request = CriarRequestValido();
        request.Model.TransitionAttributes.Add(new TransitionAttributes
        {
            SrcTag = "limit-transition",
            ExcludedSrcTag = "outra",
            DstTag = "limit-transition"
        });

        var resultado = ValidadorRequestGmPro.Validar(request);

        Assert.Single(resultado);
        Assert.Contains("preenchidos", resultado[0].Mensagem);
    }

    [Fact(DisplayName = "Validar_DstTagInexistente_RetornaAlerta")]
    public void Validar_DstTagInexistente_RetornaAlerta()
    {
        var request = CriarRequestValido();
        request.Model.TransitionAttributes.Add(new TransitionAttributes
        {
            SrcTag = "limit-transition",
            DstTag = "ASDF"
        });

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal(SeveridadeInconsistencia.Alerta, inconsistencia.Severidade);
    }

    [Fact(DisplayName = "Validar_AllowedVehicleIndexInexistente_RetornaCritico")]
    public void Validar_AllowedVehicleIndexInexistente_RetornaCritico()
    {
        var request = CriarRequestValido();
        request.Model.Shipments[0].AllowedVehicleIndices.Add(5);

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal("model.shipments[0].allowedVehicleIndices", inconsistencia.Caminho);
    }

    [Fact(DisplayName = "Validar_ShipmentSemPickupESemDelivery_RetornaCritico")]
    public void Validar_ShipmentSemPickupESemDelivery_RetornaCritico()
    {
        var request = CriarRequestValido();
        request.Model.Shipments[0].Pickups.Clear();

        var resultado = ValidadorRequestGmPro.Validar(request);

        Assert.Single(resultado);
        Assert.Equal("model.shipments[0]", resultado[0].Caminho);
    }

    [Fact(DisplayName = "Validar_JanelaDeVeiculoComFimAnteriorAoInicio_RetornaCritico")]
    public void Validar_JanelaDeVeiculoComFimAnteriorAoInicio_RetornaCritico()
    {
        var request = CriarRequestValido();
        request.Model.Vehicles[0].EndTimeWindows.Add(new TimeWindow
        {
            StartTime = new Timestamp { Seconds = 2_000 },
            EndTime = new Timestamp { Seconds = 1_000 }
        });

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal("model.vehicles[0].endTimeWindows[0]", inconsistencia.Caminho);
    }

    [Fact(DisplayName = "Validar_LabelsDeVeiculoDuplicados_RetornaAlerta")]
    public void Validar_LabelsDeVeiculoDuplicados_RetornaAlerta()
    {
        var request = CriarRequestValido();
        request.Model.Vehicles.Add(request.Model.Vehicles[0].Clone());

        var resultado = ValidadorRequestGmPro.Validar(request);

        var inconsistencia = Assert.Single(resultado);
        Assert.Equal(SeveridadeInconsistencia.Alerta, inconsistencia.Severidade);
        Assert.Equal("model.vehicles[1]", inconsistencia.Caminho);
    }

    [Fact(DisplayName = "Validar_RequestGeradoPeloBuilder_NaoPossuiInconsistenciasCriticas")]
    public void Validar_RequestGeradoPeloBuilder_NaoPossuiInconsistenciasCriticas()
    {
        var builder = new GmProBuilderService();
        var ordens = new List<OsProcessada> { TestDataFactory.CriarOsProcessada() };
        var equipes = new List<EquipeProcessada> { TestDataFactory.CriarEquipeProcessada() };
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

        var request = builder.Build(
            ordens, equipes,
            new DateTime(2025, 1, 15), new DateTime(2025, 1, 15),
            "test-project", "test-label", modoAsync: false, timeZone: timeZone);

        var resultado = ValidadorRequestGmPro.Validar(request);

        Assert.DoesNotContain(resultado, i => i.Severidade == SeveridadeInconsistencia.Critico);
    }

    private static OptimizeToursRequest CriarRequestValido()
    {
        var veiculo = new Vehicle
        {
            Label = "LV-EQ1, PERFIL: Faz tudo",
            StartWaypoint = MontarWaypoint(-23.55, -46.63),
        };
        veiculo.StartTimeWindows.Add(new TimeWindow
        {
            StartTime = new Timestamp { Seconds = 1_000 },
            EndTime = new Timestamp { Seconds = 1_000 }
        });

        var visita = new VisitRequest
        {
            ArrivalWaypoint = MontarWaypoint(-23.56, -46.64),
            Tags = { "limit-transition" }
        };
        var shipment = new Shipment { Label = "OS-1" };
        shipment.Pickups.Add(visita);

        var modelo = new ShipmentModel
        {
            GlobalStartTime = new Timestamp { Seconds = 0 },
            GlobalEndTime = new Timestamp { Seconds = 100_000 }
        };
        modelo.Vehicles.Add(veiculo);
        modelo.Shipments.Add(shipment);

        return new OptimizeToursRequest { Model = modelo };
    }

    private static Waypoint MontarWaypoint(double latitude, double longitude) => new()
    {
        Location = new Location { LatLng = new LatLng { Latitude = latitude, Longitude = longitude } }
    };
}
