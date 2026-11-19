using Energisa.Eforce.Otimizador.Models;
using Energisa.Eforce.Otimizador.Models;
using Energisa.Eforce.Otimizador.Services.Interface;
using Energisa.God.Domain.Model._OrdemServico_;
using Energisa.God.Domain.Model._OrdemServico_.Interface;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Model.Geral;
using Energisa.God.Domain.Repository._OrdemServico_.Interface;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Microsoft.Extensions.Logging;

namespace Energisa.Eforce.Otimizador.Services;

/// <summary>
/// Serviço responsável por buscar os resultados de otimização e enriquecê-los
/// com dados de ordens de serviço obtidos do repositório NoSQL.
/// </summary>
public class ResultadoOtimizacaoEnriquecimentoService(
    IOtimizadorRotasRepository otimizadorRepository,
    IOrdemServicoRepository osRepository,
    ILogger<ResultadoOtimizacaoEnriquecimentoService> logger) : IResultadoOtimizacaoEnriquecimentoService
{
    private const int TamanhoLote = 500;

    private static readonly string[] ProjecaoOs =
    [
        "NumeroOs",
        "Situacao",
        "DataRegistro",
        "DataVencimento",
        "GrupoId",
        "Indenizacoes",
        "Bloqueio",
        "Servico",
        "UnidadeConsumidoraOrigem",
        "Reincidencia",
        "Localizacao",
        "Abrangencia",
        "Regulado",
        "Callbacks",
        "Observacoes",
        "Children",
        "RegiaoWfm",
        "CategoriaPriorizacao",
        "Parent"
    ];

    /// <summary>
    /// Obtém os resultados de otimização das últimas 12 horas com dados de OS enriquecidos.
    /// </summary>
    public async Task<List<ResultadoOtimizacaoEnriquecido>> ObterResultadosEnriquecidosAsync(
        string login, string empresa)
    {
        var resultados = await otimizadorRepository.ObterResultadosOtimizacaoUltimas12HorasAsync(login, empresa);

        if (resultados.Count == 0)
            return [];

        var todosIdsOs = resultados
            .SelectMany(r => r.Equipes)
            .SelectMany(e => e.OrdensServico)
            .Where(os => !os.IsAlmoco && !string.IsNullOrWhiteSpace(os.Id))
            .Select(os => os.Id)
            .Distinct()
            .ToArray();

        var lookupOs = await BuscarOsDicionarioAsync(todosIdsOs);

        return resultados
            .Select(r => MapearResultado(r, lookupOs))
            .ToList();
    }

    private async Task<Dictionary<string, IOrdemServico>> BuscarOsDicionarioAsync(string[] ids)
    {
        if (ids.Length == 0)
            return [];

        var lookup = new Dictionary<string, IOrdemServico>(StringComparer.OrdinalIgnoreCase);

        var lotes = ids.Chunk(TamanhoLote);

        foreach (var lote in lotes)
        {
            try
            {
                var ordensServico = await osRepository.GetOrdemServicoByIdWithProjectionAsync(lote, ProjecaoOs);

                foreach (var os in ordensServico.Where(os => !string.IsNullOrWhiteSpace(os.Id)))
                {
                    lookup.TryAdd(os.Id!, os);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erro ao buscar enriquecimento de OS no lote. Lote ignorado.");
            }
        }

        return lookup;
    }

    private ResultadoOtimizacaoEnriquecido MapearResultado(
        ResultadoOtimizacaoEquipes resultado,
        Dictionary<string, IOrdemServico> lookupOs)
    {
        return new ResultadoOtimizacaoEnriquecido
        {
            Id = resultado.Id,
            TipoOtimizacao = resultado.TipoOtimizacao,
            DataOtimizacao = resultado.DataOtimizacao,
            Equipes = resultado.Equipes.Select(e => MapearEquipe(e, lookupOs)).ToList(),
            Solicitante = resultado.Solicitante
        };
    }

    private RotaEquipeEnriquecida MapearEquipe(
        RotaEquipe equipe,
        Dictionary<string, IOrdemServico> lookupOs)
    {
        return new RotaEquipeEnriquecida
        {
            Equipe = equipe.Equipe,
            TipoEquipe = equipe.TipoEquipe,
            PerfilEquipe = equipe.PerfilEquipe,
            LatitudeGaragem = equipe.LatitudeGaragem,
            LongitudeGaragem = equipe.LongitudeGaragem,
            CustoKm = equipe.CustoKm,
            MaxLoad = equipe.MaxLoad,
            OrdensServico = equipe.OrdensServico.Select(os => MapearOs(os, lookupOs)).ToList(),
            Polyline = equipe.Polyline,
            Aprovada = equipe.Aprovada,
            Aprovador = equipe.Aprovador,
            DataAprovacao = equipe.DataAprovacao
        };
    }

    private OrdemServicoAtendidaEnriquecida MapearOs(
        OrdemServicoAtendida os,
        Dictionary<string, IOrdemServico> lookupOs)
    {
        var enriquecida = new OrdemServicoAtendidaEnriquecida
        {
            Id = os.Id,
            TipoOs = os.TipoOs,
            Regulada = os.Regulada,
            RiscoVida = os.RiscoVida,
            RegiaoDespacho = os.RegiaoDespacho,
            Codserv = os.Codserv,
            PesoFinal = os.PesoFinal,
            DataVencimentoAjustada = os.DataVencimentoAjustada,
            Prioridade = os.Prioridade,
            PerfilEquipe = os.PerfilEquipe,
            Latitude = os.Latitude,
            Longitude = os.Longitude,
            DuracaoSegundos = os.DuracaoSegundos,
            PenaltyCost = os.PenaltyCost,
            HorarioInicio = os.HorarioInicio,
            Detour = os.Detour,
            IsAlmoco = os.IsAlmoco
        };

        if (!string.IsNullOrWhiteSpace(os.Id) && lookupOs.TryGetValue(os.Id, out var dadosOs))
        {
            enriquecida.NumeroOs = dadosOs.NumeroOs;
            enriquecida.Situacao = dadosOs.Situacao as Situacao;
            enriquecida.DataRegistro = dadosOs.DataRegistro;
            enriquecida.DataVencimento = dadosOs.DataVencimento;
            enriquecida.GrupoId = dadosOs.GrupoId;
            enriquecida.Indenizacoes = dadosOs.Indenizacoes?.Cast<Indenizacao>().ToList();
            enriquecida.Bloqueio = dadosOs.Bloqueio as Bloqueio;
            enriquecida.Servico = dadosOs.Servico as God.Domain.Model.Servico.CatalogoServico;
            enriquecida.UnidadeConsumidoraOrigem = dadosOs.UnidadeConsumidoraOrigem as God.Domain.Model.Cliente.UnidadeConsumidora;
            enriquecida.Reincidencia = dadosOs.Reincidencia;
            enriquecida.Localizacao = dadosOs.Localizacao as God.Domain.Model._OrdemServico_.Localizacao;
            enriquecida.Abrangencia = dadosOs.Abrangencia;
            enriquecida.Regulado = dadosOs.Regulado;
            enriquecida.Callbacks = dadosOs.Callbacks?.Cast<Callback>().ToList();
            enriquecida.Observacoes = dadosOs.Observacoes?.Cast<Observacao>().ToList();
            enriquecida.Children = dadosOs.Children;
            enriquecida.RegiaoWfm = dadosOs.RegiaoWfm;
            enriquecida.CategoriaPriorizacao = dadosOs.CategoriaPriorizacao;
            enriquecida.Parent = dadosOs.Parent;

            if (enriquecida.Situacao is null && dadosOs.Situacao is not null)
                logger.LogWarning("Cast de Situacao falhou para OS {OsId}. Tipo recebido: {Tipo}", os.Id, dadosOs.Situacao.GetType().Name);
            if (enriquecida.Bloqueio is null && dadosOs.Bloqueio is not null)
                logger.LogWarning("Cast de Bloqueio falhou para OS {OsId}. Tipo recebido: {Tipo}", os.Id, dadosOs.Bloqueio.GetType().Name);
            if (enriquecida.Servico is null && dadosOs.Servico is not null)
                logger.LogWarning("Cast de Servico falhou para OS {OsId}. Tipo recebido: {Tipo}", os.Id, dadosOs.Servico.GetType().Name);
            if (enriquecida.UnidadeConsumidoraOrigem is null && dadosOs.UnidadeConsumidoraOrigem is not null)
                logger.LogWarning("Cast de UnidadeConsumidoraOrigem falhou para OS {OsId}. Tipo recebido: {Tipo}", os.Id, dadosOs.UnidadeConsumidoraOrigem.GetType().Name);
            if (enriquecida.Localizacao is null && dadosOs.Localizacao is not null)
                logger.LogWarning("Cast de Localizacao falhou para OS {OsId}. Tipo recebido: {Tipo}", os.Id, dadosOs.Localizacao.GetType().Name);
        }

        return enriquecida;
    }
}
