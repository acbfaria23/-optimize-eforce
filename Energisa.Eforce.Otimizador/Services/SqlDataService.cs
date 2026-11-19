using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Energisa.Context.Interface;
using Energisa.Eforce.Otimizador.Configuration;
using Energisa.Eforce.Otimizador.Models.Eforce;
using Energisa.God.Domain.Repository.Config;
using Energisa.God.InfraData.Sql.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Energisa.Eforce.Otimizador.Services;



/// <summary>
/// Serviço de debug que carrega dados de ordens de serviço e equipes
/// a partir de queries SQL executadas no Oracle (PdaContext),
/// como fluxo alternativo à origem padrão (MongoDB).
/// </summary>
public class SqlDataService
{
    private readonly IContextRepository<PdaContext> _pdaContextRepository;
    private readonly IParametroConfigRepository _parametroConfigRepository;
    private readonly OtimizacaoSettings _settings;
    private readonly ILogger<SqlDataService> _logger;
    private const string ChaveValoresPesoOtimizacao = "Eforce:ValoresPesoOtimizacao";

    public SqlDataService(
        IContextRepository<PdaContext> pdaContextRepository,
        IParametroConfigRepository parametroConfigRepository,
        IOptions<OtimizacaoSettings> settings,
        ILogger<SqlDataService> logger)
    {
        _pdaContextRepository = pdaContextRepository;
        _parametroConfigRepository = parametroConfigRepository;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Carrega dados via SQL como alternativa ao MongoDB (somente debug).
    /// Lê dois arquivos .sql (OS e Equipes), executa-os no PdaContext
    /// e retorna um <see cref="DadosEforce"/> com os resultados.
    /// </summary>
    public async Task<DadosEforce?> ObterDadosViaSqlAsync(CancellationToken cancellationToken = default)
    {
        var sqlDebug = _settings.SqlDebug;
        if (sqlDebug is null || !sqlDebug.Habilitado)
        {
            _logger.LogWarning("SqlDebug não está habilitado nas configurações.");
            return null;
        }

        // 0. Carregar pesos de otimização configuráveis (Eforce:ValoresPesoOtimizacao)
        var parametroValoresPeso = await _parametroConfigRepository.ObterPorChaveAsync(ChaveValoresPesoOtimizacao);
        if (parametroValoresPeso?.Valores == null || !parametroValoresPeso.Valores.Any())
            throw new InvalidOperationException(
                $"Parâmetro '{ChaveValoresPesoOtimizacao}' não encontrado ou sem valores. Processo abortado.");
        ConversaoConstantes.CarregarDeParametro(parametroValoresPeso.Valores!);
        ConversaoConstantes.Validar();

        _logger.LogInformation("Valores de peso de otimização carregados do parâmetro '{Chave}'.", ChaveValoresPesoOtimizacao);

        _logger.LogInformation("[SQL Debug] Carregando dados via SQL. Arquivos: OS='{ArquivoOs}', Equipes='{ArquivoEquipes}'.",
            sqlDebug.CaminhoSqlOrdensServico, sqlDebug.CaminhoSqlEquipes);

        var sqlOs = await LerArquivoSqlAsync(sqlDebug.CaminhoSqlOrdensServico);
        var sqlEquipes = await LerArquivoSqlAsync(sqlDebug.CaminhoSqlEquipes);

        if (string.IsNullOrWhiteSpace(sqlOs) || string.IsNullOrWhiteSpace(sqlEquipes))
        {
            _logger.LogError("[SQL Debug] Um ou ambos os arquivos SQL estão vazios.");
            return null;
        }

        var context = _pdaContextRepository.GetContext();
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await AlterarSchemaAsync(connection, "ATDESS", cancellationToken);

        var sw = Stopwatch.StartNew();

        var ordensServico = await ExecutarQueryAsync(connection, sqlOs, MapearOrdemServico, cancellationToken);
        _logger.LogInformation("[SQL Debug] Ordens de serviço carregadas: {Count} em {Elapsed}ms.",
            ordensServico.Count, sw.ElapsedMilliseconds);

        sw.Restart();

        var resultadoEquipes = await ExecutarQueryAsync(connection, sqlEquipes, MapearEquipeComRegioes, cancellationToken);
        _logger.LogInformation("[SQL Debug] Equipes carregadas: {Count} em {Elapsed}ms.",
            resultadoEquipes.Count, sw.ElapsedMilliseconds);

        var equipes = resultadoEquipes.Select(r => r.Equipe).ToList();
        var regioes = resultadoEquipes.SelectMany(r => r.Regioes).ToList();

        // Aplicar override de percentual de carga de trabalho (mesma regra do LibGodDataService)
        var parametroCarga = await _parametroConfigRepository.ObterPorChaveAsync("Eforce:PercentualAlocacaoEquipe");
        if (parametroCarga?.Valores != null && parametroCarga.Valores.Any())
        {
            var valorCarga = parametroCarga.Valores.Values.FirstOrDefault();
            if (double.TryParse(valorCarga, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                _logger.LogInformation("[SQL Debug] Percentual de carga de trabalho sobrescrito pelo parâmetro: {Percentual}%.", parsed);
                foreach (var equipe in equipes)
                    equipe.PorcentagemCargaTrabalho = parsed;
            }
        }

        _logger.LogInformation("[SQL Debug] Regiões extraídas das equipes: {Count}.", regioes.Count);

        return new DadosEforce
        {
            OrdensServico = ordensServico,
            Equipes = equipes,
            PerfisEquipe = [],
            Regioes = regioes
        };
    }

    private static async Task<string> LerArquivoSqlAsync(string caminho)
    {
        if (!File.Exists(caminho))
            throw new FileNotFoundException($"Arquivo SQL não encontrado: {caminho}");

        return await File.ReadAllTextAsync(caminho);
    }

    private static async Task AlterarSchemaAsync(DbConnection connection, string schema, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER SESSION SET CURRENT_SCHEMA = {schema}";
        cmd.CommandType = CommandType.Text;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Separa comandos ALTER SESSION do SELECT principal, executa-os em ordem
    /// e mapeia os resultados do SELECT.
    /// </summary>
    private static async Task<List<T>> ExecutarQueryAsync<T>(
        DbConnection connection, string sql, Func<DbDataReader, T> mapper, CancellationToken cancellationToken)
    {
        var (comandosPrevios, selectPrincipal) = SepararComandosSql(sql);

        foreach (var comando in comandosPrevios)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = comando;
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var resultado = new List<T>();

        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = selectPrincipal;
        selectCmd.CommandType = CommandType.Text;

        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(mapper(reader));
        }

        return resultado;
    }

    /// <summary>
    /// Separa instruções ALTER SESSION (ou outros comandos DDL/session) do SELECT principal.
    /// Assume que o SELECT principal começa com WITH ou SELECT.
    /// </summary>
    private static (List<string> ComandosPrevios, string SelectPrincipal) SepararComandosSql(string sql)
    {
        var comandos = new List<string>();
        var linhas = sql.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var selectInicio = -1;

        for (var i = 0; i < linhas.Length; i++)
        {
            var linhaTrim = linhas[i].Trim();   // Trim() nos dois lados (pega \r e espaços)

            // Início do bloco principal: WITH ou SELECT — inclusive sozinhos na linha
            if (linhaTrim.Equals("WITH", StringComparison.OrdinalIgnoreCase) ||
                linhaTrim.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase) ||
                linhaTrim.StartsWith("WITH(", StringComparison.OrdinalIgnoreCase) ||
                linhaTrim.Equals("SELECT", StringComparison.OrdinalIgnoreCase) ||
                linhaTrim.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) ||
                linhaTrim.StartsWith("SELECT/*", StringComparison.OrdinalIgnoreCase))
            {
                selectInicio = i;
                break;
            }

            if (!string.IsNullOrWhiteSpace(linhaTrim) && !linhaTrim.StartsWith("--"))
            {
                var comando = linhaTrim.TrimEnd(';', ' ', '\t');
                if (!string.IsNullOrWhiteSpace(comando))
                    comandos.Add(comando);
            }
        }

        var selectSql = selectInicio >= 0
            ? string.Join('\n', linhas.Skip(selectInicio))
            : sql;

        // ODP.NET rejeita ';' terminador em statement único (ORA-00911)
        selectSql = selectSql.TrimEnd();
        if (selectSql.EndsWith(";"))
            selectSql = selectSql[..^1].TrimEnd();

        return (comandos, selectSql);
    }
    

