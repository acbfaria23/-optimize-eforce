using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model.Modelo;
using Microsoft.Extensions.Logging;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Helpers;
using Energisa.Eforce.Otimizador.Models.Eforce;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Dados de entrada do E-force para a conversão.
/// </summary>
public class DadosEforce
{
    public List<OrdemServicoGmpro> OrdensServico { get; set; } = [];
    public List<EquipeGmpro> Equipes { get; set; } = [];
    public List<PerfilEquipeGmpro> PerfisEquipe { get; set; } = [];
    public List<RegiaoGmpro> Regioes { get; set; } = [];
}

/// <summary>
/// Resultado detalhado da conversão E-force ? GMPRO contendo dados intermediários
/// necessários para o builder nativo e pós-processamento.
/// </summary>
public class ResultadoConversao
{
    public List<OsProcessada> OrdensProcessadas { get; set; } = [];
    public List<EquipeProcessada> EquipesProcessadas { get; set; } = [];
    public DateTime InicioTurno { get; set; }
    public DateTime FimTurno { get; set; }
    public TimeZoneInfo TimeZone { get; set; }
}

/// <summary>
/// Modelo intermediário de equipe com região e índice no array de vehicles.
/// </summary>
public class EquipeIndexada
{
    public int IndiceVehicle { get; set; }
    public EquipeGmpro Equipe { get; set; } = null!;
    public List<string> CodigosRegiao { get; set; } = [];
}

/// <summary>
/// Serviço unificado de conversão E-force ? Google GMPRO.
/// Consolida validação, filtros, processamento de equipes, ajuste de datas,
/// cálculo de pesos e atribuição de equipes às OS.
/// </summary>
public class EforceParaGmproOrchestrator
{
    private readonly ILogger<EforceParaGmproOrchestrator> _logger;
    private readonly FiltroElegibilidadeOsService? _filtroElegibilidade;

    public EforceParaGmproOrchestrator(
        ILogger<EforceParaGmproOrchestrator> logger,
        FiltroElegibilidadeOsService? filtroElegibilidade = null)
    {
        _logger = logger;
        _filtroElegibilidade = filtroElegibilidade;
    }

