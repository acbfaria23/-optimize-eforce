using System.Runtime.CompilerServices;
using Energisa.Eforce.Otimizador.Configuration;

namespace Energisa.Eforce.Otimizador.Tests.Helpers;

/// <summary>
/// Inicializa <see cref="ReotimizacaoSettings"/> com valores padrão para testes unitários,
/// garantindo que os testes sejam determinísticos e não dependam de parâmetros MongoDB
/// ou configurações externas durante execução isolada.
/// Segue o mesmo padrão de <see cref="ConversaoConstantesTestSetup"/>.
/// </summary>
internal static class ReotimizacaoSettingsTestSetup
{
    [ModuleInitializer]
    public static void Inicializar()
    {
        // Não há inicialização global necessária para ReotimizacaoSettings,
        // pois cada teste cria sua própria instância via Options.Create(new ReotimizacaoSettings()).
        // Esta classe permanece como placeholder para manter consistência com ConversaoConstantesTestSetup.
    }
}