    #region Mapeamento de OS

    private static OrdemServicoGmpro MapearOrdemServico(DbDataReader reader)
    {
        return new OrdemServicoGmpro
        {
            NumeroOs = GetValueAsString(reader, "NUMERO_OS"),
            NumcdcLote = GetValueAsStringOrNull(reader, "NUMCDC_LOTE"),
            Codserv = GetInt(reader, "CODSERV"),
            Latitude = GetDoubleOrNull(reader, "LATITUDE_OS"),
            Longitude = GetDoubleOrNull(reader, "LONGITUDE_OS"),
            DataVencimento = GetDateTime(reader, "DATA_VENCIMENTO"),
            DataSolicitacao = GetDateTime(reader, "DATA_SOLICITACAO"),
            RegiaoDespacho = GetValueAsStringOrNull(reader, "REGIAO_DESPACHO"),
            TipoOs = GetValueAsString(reader, "TIPO_OS"),
            RiscoVida = GetStringOrDefault(reader, "RISCO_VIDA", "N"),
            OsRegulada = GetStringOrDefault(reader, "OS_REGULADA", "N"),
            Reicidencia = GetStringOrDefault(reader, "REICIDENCIA", "N"),
            PessoasAtingidas = GetInt(reader, "PESSOAS_ATINGIDAS"),
            Compensacao = GetDouble(reader, "COMPENSACAO"),
            CompensacaoPorHora = GetDouble(reader, "COMPENSACAO_POR_HORA"),
            Prioridade = GetStringOrDefault(reader, "PRIORIDADE", "NORMAL"),
            MetaTme = GetDoubleOrNull(reader, "META_TME"),
            Localizacao = GetStringOrDefault(reader, "LOCALIZACAO", "U"),
            TipoCorte = GetValueAsStringOrNull(reader, "TIPO_CORTE")
        };
    }

