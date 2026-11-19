using Energisa.God.Domain.Model._OrdemServico_.Enum;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model.Modelo;


namespace Energisa.Eforce.Otimizador.Mappers;

/// <summary>
/// Mapeia entidades da LibGod (Energisa.God.Domain) para os modelos locais do projeto.
/// </summary>
public static class SourceMapper
{
    /// <summary>
    /// Converte uma <see cref="OrdemServico"/> (NoSQL) para a <see cref="OrdemServicoGmpro"/> local.
    /// </summary>
    public static OrdemServicoGmpro ToLocal(OrdemServico source) => ToLocal(source, reguladoOverride: null);

    /// <summary>
    /// Converte uma <see cref="OrdemServico"/> (NoSQL) para a <see cref="OrdemServicoGmpro"/> local,
    /// priorizando <paramref name="reguladoOverride"/> (vindo do <see cref="Servico"/>) quando disponível.
    /// </summary>
    public static OrdemServicoGmpro ToLocal(OrdemServico source, bool? reguladoOverride, TimeSpan? tmeOverride = null, double penalidadePorHora = 0, double compensacaoAdicionalVencidas = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        var regulado = reguladoOverride ?? source.Regulado;
        var osTecnica = source.Servico?.TipoServico == "T";
        var dataVencimento = source.DataVencimento?.DateTime ?? source.DataVencimentoUtc;
        var osVencidaOuVenceHoje = dataVencimento.HasValue && dataVencimento.Value.Date <= DateTime.UtcNow.Date;
        var aplicarPenalidade = (regulado == true || osTecnica) && osVencidaOuVenceHoje;

        return new OrdemServicoGmpro
        {
            NumeroOs = source.Id!,

            NumeroOsEnergisa = source.NumeroOs,

            DataVencimentoReal = source.DataVencimento?.DateTime ?? source.DataVencimentoUtc,

            NumcdcLote = source.UnidadeConsumidoraOrigem?.IdExterno,

            Codserv = source.Servico?.IdExternoNumerico ?? 0,

            Latitude = source.ObjetoMapa?.Latitude,

            Longitude = source.ObjetoMapa?.Longitude,

            DataVencimento = source.Servico.TipoServico == "T" ? source.DataRegistro?.UtcDateTime ?? DateTime.MinValue
                             : source.DataVencimentoUtc
                             ?? source.DataVencimento?.UtcDateTime
                             ?? source.DataRegistro?.UtcDateTime
                             ?? DateTime.MinValue,

            DataSolicitacao = source.DataRegistro?.UtcDateTime ?? DateTime.MinValue,

            RegiaoDespacho = FormatCodRegiao(source.Empresa?.IdExterno, source.Regiao?.IdExterno),

            TipoOs = source.Servico?.TipoServico ?? string.Empty,

            RiscoVida = source.Servico?.TemRiscoVida == true ? "S" : "N",

            OsRegulada = regulado == true ? "S" : "N",

            Reicidencia = source.Reincidencia == true ? "S" : "N",

            PessoasAtingidas = source.Abrangencia?.QuantidadeClientesAfetados ?? 0,

            Compensacao = (double)(source.Indenizacao ?? 0m) + (aplicarPenalidade ? compensacaoAdicionalVencidas : 0),

            CompensacaoPorHora = CalcularCompensacaoPorHora(source, regulado == true, aplicarPenalidade ? penalidadePorHora : 0),

            Prioridade = MapPrioridade(source.Prioridade),

            MetaTme = (tmeOverride ?? source.TempoMedioExecucao)?.TotalMinutes + 5, // Adição de 5 minutos para compensar o tempo de deslocamento, conforme regra definido pela Energisa.

            Localizacao = MapZona(source.Localizacao?.Zona),

            TipoCorte = source.Caracteristicas?.FirstOrDefault(c => c.Chave == "TIPO_CORTE")?.Valor
        };
    }

