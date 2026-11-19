using System.Runtime.CompilerServices;

namespace Energisa.Eforce.Otimizador.Tests.Helpers;

/// <summary>
/// Inicializa <see cref="ElegibilidadeOsSettings"/> com os valores padrão utilizados
/// nos testes unitários, garantindo que os testes sejam determinísticos.
/// </summary>
internal static class ElegibilidadeOsSettingsTestSetup
{
    [ModuleInitializer]
    public static void Inicializar()
    {
        ElegibilidadeOsSettings.CarregarDeParametro(new Dictionary<string, string?>
        {
            [nameof(ElegibilidadeOsSettings.IdadeMaximaOsComercialTecnicaDias)] = "30",
            [nameof(ElegibilidadeOsSettings.IdadeMaximaLotesDias)] = "2",
            [nameof(ElegibilidadeOsSettings.PrazoMaximoVencimentoOsComercialDias)] = "3"
        });

        ElegibilidadeOsSettings.Validar();
    }
}