    /// <summary>
    /// Executa a conversão dos dados E-force e retorna os dados intermediários
    /// necessários para o builder nativo Protobuf e pós-processamento.
    /// </summary>
    public ResultadoConversao ConverterComDetalhes(DadosEforce dados, DateTime dataExecucao, string label = "Cenario Hierarquia V2", int? empresa = null, bool flexibilizarRefeicao = false)
    {
        _logger.LogInformation("Iniciando conversão E-force ? GMPRO. OS: {Os}, Equipes: {Equipes}, Perfis: {Perfis}, Regiões: {Regioes}.",
            dados.OrdensServico.Count, dados.Equipes.Count, dados.PerfisEquipe.Count, dados.Regioes.Count);

        // Passo 1: Validações e filtros (§15)
        _logger.LogInformation("Passo 1/5: Validações e filtros...");
        var equipesValidas = FiltrarEquipes(dados.Equipes);
        var ordensValidas = FiltrarOrdensServico(dados.OrdensServico);
        AplicarSufixoIdentificacao(ordensValidas);

        // Passo 1.5: Aplicar filtros de elegibilidade (idade das OSs e Lotes, vencimento das OSs)
        if (_filtroElegibilidade is not null)
        {
            _logger.LogInformation("Passo 1.5/5: Aplicando filtros de elegibilidade...");
            ordensValidas = _filtroElegibilidade.FiltrarPorIdadeOsComercialTecnica(ordensValidas, dataExecucao);
            ordensValidas = _filtroElegibilidade.FiltrarPorVencimentoOsComercial(ordensValidas, dataExecucao);
            ordensValidas = _filtroElegibilidade.FiltrarPorIdadeLotes(ordensValidas, dataExecucao);
        }

        // Passo 2: Processar equipes (§5)
        _logger.LogInformation("Passo 2/5: Processamento de equipes...");
        var equipesProcessadas = ProcessarEquipes(equipesValidas, dados.Regioes, dataExecucao, flexibilizarRefeicao);

        var inicioTurno = equipesProcessadas.Count > 0
            ? equipesProcessadas.Min(e => e.InicioTurnoAjustado)
            : dataExecucao;
        var fimTurno = equipesProcessadas.Count > 0
            ? equipesProcessadas.Max(e => e.FimTurnoAjustado)
            : dataExecucao.AddHours(9);

        // Passo 3: Ajuste de datas (§9)
        _logger.LogInformation("Passo 3/5: Ajuste de datas...");
        var timeZone = ObterTimeZonePorEmpresa(empresa);
        var dataExecucaoLocal = TimeZoneInfo.ConvertTimeFromUtc(dataExecucao, timeZone);
        var inicioDiaOtimizacaoUtc = TimeZoneInfo.ConvertTimeToUtc(dataExecucaoLocal.Date, timeZone);
        var inicioDiaSeguinteUtc = inicioDiaOtimizacaoUtc.AddDays(1);
        var ordensProcessadas = ProcessarAjusteDatas(ordensValidas, dados.PerfisEquipe, inicioTurno, fimTurno, inicioDiaOtimizacaoUtc, inicioDiaSeguinteUtc);

        // Passo 4: Calcular pesos (§8)
        _logger.LogInformation("Passo 4/5: Cálculo de pesos (penaltyCost)...");
        CalcularPesos(ordensProcessadas, inicioTurno);

        // Passo 5: Atribuir equipes às OS (§10)
        _logger.LogInformation("Passo 5/5: Atribuição OS ? Equipe...");
        var equipesIndexadas = equipesProcessadas
            .Select((ep, idx) => new EquipeIndexada
            {
                IndiceVehicle = idx,
                Equipe = ep.Equipe,
                CodigosRegiao = ep.CodigosRegiao
            })
            .ToList();
        AtribuirEquipes(ordensProcessadas, equipesIndexadas);

        _logger.LogInformation("Conversão E-force ? GMPRO concluída. OS processadas: {Os}, Equipes processadas: {Equipes}.",
            ordensProcessadas.Count, equipesProcessadas.Count);

        return new ResultadoConversao
        {
            OrdensProcessadas = ordensProcessadas,
            EquipesProcessadas = equipesProcessadas,
            InicioTurno = inicioTurno,
            FimTurno = fimTurno,
            TimeZone = timeZone
        };
    }