    /// <summary>
    /// Converte uma <see cref="GodEquipe"/> (NoSQL) para a <see cref="Equipe"/> local.
    /// </summary>
    public static EquipeGmpro ToLocal(Equipe source, PerfilEquipe? perfil)
    {
        ArgumentNullException.ThrowIfNull(source);

        var dados = source.DadosAdicionais;

        var porcentagemCarga = perfil?.PercentualAlocacaoEquipe ?? 100;

        return new EquipeGmpro
        {
            CodigoEquipe = source.Nome,

            TipoEquipe = source.Tipo,

            AtuacaoEquipe = ResolverAtuacaoEquipe(source.RealizaServicosRurais, source.RealizaServicosUrbanos),

            CustoKm = ConversaoConstantes.CustoKmEquipe,

            CustoFixo = ConversaoConstantes.CustoFixoEquipe,

            InicioTurno = source.Escala?.DataInicioUtc
                          ?? source.Escala?.DataInicio?.UtcDateTime
                          ?? DateTime.MinValue,

            FimTurno = source.Escala?.DataFinalUtc
                       ?? source.Escala?.DataFinal?.UtcDateTime
                       ?? DateTime.MinValue,

            InicioRefeicao = source.Escala?.IntervaloDescanso?.Inicio?.UtcDateTime ?? DateTime.MinValue,

            FimRefeicao = source.Escala?.IntervaloDescanso?.Fim?.UtcDateTime ?? DateTime.MinValue, // Alterado pois o GMPRO considera o tempo de refeição como um período fixo (ex: 12:00 às 13:00), e não um intervalo flexível.

            DuracaoAlmoco = ParseDoubleOrNull(dados?.GetValueOrDefault("TempoParadaLegal")),

            LatitudeGaragem = source.PontoPartida?.Latitude,

            LongitudeGaragem = source.PontoPartida?.Longitude,

            LatitudeAlmoco = null,//source.PontoPartida?.Latitude,

            LongitudeAlmoco = null,//source.PontoPartida?.Longitude,

            PorcentagemCargaTrabalho = porcentagemCarga,

            TempoAdicionalChecklist = perfil?.TempoAdicionalChecklist,

            ToleranciaInicioAlmoco = source.ToleranciaInicioAlmoco,

            ToleranciaFimAlmoco = source.ToleranciaFimAlmoco,

            RetornaBaseFimExpediente = source.RetornaBaseFimExpediente,

            RetornaAlmocoBase = source.RetornaAlmocoBase,

            MaxLoad = ParseIntOrDefault(dados?.GetValueOrDefault("MAX_LOAD"), ConversaoConstantes.CapacidadeEquipePadrao),

            PerfilEquipe = source.Perfil?.Id ?? string.Empty,

            NPessoas = source.Funcionarios?.Count ?? 1,

            EquipeNome = source.Nome ?? string.Empty,

            CodRegiao = FormatCodRegiao(source.Empresa?.IdExterno, source.Regioes?.FirstOrDefault()?.IdExterno),

            CodServ = dados?.GetValueOrDefault("COD_SERV") ?? string.Empty,

            LatitudeLocalPartida = source.PontoPartida?.Latitude ?? 0,

            LongitudeLocalPartida = source.PontoPartida?.Longitude ?? 0,

            LatitudeLocalRetorno = null,//source.PontoPartida?.Latitude,

            LongitudeLocalRetorno = null,//source.PontoPartida?.Longitude,

            LatitudeLocalRefeicao = null,//source.PontoPartida?.Latitude,

            LongitudeLocalRefeicao = null,//source.PontoPartida?.Longitude,

            TempoRefeicao = ParseIntOrDefault(dados?.GetValueOrDefault("TempoParadaLegal")),

            TravelDurationMultiple = ParseDoubleOrDefault(dados?.GetValueOrDefault("TRAVEL_DURATION_MULTIPLE"), 1.0)
        };
    }

