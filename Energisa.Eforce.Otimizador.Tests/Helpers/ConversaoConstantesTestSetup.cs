using System.Runtime.CompilerServices;

namespace Energisa.Eforce.Otimizador.Tests.Helpers;

/// <summary>
/// Inicializa <see cref="ConversaoConstantes"/> com os valores padrão utilizados
/// antes da migração para carregamento dinâmico via MongoDB (ParametroConfig),
/// garantindo que os testes unitários continuem determinísticos.
/// </summary>
internal static class ConversaoConstantesTestSetup
{
    [ModuleInitializer]
    public static void Inicializar()
    {
        ConversaoConstantes.CarregarDeParametro(new Dictionary<string, string?>
        {
            [nameof(ConversaoConstantes.PesoRiscoVida)] = "100000",
            [nameof(ConversaoConstantes.PesoQtdPessoasAtingidas)] = "1",
            [nameof(ConversaoConstantes.PesoBase)] = "1",
            [nameof(ConversaoConstantes.PesoRegulada)] = "1",
            [nameof(ConversaoConstantes.PesoReicidente)] = "1",
            [nameof(ConversaoConstantes.PesoPrazoQuaseExcedendo)] = "1",
            [nameof(ConversaoConstantes.PesoIdadeOsTecnica)] = "1",
            [nameof(ConversaoConstantes.PesoAtrasoCompensacaoPorHora)] = "2",
            [nameof(ConversaoConstantes.PesoAtrasoCompensacaoNReguladaPorHora)] = "1",
            [nameof(ConversaoConstantes.PesoClienteEssencial)] = "100000",
            [nameof(ConversaoConstantes.TempoParaExecucaoClienteEssencialMinutos)] = "10",
            [nameof(ConversaoConstantes.MultaAtrasoClienteEssencialHora)] = "1000000",
            [nameof(ConversaoConstantes.MultaAtrasoTecnicaHora)] = "100",
            [nameof(ConversaoConstantes.RiscoVidaImportancia)] = "11",
            [nameof(ConversaoConstantes.ClienteEssencialImportancia)] = "11",
            [nameof(ConversaoConstantes.PessoasAtingidasImportancia)] = "10",
            [nameof(ConversaoConstantes.CompensacaoImportancia)] = "9",
            [nameof(ConversaoConstantes.PrioridadeEReguladaImportancia)] = "8",
            [nameof(ConversaoConstantes.ReicidenciaImportancia)] = "7",
            [nameof(ConversaoConstantes.TempoTecnicaImportancia)] = "6",
            [nameof(ConversaoConstantes.PrioridadeImportancia)] = "5",
            [nameof(ConversaoConstantes.EDoLoteImportancia)] = "4",
            [nameof(ConversaoConstantes.OsPrazoImportancia)] = "3",
            [nameof(ConversaoConstantes.MultaAtrasoRiscoVidaHora)] = "1000000",
            [nameof(ConversaoConstantes.MinCostPerHourAfterSoftEnd)] = "1.0",
            [nameof(ConversaoConstantes.MultaAtrasoReguladaHora)] = "1",
            [nameof(ConversaoConstantes.MultaAtrasoNReguladaHora)] = "1",
            [nameof(ConversaoConstantes.PrioridadeUrgente)] = "4",
            [nameof(ConversaoConstantes.PrioridadeAlta)] = "3",
            [nameof(ConversaoConstantes.PrioridadeNormal)] = "2",
            [nameof(ConversaoConstantes.CapacidadeEquipePadrao)] = "60",
            [nameof(ConversaoConstantes.TempoParaExecucaoRiscoVidaMinutos)] = "10",
            [nameof(ConversaoConstantes.DistribuicaoAutomatica)] = "false",
            [nameof(ConversaoConstantes.CustoFixoVeiculo)] = "200",
            [nameof(ConversaoConstantes.DuracaoAlmocoSegundos)] = "3600",
            [nameof(ConversaoConstantes.PenalidadeAlmoco)] = "1000000000",
            [nameof(ConversaoConstantes.MargemAlmocoHoras)] = "1",
            [nameof(ConversaoConstantes.MetrosDiametroCluster)] = "2000",
            [nameof(ConversaoConstantes.MetrosLimiteRotaPorEquipe)] = "100000",
            [nameof(ConversaoConstantes.MetrosLimiteRotaPorEquipeSoft)] = "30000",
            [nameof(ConversaoConstantes.PotenciaPesos)] = "1",
            [nameof(ConversaoConstantes.OSTecnicaImportancia)] = "7",
            [nameof(ConversaoConstantes.CustoKmEquipe)] = "1.5",
            [nameof(ConversaoConstantes.CustoFixoEquipe)] = "0",
            [nameof(ConversaoConstantes.DistanciaParaPenalidade)] = "10"
        });
    }
}