    private static TimeZoneInfo ObterTimeZonePorEmpresa(int? empresa)
    {
        var timeZoneId = empresa switch
        {
            226 => "SA Western Standard Time",       // Acre (UTC-5)
            191 or 193 => "Central Brazilian Standard Time", // Mato Grosso (UTC-4)
            _ => "E. South America Standard Time"    // São Paulo (UTC-3)
        };

        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    /// <summary>
    /// Resolve o fuso horário da empresa (mesma regra utilizada na conversão completa),
    /// para uso pelo <c>ReotimizacaoDataService</c> (§5).
    /// </summary>
    public TimeZoneInfo ResolverTimeZonePorEmpresa(int? empresa) => ObterTimeZonePorEmpresa(empresa);

    /// <summary>
    /// Processa uma lista de novas OSs pendentes para uso na reotimização intradiária (§12):
    /// aplica os mesmos filtros, sufixo de identificação, ajuste de datas e cálculo de pesos
    /// usados na otimização completa, restringindo a atribuição de equipes apenas às equipes
    /// selecionadas (já convertidas para os índices de vehicle do request original, §9).
    /// </summary>
    public List<OsProcessada> ProcessarNovasOrdensParaReotimizacao(
        List<OrdemServicoGmpro> novasOrdens,
        List<PerfilEquipeGmpro> perfis,
        List<EquipeIndexada> equipesIndexadas,
        DateTime inicioTurno,
        DateTime fimTurno,
        DateTime dataCorteUtc)
    {
        if (novasOrdens.Count == 0) return [];

        _logger.LogInformation("Processando {Total} nova(s) OS pendente(s) para reotimização...", novasOrdens.Count);

        var ordensValidas = FiltrarOrdensServico(novasOrdens);
        AplicarSufixoIdentificacao(ordensValidas);

        var inicioDiaOtimizacaoUtc = dataCorteUtc.Date;
        var inicioDiaSeguinteUtc = inicioDiaOtimizacaoUtc.AddDays(1);

        var ordensProcessadas = ProcessarAjusteDatas(
            ordensValidas, perfis, inicioTurno, fimTurno, inicioDiaOtimizacaoUtc, inicioDiaSeguinteUtc);

        CalcularPesos(ordensProcessadas, inicioTurno);
        AtribuirEquipes(ordensProcessadas, equipesIndexadas);

        _logger.LogInformation("Processamento de novas OSs para reotimização concluído: {Total} OS(s).", ordensProcessadas.Count);

        return ordensProcessadas;
    }

    #region Validação e Filtros (§15)

    private List<EquipeGmpro> FiltrarEquipes(List<EquipeGmpro> equipes)
    {
        var totalOriginal = equipes.Count;

        var resultado = equipes
            .Where(e =>
            {
                if (e.LatitudeGaragem is null || double.IsNaN(e.LatitudeGaragem.Value))
                {
                    _logger.LogWarning("Equipe '{Equipe}' removida: latitude da garagem nula.", e.CodigoEquipe);
                    return false;
                }

                if (e.LatitudeGaragem >= 0)
                {
                    _logger.LogWarning("Equipe '{Equipe}' removida: latitude da garagem >= 0 ({Latitude}).",
                        e.CodigoEquipe, e.LatitudeGaragem);
                    return false;
                }

                return true;
            })
            .ToList();

        _logger.LogInformation("Filtro de equipes: {Total} original(is), {Validas} válida(s), {Removidas} removida(s).",
            totalOriginal, resultado.Count, totalOriginal - resultado.Count);

        return resultado;
    }

    private List<OrdemServicoGmpro> FiltrarOrdensServico(List<OrdemServicoGmpro> ordens)
    {
        var totalOriginal = ordens.Count;

        var resultado = ordens
            .Where(os =>
            {
                if (os.Latitude is null || double.IsNaN(os.Latitude.Value))
                {
                    _logger.LogWarning("OS '{NumeroOs}' removida: latitude nula.", os.NumeroOs);
                    return false;
                }

                if (os.RegiaoDespacho is null)
                {
                    _logger.LogWarning("OS '{NumeroOs}' removida: região de despacho nula.", os.NumeroOs);
                    return false;
                }

                return true;
            })
            .ToList();

        _logger.LogInformation("Filtro de OS: {Total} original(is), {Validas} válida(s), {Removidas} removida(s).",
            totalOriginal, resultado.Count, totalOriginal - resultado.Count);

        return resultado;
    }

    private void AplicarSufixoIdentificacao(List<OrdemServicoGmpro> ordens)
    {
        foreach (var os in ordens)
        {
            if (!os.NumeroOs.EndsWith(os.TipoOs, StringComparison.OrdinalIgnoreCase))
            {
                os.NumeroOs = os.NumeroOs + "-" + os.TipoOs;
            }

            if (os.TipoOs == "X"
                && os.NumcdcLote is not null
                && !os.NumcdcLote.EndsWith(os.TipoOs, StringComparison.OrdinalIgnoreCase))
            {
                os.NumcdcLote = os.NumcdcLote + "-" + os.TipoOs;
            }
        }

        _logger.LogInformation("Sufixo de identificação aplicado em {Total} OS.", ordens.Count);
    }

    #endregion

    #region Processamento de Equipes (§5)

    private List<EquipeProcessada> ProcessarEquipes(
        List<EquipeGmpro> equipes,
        List<RegiaoGmpro> regioes,
        DateTime dataExecucao,
        bool flexibilizarRefeicao)
    {
        var equipesProcessadas = new List<EquipeProcessada>();

        var regioesPorEquipe = regioes
            .GroupBy(r => (r.CodigoEquipe, r.TipoEquipe))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.CodRegiao).Distinct().ToList());