    /// <summary>
    /// Converte as relações perfil-equipe × serviço (NoSQL) em uma lista de
    /// <see cref="PerfilEquipeGmpro"/> locais (Codserv ↔ Perfil).
    /// </summary>
    public static List<PerfilEquipeGmpro> ToLocalPerfis(
        IEnumerable<PerfilEquipeRelacao> relacoes,
        IDictionary<string, string> perfilNomes)
    {
        ArgumentNullException.ThrowIfNull(relacoes);
        ArgumentNullException.ThrowIfNull(perfilNomes);

        var result = new List<PerfilEquipeGmpro>();

        foreach (var relacao in relacoes)
        {
            if (relacao.ServicosAssociados is null) continue;

            var nomePerfil = relacao.PerfilEquipeId is not null
                             && perfilNomes.TryGetValue(relacao.PerfilEquipeId, out var nome)
                ? nome
                : string.Empty;

            foreach (var servico in relacao.ServicosAssociados)
            {
                var codserv = ParseIntOrDefault(servico.Codigo);
                if (codserv == 0) continue;

                result.Add(new PerfilEquipeGmpro
                {
                    Codserv = codserv,
                    Perfil = nomePerfil,
                    Codigo = codserv
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Converte uma <see cref="GodEquipe"/> e suas regiões em registros de
    /// <see cref="RegiaoGmpro"/> locais.
    /// </summary>
    public static List<RegiaoGmpro> ToLocalRegioes(Equipe source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Regioes is null || source.Regioes.Count == 0)
            return [];

        var codigoEquipe = source.Nome ?? source.IdExterno ?? string.Empty;
        var tipoEquipe = source.Tipo ?? string.Empty;
        var empresaId = source.Empresa?.IdExterno ?? string.Empty;

        return source.Regioes
            .Where(r => r.IdExterno is not null)
            .Select(r => new { Regiao = r, CodRegiao = FormatCodRegiao(empresaId, r.IdExterno) })
            .Where(r => !string.IsNullOrEmpty(r.CodRegiao))
            .Select(r => new RegiaoGmpro
            {
                CodigoEquipe = codigoEquipe,
                TipoEquipe = tipoEquipe,
                CodRegiao = r.CodRegiao
            })
            .ToList();
    }

    #region Helpers

    private static string MapPrioridade(ushort? prioridade) => prioridade switch
    {
        >= 1 and <= 2 => "URGENTE",
        >= 3 and <= 4 => "ALTA",
        _ => "NORMAL"
    };

    private static string MapZona(Zona? zona) => zona switch
    {
        Zona.Rural => "R",
        _ => "U"
    };

    private static double CalcularCompensacaoPorHora(OrdemServico source, bool regulado, double penalidadePorHora = 0)
    {
        if (source.Indenizacoes is null || source.Indenizacoes.Count == 0)
        {
            var valorEscalar = (double)(source.Indenizacao ?? 0m);
            return valorEscalar > 0 ? valorEscalar + penalidadePorHora : penalidadePorHora > 0 ? penalidadePorHora : 0;
        }

        var t0 = source.Indenizacoes
            .Where(i => i.OffsetMinutos == 0)
            .Select(i => i.Valor)
            .FirstOrDefault();

        var taxasPorHora = source.Indenizacoes
            .Where(i => i.OffsetMinutos > 0 && i.Valor != 0)
            .Select(i =>
            {
                var n = i.OffsetMinutos / 60m;
                return (i.Valor - t0) / n;
            })
            .ToList();

        if (taxasPorHora.Count == 0)
        {
            var valorBase = (double)t0;
            return valorBase > 0 ? valorBase + penalidadePorHora : penalidadePorHora > 0 ? penalidadePorHora : 0;
        }

        var media = Math.Round((double)taxasPorHora.Average(), 2);
        var compensacao = media > 0 ? media : (double)t0;

        return compensacao + penalidadePorHora;
    }

    /// <summary>
    /// Converte o IdExterno da região (<c>codreg-codpolo-codregiao</c>) em <c>{empresa}_{codpolo}-{codregiao}</c>.
    /// O polo faz parte do código porque a região só é única dentro dele em algumas empresas.
    /// Retorna vazio quando o IdExterno é inválido.
    /// </summary>
    public static string FormatCodRegiao(string? empresaId, string? idExterno)
    {
        if (string.IsNullOrEmpty(idExterno)) return string.Empty;

        var parts = idExterno.Split('-');
        if (parts.Length < 3 || string.IsNullOrEmpty(parts[1]) || string.IsNullOrEmpty(parts[2])) return string.Empty;

        return $"{empresaId}_{parts[1]}-{parts[2]}";
    }

    private static int? ParseIntOrNull(string? value) =>
        int.TryParse(value, out var n) ? n : null;

    private static int ParseIntOrDefault(string? value, int fallback = 0) =>
        int.TryParse(value, out var n) ? n : fallback;

    private static double ParseDoubleOrDefault(string? value, double fallback = 0) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static double? ParseDoubleOrNull(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string ResolverAtuacaoEquipe(bool? realizaRurais, bool? realizaUrbanos)
    {
        if (realizaRurais == true && realizaUrbanos == true)
            return "U/R";

        if (realizaRurais == true)
            return "R";

        if (realizaUrbanos == true)
            return "U";

        return "U/R";
    }

    #endregion
}
