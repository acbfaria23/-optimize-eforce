using System.Globalization;

namespace Energisa.Eforce.Otimizador.Configuration;

/// <summary>
/// Configurações de filtros de elegibilidade de Ordens de Serviço (OSs).
/// Define a idade máxima de OSs Comerciais/Técnicas, prazo de vencimento para OSs Comerciais
/// e idade máxima de Lotes.
/// 
/// Os valores DEVEM ser carregados via <see cref="CarregarDeParametro"/> a partir do
/// parâmetro "Elegibilidade" antes de cada execução. Não há fallback silencioso —
/// se o parâmetro não estiver na base ou estiver incompleto, o processo é abortado por <see cref="Validar"/>.
/// </summary>
public class ElegibilidadeOsSettings
{
    public const string SectionName = "Elegibilidade";

    /// <summary>
    /// Chave do ParametroConfig (MongoDB) utilizada para carregamento dinâmico via
    /// <see cref="CarregarDeParametro"/>.
    /// </summary>
    public const string ChaveElegibilidadeSettings = "Eforce:ElegibilidadeSettings";

    // ?? Controle de carregamento ??????????????????????????????????????????????

    private static readonly HashSet<string> _chavesCarregadas = [];
    private static readonly Dictionary<string, int> _valoresCarregados = new();

    // ?? Chaves obrigatórias ????????????????????????????????????????????????????

    private static readonly IReadOnlyList<string> _chavesObrigatorias =
    [
        nameof(IdadeMaximaOsComercialTecnicaDias),
        nameof(IdadeMaximaLotesDias),
        nameof(PrazoMaximoVencimentoOsComercialDias)
    ];

    // ?? Parâmetros ?????????????????????????????????????????????????????????????

    /// <summary>
    /// Idade máxima (em dias) das OSs Comerciais e Técnicas que poderão participar da otimização.
    /// Calculado como: dataExecucao - DataSolicitacao (em dias).
    /// Valor 0 considera apenas registros do dia corrente.
    /// Valores negativos são rejeitados.
    /// </summary>
    public int IdadeMaximaOsComercialTecnicaDias
    {
        get => _valoresCarregados.TryGetValue(nameof(IdadeMaximaOsComercialTecnicaDias), out var valor) ? valor : 0;
    }

    /// <summary>
    /// Idade máxima (em dias) dos Lotes (TipoOs = 'X') que poderão participar da otimização.
    /// Calculado como: dataExecucao - DataSolicitacao (em dias).
    /// Valor 0 considera apenas lotes criados no dia corrente.
    /// Valores negativos são rejeitados.
    /// Este parâmetro é independente de <see cref="IdadeMaximaOsComercialTecnicaDias"/>.
    /// </summary>
    public int IdadeMaximaLotesDias
    {
        get => _valoresCarregados.TryGetValue(nameof(IdadeMaximaLotesDias), out var valor) ? valor : 0;
    }

    /// <summary>
    /// Prazo máximo (em dias) para vencimento de OSs Comerciais que poderão participar da otimização.
    /// Calculado como: DataVencimento - dataExecucao (em dias).
    /// Inclui: OSs vencidas, OSs que vencem hoje, OSs com vencimento nos próximos N dias.
    /// Este parâmetro é aplicado apenas a OSs Comerciais (TipoOs = 'C').
    /// OSs Técnicas e Lotes não são filtrados por vencimento.
    /// Valor 0 considera apenas OSs vencidas ou vencendo hoje.
    /// Valores negativos são rejeitados.
    /// </summary>
    public int PrazoMaximoVencimentoOsComercialDias
    {
        get => _valoresCarregados.TryGetValue(nameof(PrazoMaximoVencimentoOsComercialDias), out var valor) ? valor : 0;
    }

    // ?? Carregamento dinâmico ??????????????????????????????????????????????????

    /// <summary>
    /// Carrega os valores de configuração a partir de um dicionário de parâmetros.
    /// Valida que todos os valores sejam inteiros não-negativos.
    /// </summary>
    public static void CarregarDeParametro(IDictionary<string, string?> parametros)
    {
        _chavesCarregadas.Clear();
        _valoresCarregados.Clear();

        foreach (var chave in _chavesObrigatorias)
        {
            if (parametros.TryGetValue(chave, out var valor) && valor is not null)
            {
                if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                {
                    throw new InvalidOperationException(
                        $"Parâmetro '{chave}' é inválido: '{valor}' não é um número inteiro.");
                }

                if (intValue < 0)
                {
                    throw new InvalidOperationException(
                        $"Parâmetro '{chave}' é inválido: '{intValue}' é negativo. " +
                        $"Apenas valores inteiros não-negativos são aceitos.");
                }

                _valoresCarregados[chave] = intValue;
                _chavesCarregadas.Add(chave);
            }
        }
    }

    /// <summary>
    /// Valida que todos os parâmetros obrigatórios foram carregados.
    /// Deve ser chamado após <see cref="CarregarDeParametro"/> e antes de usar os valores.
    /// </summary>
    public static void Validar()
    {
        var naoCarregadas = _chavesObrigatorias
            .Where(chave => !_chavesCarregadas.Contains(chave))
            .ToList();

        if (naoCarregadas.Count != 0)
        {
            throw new InvalidOperationException(
                $"Configuração '{SectionName}' incompleta. Chaves obrigatórias não carregadas: " +
                $"{string.Join(", ", naoCarregadas)}. " +
                $"Verifique o parâmetro '{ChaveElegibilidadeSettings}' no ParametroConfig.");
        }
    }
}