    #endregion

    #region Mapeamento de Equipes + Regiões

    private record EquipeComRegioes(EquipeGmpro Equipe, List<RegiaoGmpro> Regioes);

    private static EquipeComRegioes MapearEquipeComRegioes(DbDataReader reader)
    {
        var codigoEquipe = GetValueAsString(reader, "EQUIPE");
        var tipoEquipe = GetValueAsString(reader, "TIPO_EQUIPE");

        var equipe = new EquipeGmpro
        {
            CodigoEquipe = codigoEquipe,
            TipoEquipe = tipoEquipe,
            AtuacaoEquipe = GetStringOrDefault(reader, "ATUACAO_EQUIPE", "U/R"),
            CustoKm = GetDouble(reader, "CUSTO_KM"),
            CustoFixo = GetDouble(reader, "CUSTO_FIXO"),
            InicioTurno = GetDateTimeFromStringOrDate(reader, "INICIO_TURNO"),
            FimTurno = GetDateTimeFromStringOrDate(reader, "FIM_TURNO"),
            InicioRefeicao = GetDateTimeFromStringOrDate(reader, "INICIO_REFEICAO"),
            FimRefeicao = GetDateTimeFromStringOrDate(reader, "FIM_REFEICAO"),
            DuracaoAlmoco = HasColumn(reader, "TEMPO_REFEICAO") ? GetDoubleOrNull(reader, "TEMPO_REFEICAO") : null,
            LatitudeGaragem = GetDoubleOrNull(reader, "LATITUDE_LOCAL_PARTIDA"),
            LongitudeGaragem = GetDoubleOrNull(reader, "LONGITUDE_LOCAL_PARTIDA"),
            LatitudeAlmoco = null,//GetDoubleOrNull(reader, "LATITUDE_LOCAL_REFEICAO"),
            LongitudeAlmoco = null,// GetDoubleOrNull(reader, "LONGITUDE_LOCAL_REFEICAO"),
            PorcentagemCargaTrabalho = GetDouble(reader, "PORCENTAGEM_CARGA_TRABALHO") is 0 ? 100.0 : GetDouble(reader, "PORCENTAGEM_CARGA_TRABALHO"),
            MaxLoad = GetInt(reader, "MAX_LOAD") is 0 ? ConversaoConstantes.CapacidadeEquipePadrao : GetInt(reader, "MAX_LOAD"),
            PerfilEquipe = GetStringOrDefault(reader, "PERFIL_EQUIPE", ""),
            NPessoas = GetInt(reader, "N_PESSOAS") is 0 ? 1 : GetInt(reader, "N_PESSOAS"),
            EquipeNome = codigoEquipe,
            CodRegiao = HasColumn(reader, "REGIAO_DESPACHO") ? GetValueAsStringOrNull(reader, "REGIAO_DESPACHO") ?? string.Empty : string.Empty,
            CodServ = HasColumn(reader, "CODSERV") ? GetStringOrDefault(reader, "CODSERV", "") : string.Empty,
            LatitudeLocalPartida = GetDouble(reader, "LATITUDE_LOCAL_PARTIDA"),
            LongitudeLocalPartida = GetDouble(reader, "LONGITUDE_LOCAL_PARTIDA"),
            LatitudeLocalRetorno = null,//GetDoubleOrNull(reader, "LATITUDE_LOCAL_PARTIDA"),
            LongitudeLocalRetorno = null,//GetDoubleOrNull(reader, "LONGITUDE_LOCAL_PARTIDA"),
            LatitudeLocalRefeicao =null,// GetDoubleOrNull(reader, "LATITUDE_LOCAL_REFEICAO"),
            LongitudeLocalRefeicao = null,// GetDoubleOrNull(reader, "LONGITUDE_LOCAL_REFEICAO"),
            TempoRefeicao = HasColumn(reader, "TEMPO_REFEICAO") ? GetInt(reader, "TEMPO_REFEICAO") : 0,
            TravelDurationMultiple = HasColumn(reader, "TRAVEL_DURATION_MULTIPLE") && GetDouble(reader, "TRAVEL_DURATION_MULTIPLE") is not 0 ? GetDouble(reader, "TRAVEL_DURATION_MULTIPLE") : 1.0
        };

        var regioes = ParseRegioes(
            HasColumn(reader, "REGIOES_EQUIPE") ? GetValueAsStringOrNull(reader, "REGIOES_EQUIPE") : null,
            codigoEquipe,
            tipoEquipe);

        return new EquipeComRegioes(equipe, regioes);
    }

