using Energisa.Eforce.Otimizador.Models.Eforce;
using Microsoft.Extensions.Logging;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço responsável por aplicar filtros de validação na otimização após a conversão,
/// removendo equipes com horários de almoço inválidos, equipes que não podem atender nenhuma OS
/// e OSs que não podem ser atendidas por nenhuma equipe.
/// </summary>
public class FiltroValidacaoOtimizacaoService
{
    private readonly ILogger<FiltroValidacaoOtimizacaoService> _logger;

    public FiltroValidacaoOtimizacaoService(ILogger<FiltroValidacaoOtimizacaoService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Aplica filtros de validação no resultado da conversão E-force ? GMPRO.
    /// Remove:
    /// 1. Equipes com coordenadas inválidas
    /// 2. Equipes cujo horário de almoço (início ou fim) excede o tempo global de início e fim
    /// 3. OSs com coordenadas inválidas
    /// 4. OSs que não podem ser atendidas por nenhuma equipe
    /// 5. Equipes que não podem atender nenhuma OS (após filtro de compatibilidade)
    /// 
    /// Remapeia os índices das equipes nas OSs após todas as remoções de equipes.
    /// </summary>
    public ResultadoConversao AplicarFiltros(
        ResultadoConversao resultado,
        DateTime tempoGlobalInicio,
        DateTime tempoGlobalFim)
    {
        var equipesIniciais = resultado.EquipesProcessadas.Count;
        var osIniciais = resultado.OrdensProcessadas.Count;

        // Passo 1: Remover equipes com coordenadas inválidas
        _logger.LogInformation(
            "Iniciando filtros de validação. EquipesIniciais: {Equipes}. OsIniciais: {Os}.",
            equipesIniciais, osIniciais);

        var (equipesComCoordenadas, mapeamento1) = FiltrarEquipesComCoordenadaValidaComMapeamento(
            resultado.EquipesProcessadas);

        // Passo 2: Remover equipes com horário de almoço inválido
        var (equipesValidas, mapeamento2) = FiltrarEquipesComHorarioAlmocoValidoComMapeamento(
            equipesComCoordenadas, tempoGlobalInicio, tempoGlobalFim);

        // Passo 3: Remover OSs com coordenadas inválidas
        var osComCoordenadas = FiltrarOsComCoordenadaValida(
            resultado.OrdensProcessadas);

        // Passo 4: Remover OSs que não podem ser atendidas por nenhuma equipe
        var osValidasAntesFiltro = osComCoordenadas.Count;
        var osValidas = FiltrarOsComEquipeCompativel(
            osComCoordenadas, equipesValidas);

        var osRemovidas = (osIniciais - osValidasAntesFiltro) + (osValidasAntesFiltro - osValidas.Count);

        // Passo 5: Remover equipes que não podem atender nenhuma OS
        var (equipesValidas2, mapeamento3) = FiltrarEquipesQueAtenenOsCompativelComMapeamento(
            equipesValidas, osValidas);

        var equipesRemovidas = equipesIniciais - equipesValidas2.Count;

        // Aplicar remapeamento de índices de equipes nas OSs após todos os filtros
        // Composição dos mapeamentos: original -> após coord inválidas -> após almoço inválido -> após remoção de equipes sem OS
        var mapeamento1E2 = ComporMapeamentos(mapeamento1, mapeamento2);
        var mapeamentoFinal = ComporMapeamentos(mapeamento1E2, mapeamento3);
        RemapearIndicesEquipesNasOS(osValidas, mapeamentoFinal);

        // Passo 6: Remover OSs que ficaram sem equipes compatíveis após remapeamento
        var osValidasAntesPosRemapeamento = osValidas.Count;
        osValidas = FiltrarOsComEquipeCompativel(osValidas, equipesValidas2);
        osRemovidas += osValidasAntesPosRemapeamento - osValidas.Count;

        _logger.LogInformation(
            "Filtros aplicados com sucesso. EquipesRemovidas: {EquipesRemovidas}. OsRemovidas: {OsRemovidas}. " +
            "EquipesFinal: {EquipesFinal}. OsFinal: {OsFinal}.",
            equipesRemovidas, osRemovidas, equipesValidas2.Count, osValidas.Count);

        return new ResultadoConversao
        {
            OrdensProcessadas = osValidas,
            EquipesProcessadas = equipesValidas2,
            InicioTurno = resultado.InicioTurno,
            FimTurno = resultado.FimTurno,
            TimeZone = resultado.TimeZone
        };
    }

    /// <summary>
    /// Remove equipes com coordenadas inválidas (garagem ou almoço).
    /// Retorna também um mapeamento de índices antigos ? novos.
    /// </summary>
    private (List<EquipeProcessada>, Dictionary<int, int>) FiltrarEquipesComCoordenadaValidaComMapeamento(
        List<EquipeProcessada> equipes)
    {
        var resultado = new List<EquipeProcessada>();
        var mapeamento = new Dictionary<int, int>();
        var removidas = 0;

        for (int i = 0; i < equipes.Count; i++)
        {
            var equipe = equipes[i];

            if (!SaoCoordenadasValidas(equipe.Equipe.LatitudeGaragem, equipe.Equipe.LongitudeGaragem))
            {
                _logger.LogWarning(
                    "Equipe '{CodigoEquipe}' removida: coordenadas da garagem inválidas. " +
                    "Latitude: {Latitude}. Longitude: {Longitude}.",
                    equipe.Equipe.CodigoEquipe, equipe.Equipe.LatitudeGaragem, equipe.Equipe.LongitudeGaragem);
                removidas++;
                continue;
            }

            if (equipe.Equipe.RetornaGaragemAlmoco)
            {
                if (!SaoCoordenadasValidas(equipe.Equipe.LatitudeAlmoco, equipe.Equipe.LongitudeAlmoco))
                {
                    _logger.LogWarning(
                        "Equipe '{CodigoEquipe}' removida: coordenadas do almoço inválidas. " +
                        "Latitude: {Latitude}. Longitude: {Longitude}.",
                        equipe.Equipe.CodigoEquipe, equipe.Equipe.LatitudeAlmoco, equipe.Equipe.LongitudeAlmoco);
                    removidas++;
                    continue;
                }
            }

            mapeamento[i] = resultado.Count;
            resultado.Add(equipe);
        }

        _logger.LogInformation(
            "Filtro de coordenadas de equipe: {Removidas} equipe(s) removida(s). {Validas} equipe(s) mantida(s).",
            removidas, resultado.Count);

        return (resultado, mapeamento);
    }

    /// <summary>
    /// Remove equipes com coordenadas inválidas (garagem ou almoço).
    /// </summary>
    private List<EquipeProcessada> FiltrarEquipesComCoordenadaValida(
        List<EquipeProcessada> equipes)
    {
        var (resultado, _) = FiltrarEquipesComCoordenadaValidaComMapeamento(equipes);
        return resultado;
    }

    /// <summary>
    /// Remove OSs com coordenadas inválidas.
    /// </summary>
    private List<OsProcessada> FiltrarOsComCoordenadaValida(
        List<OsProcessada> ordensProcessadas)
    {
        var resultado = new List<OsProcessada>();
        var removidas = 0;

        foreach (var os in ordensProcessadas)
        {
            if (!SaoCoordenadasValidas(os.Os.Latitude, os.Os.Longitude))
            {
                _logger.LogWarning(
                    "OS '{NumeroOs}' removida: coordenadas inválidas. " +
                    "Latitude: {Latitude}. Longitude: {Longitude}.",
                    os.Os.NumeroOs, os.Os.Latitude, os.Os.Longitude);
                removidas++;
                continue;
            }

            resultado.Add(os);
        }

        _logger.LogInformation(
            "Filtro de coordenadas de OS: {Removidas} OS(s) removida(s). {Validas} OS(s) mantida(s).",
            removidas, resultado.Count);

        return resultado;
    }

    /// <summary>
    /// Valida se as coordenadas (latitude e longitude) estão dentro dos intervalos válidos.
    /// Latitude deve estar entre -90 e 90. Longitude deve estar entre -180 e 180.
    /// Ambas as coordenadas devem ser fornecidas (não nulas).
    /// </summary>
    private static bool SaoCoordenadasValidas(double? latitude, double? longitude)
    {
        return latitude.HasValue && longitude.HasValue &&
               latitude >= -90 && latitude <= 90 &&
               longitude >= -180 && longitude <= 180;
    }

    /// <summary>
    /// Remove equipes com horário de almoço inválido.
    /// Retorna também um mapeamento de índices antigos ? novos.
    /// </summary>
    private (List<EquipeProcessada>, Dictionary<int, int>) FiltrarEquipesComHorarioAlmocoValidoComMapeamento(
        List<EquipeProcessada> equipes,
        DateTime tempoGlobalInicio,
        DateTime tempoGlobalFim)
    {
        var resultado = new List<EquipeProcessada>();
        var mapeamento = new Dictionary<int, int>();
        var removidas = 0;

        for (int i = 0; i < equipes.Count; i++)
        {
            var equipe = equipes[i];

            var inicioAlmocoValido = equipe.InicioRefeicaoAjustado >= tempoGlobalInicio &&
                                     equipe.InicioRefeicaoAjustado <= tempoGlobalFim;
            var fimAlmocoValido = equipe.FimRefeicaoAjustado >= tempoGlobalInicio &&
                                  equipe.FimRefeicaoAjustado <= tempoGlobalFim;

            if (!inicioAlmocoValido || !fimAlmocoValido)
            {
                _logger.LogWarning(
                    "Equipe '{CodigoEquipe}' removida: horário de almoço inválido. " +
                    "InicioAlmocoAjustado: {InicioAlmoco}. FimAlmocoAjustado: {FimAlmoco}. " +
                    "InteraloTempo: {TempoGlobalInicio} - {TempoGlobalFim}.",
                    equipe.Equipe.CodigoEquipe, equipe.InicioRefeicaoAjustado, equipe.FimRefeicaoAjustado,
                    tempoGlobalInicio, tempoGlobalFim);
                removidas++;
                continue;
            }

            mapeamento[i] = resultado.Count;
            resultado.Add(equipe);
        }

        _logger.LogInformation(
            "Filtro de horário de almoço: {Removidas} equipe(s) removida(s). {Validas} equipe(s) mantida(s).",
            removidas, resultado.Count);

        return (resultado, mapeamento);
    }

    /// <summary>
    /// Remove equipes com horário de almoço inválido.
    /// </summary>
    private List<EquipeProcessada> FiltrarEquipesComHorarioAlmocoValido(
        List<EquipeProcessada> equipes,
        DateTime tempoGlobalInicio,
        DateTime tempoGlobalFim)
    {
        var (resultado, _) = FiltrarEquipesComHorarioAlmocoValidoComMapeamento(
            equipes, tempoGlobalInicio, tempoGlobalFim);
        return resultado;
    }

    /// <summary>
    /// Remove OSs que não têm nenhuma equipe compatível após todos os filtros de validação.
    /// </summary>
    private List<OsProcessada> FiltrarOsComEquipeCompativel(
        List<OsProcessada> ordensProcessadas,
        List<EquipeProcessada> equipesValidas)
    {
        var resultado = new List<OsProcessada>();
        var removidas = 0;

        foreach (var os in ordensProcessadas)
        {
            if (os.IndicesEquipesPermitidas.Count == 0)
            {
                _logger.LogWarning(
                    "OS '{NumeroOs}' removida: nenhuma equipe compatível encontrada.",
                    os.Os.NumeroOs);
                removidas++;
                continue;
            }

            resultado.Add(os);
        }

        _logger.LogInformation(
            "Filtro de compatibilidade OS-Equipe: {Removidas} OS(s) removida(s). {Validas} OS(s) mantida(s).",
            removidas, resultado.Count);

        return resultado;
    }

    /// <summary>
    /// Remove equipes que não conseguem atender nenhuma OS válida.
    /// Retorna também um mapeamento de índices antigos ? novos.
    /// </summary>
    private (List<EquipeProcessada>, Dictionary<int, int>) FiltrarEquipesQueAtenenOsCompativelComMapeamento(
        List<EquipeProcessada> equipesValidas,
        List<OsProcessada> osValidas)
    {
        var indicesEquipesUsadas = new HashSet<int>();

        foreach (var os in osValidas)
        {
            foreach (var indiceEquipe in os.IndicesEquipesPermitidas)
            {
                indicesEquipesUsadas.Add(indiceEquipe);
            }
        }

        var mapeamentoIndices = new Dictionary<int, int>();
        var resultado = new List<EquipeProcessada>();
        var removidas = 0;

        for (int i = 0; i < equipesValidas.Count; i++)
        {
            var equipe = equipesValidas[i];

            if (!indicesEquipesUsadas.Contains(i))
            {
                _logger.LogWarning(
                    "Equipe '{CodigoEquipe}' removida: não pode atender nenhuma OS compatível.",
                    equipe.Equipe.CodigoEquipe);
                removidas++;
                continue;
            }

            mapeamentoIndices[i] = resultado.Count;
            resultado.Add(equipe);
        }

        _logger.LogInformation(
            "Filtro de equipes sem OS compatível: {Removidas} equipe(s) removida(s). {Validas} equipe(s) mantida(s).",
            removidas, resultado.Count);

        return (resultado, mapeamentoIndices);
    }

    /// <summary>
    /// Remove equipes que não conseguem atender nenhuma OS válida.
    /// </summary>
    private List<EquipeProcessada> FiltrarEquipesQueAtenenOsCompativel(
        List<EquipeProcessada> equipesValidas,
        List<OsProcessada> osValidas)
    {
        var (resultado, _) = FiltrarEquipesQueAtenenOsCompativelComMapeamento(equipesValidas, osValidas);
        return resultado;
    }

    /// <summary>
    /// Remapeia os índices de equipes nas OSs para refletir as posições na nova lista de equipes.
    /// Necessário porque o filtro remove equipes, alterando os índices válidos.
    /// </summary>
    private void RemapearIndicesEquipesNasOS(
        List<OsProcessada> ordensProcessadas,
        Dictionary<int, int> mapeamentoIndices)
    {
        foreach (var os in ordensProcessadas)
        {
            var indicesAtualizados = new List<int>();

            foreach (var indiceAntigo in os.IndicesEquipesPermitidas)
            {
                if (mapeamentoIndices.TryGetValue(indiceAntigo, out var indiceNovo))
                {
                    indicesAtualizados.Add(indiceNovo);
                }
            }

            if (indicesAtualizados.Count == 0)
            {
                _logger.LogWarning(
                    "OS '{NumeroOs}' ficou sem equipes compatíveis após remapeamento. " +
                    "Esta OS deveria ter sido removida antes.",
                    os.Os.NumeroOs);
            }

            os.IndicesEquipesPermitidas = indicesAtualizados;
        }
    }

    /// <summary>
    /// Compõe dois mapeamentos sequenciais em um único mapeamento final.
    /// Se mapeamento1 mapeia 0→2 e mapeamento2 mapeia 2→1, o resultado mapeia 0→1.
    /// </summary>
    private Dictionary<int, int> ComporMapeamentos(
        Dictionary<int, int> mapeamento1,
        Dictionary<int, int> mapeamento2)
    {
        var mapeamentoFinal = new Dictionary<int, int>();

        foreach (var kvp in mapeamento1)
        {
            var indiceOriginal = kvp.Key;
            var indiceApos1 = kvp.Value;

            if (mapeamento2.TryGetValue(indiceApos1, out var indiceApos2))
            {
                mapeamentoFinal[indiceOriginal] = indiceApos2;
            }
        }

        return mapeamentoFinal;
    }
}
