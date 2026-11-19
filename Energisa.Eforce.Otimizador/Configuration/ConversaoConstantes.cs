using System.Globalization;

namespace Energisa.Eforce.Otimizador.Configuration;

/// <summary>
/// Parâmetros de peso do otimizador. Todos os valores DEVEM ser carregados via
/// <see cref="CarregarDeParametro"/> antes do uso. Não há valores padrão —
/// se o parâmetro não estiver na base, o processo é abortado por <see cref="Validar"/>.
/// </summary>
public static class ConversaoConstantes
{
    // ?? Controle de carregamento ??????????????????????????????????????????????

    private static readonly HashSet<string> _chavesCarregadas = [];

    // ?? Pesos individuais (§13 / §8.3) ??????????????????????????????????????

    /// <summary>Peso base para OS com risco à vida.</summary>
    public static double PesoRiscoVida { get; set; }

    /// <summary>Multiplicador por pessoa afetada (somente OS Técnica).</summary>
    public static double PesoQtdPessoasAtingidas { get; set; }

    /// <summary>Peso base (não escalado).</summary>
    public static double PesoBase { get; set; }

    /// <summary>Peso para OS regulada.</summary>
    public static double PesoRegulada { get; set; }

    /// <summary>Peso para OS reincidente.</summary>
    public static double PesoReicidente { get; set; }

    /// <summary>Peso para OS próxima do vencimento.</summary>
    public static double PesoPrazoQuaseExcedendo { get; set; }

    /// <summary>Peso por hora de OS técnica aberta.</summary>
    public static double PesoIdadeOsTecnica { get; set; }

    /// <summary>Multiplicador para horas de atraso (OS regulada).</summary>
    public static double PesoAtrasoCompensacaoPorHora { get; set; }

    /// <summary>Multiplicador para horas de atraso (OS não-regulada).</summary>
    public static double PesoAtrasoCompensacaoNReguladaPorHora { get; set; }

    // ?? Cliente Essencial (§13 / §8.3) ??????????????????????????????????????

    /// <summary>Peso base para OS com cliente essencial.</summary>
    public static double PesoClienteEssencial { get; set; }

    /// <summary>Janela em minutos para atendimento de cliente essencial.</summary>
    public static int TempoParaExecucaoClienteEssencialMinutos { get; set; }

    /// <summary>Multa por hora de atraso em cliente essencial.</summary>
    public static double MultaAtrasoClienteEssencialHora { get; set; }

    /// <summary>Multa por hora de atraso (OS Técnica).</summary>
    public static double MultaAtrasoTecnicaHora { get; set; }

    // ?? Hierarquia de importância — expoentes (§13.2) ???????????????????????

    /// <summary>1º nível — Risco à vida.</summary>
    public static int RiscoVidaImportancia { get; set; }

    /// <summary>2º nível — Cliente essencial.</summary>
    public static int ClienteEssencialImportancia { get; set; }

    /// <summary>3º nível — Pessoas atingidas.</summary>
    public static int PessoasAtingidasImportancia { get; set; }

    /// <summary>4º nível — Compensação.</summary>
    public static int CompensacaoImportancia { get; set; }

    /// <summary>5º nível — OS regulada / prazo.</summary>
    public static int PrioridadeEReguladaImportancia { get; set; }

    /// <summary>6º nível — Reincidência.</summary>
    public static int ReicidenciaImportancia { get; set; }

    /// <summary>7º nível — Tempo de OS técnica aberta.</summary>
    public static int TempoTecnicaImportancia { get; set; }

    /// <summary>8º nível — Prioridade operacional.</summary>
    public static int PrioridadeImportancia { get; set; }

    /// <summary>9º nível — OS pertence a lote.</summary>
    public static int EDoLoteImportancia { get; set; }

    /// <summary>10º nível — OS próxima do prazo.</summary>
    public static int OsPrazoImportancia { get; set; }

    /// <summary>11º nível — OS pertence a servico Técnica.</summary>
    public static int OSTecnicaImportancia { get; set; }

    // ?? Multas (§13) ????????????????????????????????????????????????????????

    /// <summary>Multa por hora de atraso em risco à vida.</summary>
    public static double MultaAtrasoRiscoVidaHora { get; set; }

    /// <summary>Valor mínimo para CostPerHourAfterSoftEndTime.</summary>
    public static double MinCostPerHourAfterSoftEnd { get; set; }

    /// <summary>Multa por hora de atraso (OS regulada).</summary>
    public static double MultaAtrasoReguladaHora { get; set; }

    /// <summary>Multa por hora de atraso (OS não-regulada).</summary>
    public static double MultaAtrasoNReguladaHora { get; set; }

