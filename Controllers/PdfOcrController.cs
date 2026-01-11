using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;
using System.Security.Claims;

namespace pdf_ocr.Controllers
{
    /// <summary>
    /// Controlador responsável pelo processamento de PDFs com OCR
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
        /// Inicia o processamento assíncrono de um PDF
        /// </summary>
        /// <param name="file">Arquivo PDF para processar</param>
        /// <returns>Informações do job criado</returns>
        /// <response code="200">Job criado com sucesso</response>
        /// <response code="400">Arquivo inválido ou muito grande</response>
        /// <response code="500">Erro ao criar job</response>
        [HttpPost("process")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(ProcessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> ProcessAsync(
    [FromForm] PdfUploadRequest request)
        {
            _logger.LogInformation("Recebida requisição assíncrona de processamento");
            var file = request.File;
            // Verificar créditos ANTES de processar
            var creditCheck = await CreditCheckAttribute.CheckCredits(
                HttpContext, _userService, _logger);

            if (creditCheck != null)
                return creditCheck; // Sem créditos


            // Validações
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
                    "Job criado: {JobId} por usuário {UserId}, arquivo {FileName}",
                    jobId, userId, file.FileName);

                return Ok(new ProcessResponse
                {
                    JobId = jobId,
                    Status = "queued",
                    Message = "PDF recebido e aguardando processamento",
                    StatusUrl = $"/api/jobs/{jobId}/status",
                    DownloadUrl = $"/api/jobs/{jobId}/download",
                    CreditsRemaining = await _userService.GetCreditsAsync(userId)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar job assíncrono");
                // Devolver crédito em caso de erro
                await _userService.AddCreditsAsync(GetUserId(), 1);
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Erro ao criar job de processamento",
                    Details = ex.Message
                });
            }
        }

        /// <summary>
        /// DEMO: Processar sem autenticação (limitado)
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
                return BadRequest(new
                {
                    error = "Demo limitado a 1MB",
                    message = "Crie uma conta gratuita para processar PDFs maiores"
                });

            // Processar normalmente
            var jobId = await _jobService.CreateJobAsync(file);

            return Ok(new ProcessResponse
            {
                JobId = jobId,
                Status = "queued",
                Message = "Demo - Processamento iniciado",
                StatusUrl = $"/api/jobs/{jobId}/status",
                DownloadUrl = $"/api/jobs/{jobId}/download",
                CreditsRemaining = 0,
                UpgradeMessage = "Crie uma conta para mais recursos"
            });
        }

        private string GetUserId()
        {
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
                return BadRequest(new ErrorResponse
                {
                    Error = "Nenhum arquivo enviado",
                    Details = "É necessário enviar um arquivo PDF"
                });
            }

            if (file.Length > 10_000_000) // 10MB
            {
                _logger.LogWarning("Arquivo muito grande: {Size} bytes", file.Length);
                return BadRequest(new ErrorResponse
                {
                    Error = "Arquivo muito grande",
                    Details = "O tamanho máximo permitido é 10MB"
                });
            }

            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Tipo de arquivo inválido: {FileName}", file.FileName);
                return BadRequest(new ErrorResponse
                {
                    Error = "Tipo de arquivo inválido",
                    Details = "Apenas arquivos PDF são aceitos"
                });
            }

            return null;
        }

        /// <summary>
        /// Limpa diretório temporário de forma segura
        /// </summary>
        private void CleanupDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                    _logger.LogDebug("Diretório temporário removido: {Directory}", directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao limpar diretório: {Directory}", directory);
            }
        }
    }
}