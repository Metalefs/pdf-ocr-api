using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;

namespace pdf_ocr.Controllers
{
    /// <summary>
    /// Controlador responsável pelo gerenciamento de jobs de processamento
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class JobsController : ControllerBase
    {
        private readonly IJobService _jobService;
        private readonly ILogger<JobsController> _logger;

        public JobsController(IJobService jobService, ILogger<JobsController> logger)
        {
            _jobService = jobService;
            _logger = logger;
        }

        /// <summary>
        /// Obtém o status de um job específico
        /// </summary>
        /// <param name="jobId">ID do job</param>
        /// <returns>Informações detalhadas do job</returns>
        /// <response code="200">Status obtido com sucesso</response>
        /// <response code="404">Job não encontrado</response>
        [HttpGet("{jobId}/status")]
        [ProducesResponseType(typeof(JobStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatus(string jobId)
        {
            _logger.LogInformation("Consultando status do job: {JobId}", jobId);

            var job = await _jobService.GetJobAsync(jobId);

            if (job == null)
            {
                _logger.LogWarning("Job não encontrado: {JobId}", jobId);
                return NotFound(new ErrorResponse
                {
                    Error = "Job não encontrado",
                    Details = $"Nenhum job foi encontrado com o ID: {jobId}"
                });
            }

            var response = new JobStatusResponse
            {
                JobId = job.Id,
                Status = job.Status,
                FileName = job.FileName,
                FileSize = job.FileSize,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                Logs = job.Logs,
                Error = job.Error,
                DownloadUrl = job.Status == "completed" ? $"/api/jobs/{jobId}/download" : null,
                Progress = job.ProgressPercent > 0 ? job.ProgressPercent : CalculateProgress(job.Status),
                Message = job.ProgressInfo?.Message,
                Stage = job.ProgressInfo?.Stage,
                TotalPages = job.ProgressInfo?.TotalPages,
                ProcessedPages = job.ProgressInfo?.ProcessedPages,
                ActivePages = job.ProgressInfo?.ActivePages
            };

            return Ok(response);
        }

        /// <summary>
        /// Faz o download do PDF processado
        /// </summary>
        /// <param name="jobId">ID do job</param>
        /// <returns>Arquivo PDF processado</returns>
        /// <response code="200">Download iniciado</response>
        /// <response code="404">Job não encontrado ou arquivo não disponível</response>
        /// <response code="400">Job ainda não concluído</response>
        [HttpGet("{jobId}/download")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Download(string jobId)
        {
            _logger.LogInformation("Solicitado download do job: {JobId}", jobId);

            var job = await _jobService.GetJobAsync(jobId);

            if (job == null)
            {
                _logger.LogWarning("Job não encontrado para download: {JobId}", jobId);
                return NotFound(new ErrorResponse
                {
                    Error = "Job não encontrado",
                    Details = $"Nenhum job foi encontrado com o ID: {jobId}"
                });
            }

            if (job.Status != "completed")
            {
                _logger.LogWarning("Tentativa de download de job não concluído: {JobId}, Status: {Status}",
                    jobId, job.Status);
                return BadRequest(new ErrorResponse
                {
                    Error = "Job ainda não concluído",
                    Details = $"Status atual: {job.Status}. Aguarde a conclusão do processamento."
                });
            }

            if (string.IsNullOrEmpty(job.OutputPath) || !System.IO.File.Exists(job.OutputPath))
            {
                _logger.LogError("Arquivo processado não encontrado: {JobId}, Path: {Path}",
                    jobId, job.OutputPath);
                return NotFound(new ErrorResponse
                {
                    Error = "Arquivo processado não encontrado",
                    Details = "O arquivo pode ter sido removido por limpeza automática"
                });
            }

            try
            {
                var fileBytes = await System.IO.File.ReadAllBytesAsync(job.OutputPath);
                var fileName = $"ocr_{job.FileName}";

                _logger.LogInformation("Download iniciado: {FileName} ({Size} bytes)",
                    fileName, fileBytes.Length);

                return File(fileBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao ler arquivo para download: {JobId}", jobId);
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Erro ao processar download",
                    Details = ex.Message
                });
            }
        }

        /// <summary>
        /// Lista todos os jobs (paginado)
        /// </summary>
        /// <param name="page">Número da página (padrão: 1)</param>
        /// <param name="pageSize">Itens por página (padrão: 20, máximo: 100)</param>
        /// <param name="status">Filtrar por status (opcional)</param>
        /// <returns>Lista paginada de jobs</returns>
        /// <response code="200">Lista obtida com sucesso</response>
        [HttpGet]
        [ProducesResponseType(typeof(JobListResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListJobs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null)
        {
            _logger.LogInformation("Listando jobs - Página: {Page}, Tamanho: {PageSize}, Status: {Status}",
                page, pageSize, status);

            // Validar parâmetros
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var jobs = await _jobService.ListJobsAsync(page, pageSize, status);
            var totalJobs = await _jobService.GetTotalJobsAsync(status);
            var totalPages = (int)Math.Ceiling(totalJobs / (double)pageSize);

            var response = new JobListResponse
            {
                Jobs = jobs.Select(j => new JobSummary
                {
                    JobId = j.Id,
                    FileName = j.FileName,
                    Status = j.Status,
                    CreatedAt = j.CreatedAt,
                    CompletedAt = j.CompletedAt,
                    FileSize = j.FileSize
                }).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalJobs = totalJobs,
                TotalPages = totalPages
            };

            return Ok(response);
        }

        /// <summary>
        /// Cancela um job em processamento
        /// </summary>
        /// <param name="jobId">ID do job</param>
        /// <returns>Confirmação de cancelamento</returns>
        /// <response code="200">Job cancelado com sucesso</response>
        /// <response code="404">Job não encontrado</response>
        /// <response code="400">Job não pode ser cancelado</response>
        [HttpPost("{jobId}/cancel")]
        [ProducesResponseType(typeof(CancelJobResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CancelJob(string jobId)
        {
            _logger.LogInformation("Solicitado cancelamento do job: {JobId}", jobId);

            var job = await _jobService.GetJobAsync(jobId);

            if (job == null)
            {
                _logger.LogWarning("Job não encontrado para cancelamento: {JobId}", jobId);
                return NotFound(new ErrorResponse
                {
                    Error = "Job não encontrado",
                    Details = $"Nenhum job foi encontrado com o ID: {jobId}"
                });
            }

            if (job.Status == "completed" || job.Status == "failed")
            {
                _logger.LogWarning("Tentativa de cancelar job já finalizado: {JobId}, Status: {Status}",
                    jobId, job.Status);
                return BadRequest(new ErrorResponse
                {
                    Error = "Job não pode ser cancelado",
                    Details = $"O job já está no status: {job.Status}"
                });
            }

            var success = await _jobService.CancelJobAsync(jobId);

            if (!success)
            {
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Erro ao cancelar job",
                    Details = "Não foi possível cancelar o job"
                });
            }

            _logger.LogInformation("Job cancelado com sucesso: {JobId}", jobId);

            return Ok(new CancelJobResponse
            {
                JobId = jobId,
                Status = "cancelled",
                Message = "Job cancelado com sucesso"
            });
        }

        /// <summary>
        /// Remove jobs antigos (executar manualmente ou via cron)
        /// </summary>
        /// <param name="hoursOld">Remover jobs com mais de X horas (padrão: 24h)</param>
        /// <returns>Número de jobs removidos</returns>
        /// <response code="200">Limpeza concluída</response>
        [HttpPost("cleanup")]
        [ProducesResponseType(typeof(CleanupResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Cleanup([FromQuery] int hoursOld = 24)
        {
            _logger.LogInformation("Iniciando limpeza de jobs antigos (>{Hours}h)", hoursOld);

            if (hoursOld < 1) hoursOld = 24;
            if (hoursOld > 168) hoursOld = 168; // Máximo 7 dias

            var removed = await _jobService.CleanupOldJobsAsync(hoursOld);

            _logger.LogInformation("Limpeza concluída. Jobs removidos: {Count}", removed);

            return Ok(new CleanupResponse
            {
                RemovedCount = removed,
                Message = $"Removidos {removed} job(s) com mais de {hoursOld} hora(s)"
            });
        }

        /// <summary>
        /// Obtém estatísticas gerais dos jobs
        /// </summary>
        /// <returns>Estatísticas de processamento</returns>
        /// <response code="200">Estatísticas obtidas</response>
        [HttpGet("stats")]
        [ProducesResponseType(typeof(JobStatsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStats()
        {
            _logger.LogInformation("Consultando estatísticas de jobs");

            var stats = await _jobService.GetStatsAsync();

            return Ok(stats);
        }

        /// <summary>
        /// Calcula o progresso aproximado baseado no status
        /// </summary>
        private int CalculateProgress(string status)
        {
            return status switch
            {
                "queued" => 0,
                "processing" => 50,
                "completed" => 100,
                "failed" => 0,
                "cancelled" => 0,
                _ => 0
            };
        }
    }
}