    // ?? Prioridades (§13.1) ?????????????????????????????????????????????????

    /// <summary>Valor numérico da prioridade URGENTE.</summary>
    public static int PrioridadeUrgente { get; set; }

    /// <summary>Valor numérico da prioridade ALTA.</summary>
    public static int PrioridadeAlta { get; set; }

    /// <summary>Valor numérico da prioridade NORMAL.</summary>
    public static int PrioridadeNormal { get; set; }

    // ?? Parâmetros operacionais (§13) ???????????????????????????????????????

    /// <summary>Capacidade padrão de visitas (se não informada).</summary>
    public static int CapacidadeEquipePadrao { get; set; }

    /// <summary>Janela em minutos para atendimento de risco à vida.</summary>
    public static int TempoParaExecucaoRiscoVidaMinutos { get; set; }

    /// <summary>Se true, respeita MAX_LOAD na distribuição de lotes.</summary>
    public static bool DistribuicaoAutomatica { get; set; }

    /// <summary>Custo fixo por veículo utilizado.</summary>
    public static int CustoFixoVeiculo { get; set; }

    /// <summary>Duração do almoço em segundos.</summary>
    public static int DuracaoAlmocoSegundos { get; set; }

    /// <summary>Penalidade altíssima para forçar execução do almoço.</summary>
    public static long PenalidadeAlmoco { get; set; }

    /// <summary>Margem em horas antes/depois do almoço na timeWindow.</summary>
    public static int MargemAlmocoHoras { get; set; }

    public static int MetrosDiametroCluster { get; set; }
    public static int MetrosLimiteRotaPorEquipe { get; set; }
    public static int MetrosLimiteRotaPorEquipeSoft { get; set; }
    public static int PotenciaPesos { get; set; }

    /// <summary>Custo por quilômetro rodado pela equipe.</summary>
    public static double CustoKmEquipe { get; set; }

    /// <summary>Custo fixo por equipe utilizada.</summary>
    public static double CustoFixoEquipe { get; set; }

    /// <summary>Distância máxima ideal (em quilômetros) para deslocamentos entre atendimentos.
    /// Quando ultrapassada, uma penalidade é aplicada para desestimular rotas com deslocamentos excessivos.</summary>
    public static int DistanciaParaPenalidade { get; set; }

    public const int AntecedenciaVencimentoNoDiaHoras = 2;

    /// <summary>
    /// Chave única de capacidade (LoadDemands/LoadLimits) utilizada em todo o pipeline,
    /// incluindo a reotimização (§10). Centralizada para evitar divergência de grafia
    /// entre "NUMERO_VISITAS" e variantes incorretas.
    /// </summary>
    public const string ChaveCapacidadeVisitas = "NUMERO_VISITAS";

    // ?? Serviços restritos (§10.3) ??????????????????????????????????????????

    /// <summary>Serviços que exigem equipe com 2 ou mais pessoas.</summary>
    public static readonly string[] ServicosRestritos1Pessoa =
    [
        "3 - Corte no Poste",
        "Corte no Poste",
        "Retirada de Ramal"
    ];

    // ?? Chaves obrigatórias ??????????????????????????????????????????????????

    private static readonly IReadOnlyList<string> _chavesObrigatorias =
    [
        nameof(PesoRiscoVida),
        nameof(PesoQtdPessoasAtingidas),
        nameof(PesoBase),
        nameof(PesoRegulada),
        nameof(PesoReicidente),
        nameof(PesoPrazoQuaseExcedendo),
        nameof(PesoIdadeOsTecnica),
        nameof(PesoAtrasoCompensacaoPorHora),
        nameof(PesoAtrasoCompensacaoNReguladaPorHora),
        nameof(PesoClienteEssencial),
        nameof(TempoParaExecucaoClienteEssencialMinutos),
        nameof(MultaAtrasoClienteEssencialHora),
        nameof(MultaAtrasoTecnicaHora),
        nameof(RiscoVidaImportancia),
        nameof(ClienteEssencialImportancia),
        nameof(PessoasAtingidasImportancia),
        nameof(CompensacaoImportancia),
        nameof(PrioridadeEReguladaImportancia),
        nameof(ReicidenciaImportancia),
        nameof(TempoTecnicaImportancia),
        nameof(PrioridadeImportancia),
        nameof(EDoLoteImportancia),
        nameof(OsPrazoImportancia),
        nameof(MultaAtrasoRiscoVidaHora),
        nameof(MinCostPerHourAfterSoftEnd),
        nameof(MultaAtrasoReguladaHora),
        nameof(MultaAtrasoNReguladaHora),
        nameof(PrioridadeUrgente),
        nameof(PrioridadeAlta),
        nameof(PrioridadeNormal),
        nameof(CapacidadeEquipePadrao),
        nameof(TempoParaExecucaoRiscoVidaMinutos),
        nameof(DistribuicaoAutomatica),
        nameof(CustoFixoVeiculo),
        nameof(DuracaoAlmocoSegundos),
        nameof(PenalidadeAlmoco),
        nameof(MargemAlmocoHoras),
        nameof(MetrosDiametroCluster),
        nameof(MetrosLimiteRotaPorEquipe),
        nameof(MetrosLimiteRotaPorEquipeSoft),
             nameof(PotenciaPesos),
             nameof(OSTecnicaImportancia),
             nameof(CustoKmEquipe),
             nameof(CustoFixoEquipe),
             nameof(DistanciaParaPenalidade)
        ];

