using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.Eforce.Otimizador.Services;

namespace Energisa.Eforce.Otimizador.Tests.Helpers;

public static class TestDataFactory
{
    public static readonly DateTime DataExecucao = new(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime InicioTurno = new(2025, 1, 15, 8, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime FimTurno = new(2025, 1, 15, 17, 0, 0, DateTimeKind.Utc);

    public static OrdemServicoGmpro CriarOsValida(string numeroOs = "12345", string tipoOs = "C")
    {
        return new OrdemServicoGmpro
        {
            NumeroOs = numeroOs,
            TipoOs = tipoOs,
            Codserv = 100,
            Latitude = -23.5505,
            Longitude = -46.6333,
            DataVencimento = new DateTime(2025, 1, 15, 16, 0, 0, DateTimeKind.Utc),
            DataSolicitacao = new DateTime(2025, 1, 14, 10, 0, 0, DateTimeKind.Utc),
            RegiaoDespacho = "1",
            RiscoVida = "N",
            OsRegulada = "N",
            Reicidencia = "N",
            PessoasAtingidas = 0,
            Compensacao = 0,
            CompensacaoPorHora = 0,
            Prioridade = "NORMAL",
            MetaTme = 30,
            Localizacao = "U"
        };
    }

    public static OrdemServicoGmpro CriarOsRiscoVida(string numeroOs = "99999")
    {
        var os = CriarOsValida(numeroOs);
        os.RiscoVida = "S";
        os.Prioridade = "URGENTE";
        return os;
    }

    public static OrdemServicoGmpro CriarOsRegulada(string numeroOs = "88888")
    {
        var os = CriarOsValida(numeroOs);
        os.OsRegulada = "S";
        return os;
    }

    public static OrdemServicoGmpro CriarOsTecnica(string numeroOs = "77777")
    {
        var os = CriarOsValida(numeroOs, "T");
        os.PessoasAtingidas = 50;
        return os;
    }

    public static OrdemServicoGmpro CriarOsLote(string numeroOs = "66666", string numcdcLote = "CDC001")
    {
        var os = CriarOsValida(numeroOs, "X");
        os.NumcdcLote = numcdcLote;
        return os;
    }

    public static OrdemServicoGmpro CriarOsVencida(string numeroOs = "55555")
    {
        var os = CriarOsValida(numeroOs);
        os.DataVencimento = new DateTime(2025, 1, 14, 8, 0, 0, DateTimeKind.Utc);
        return os;
    }

    public static OrdemServicoGmpro CriarOsComCompensacao(string numeroOs = "44444")
    {
        var os = CriarOsValida(numeroOs);
        os.Compensacao = 500;
        os.CompensacaoPorHora = 100;
        return os;
    }

    public static OrdemServicoGmpro CriarOsSemCoordenadas(string numeroOs = "SEMCOORD")
    {
        var os = CriarOsValida(numeroOs);
        os.Latitude = null;
        os.Longitude = null;
        return os;
    }

    public static OrdemServicoGmpro CriarOsSemRegiao(string numeroOs = "SEMREG")
    {
        var os = CriarOsValida(numeroOs);
        os.RegiaoDespacho = null;
        return os;
    }

    public static EquipeGmpro CriarEquipeValida(string codigo = "CGRGA34", string tipo = "LV")
    {
        return new EquipeGmpro
        {
            CodigoEquipe = codigo,
            TipoEquipe = tipo,
            AtuacaoEquipe = "U",
            CustoKm = 1.5,
            InicioTurno = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            FimTurno = new DateTime(2025, 1, 1, 17, 0, 0, DateTimeKind.Utc),
            InicioRefeicao = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicao = new DateTime(2025, 1, 1, 13, 0, 0, DateTimeKind.Utc),
            DuracaoAlmoco = 60,
            LatitudeGaragem = -23.5505,
            LongitudeGaragem = -46.6333,
            LatitudeAlmoco = -23.5600,
            LongitudeAlmoco = -46.6400,
            PorcentagemCargaTrabalho = 80,
            TempoAdicionalChecklist = 10,
            ToleranciaInicioAlmoco = 60,
            ToleranciaFimAlmoco = 60,
            RetornaBaseFimExpediente = false,
            RetornaAlmocoBase = true,
            MaxLoad = 30,
            PerfilEquipe = "Faz tudo",
            NPessoas = 2
        };
    }

    /// <summary>
    /// Cria equipe com horários de refeição contendo segundos residuais (ex: 12:00:01 e 12:59:59),
    /// simulando o ruído de representação que vem do banco de dados.
    /// </summary>
    public static EquipeGmpro CriarEquipeComSegundosResiduaisNaRefeicao(string codigo = "SEGUNDOS", string tipo = "LV")
    {
        var equipe = CriarEquipeValida(codigo, tipo);
        // Mesma quantidade de segundos residuais em ambos os limites para que, após o truncamento
        // de segundos feito em AjustarDataParaDiaCorrente, a duração resultante seja exatamente 1 hora.
        equipe.InicioRefeicao = new DateTime(2025, 1, 1, 12, 0, 30, DateTimeKind.Utc);
        equipe.FimRefeicao = new DateTime(2025, 1, 1, 13, 0, 30, DateTimeKind.Utc);
        equipe.ToleranciaInicioAlmoco = 0;
        equipe.ToleranciaFimAlmoco = 0;
        return equipe;
    }

    /// <summary>
    /// Cria equipe com turno noturno (22h–06h) e refeição após meia-noite (00:30–01:30).
    /// Usada para validar o ajuste de datas em turnos que cruzam a virada do dia.
    /// </summary>
    public static EquipeGmpro CriarEquipeTurnoNoturno(string codigo = "NOTURNA", string tipo = "LV")
    {
        var equipe = CriarEquipeValida(codigo, tipo);
        equipe.InicioTurno = new DateTime(2025, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        equipe.FimTurno = new DateTime(2025, 1, 2, 6, 0, 0, DateTimeKind.Utc);
        equipe.InicioRefeicao = new DateTime(2025, 1, 2, 0, 30, 0, DateTimeKind.Utc);
        equipe.FimRefeicao = new DateTime(2025, 1, 2, 1, 30, 0, DateTimeKind.Utc);
        return equipe;
    }

    public static EquipeGmpro CriarEquipeComLatitudeInvalida(string codigo = "INVALIDA")
    {
        var equipe = CriarEquipeValida(codigo);
        equipe.LatitudeGaragem = 10.0;
        return equipe;
    }

    public static EquipeGmpro CriarEquipeComLatitudeNula(string codigo = "LATNULL")
    {
        var equipe = CriarEquipeValida(codigo);
        equipe.LatitudeGaragem = null;
        return equipe;
    }

    public static PerfilEquipeGmpro CriarPerfilEquipe(int codserv = 100, string perfil = "Faz tudo")
    {
        return new PerfilEquipeGmpro
        {
            Codserv = codserv,
            Perfil = perfil,
            Codigo = codserv
        };
    }

    public static RegiaoGmpro CriarRegiao(string codigoEquipe = "CGRGA34", string tipoEquipe = "LV", string codRegiao = "1")
    {
        return new RegiaoGmpro
        {
            CodigoEquipe = codigoEquipe,
            TipoEquipe = tipoEquipe,
            CodRegiao = codRegiao
        };
    }

    public static DadosEforce CriarDadosEforceCompletos()
    {
        return new DadosEforce
        {
            OrdensServico = [CriarOsValida()],
            Equipes = [CriarEquipeValida()],
            PerfisEquipe = [CriarPerfilEquipe()],
            Regioes = [CriarRegiao()]
        };
    }

    public static OsProcessada CriarOsProcessada(OrdemServicoGmpro? os = null)
    {
        os ??= CriarOsValida();
        return new OsProcessada
        {
            Os = os,
            DataSolicitacaoAjustada = InicioTurno,
            DataVencimentoAjustada = new DateTime(2025, 1, 15, 16, 0, 0, DateTimeKind.Utc),
            MultaAtraso = 0,
            TempoVencida = 0,
            TempoAteVencer = 8,
            TempoAberta = 22,
            Vencida = false,
            VencendoNoTurno = true,
            IsCompensacao = false,
            PesoFinal = 100,
            DuracaoSegundos = 1800,
            PerfisEquipe = ["Faz tudo"],
            IndicesEquipesPermitidas = [0]
        };
    }

    public static EquipeProcessada CriarEquipeProcessada(EquipeGmpro? equipe = null, string codRegiao = "1")
    {
        equipe ??= CriarEquipeValida();
        return new EquipeProcessada
        {
            Equipe = equipe,
            CodigosRegiao = [codRegiao],
            InicioTurnoAjustado = InicioTurno,
            FimTurnoAjustado = new DateTime(2025, 1, 15, 15, 12, 0, DateTimeKind.Utc),
            InicioRefeicaoAjustado = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            FimRefeicaoAjustado = new DateTime(2025, 1, 15, 13, 0, 0, DateTimeKind.Utc)
        };
    }

    public static EquipeIndexada CriarEquipeIndexada(int indice = 0, EquipeGmpro? equipe = null, string codRegiao = "1")
    {
        equipe ??= CriarEquipeValida();
        return new EquipeIndexada
        {
            IndiceVehicle = indice,
            Equipe = equipe,
            CodigosRegiao = [codRegiao]
        };
    }
}
