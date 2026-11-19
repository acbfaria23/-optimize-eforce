using System.Globalization;

namespace Energisa.Eforce.Otimizador.Configuration;

/// <summary>
/// Configurações da funcionalidade de reotimização intradiária (§21).
/// Os valores DEVEM ser carregados via <see cref="CarregarDeParametro"/> a partir do
/// parâmetro "Eforce:ReotimizacaoSettings" antes de cada execução. Não há fallback silencioso —
/// se o parâmetro não estiver na base ou estiver incompleto, o processo é abortado por <see cref="Validar"/>.
/// </summary>
public class ReotimizacaoSettings
{
    public const string SectionName = "Reotimizacao";

    /// <summary>
    /// Chave do ParametroConfig (MongoDB) utilizada para carregamento dinâmico via
    /// <see cref="CarregarDeParametro"/>.
    /// </summary>
    public const string ChaveReotimizacaoSettings = "Eforce:ReotimizacaoSettings";

    /// <summary>
    /// Valores conhecidos e aceitos para <see cref="ModoBusca"/>. Qualquer valor fora
    /// desta lista faz <see cref="Validar"/> abortar o processo.
    /// </summary>
    private static readonly HashSet<string> _modosBuscaValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReturnFast",
        "ConsumeAllAvailableTime",
        "Unspecified"
    };

    // ── Controle de carregamento ──────────────────────────────────────────────

    private readonly HashSet<string> _chavesCarregadas = [];

    // ── Chaves obrigatórias ────────────────────────────────────────────────────

    private static readonly IReadOnlyList<string> _chavesObrigatorias =
    [
        nameof(Enabled),
        nameof(MaxIdadePosicaoMinutos),
        nameof(TempoMaximoProcessamentoMinutos),
        nameof(ModoBusca)
    ];

    /// <summary>
    /// Feature flag. Permite habilitar ou desabilitar a reotimização por ambiente/empresa
    /// sem novo deploy. Quando false, a API deve retornar que a funcionalidade está indisponível.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Define por quanto tempo (em minutos) a última coordenada de uma equipe apresentada
    /// é considerada válida. Evita iniciar uma rota usando uma posição muito antiga.
    /// </summary>
    public int MaxIdadePosicaoMinutos { get; set; }

    /// <summary>
    /// Tempo máximo (em minutos) durante o qual uma reotimização pode permanecer em
    /// processamento. Após esse período, um monitor pode marcá-la como expirada ou com falha.
    /// Não representa timeout HTTP, pois o fluxo é assíncrono.
    /// </summary>
    public int TempoMaximoProcessamentoMinutos { get; set; }

    /// <summary>
    /// Define o SearchMode enviado ao GMPRO (ex: "ReturnFast", "ConsumeAllAvailableTime" ou "Unspecified").
    /// Deve aceitar somente valores conhecidos e falhar na inicialização se estiver inválido.
    /// </summary>
    public string? ModoBusca { get; set; }

    /// <summary>
    /// Aplica os valores do dicionário (campo <c>Valores</c> do ParametroConfig) sobre as
    /// propriedades da instância. Registra internamente quais chaves foram carregadas para
    /// validação posterior via <see cref="Validar"/>.
    /// </summary>
    public void CarregarDeParametro(Dictionary<string, string?> valores)
    {
        _chavesCarregadas.Clear();

        void SetBool(string key, Action<bool> setter)
        {
            if (valores.TryGetValue(key, out var raw) && bool.TryParse(raw, out var v))
            {
                setter(v);
                _chavesCarregadas.Add(key);
            }
        }
        void SetInt(string key, Action<int> setter)
        {
            if (valores.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                setter(v);
                _chavesCarregadas.Add(key);
            }
        }
        void SetString(string key, Action<string> setter)
        {
            if (valores.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                setter(raw);
                _chavesCarregadas.Add(key);
            }
        }

        SetBool(nameof(Enabled), v => Enabled = v);
        SetInt(nameof(MaxIdadePosicaoMinutos), v => MaxIdadePosicaoMinutos = v);
        SetInt(nameof(TempoMaximoProcessamentoMinutos), v => TempoMaximoProcessamentoMinutos = v);
        SetString(nameof(ModoBusca), v => ModoBusca = v);
    }

    /// <summary>
    /// Valida que todas as chaves obrigatórias foram carregadas via <see cref="CarregarDeParametro"/>
    /// e que <see cref="ModoBusca"/> possui um valor conhecido.
    /// Lança <see cref="InvalidOperationException"/> listando os problemas encontrados.
    /// </summary>
    /// <exception cref="InvalidOperationException">Quando uma ou mais chaves obrigatórias não foram carregadas ou são inválidas.</exception>
    public void Validar()
    {
        var ausentes = _chavesObrigatorias
            .Where(c => !_chavesCarregadas.Contains(c))
            .ToList();

        if (ausentes.Count > 0)
            throw new InvalidOperationException(
                $"Parametro '{ChaveReotimizacaoSettings}' esta incompleto. " +
                $"Chaves ausentes ou invalidas: {string.Join(", ", ausentes)}");

        if (!_modosBuscaValidos.Contains(ModoBusca))
            throw new InvalidOperationException(
                $"Parametro '{ChaveReotimizacaoSettings}': valor invalido para '{nameof(ModoBusca)}': '{ModoBusca}'. " +
                $"Valores aceitos: {string.Join(", ", _modosBuscaValidos)}.");
    }
}
