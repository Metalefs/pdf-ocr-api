using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;
using System.Security.Claims;
using StackExchange.Redis;

namespace pdf_ocr.Controllers
{
    /// <summary>
    /// Controlador respons�vel pelo processamento de PDFs com OCR
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class PdfController : ControllerBase
    {
        private readonly IJobService _jobService;
        private readonly IUserService _userService;
        private readonly ILogger<PdfController> _logger;

        public PdfController(
            IJobService jobService,
            IUserService userService,
            ILogger<PdfController> logger)
        {
            _jobService = jobService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Inicia o processamento ass�ncrono de um PDF
        /// </summary>
        /// <param name="file">Arquivo PDF para processar</param>
        /// <returns>Informa��es do job criado</returns>
        /// <response code="200">Job criado com sucesso</response>
        /// <response code="400">Arquivo inv�lido ou muito grande</response>
        /// <response code="500">Erro ao criar job</response>
        [HttpPost("process")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [RequestSizeLimit(10_000_000)]
        [Authorize]
        public async Task<IActionResult> ProcessAsync(
    [FromForm] PdfUploadRequest request)
        {
            _logger.LogInformation("Recebida requisi��o ass�ncrona de processamento");
            var file = request.File;
            // Verificar cr�ditos ANTES de processar
            var creditCheck = await CreditCheckAttribute.CheckCredits(
                HttpContext, _userService, _logger);

            if (creditCheck != null)
                return creditCheck; // Sem cr�ditos


            // Valida��es
            var validationError = ValidateFile(request.File);
            if (validationError != null)
            {
                return validationError;
            }

            try
            {
                // Criar job
                var userId = GetUserId();
                var jobId = await _jobService.CreateJobAsync(file);

                _logger.LogInformation(
                    "Job criado: {JobId} por usu�rio {UserId}, arquivo {FileName}",
                    jobId, userId, file.FileName);

                return Ok(new ProcessResponse
                {
                    JobId = jobId,
                    Status = "queued",
                    Message = ApiMessages.PdfQueued(HttpContext),
                    StatusUrl = $"/api/jobs/{jobId}/status",
                    DownloadUrl = $"/api/jobs/{jobId}/download",
                    CreditsRemaining = await _userService.GetCreditsAsync(userId)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar job ass�ncrono");
                // Devolver cr�dito em caso de erro
                await _userService.AddCreditsAsync(GetUserId(), 1);
                var msg = ApiMessages.CreateJobFailed(HttpContext, ex.Message);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }
        }

        /// <summary>
        /// DEMO: Processar sem autentica��o (limitado)
        /// </summary>
        [HttpPost("demo")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> ProcessDemo([FromForm] PdfUploadRequest request)
        {
            var file = request.File;

            // Limites para demo
            if (file.Length > 1_000_000) // 1MB
            {
                var msg = ApiMessages.DemoFileTooLarge(HttpContext);
                return BadRequest(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details,
                    UpgradeUrl = msg.UpgradeUrl
                });
            }

            // Rate-limit por IP: até 3 chamadas por período (24h)
            string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            try
            {
                var multiplexer = HttpContext.RequestServices.GetService(typeof(IConnectionMultiplexer)) as IConnectionMultiplexer;
                if (multiplexer != null)
                {
                    var db = multiplexer.GetDatabase();
                    var key = $"demo_count:{ip}";
                    // Incrementa e retorna novo contador
                    var count = (long)await db.StringIncrementAsync(key).ConfigureAwait(false);
                    if (count == 1)
                    {
                        // Expira em 24h
                        await db.KeyExpireAsync(key, TimeSpan.FromHours(24)).ConfigureAwait(false);
                    }

                    if (count > 3)
                    {
                        _logger.LogWarning("IP {Ip} excedeu limite demo: {Count}", ip, count);
                        var msg = ApiMessages.DemoLimitExceeded(HttpContext);
                        return StatusCode(429, new ErrorResponse
                        {
                            Error = msg.Error,
                            Details = msg.Details,
                            UpgradeUrl = msg.UpgradeUrl
                        });
                    }
                }
                else
                {
                    _logger.LogDebug("Redis IConnectionMultiplexer não registrado — demo rate-limit não será aplicado");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao verificar contador demo no Redis — permitindo demo sem contagem");
            }

            // Processar normalmente
            var jobId = await _jobService.CreateJobAsync(file);

            var queued = ApiMessages.DemoQueued(HttpContext);

            return Ok(new ProcessResponse
            {
                JobId = jobId,
                Status = "queued",
                Message = queued.Message,
                StatusUrl = $"/api/jobs/{jobId}/status",
                DownloadUrl = $"/api/jobs/{jobId}/download",
                CreditsRemaining = 0,
                UpgradeMessage = queued.UpgradeMessage
            });
        }

        /// <summary>
        /// Processa um PDF de forma s�ncrona e retorna o arquivo processado imediatamente
        /// </summary>
        /// <param name="file">Arquivo PDF para processar</param>
        /// <returns>PDF processado com OCR</returns>
        /// <response code="200">PDF processado com sucesso</response>
        /// <response code="400">Arquivo inv�lido ou muito grande</response>
        /// <response code="500">Erro no processamento</response>
        [HttpPost("process-sync")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [RequestSizeLimit(10_000_000)]
        [Authorize]
        public async Task<IActionResult> ProcessSync(
        [FromForm] PdfUploadRequest request)
        {
            _logger.LogInformation("Recebida requisi��o s�ncrona de processamento");
            var file = request.File;
            // Verificar cr�ditos ANTES de processar
            var creditCheck = await CreditCheckAttribute.CheckCredits(
                HttpContext, _userService, _logger);

            if (creditCheck != null)
                return creditCheck; // Sem cr�ditos


            // Valida��es
            var validationError = ValidateFile(request.File);
            if (validationError != null)
            {
                return validationError;
            }

            try
            {
                // Criar diret�rio tempor�rio
                string jobDir = Path.Combine(Path.GetTempPath(), "ocr_sync", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(jobDir);

                string inputPath = Path.Combine(jobDir, "input.pdf");

                _logger.LogInformation("Salvando arquivo: {FileName} ({Size} bytes)",
                    request.File.FileName, request.File.Length);

                // Salvar arquivo
                using (var stream = System.IO.File.Create(inputPath))
                {
                    await request.File.CopyToAsync(stream);
                }

                // Processar imediatamente
                _logger.LogInformation("Iniciando processamento s�ncrono");
                var result = await Task.Run(() => OcrPipelineService.Run(jobDir));

                if (!result.Success)
                {
                    _logger.LogError("Falha no processamento: {Error}", result.Error);
                    // Devolver cr�dito em caso de erro
                    await _userService.AddCreditsAsync(GetUserId(), 1);
                    // Limpar diret�rio tempor�rio
                    CleanupDirectory(jobDir);

                    var msg = ApiMessages.OcrProcessingFailed(HttpContext, result.Error);

                    return StatusCode(500, new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details,
                        Logs = result.Logs
                    });
                }

                // Ler arquivo processado
                var processedBytes = await System.IO.File.ReadAllBytesAsync(result.OutputPdf);

                _logger.LogInformation("Processamento conclu�do com sucesso. Arquivo: {Size} bytes",
                    processedBytes.Length);

                // Limpar diret�rio tempor�rio
                CleanupDirectory(jobDir);

                // Retornar PDF processado
                return File(
                    processedBytes,
                    "application/pdf",
                    $"ocr_{request.File.FileName}"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro cr�tico no processamento s�ncrono");
                var msg = ApiMessages.InternalServerError(HttpContext);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }
        }


        private string GetUserId()
        {
            // Prefer user id from ApiKey (set during credit check) for requests authenticated with an API key
            if (HttpContext.Items.TryGetValue("ApiKeyUserId", out var obj) && obj is string apiUserId && !string.IsNullOrEmpty(apiUserId))
                return apiUserId;

            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException();
        }

        /// <summary>
        /// Valida o arquivo PDF recebido
        /// </summary>
        private IActionResult? ValidateFile(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("Nenhum arquivo foi enviado");
                var msg = ApiMessages.NoFileProvided(HttpContext);
                return BadRequest(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            if (file.Length > 10_000_000) // 10MB
            {
                _logger.LogWarning("Arquivo muito grande: {Size} bytes", file.Length);
                var msg = ApiMessages.FileTooLarge(HttpContext, 10);
                return BadRequest(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Tipo de arquivo inv�lido: {FileName}", file.FileName);
                var msg = ApiMessages.InvalidFileType(HttpContext);
                return BadRequest(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            return null;
        }

        /// <summary>
        /// Limpa diret�rio tempor�rio de forma segura
        /// </summary>
        private void CleanupDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                    _logger.LogDebug("Diret�rio tempor�rio removido: {Directory}", directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao limpar diret�rio: {Directory}", directory);
            }
        }
    }

    public static class CreditCheckAttribute
    {
        public static async Task<IActionResult?> CheckCredits(
            HttpContext context,
            IUserService userService,
            ILogger logger)
        {
            // Primeiro, verificar se há uma API key no header
            string? apiKey = null;
            if (context.Request.Headers.TryGetValue("X-API-Key", out var headerValues))
            {
                apiKey = headerValues.Count > 0 ? headerValues[0] : null;
            }

            string? userId = null;

            if (!string.IsNullOrEmpty(apiKey))
            {
                // Validar chave via serviço de API Keys
                var apiKeyService = context.RequestServices.GetService(typeof(IApiKeyService)) as IApiKeyService;
                if (apiKeyService == null)
                {
                    logger.LogWarning("IApiKeyService não está registrado no DI");
                    var msg = ApiMessages.InternalServerError(context);
                    return new ObjectResult(new ErrorResponse { Error = msg.Error, Details = msg.Details })
                    {
                        StatusCode = 500
                    };
                }

                try
                {
                    userId = await apiKeyService.ValidateKeyAsync(apiKey);
                    if (string.IsNullOrEmpty(userId))
                    {
                        logger.LogWarning("Chave de API inválida ou expirada");
                        var msg = ApiMessages.ApiKeyInvalidOrExpired(context);
                        return new UnauthorizedObjectResult(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                    }

                    // Marcar o contexto para que o controlador possa recuperar o userId quando necessário
                    context.Items["ApiKeyUserId"] = userId;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao validar API Key");
                    var msg = ApiMessages.ApiKeyInvalidOrExpired(context);
                    return new UnauthorizedObjectResult(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }
            }
            else
            {
                // Sem API key: verificar usuário autenticado via Claims
                userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    var msg = ApiMessages.UserNotAuthenticated(context);
                    return new UnauthorizedObjectResult(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }
            }

            // Custo: 1 cr�dito por PDF
            const int COST = 1;

            var credits = await userService.GetCreditsAsync(userId);
            if (credits < COST)
            {
                logger.LogWarning("Cr�ditos insuficientes: {UserId} tem {Credits}", userId, credits);
                var msg = ApiMessages.InsufficientCredits(context, COST, credits);
                return new ObjectResult(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details,
                    UpgradeUrl = msg.UpgradeUrl
                })
                { StatusCode = 402 }; // Payment Required
            }

            // Deduzir cr�ditos
            var success = await userService.DeductCreditsAsync(userId, COST);
            if (!success)
            {
                var msg = ApiMessages.DeductCreditsFailed(context);
                return new ObjectResult(new ErrorResponse { Error = msg.Error, Details = msg.Details })
                {
                    StatusCode = 500
                };
            }

            return null; // OK, pode processar
        }
    }
}