    /// <summary>
    /// Converte a string comma-separated de regiões (ex: "226_1,226_2") em objetos <see cref="RegiaoGmpro"/>.
    /// </summary>
    private static List<RegiaoGmpro> ParseRegioes(string? regioesStr, string codigoEquipe, string tipoEquipe)
    {
        if (string.IsNullOrWhiteSpace(regioesStr))
            return [];

        return regioesStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(codRegiao => new RegiaoGmpro
            {
                CodigoEquipe = codigoEquipe,
                TipoEquipe = tipoEquipe,
                CodRegiao = codRegiao
            })
            .ToList();
    }

    #endregion

    #region Helpers de leitura

    /// <summary>
    /// Lê qualquer tipo de coluna (NUMBER, VARCHAR2 etc.) e converte para string.
    /// </summary>
    private static string GetValueAsString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string? GetValueAsStringOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static string GetStringOrDefault(DbDataReader reader, string column, string defaultValue)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;

        var valor = Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(valor) ? defaultValue : valor;
    }

    private static int GetInt(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static bool HasColumn(DbDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static double GetDouble(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0.0 : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static double? GetDoubleOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTime GetDateTime(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? DateTime.MinValue : reader.GetDateTime(ordinal);
    }

    /// <summary>
    /// Lê uma coluna que pode ser DATE nativo ou VARCHAR2 (TO_CHAR com formato 'YYYY-MM-DD HH24:MI:SS').
    /// </summary>
    private static DateTime GetDateTimeFromStringOrDate(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return DateTime.MinValue;

        var valor = reader.GetValue(ordinal);

        if (valor is DateTime dt)
            return dt;

        var str = Convert.ToString(valor, CultureInfo.InvariantCulture);
        return DateTime.TryParseExact(str, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.MinValue;
    }

    #endregion
}
