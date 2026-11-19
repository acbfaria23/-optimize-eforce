using Energisa.Context.Interface;
using Energisa.Eforce.Otimizador.Models;
using Energisa.Eforce.Otimizador.Models.Reotimizacao;
using Energisa.Eforce.Otimizador.Services;
using Energisa.Eforce.Otimizador.Services.Interface;
using Energisa.God.Domain.Model.Alocacao.OtimizadorRotas;
using Energisa.God.Domain.Repository.Alocacao.Interface;
using Energisa.God.Domain.Service.Alocacao.Interface;
using Energisa.God.InfraData.Http.Definitions;
using Google.Maps.RouteOptimization.V1;
using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Energisa.Eforce.Otimizador.Api.Controllers
{
    /// <summary>
    /// Controller utilizado para recurso do Otimizador.
    /// </summary>
    [Route("otimizar-rotas")]
    [ApiController]
    [Produces("application/json")]
    public class OtimizadorController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly IOtimizadorRotasService _service;
        private readonly OtimizacaoPipelineService _pipeline;
        private readonly IOtimizadorRotasRepository _repository;
        private readonly IContextRepository _context;
        private readonly DespachoAutomaticoProducerService _despachoProducer;
        private readonly GoogleStorageDownloadService _storageService;
        private readonly GoogleCloudStorageService _googleCloudStorageService;
        private readonly IResultadoOtimizacaoEnriquecimentoService _enriquecimentoService;
        private readonly ReotimizacaoDataService _reotimizacaoDataService;
        private readonly JsonParser _jsonParser = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

        public OtimizadorController(
            ILogger<OtimizadorController> logger,
            IOtimizadorRotasService service,
            OtimizacaoPipelineService pipeline,
            IOtimizadorRotasRepository repository,
            IContextRepository context,
            DespachoAutomaticoProducerService despachoProducer,
            GoogleStorageDownloadService storageService,
            GoogleCloudStorageService googleCloudStorageService,
            IResultadoOtimizacaoEnriquecimentoService enriquecimentoService,
            ReotimizacaoDataService reotimizacaoDataService)
        {
            _logger = logger;
            _service = service;
            _pipeline = pipeline;
            _repository = repository;
            _context = context;
            _despachoProducer = despachoProducer;
            _storageService = storageService;
            _googleCloudStorageService = googleCloudStorageService;
            _enriquecimentoService = enriquecimentoService;
            _reotimizacaoDataService = reotimizacaoDataService;
        }

        /// <summary>
        /// Obter o retorno assíncrono do Otimizador através do GCP (Google Cloud Platform).
        /// Recebe uma URI do Google Cloud Storage contendo o JSON de resposta.
        /// </summary>
        /// <returns></returns>
        [HttpPatch("atualizar-response")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostAsync(
            [FromBody] OtimizadorResponseUriRequest request,
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null)
        {
            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            if (string.IsNullOrWhiteSpace(request?.Uri) || !request.Uri.StartsWith("gs://"))
                return BadRequest("URI inválida. Esperado formato gs://bucket/path");

            try
            {
               var json = await _storageService.DownloadAsStringAsync(request.Uri);

               var parsedResponse = _jsonParser.Parse<OptimizeToursResponse>(json);

               var otimizadorRotas = await _service.CriaResponseOtimizador(parsedResponse, _context.Empresa.ToString(), TipoOtimizacao.Automatica);

               // §17: capturar a referência exata e imutável (geração + hash MD5) do objeto de
               // output confirmado pelo Google, e persisti-la no registro correspondente, para
               // permitir a recuperação garantida do protobuf bruto original (input e output) em
               // reotimizações futuras, sem depender de convenção de nomes.
               try
               {
                   var (bucketName, objectName) = ExtrairBucketEObjeto(request.Uri);
                   var referenciaOutput = await _googleCloudStorageService.ObterReferenciaObjetoAsync(bucketName, objectName);

                   // Quando o OperationName é informado, ele garante a correlação direta com o
                   // registro que já possui a referência de input (gravada no envio da otimização).
                   // Caso contrário, usa o Id retornado por CriaResponseOtimizador como alternativa.
                   if (!string.IsNullOrWhiteSpace(request.OperationName))
                   {
                       await _repository.AtualizarReferenciaOutputPorOperationNameAsync(request.OperationName, referenciaOutput);
                   }
                   else
                   {
                       await _repository.AtualizarReferenciaOutputAsync(otimizadorRotas.Id, referenciaOutput);
                   }

                   _logger.LogInformation(
                       "Referência GCS do output persistida com sucesso. Bucket: {Bucket}, Objeto: {Objeto}, Geração: {Geracao}.",
                       referenciaOutput.BucketName, referenciaOutput.ObjectName, referenciaOutput.Generation);
               }
               catch (Exception ex)
               {
                   _logger.LogWarning(ex, "Não foi possível capturar/persistir a referência exata do objeto de output no GCS.");
               }

               return StatusCode(StatusCodes.Status201Created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gravar informações de Otimização");
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Extrai o nome do bucket e do objeto a partir de uma URI no formato gs://bucket/objeto.
        /// </summary>
        private static (string BucketName, string ObjectName) ExtrairBucketEObjeto(string gsUri)
        {
            var semPrefixo = gsUri["gs://".Length..];
            var indiceBarra = semPrefixo.IndexOf('/');
            return (semPrefixo[..indiceBarra], semPrefixo[(indiceBarra + 1)..]);
        }

        /// <summary>
        /// Requisita uma otimização síncrona para o GCP (Google Cloud Platform) filtrando apenas as ordens de serviço informadas.
        /// </summary>
        /// <param name="request">Lista de números de OS a otimizar.</param>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        [HttpPost("solicitar-otimizacao")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> OtimizarAsync(
            [FromBody] OtimizacaoRequest request,
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            if (request?.NumerosOs == null || request.NumerosOs.Count == 0)
                return BadRequest("A lista de ordens de serviço é obrigatória.");

            try
            {
                // 1. Carregar dados E-force
                var dados = await _pipeline.CarregarDadosAsync(cancellationToken);
                if (dados is null)
                    return Problem("Não foi possível obter dados E-force do NoSQL.");

                // 2. Filtrar apenas as OS solicitadas
                var numerosOsSet = new HashSet<string>(request.NumerosOs, StringComparer.OrdinalIgnoreCase);
                dados.OrdensServico = dados.OrdensServico
                    .Where(os => numerosOsSet.Contains(os.NumeroOs))
                    .ToList();

                if (dados.OrdensServico.Count == 0)
                    return BadRequest("Nenhuma ordem de serviço encontrada após filtro.");

                // 3. Executar pipeline síncrono
                var resultado = await _pipeline.ExecutarPipelineSincronoAsync(
                    dados, "API", modoAsync: false, cancellationToken);

                // 4. Obter o ResultadoOtimizacao persistido no MongoDB
                var resultadoOtimizacao = await _pipeline.ObterResultadoOtimizacaoAsync(resultado.Label);

                if (resultadoOtimizacao is null)
                    return Problem("ResultadoOtimizacao não encontrado no MongoDB após persistência.");

                return Ok((object)resultadoOtimizacao);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Erro de validação na otimização via API.");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar otimização via API.");
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Obtém os resultados de otimização das últimas 12 horas para o usuário autenticado.
        /// </summary>
        [HttpGet("resultados")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ObterResultadosAsync(
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null)
        {
            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            try
            {
                var resultados = await _repository.ObterResultadosOtimizacaoUltimas12HorasAsync(_context.User, _context.Empresa.ToString());
                return Ok(resultados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter resultados de otimização.");
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Obtém os resultados de otimização das últimas 12 horas com dados de OS enriquecidos.
        /// </summary>
        [HttpGet("resultados-enriquecidos")]
        [ProducesResponseType(typeof(List<ResultadoOtimizacaoEnriquecido>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ObterResultadosEnriquecidosAsync(
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null)
        {
            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            try
            {
                var resultados = await _enriquecimentoService.ObterResultadosEnriquecidosAsync(
                    _context.User, _context.Empresa.ToString());
                return Ok(resultados);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter resultados de otimização enriquecidos.");
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Aprova as equipes de uma otimização.
        /// </summary>
        /// <param name="request">ID da otimização e lista de equipes.</param>
        [HttpPatch("aprovar-equipes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AprovarEquipesAsync(
            [FromBody] AprovarEquipesRequest request,
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null)
        {
            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            if (string.IsNullOrWhiteSpace(request?.Id))
                return BadRequest("O ID da otimização é obrigatório.");

            if (request.Equipes == null || request.Equipes.Count == 0)
                return BadRequest("A lista de equipes é obrigatória.");

            try
            {
                var resultadoOtimizacao = await _repository.GetByIdAsync(request.Id);

                if (resultadoOtimizacao?.Equipes != null)
                {
                    var equipesSet = new HashSet<string>(request.Equipes, StringComparer.OrdinalIgnoreCase);
                    foreach (var rotaEquipe in resultadoOtimizacao.Equipes.Where(e => equipesSet.Contains(e.Equipe)))
                    {
                        await _despachoProducer.ProduceDespachoAsync(rotaEquipe, _context);
                    }
                }

                await _repository.AprovarEquipesAsync(request.Id, request.Equipes);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao aprovar equipes da otimização.");
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Solicita a reotimização intradiária (§3) de uma otimização já executada, restringindo
        /// o novo cálculo às equipes selecionadas. Monta o snapshot operacional completo (§5) a
        /// partir dos dados reais (posição/turno das equipes, situação atual das OSs) e dispara
        /// a otimização de forma assíncrona (fire-and-forget) via URI, assim como o fluxo automático.
        /// </summary>
        /// <param name="command">Identificador da otimização original e códigos das equipes selecionadas.
        /// Não deve conter request/response originais, posições, status de OSs, cadastro de
        /// equipes, data/hora da solicitação ou timezone — esses dados são recuperados pelo
        /// backend (§3).</param>
        [HttpPost("reotimizacoes")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SolicitarReotimizacaoAsync(
            [FromBody] SolicitarReotimizacaoCommand command,
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            _logger.LogInformation(
                "Iniciando solicitação de reotimização via API. Empresa: {Empresa}. Usuario: {Usuario}. " +
                "OtimizacaoOriginalId: {OtimizacaoOriginalId}. Equipes: {EquipeCount}.",
                _context.Empresa, _context.User, command?.OtimizacaoOriginalId, command?.Equipes?.Count ?? 0);

            if (command is null || string.IsNullOrWhiteSpace(command.OtimizacaoOriginalId))
            {
                _logger.LogWarning("Validação falhou: OtimizacaoOriginalId é obrigatório. Empresa: {Empresa}.", _context.Empresa);
                return BadRequest("O OtimizacaoOriginalId é obrigatório.");
            }

            if (command.Equipes is null || command.Equipes.Count == 0)
            {
                _logger.LogWarning("Validação falhou: Ao menos uma equipe deve ser selecionada. Empresa: {Empresa}.", _context.Empresa);
                return BadRequest("Ao menos uma equipe deve ser selecionada para a reotimização.");
            }

            if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                _logger.LogWarning("Validação falhou: Idempotency-Key é obrigatório. Empresa: {Empresa}.", _context.Empresa);
                return BadRequest("O Idempotency-Key é obrigatório.");
            }

            // §3: a data/hora da solicitação é capturada uma única vez pelo backend (DataCorteUtc),
            // no início do processamento (dentro de MontarContextoAsync). O label utilizado apenas
            // para correlação com o fluxo GMPRO/GCS (nunca como chave de negócio, §17).
            var dataSolicitacaoUtc = DateTime.UtcNow;
            var label = $"Reotimizacao Energisa API: {dataSolicitacaoUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";

            try
            {
                // 1. Montar o snapshot operacional completo (§5), reservar equipes/turno (§16)
                // e obter os shipments das novas OSs (§12).
                _logger.LogInformation(
                    "Montando contexto de reotimização. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}. Label: {Label}.",
                    _context.Empresa, command.OtimizacaoOriginalId, label);

                var (contexto, novosShipments) = await _reotimizacaoDataService.MontarContextoAsync(command, cancellationToken);

                _logger.LogInformation(
                    "Contexto montado com sucesso. Empresa: {Empresa}. Equipes: {EquipeCount}. NovosShipments: {ShipmentCount}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, contexto.EquipesSelecionadas.Count, novosShipments.Count, stopwatch.ElapsedMilliseconds);

                // 2. Construir o novo request, persistir e disparar a otimização assíncrona (§17).
                _logger.LogInformation(
                    "Disparando otimização assíncrona de reotimização. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}.",
                    _context.Empresa, contexto.OtimizacaoOriginalId);

                var resultado = await _pipeline.ExecutarReotimizacaoAsync(contexto, novosShipments, label, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Reotimização disparada com sucesso. Empresa: {Empresa}. ReotimizacaoId: {ReotimizacaoId}. " +
                    "OrdensNaoAtendidas: {OrdensNaoAtendidas}. OrdensEmExecucao: {OrdensEmExecucao}. " +
                    "OrdensEmDeslocamento: {OrdensEmDeslocamento}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, resultado.ReotimizacaoId, resultado.OrdensNaoAtendidas.Count,
                    resultado.OrdensEmExecucao.Count, resultado.OrdensEmDeslocamento.Count, stopwatch.ElapsedMilliseconds);

                return Accepted(new
                {
                    reotimizacaoId = resultado.ReotimizacaoId,
                    otimizacaoOriginalId = contexto.OtimizacaoOriginalId,
                    status = "Processando",
                    dataSolicitacaoUtc,
                    resultado.OrdensNaoAtendidas,
                    resultado.OrdensEmExecucao,
                    resultado.OrdensEmDeslocamento,
                    resultado.Avisos
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex,
                    "Usuário sem acesso para reotimização via API. Empresa: {Empresa}. Usuario: {Usuario}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, _context.User, stopwatch.ElapsedMilliseconds);
                return Forbid();
            }
            catch (KeyNotFoundException ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex,
                    "Otimização ou equipe inexistente na reotimização via API. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, command?.OtimizacaoOriginalId, stopwatch.ElapsedMilliseconds);
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("§16") || ex.Message.Contains("conflit", StringComparison.OrdinalIgnoreCase))
            {
                stopwatch.Stop();
                _logger.LogWarning(ex,
                    "Conflito de concorrência/idempotência na reotimização via API. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, command?.OtimizacaoOriginalId, stopwatch.ElapsedMilliseconds);
                return Conflict(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex,
                    "Cenário inconsistente na reotimização via API. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, command?.OtimizacaoOriginalId, stopwatch.ElapsedMilliseconds);
                return UnprocessableEntity(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex,
                    "Erro ao executar reotimização via API. Empresa: {Empresa}. OtimizacaoOriginalId: {OtimizacaoOriginalId}. DuracaoMs: {DuracaoMs}.",
                    _context.Empresa, command?.OtimizacaoOriginalId, stopwatch.ElapsedMilliseconds);
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }

        /// <summary>
        /// Atualiza a rota de otimização identificada pelo <c>operationName</c> com uma mensagem de erro.
        /// Deve ser chamado pelo pós-processamento quando o processamento assíncrono da rota falhar.
        /// </summary>
        /// <param name="request">OperationName da rota e mensagem de erro.</param>
        [HttpPatch("atualizar-erro")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AtualizarErroAsync(
            [FromBody] AtualizarErroOtimizacaoRequest request,
            [FromHeader(Name = "User")] string? user = null,
            [FromHeader(Name = "Empresa")] int? empresa = null)
        {
            if (!string.IsNullOrWhiteSpace(user)) _context.User = user;
            if (empresa.HasValue) _context.Empresa = empresa.Value;

            if (string.IsNullOrWhiteSpace(request?.OperationName))
                return BadRequest("O OperationName é obrigatório.");

            if (string.IsNullOrWhiteSpace(request.MensagemErro))
                return BadRequest("A mensagem de erro é obrigatória.");

            try
            {
                await _repository.AtualizarMensagemErroPorOperationNameAsync(request.OperationName, request.MensagemErro);
                return Ok();
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "OperationName não encontrado: {OperationName}", request.OperationName);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar mensagem de erro da otimização.");
                return Problem("Erro interno da API. Verificar Logs para mais detalhes.");
            }
        }
    }

    /// <summary>
    /// Request para o endpoint de aprovação de equipes.
    /// </summary>
    public class AprovarEquipesRequest
    {
        /// <summary>
        /// ID da otimização.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Lista de equipes a serem aprovadas.
        /// </summary>
        public List<string> Equipes { get; set; } = [];
    }

    /// <summary>
    /// Request para o endpoint de otimização via API.
    /// </summary>
    public class OtimizacaoRequest
    {
        /// <summary>
        /// Lista de números de ordens de serviço a serem otimizadas.
        /// </summary>
        public List<string> NumerosOs { get; set; } = [];
    }

    /// <summary>
    /// Request para o endpoint de atualização de mensagem de erro da otimização.
    /// </summary>
    public class AtualizarErroOtimizacaoRequest
    {
        /// <summary>
        /// Nome da operação LRO retornado pelo Google Cloud ao disparar a otimização assíncrona.
        /// </summary>
        public string OperationName { get; set; } = string.Empty;

        /// <summary>
        /// Mensagem de erro ocorrida durante o pós-processamento da rota.
        /// </summary>
        public string MensagemErro { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request para o endpoint de atualização de response do otimizador via URI do Google Cloud Storage.
    /// </summary>
    public class OtimizadorResponseUriRequest
    {
        /// <summary>
        /// URI do Google Cloud Storage contendo o JSON de resposta (ex: gs://bucket/path/file.json).
        /// </summary>
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// OperationName da rota (LRO) retornado pelo Google Cloud ao disparar a otimização
        /// assíncrona. Quando informado, garante a correlação direta com o registro que já
        /// possui a referência de input (§17), evitando depender apenas do Id retornado pela
        /// criação do response.
        /// </summary>
        public string? OperationName { get; set; }
    }
}