    // ?? Serialização / carregamento via MongoDB (Eforce:ValoresPesoOtimizacao) ??????????

    /// <summary>
    /// Retorna um dicionário com o nome e valor atual de todos os campos configuráveis,
    /// no formato esperado pelo campo <c>Valores</c> do ParametroConfig no MongoDB.
    /// </summary>
    public static Dictionary<string, string> ToValoresDictionary() => new()
    {
        [nameof(PesoRiscoVida)]                            = PesoRiscoVida.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoQtdPessoasAtingidas)]                  = PesoQtdPessoasAtingidas.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoBase)]                                 = PesoBase.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoRegulada)]                             = PesoRegulada.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoReicidente)]                           = PesoReicidente.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoPrazoQuaseExcedendo)]                  = PesoPrazoQuaseExcedendo.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoIdadeOsTecnica)]                       = PesoIdadeOsTecnica.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoAtrasoCompensacaoPorHora)]             = PesoAtrasoCompensacaoPorHora.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoAtrasoCompensacaoNReguladaPorHora)]    = PesoAtrasoCompensacaoNReguladaPorHora.ToString(CultureInfo.InvariantCulture),
        [nameof(PesoClienteEssencial)]                     = PesoClienteEssencial.ToString(CultureInfo.InvariantCulture),
        [nameof(TempoParaExecucaoClienteEssencialMinutos)] = TempoParaExecucaoClienteEssencialMinutos.ToString(CultureInfo.InvariantCulture),
        [nameof(MultaAtrasoClienteEssencialHora)]          = MultaAtrasoClienteEssencialHora.ToString(CultureInfo.InvariantCulture),
        [nameof(MultaAtrasoTecnicaHora)]                   = MultaAtrasoTecnicaHora.ToString(CultureInfo.InvariantCulture),
        [nameof(RiscoVidaImportancia)]                     = RiscoVidaImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(ClienteEssencialImportancia)]              = ClienteEssencialImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(PessoasAtingidasImportancia)]              = PessoasAtingidasImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(CompensacaoImportancia)]                   = CompensacaoImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(PrioridadeEReguladaImportancia)]           = PrioridadeEReguladaImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(ReicidenciaImportancia)]                   = ReicidenciaImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(TempoTecnicaImportancia)]                  = TempoTecnicaImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(PrioridadeImportancia)]                    = PrioridadeImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(EDoLoteImportancia)]                       = EDoLoteImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(OsPrazoImportancia)]                       = OsPrazoImportancia.ToString(CultureInfo.InvariantCulture),
        [nameof(MultaAtrasoRiscoVidaHora)]                 = MultaAtrasoRiscoVidaHora.ToString(CultureInfo.InvariantCulture),
        [nameof(MinCostPerHourAfterSoftEnd)]               = MinCostPerHourAfterSoftEnd.ToString(CultureInfo.InvariantCulture),
        [nameof(MultaAtrasoReguladaHora)]                  = MultaAtrasoReguladaHora.ToString(CultureInfo.InvariantCulture),
        [nameof(MultaAtrasoNReguladaHora)]                 = MultaAtrasoNReguladaHora.ToString(CultureInfo.InvariantCulture),
        [nameof(PrioridadeUrgente)]                        = PrioridadeUrgente.ToString(CultureInfo.InvariantCulture),
        [nameof(PrioridadeAlta)]                           = PrioridadeAlta.ToString(CultureInfo.InvariantCulture),
        [nameof(PrioridadeNormal)]                         = PrioridadeNormal.ToString(CultureInfo.InvariantCulture),
        [nameof(CapacidadeEquipePadrao)]                   = CapacidadeEquipePadrao.ToString(CultureInfo.InvariantCulture),
        [nameof(TempoParaExecucaoRiscoVidaMinutos)]        = TempoParaExecucaoRiscoVidaMinutos.ToString(CultureInfo.InvariantCulture),
        [nameof(DistribuicaoAutomatica)]                   = DistribuicaoAutomatica.ToString(CultureInfo.InvariantCulture),
        [nameof(CustoFixoVeiculo)]                         = CustoFixoVeiculo.ToString(CultureInfo.InvariantCulture),
        [nameof(DuracaoAlmocoSegundos)]                    = DuracaoAlmocoSegundos.ToString(CultureInfo.InvariantCulture),
        [nameof(PenalidadeAlmoco)]                         = PenalidadeAlmoco.ToString(CultureInfo.InvariantCulture),
        [nameof(MargemAlmocoHoras)]                        = MargemAlmocoHoras.ToString(CultureInfo.InvariantCulture),
        [nameof(MetrosDiametroCluster)]                    = MetrosDiametroCluster.ToString(CultureInfo.InvariantCulture),
        [nameof(MetrosLimiteRotaPorEquipe)]                = MetrosLimiteRotaPorEquipe.ToString(CultureInfo.InvariantCulture),
        [nameof(MetrosLimiteRotaPorEquipeSoft)]            = MetrosLimiteRotaPorEquipeSoft.ToString(CultureInfo.InvariantCulture),
        [nameof(PotenciaPesos)]                            = PotenciaPesos.ToString(CultureInfo.InvariantCulture),
             [nameof(OSTecnicaImportancia)]                     = OSTecnicaImportancia.ToString(CultureInfo.InvariantCulture),
             [nameof(CustoKmEquipe)]                            = CustoKmEquipe.ToString(CultureInfo.InvariantCulture),
             [nameof(CustoFixoEquipe)]                          = CustoFixoEquipe.ToString(CultureInfo.InvariantCulture),
             [nameof(DistanciaParaPenalidade)]                  = DistanciaParaPenalidade.ToString(CultureInfo.InvariantCulture)
        };

    /// <summary>
    /// Aplica os valores do dicionário (campo <c>Valores</c> do ParametroConfig) sobre as propriedades estáticas.
    /// Registra internamente quais chaves foram carregadas para validação posterior via <see cref="Validar"/>.
    /// </summary>
    public static void CarregarDeParametro(Dictionary<string, string?> valores)
    {
        _chavesCarregadas.Clear();

        void SetDouble(string key, Action<double> setter)
        {
            if (valores.TryGetValue(key, out var raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
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
        void SetLong(string key, Action<long> setter)
        {
            if (valores.TryGetValue(key, out var raw) && long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                setter(v);
                _chavesCarregadas.Add(key);
            }
        }
        void SetBool(string key, Action<bool> setter)
        {
            if (valores.TryGetValue(key, out var raw) && bool.TryParse(raw, out var v))
            {
                setter(v);
                _chavesCarregadas.Add(key);
            }
        }

        SetDouble(nameof(PesoRiscoVida),                         v => PesoRiscoVida = v);
        SetDouble(nameof(PesoQtdPessoasAtingidas),               v => PesoQtdPessoasAtingidas = v);
        SetDouble(nameof(PesoBase),                              v => PesoBase = v);
        SetDouble(nameof(PesoRegulada),                          v => PesoRegulada = v);
        SetDouble(nameof(PesoReicidente),                        v => PesoReicidente = v);
        SetDouble(nameof(PesoPrazoQuaseExcedendo),               v => PesoPrazoQuaseExcedendo = v);
        SetDouble(nameof(PesoIdadeOsTecnica),                    v => PesoIdadeOsTecnica = v);
        SetDouble(nameof(PesoAtrasoCompensacaoPorHora),          v => PesoAtrasoCompensacaoPorHora = v);
        SetDouble(nameof(PesoAtrasoCompensacaoNReguladaPorHora), v => PesoAtrasoCompensacaoNReguladaPorHora = v);
        SetDouble(nameof(PesoClienteEssencial),                  v => PesoClienteEssencial = v);
        SetInt   (nameof(TempoParaExecucaoClienteEssencialMinutos), v => TempoParaExecucaoClienteEssencialMinutos = v);
        SetDouble(nameof(MultaAtrasoClienteEssencialHora),       v => MultaAtrasoClienteEssencialHora = v);
        SetDouble(nameof(MultaAtrasoTecnicaHora),                v => MultaAtrasoTecnicaHora = v);
        SetInt   (nameof(RiscoVidaImportancia),                  v => RiscoVidaImportancia = v);
        SetInt   (nameof(ClienteEssencialImportancia),           v => ClienteEssencialImportancia = v);
        SetInt   (nameof(PessoasAtingidasImportancia),           v => PessoasAtingidasImportancia = v);
        SetInt   (nameof(CompensacaoImportancia),                v => CompensacaoImportancia = v);
        SetInt   (nameof(PrioridadeEReguladaImportancia),        v => PrioridadeEReguladaImportancia = v);
        SetInt   (nameof(ReicidenciaImportancia),                v => ReicidenciaImportancia = v);
        SetInt   (nameof(TempoTecnicaImportancia),               v => TempoTecnicaImportancia = v);
        SetInt   (nameof(PrioridadeImportancia),                 v => PrioridadeImportancia = v);
        SetInt   (nameof(EDoLoteImportancia),                    v => EDoLoteImportancia = v);
        SetInt   (nameof(OsPrazoImportancia),                    v => OsPrazoImportancia = v);
        SetDouble(nameof(MultaAtrasoRiscoVidaHora),              v => MultaAtrasoRiscoVidaHora = v);
        SetDouble(nameof(MinCostPerHourAfterSoftEnd),            v => MinCostPerHourAfterSoftEnd = v);
        SetDouble(nameof(MultaAtrasoReguladaHora),               v => MultaAtrasoReguladaHora = v);
        SetDouble(nameof(MultaAtrasoNReguladaHora),              v => MultaAtrasoNReguladaHora = v);
        SetInt   (nameof(PrioridadeUrgente),                     v => PrioridadeUrgente = v);
        SetInt   (nameof(PrioridadeAlta),                        v => PrioridadeAlta = v);
        SetInt   (nameof(PrioridadeNormal),                      v => PrioridadeNormal = v);
        SetInt   (nameof(CapacidadeEquipePadrao),                v => CapacidadeEquipePadrao = v);
        SetInt   (nameof(TempoParaExecucaoRiscoVidaMinutos),     v => TempoParaExecucaoRiscoVidaMinutos = v);
        SetBool  (nameof(DistribuicaoAutomatica),                v => DistribuicaoAutomatica = v);
        SetInt   (nameof(CustoFixoVeiculo),                      v => CustoFixoVeiculo = v);
        SetInt   (nameof(DuracaoAlmocoSegundos),                 v => DuracaoAlmocoSegundos = v);
        SetLong  (nameof(PenalidadeAlmoco),                      v => PenalidadeAlmoco = v);
        SetInt   (nameof(MargemAlmocoHoras),                     v => MargemAlmocoHoras = v);
        SetInt   (nameof(MetrosDiametroCluster),                 v => MetrosDiametroCluster = v);
        SetInt   (nameof(MetrosLimiteRotaPorEquipe),             v => MetrosLimiteRotaPorEquipe = v);
        SetInt   (nameof(MetrosLimiteRotaPorEquipeSoft),         v => MetrosLimiteRotaPorEquipeSoft = v);
        SetInt   (nameof(PotenciaPesos),                         v => PotenciaPesos = v);
             SetInt   (nameof(OSTecnicaImportancia),                  v => OSTecnicaImportancia = v);
             SetDouble(nameof(CustoKmEquipe),                         v => CustoKmEquipe = v);
             SetDouble(nameof(CustoFixoEquipe),                       v => CustoFixoEquipe = v);
             SetInt   (nameof(DistanciaParaPenalidade),               v => DistanciaParaPenalidade = v);
        }

    /// <summary>
    /// Valida que todas as chaves obrigatórias foram carregadas via <see cref="CarregarDeParametro"/>.
    /// Lança <see cref="InvalidOperationException"/> listando as chaves ausentes caso alguma falte.
    /// </summary>
    /// <exception cref="InvalidOperationException">Quando uma ou mais chaves obrigatórias não foram carregadas.</exception>
    public static void Validar()
    {
        var ausentes = _chavesObrigatorias
            .Where(c => !_chavesCarregadas.Contains(c))
            .ToList();

        if (ausentes.Count > 0)
            throw new InvalidOperationException(
                $"Parametro 'Eforce:ValoresPesoOtimizacao' esta incompleto. " +
                $"Chaves ausentes ou invalidas: {string.Join(", ", ausentes)}");
    }

    // ?? Funções auxiliares de prioridade ?????????????????????????????????????

    /// <summary>
    /// Retorna o valor numérico de uma prioridade textual (§13.1).
    /// </summary>
    public static int ObterValorPrioridade(string prioridade) => prioridade?.ToUpperInvariant() switch
    {
        "URGENTE" => PrioridadeUrgente,
        "ALTA" => PrioridadeAlta,
        "NORMAL" => PrioridadeNormal,
        _ => 0
    };
}
