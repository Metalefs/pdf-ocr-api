using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;

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
        private readonly ILogger<PdfController> _logger;

        public PdfController(IJobService jobService, ILogger<PdfController> logger)
        {
            _jobService = jobService;
            _logger = logger;
        }

        /// <summary>
        /// Processa um PDF de forma síncrona e retorna o arquivo processado imediatamente
        /// </summary>
        /// <param name="file">Arquivo PDF para processar</param>
        /// <returns>PDF processado com OCR</returns>
        /// <response code="200">PDF processado com sucesso</response>
        /// <response code="400">Arquivo inválido ou muito grande</response>
        /// <response code="500">Erro no processamento</response>
        [HttpPost("process-sync")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> ProcessSync(
     [FromForm] PdfUploadRequest request)
        {
            _logger.LogInformation("Recebida requisição síncrona de processamento");

            // Validações
            var validationError = ValidateFile(request.File);
            if (validationError != null)
            {
                return validationError;
            }

            try
            {
                // Criar diretório temporário
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
                _logger.LogInformation("Iniciando processamento síncrono");
                var result = await Task.Run(() => OcrPipelineService.Run(jobDir));

                if (!result.Success)
                {
                    _logger.LogError("Falha no processamento: {Error}", result.Error);

                    // Limpar diretório temporário
                    CleanupDirectory(jobDir);

                    return StatusCode(500, new ErrorResponse
                    {
                        Error = "Erro no processamento OCR",
                        Details = result.Error,
                        Logs = result.Logs
                    });
                }

                // Ler arquivo processado
                var processedBytes = await System.IO.File.ReadAllBytesAsync(result.OutputPdf);

                _logger.LogInformation("Processamento concluído com sucesso. Arquivo: {Size} bytes",
                    processedBytes.Length);

                // Limpar diretório temporário
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
                _logger.LogError(ex, "Erro crítico no processamento síncrono");
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Erro interno no servidor",
                    Details = ex.Message
                });
            }
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

            // Validações
            var validationError = ValidateFile(request.File);
            if (validationError != null)
            {
                return validationError;
            }

            try
            {
                // Criar job
                var jobId = await _jobService.CreateJobAsync(request.File);

                _logger.LogInformation("Job criado com sucesso: {JobId}", jobId);

                return Ok(new ProcessResponse
                {
                    JobId = jobId,
                    Status = "queued",
                    Message = "PDF recebido e aguardando processamento",
                    StatusUrl = $"/api/jobs/{jobId}/status",
                    DownloadUrl = $"/api/jobs/{jobId}/download"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar job assíncrono");
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Erro ao criar job de processamento",
                    Details = ex.Message
                });
            }
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