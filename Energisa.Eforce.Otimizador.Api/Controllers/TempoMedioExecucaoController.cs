using Energisa.God.Domain.Model.Alocacao.Metricas;
using Energisa.God.Domain.Service.Alocacao.Interface;
using Microsoft.AspNetCore.Mvc;

namespace Energisa.Eforce.Otimizador.Api.Controllers
{
    /// <summary>
    /// Controller para gerenciamento do Tempo Médio de Execução (TME) por código de serviço.
    /// </summary>
    [Route("tempo-medio-execucao")]
    [ApiController]
    [Produces("application/json")]
    public class TempoMedioExecucaoController : ControllerBase
    {
        private readonly ILogger<TempoMedioExecucaoController> _logger;
        private readonly ITempoMedioExecucaoService _service;

        public TempoMedioExecucaoController(
            ILogger<TempoMedioExecucaoController> logger,
            ITempoMedioExecucaoService service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// Obtém o TME de um serviço pelo código externo numérico.
        /// </summary>
        /// <param name="codigoServico">Código externo numérico do serviço.</param>
        [HttpGet("{codigoServico:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAsync([FromRoute] int codigoServico)
        {
            try
            {
                var tme = await _service.GetByCodigoServicoAsync(codigoServico);

                if (tme is null)
                    return NotFound($"TME não encontrado para o código de serviço {codigoServico}.");

                return Ok(tme);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar TME para o código de serviço {CodigoServico}.", codigoServico);
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Cria ou atualiza o TME de um serviço (upsert).
        /// </summary>
        /// <param name="request">Dados do TME a ser criado ou atualizado.</param>
        [HttpPut]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpsertAsync([FromBody] UpsertTempoMedioExecucaoRequest request)
        {
            if (!request.CodigoServico.HasValue)
                return BadRequest("O código de serviço é obrigatório.");

            try
            {
                var tme = new TempoMedioExecucao
                {
                    CodigoServico = request.CodigoServico,
                    TempoMedioDireto = request.TempoMedioDireto,
                    TempoMedioIndireto = request.TempoMedioIndireto,
                    CodigoServicoFilho = request.CodigoServicoFilho
                };

                var resultado = await _service.UpsertAsync(tme);
                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar TME para o código de serviço {CodigoServico}.", request.CodigoServico);
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }
    }

    /// <summary>
    /// Request para criação ou atualização do Tempo Médio de Execução (TME).
    /// </summary>
    public class UpsertTempoMedioExecucaoRequest
    {
        /// <summary>
        /// Código externo numérico do serviço.
        /// </summary>
        public int? CodigoServico { get; set; }

        /// <summary>
        /// Tempo médio de execução direto (campo principal utilizado na otimização).
        /// </summary>
        public TimeSpan? TempoMedioDireto { get; set; }

        /// <summary>
        /// Tempo médio de execução indireto.
        /// </summary>
        public TimeSpan? TempoMedioIndireto { get; set; }

        /// <summary>
        /// Código de serviço filho, cujo TME será somado ao principal durante a otimização.
        /// </summary>
        public int? CodigoServicoFilho { get; set; }
    }
}