        foreach (var equipe in equipes)
        {
            var chave = (equipe.CodigoEquipe, equipe.TipoEquipe);

            if (!regioesPorEquipe.TryGetValue(chave, out var codigosRegiao))
            {
                _logger.LogWarning("Equipe '{Equipe}' ({Tipo}): sem região associada, ignorada.",
                    equipe.CodigoEquipe, equipe.TipoEquipe);
                continue;
            }

            var processada = ProcessarEquipe(equipe, codigosRegiao, dataExecucao);
            equipesProcessadas.Add(processada);
        }

        _logger.LogInformation("Processamento concluído: {Total} entrada(s) gerada(s) a partir de {Equipes} equipe(s).",
            equipesProcessadas.Count, equipes.Count);

        return equipesProcessadas;
    }

    private static EquipeProcessada ProcessarEquipe(EquipeGmpro equipe, List<string> codigosRegiao, DateTime dataExecucao)
    {
        var inicioTurno = AjustarDataParaDiaCorrente(equipe.InicioTurno, dataExecucao)
            .AddMinutes(equipe.TempoAdicionalChecklist ?? 0);
        var fimTurno = AjustarDataParaDiaCorrente(equipe.FimTurno, dataExecucao);
        var inicioRefeicao = AjustarDataParaDiaCorrente(equipe.InicioRefeicao, dataExecucao)
            .AddMinutes(-(equipe.ToleranciaInicioAlmoco ?? 0));
        var fimRefeicao = AjustarDataParaDiaCorrente(equipe.FimRefeicao, dataExecucao)
            .AddMinutes(equipe.ToleranciaFimAlmoco ?? 0);

        if (inicioTurno > fimTurno)
            fimTurno = fimTurno.AddDays(1);

        if (inicioRefeicao > fimRefeicao)
            fimRefeicao = fimRefeicao.AddDays(1);

        // Turno noturno: a refeição ocorre após meia-noite (dia seguinte).
        // AjustarDataParaDiaCorrente projeta todas as horas para dataExecucao, então
        // inicioRefeicao pode ficar antes de inicioTurno quando deveria estar no dia seguinte.
        if (inicioRefeicao < inicioTurno)
        {
            inicioRefeicao = inicioRefeicao.AddDays(1);
            fimRefeicao = fimRefeicao.AddDays(1);
        }

        var duracaoOriginal = fimTurno - inicioTurno;
        var novaDuracao = duracaoOriginal * (equipe.PorcentagemCargaTrabalho / 100.0);
        var fimTurnoAjustado = inicioTurno + novaDuracao;

        return new EquipeProcessada
        {
            Equipe = equipe,
            CodigosRegiao = codigosRegiao,
            InicioTurnoAjustado = inicioTurno,
            FimTurnoAjustado = fimTurnoAjustado,
            InicioRefeicaoAjustado = inicioRefeicao,
            FimRefeicaoAjustado = fimRefeicao
        };
    }

    private static DateTime AjustarDataParaDiaCorrente(DateTime original, DateTime dataExecucao)
    {
        return new DateTime(
            dataExecucao.Year,
            dataExecucao.Month,
            dataExecucao.Day,
            original.Hour,
            original.Minute,
            0,
            DateTimeKind.Utc);
    }

    #endregion

    #region Ajuste de Datas (§9)

    private List<OsProcessada> ProcessarAjusteDatas(
        List<OrdemServicoGmpro> ordens,
        List<PerfilEquipeGmpro> perfis,
        DateTime inicioTurno,
        DateTime fimTurno,
        DateTime inicioDiaOtimizacaoUtc,
        DateTime inicioDiaSeguinteUtc)
    {
        var mediaTmeSegundos = CalcularMediaTmeSegundos(ordens);
        var perfisPorCodserv = perfis
            .GroupBy(p => p.Codserv)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Perfil).Distinct().ToList());

        var resultado = new List<OsProcessada>();

        foreach (var os in ordens)
        {
            var processada = CriarOsProcessada(os, perfisPorCodserv, mediaTmeSegundos, inicioTurno, fimTurno, inicioDiaOtimizacaoUtc, inicioDiaSeguinteUtc);
            resultado.Add(processada);
        }

        _logger.LogInformation("Ajuste de datas concluído para {Total} OS.", resultado.Count);
        return resultado;
    }

    private OsProcessada CriarOsProcessada(
        OrdemServicoGmpro os,
        Dictionary<int, List<string>> perfisPorCodserv,
        int mediaTmeSegundos,
        DateTime inicioTurno,
        DateTime fimTurno,
        DateTime inicioDiaOtimizacaoUtc,
        DateTime inicioDiaSeguinteUtc)
    {
        var processada = new OsProcessada
        {
            Os = os,
            DataSolicitacaoAjustada = inicioTurno,
            DataVencimentoAjustada = os.DataVencimento,
            MultaAtraso =
                os.TipoOs == "C" &&
                os.RiscoVida != "S" &&
                os.ClienteEssencial != 1
                    ? os.OsRegulada == "S"
                        ? ConversaoConstantes.MultaAtrasoReguladaHora
                        : ConversaoConstantes.MultaAtrasoNReguladaHora
                    : 0,
            TempoVencida = (int)((inicioTurno - os.DataVencimento).TotalHours),
            TempoAteVencer = Math.Max(0, (int)((os.DataVencimento - inicioTurno).TotalHours)),
            TempoAberta = (int)((inicioTurno - os.DataSolicitacao).TotalHours),
            Vencida = os.DataVencimento < inicioTurno,
            VencendoNoTurno = os.DataVencimento > inicioTurno && os.DataVencimento < fimTurno,
            IsCompensacao = os.Compensacao > 0 && os.RiscoVida != "S",
            DuracaoSegundos = CalcularDuracaoSegundos(os.MetaTme, mediaTmeSegundos),
            PerfisEquipe = perfisPorCodserv.TryGetValue(os.Codserv, out var perfis) ? perfis : [],
        };

        AplicarRegraRiscoVida(processada, inicioTurno);
        AplicarRegraClienteEssencial(processada, inicioTurno);
        AplicarRegraVencida(processada, inicioTurno, fimTurno);
        AplicarRegraVencimentoAposTurno(processada, fimTurno, inicioDiaOtimizacaoUtc, inicioDiaSeguinteUtc);
        AplicarRegraVencendoNoTurno(processada);
        AplicarRegraOsTecnica(processada); 

        return processada;
    }

    private void AplicarRegraRiscoVida(OsProcessada processada, DateTime inicioTurno)
    {
        if (processada.Os.RiscoVida != "S") return;

        processada.DataSolicitacaoAjustada = inicioTurno;
        processada.DataVencimentoAjustada = inicioTurno.AddMinutes(ConversaoConstantes.TempoParaExecucaoRiscoVidaMinutos);
        processada.MultaAtraso = ConversaoConstantes.MultaAtrasoRiscoVidaHora;
    }

    private void AplicarRegraClienteEssencial(OsProcessada processada, DateTime inicioTurno)
    {
        if (processada.Os.ClienteEssencial != 1 || processada.Os.RiscoVida == "S") return;

        processada.DataSolicitacaoAjustada = inicioTurno;
        processada.DataVencimentoAjustada = inicioTurno.AddMinutes(ConversaoConstantes.TempoParaExecucaoClienteEssencialMinutos);
        processada.MultaAtraso = ConversaoConstantes.MultaAtrasoClienteEssencialHora;
    }

    private static void AplicarRegraVencida(OsProcessada processada, DateTime inicioTurno, DateTime fimTurno)
    {
        if (!processada.Vencida || processada.Os.RiscoVida == "S" || processada.Os.ClienteEssencial == 1) return;

        processada.DataVencimentoAjustada = processada.IsCompensacao || processada.Os.TipoOs == "T" ? processada.DataSolicitacaoAjustada : fimTurno;
    }

    private static void AplicarRegraVencimentoAposTurno(OsProcessada processada, DateTime fimTurno, DateTime inicioDiaOtimizacaoUtc, DateTime inicioDiaSeguinteUtc)
    {
        if (processada.Os.RiscoVida == "S" || processada.Os.ClienteEssencial == 1)
            return;

        if (processada.Os.DataVencimento <= fimTurno)
            return;

        var venceNoDiaDaOtimizacao =
            processada.Os.DataVencimento >= inicioDiaOtimizacaoUtc &&
            processada.Os.DataVencimento < inicioDiaSeguinteUtc;

        if (venceNoDiaDaOtimizacao)
        {
            processada.DataVencimentoAjustada =
                fimTurno.AddHours(-ConversaoConstantes.AntecedenciaVencimentoNoDiaHoras);
            processada.VencimentoAjustadoAposTurnoNoMesmoDia = true;
        }
        else
        {
            processada.DataVencimentoAjustada = fimTurno;
        }
    }

    private static void AplicarRegraVencendoNoTurno(OsProcessada processada)
    {
        if (processada.Os.RiscoVida == "S" || processada.Os.ClienteEssencial == 1) return;

        if (processada.VencendoNoTurno)
            processada.DataVencimentoAjustada = processada.Os.DataVencimento;
    }

    private static void AplicarRegraOsTecnica(OsProcessada processada)
    {
        if (processada.Os.TipoOs != "T") return;
    
            processada.MultaAtraso += ConversaoConstantes.MultaAtrasoTecnicaHora;
    }

    private static int CalcularDuracaoSegundos(double? metaTme, int mediaTmeSegundos)
    {
        if (metaTme.HasValue && metaTme.Value > 0)
            return (int)(metaTme.Value * 60);
        return mediaTmeSegundos;
    }

    private static int CalcularMediaTmeSegundos(List<OrdemServicoGmpro> ordens)
    {
        var comTme = ordens.Where(o => o.MetaTme.HasValue && o.MetaTme.Value > 0).ToList();
        if (comTme.Count == 0) return 0;
        return (int)(comTme.Average(o => o.MetaTme!.Value) * 60);
    }

    #endregion

    #region Cálculo de Pesos (§8)

    private void CalcularPesos(List<OsProcessada> ordensProcessadas, DateTime inicioTurno)
    {
        if (ordensProcessadas.Count == 0) return;

        var total = ordensProcessadas.Count;

        var pesosRiscoVida = new double[total];
        var pesosClienteEssencial = new double[total];
        var pesosPessoasAtingidas = new double[total];
        var pesosCompensacao = new double[total];
        var pesosOsReguladaPrazo = new double[total];
        var pesosOsRegulada = new double[total];
        var pesosOsLote = new double[total];
        var pesosOsTecnica = new double[total];
        var pesosReicidencia = new double[total];
        var pesosTempoOsTecnicaAberta = new double[total];
        var pesosPrioridade = new double[total];

        for (var i = 0; i < total; i++)
        {
            var p = ordensProcessadas[i];
            pesosRiscoVida[i] = p.Os.RiscoVida == "S" ? ConversaoConstantes.PesoRiscoVida : 0;
            pesosClienteEssencial[i] = p.Os.ClienteEssencial == 1 ? ConversaoConstantes.PesoClienteEssencial : 0;
            pesosPessoasAtingidas[i] = p.Os.TipoOs == "T"
                ? ConversaoConstantes.PesoQtdPessoasAtingidas * (p.Os.PessoasAtingidas > 0 ? p.Os.PessoasAtingidas : 1)
                : 0;
            if (p.IsCompensacao && !p.Vencida && p.Os.RiscoVida != "S" && p.Os.TipoOs == "C")
            {
                int divisor = DiasCalendarioAteVencimento(inicioTurno, p.Os.DataVencimento);
                pesosCompensacao[i] = (p.Os.Compensacao + p.Os.CompensacaoPorHora) / divisor;
            }
            else
            {
                pesosCompensacao[i] = p.Os.Compensacao + p.Os.CompensacaoPorHora;
            }
            pesosOsReguladaPrazo[i] = p.Os.OsRegulada == "S"
                ? p.TempoVencida * ConversaoConstantes.PesoAtrasoCompensacaoPorHora
                : p.TempoVencida * ConversaoConstantes.PesoAtrasoCompensacaoNReguladaPorHora;
            pesosOsRegulada[i] = p.Os.OsRegulada == "S" && p.Os.RiscoVida != "S" && p.Os.ClienteEssencial != 1
                ? ConversaoConstantes.PesoRegulada
                : 0;
            pesosOsLote[i] = p.Os.TipoOs == "X" ? 1 : 0;
            pesosOsTecnica[i] = p.Os.TipoOs == "T" ? 1 : 0;
            pesosReicidencia[i] = p.Os.Reicidencia == "S" ? ConversaoConstantes.PesoReicidente : 0;
            pesosTempoOsTecnicaAberta[i] = p.Os.TipoOs == "T" ? ConversaoConstantes.PesoIdadeOsTecnica * p.TempoAberta : 0;
            pesosPrioridade[i] = ConversaoConstantes.ObterValorPrioridade(p.Os.Prioridade);
        }

        var nRiscoVida = ConversaoHelper.MinMaxScale(pesosRiscoVida);
        var nClienteEssencial = ConversaoHelper.MinMaxScale(pesosClienteEssencial);
        var nPessoasAtingidas = ConversaoHelper.MinMaxScale(pesosPessoasAtingidas);
        var nCompensacao = ConversaoHelper.MinMaxScale(pesosCompensacao);
        var nOsReguladaPrazo = ConversaoHelper.MinMaxScale(pesosOsReguladaPrazo);
        var nOsRegulada = ConversaoHelper.MinMaxScale(pesosOsRegulada);
        var nOsLote = ConversaoHelper.MinMaxScale(pesosOsLote);
        var nOsTecnica = ConversaoHelper.MinMaxScale(pesosOsTecnica);
        var nReicidencia = ConversaoHelper.MinMaxScale(pesosReicidencia);
        var nTempoOsTecnicaAberta = ConversaoHelper.MinMaxScale(pesosTempoOsTecnicaAberta);
        var nPrioridade = ConversaoHelper.MinMaxScale(pesosPrioridade);

        for (var i = 0; i < total; i++)
        {
            ordensProcessadas[i].PesoFinal +=
                nRiscoVida[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.RiscoVidaImportancia)
              + nClienteEssencial[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.ClienteEssencialImportancia)
              + nPessoasAtingidas[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.PessoasAtingidasImportancia)
              + nCompensacao[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.CompensacaoImportancia)
              + nOsRegulada[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.PrioridadeEReguladaImportancia)
              + nReicidencia[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.ReicidenciaImportancia)
              + nTempoOsTecnicaAberta[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.TempoTecnicaImportancia)
              + nPrioridade[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.PrioridadeImportancia)
              + nOsLote[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.EDoLoteImportancia)
              + nOsTecnica[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.OSTecnicaImportancia)
              + nOsReguladaPrazo[i] * Math.Pow(ConversaoConstantes.PotenciaPesos, ConversaoConstantes.OsPrazoImportancia);
        }

        _logger.LogInformation("Cálculo de pesos concluído para {Total} OS.", total);
    }

    private static int DiasCalendarioAteVencimento(DateTime referenciaUtc, DateTime vencimentoUtc)
    {
        // Trabalha com datas em UTC; a virada de dia já é coerente para comparação relativa
        int dias = (vencimentoUtc.Date - referenciaUtc.Date).Days + 1;
        return Math.Max(1, dias);
    }

    #endregion

    #region Atribuição OS ? Equipe (§10)

    private void AtribuirEquipes(List<OsProcessada> ordensProcessadas, List<EquipeIndexada> equipesIndexadas)
    {
        var lotes = ordensProcessadas.Where(p => p.Os.TipoOs == "X").ToList();
        var avulsas = ordensProcessadas.Where(p => p.Os.TipoOs != "X").ToList();

        foreach (var os in lotes)
        {
            var compativeis = ObterEquipesCompativeis(os, equipesIndexadas);
            if (compativeis.Count == 0)
            {
                _logger.LogWarning("OS Lote '{NumeroOs}': nenhuma equipe compatível encontrada.", os.Os.NumeroOs);
                continue;
            }

            var maisProxima = compativeis
                .OrderBy(e => ConversaoHelper.Haversine(
                    os.Os.Latitude!.Value, os.Os.Longitude!.Value,
                    e.Equipe.LatitudeGaragem!.Value, e.Equipe.LongitudeGaragem!.Value))
                .First();

            os.IndicesEquipesPermitidas = [maisProxima.IndiceVehicle];
        }

        foreach (var os in avulsas)
        {
            var compativeis = ObterEquipesCompativeis(os, equipesIndexadas);
            if (compativeis.Count == 0)
            {
                _logger.LogWarning("OS '{NumeroOs}': nenhuma equipe compatível encontrada.", os.Os.NumeroOs);
                continue;
            }

            os.IndicesEquipesPermitidas = compativeis.Select(e => e.IndiceVehicle).ToList();
        }

        var semEquipe = ordensProcessadas.Count(p => p.IndicesEquipesPermitidas.Count == 0);
        _logger.LogInformation(
            "Atribuição concluída: {Lotes} lote(s), {Avulsas} avulsa(s), {SemEquipe} OS sem equipe compatível.",
            lotes.Count, avulsas.Count, semEquipe);
    }

    private static List<EquipeIndexada> ObterEquipesCompativeis(OsProcessada os, List<EquipeIndexada> equipesIndexadas)
    {
        return equipesIndexadas
            .Where(e => VerificarCompatibilidade(os, e))
            .ToList();
    }

    private static bool RegiaoCompativel(string? regiaoDespacho, string? codRegiao)
    {
        if (string.IsNullOrWhiteSpace(regiaoDespacho) || string.IsNullOrWhiteSpace(codRegiao))
            return false;

        var regiao = regiaoDespacho.Trim();
        var codRegiaoTrimmed = codRegiao.Trim();

        // CodRegiao pode estar no formato "empresaId_código" (ou "empresaId_polo-região"); extrair o que vem após o prefixo
        var underscoreIndex = codRegiaoTrimmed.IndexOf('_');
        var codigoRegiao = underscoreIndex >= 0
            ? codRegiaoTrimmed[(underscoreIndex + 1)..]
            : codRegiaoTrimmed;

        return string.Equals(regiao, codigoRegiao, StringComparison.OrdinalIgnoreCase)
            || string.Equals(regiao, codRegiaoTrimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool VerificarCompatibilidade(OsProcessada os, EquipeIndexada equipeIdx)
    {
        // Se a equipe tem um perfil específico exigido, a OS deve ter perfis mapeados e conter esse perfil
        if (!string.IsNullOrWhiteSpace(equipeIdx.Equipe.PerfilEquipe))
        {
            if (os.PerfisEquipe.Count == 0)
                return false; // OS sem perfil mapeado é incompatível com equipe que exige perfil específico

            if (!os.PerfisEquipe.Contains(equipeIdx.Equipe.PerfilEquipe, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        if (!equipeIdx.CodigosRegiao.Any(r => RegiaoCompativel(os.Os.RegiaoDespacho, r)))
            return false;

        var atuacao = equipeIdx.Equipe.AtuacaoEquipe?.Trim().ToUpperInvariant();
        if (atuacao != "U/R")
        {
            var localizacao = os.Os.Localizacao?.Trim().ToUpperInvariant();
            if (atuacao != localizacao)
                return false;
        }

        var tipoCorteParaFiltro = os.Os.TipoOs == "X" ? os.Os.DescricaoTipoCorte : os.Os.TipoCorte;
        if (!string.IsNullOrEmpty(tipoCorteParaFiltro))
        {
            var ehRestrito = ConversaoConstantes.ServicosRestritos1Pessoa
                .Any(s => tipoCorteParaFiltro.Contains(s, StringComparison.OrdinalIgnoreCase));
            if (ehRestrito && equipeIdx.Equipe.NPessoas == 1)
                return false;
        }

        return true;
    }

    #endregion
}
