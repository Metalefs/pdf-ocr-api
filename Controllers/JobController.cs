using Microsoft.AspNetCore.Authorization;
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
        private readonly IJobPersistenceService _persistenceService;
        private readonly IJobService _jobService;
        private readonly ILogger<JobsController> _logger;
        public JobsController(IJobPersistenceService persistenceService,
        IJobService jobService,
        ILogger<JobsController> logger)
        {
            _persistenceService = persistenceService;
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
                var msg = ApiMessages.JobNotFound(HttpContext, jobId);
                return NotFound(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
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
                Message = ApiMessages.JobProgressMessage(HttpContext, job.ProgressInfo, job.Status),
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
                var msg = ApiMessages.JobNotFound(HttpContext, jobId);
                return NotFound(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            if (job.Status != "completed")
            {
                _logger.LogWarning("Tentativa de download de job não concluído: {JobId}, Status: {Status}",
                    jobId, job.Status);
                var msg = ApiMessages.JobNotCompleted(HttpContext, job.Status);
                return BadRequest(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            if (string.IsNullOrEmpty(job.OutputPath) || !System.IO.File.Exists(job.OutputPath))
            {
                _logger.LogError("Arquivo processado não encontrado: {JobId}, Path: {Path}",
                    jobId, job.OutputPath);
                var msg = ApiMessages.JobOutputMissing(HttpContext);
                return NotFound(new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
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
                var msg = ApiMessages.DownloadFailed(HttpContext, ex.Message);
                return StatusCode(500, new ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
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
        /// Busca jobs do usuário autenticado
        /// </summary>
        [HttpGet("my-jobs")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<JobsListResponse>> GetMyJobs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var userIdClaim = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    var msg = ApiMessages.UserNotAuthenticated(HttpContext);
                    return Unauthorized(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }

                var jobs = await _persistenceService.GetUserJobsAsync(userIdClaim, page, pageSize);

                return Ok(new JobsListResponse
                {
                    Jobs = jobs.Select(j => new JobDto
                    {
                        JobId = j.JobId,
                        FileName = j.FileName,
                        Status = j.Status,
                        Progress = j.Progress,
                        ErrorMessage = j.ErrorMessage,
                        CreatedAt = j.CreatedAt,
                        CompletedAt = j.CompletedAt
                    }),
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = jobs.Count()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar jobs do usuário");
                var msg = ApiMessages.JobsFetchFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        /// <summary>
        /// Retoma um job específico (útil após restart)
        /// </summary>
        [HttpPost("{jobId}/resume")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> ResumeJob(string jobId)
        {
            try
            {
                var job = await _persistenceService.GetJobAsync(jobId);
                if (job == null)
                {
                    var msg = ApiMessages.JobNotFound(HttpContext, jobId);
                    return NotFound(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }

                // Só pode retomar jobs pending ou failed
                if (job.Status != JobStatusConstants.Pending && job.Status != JobStatusConstants.Failed)
                {
                    var msg = ApiMessages.JobResumeNotAllowed(HttpContext, job.Status);
                    return BadRequest(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }

                // Resetar para pending
                await _persistenceService.UpdateJobAsync(jobId, new UpdateJobDto
                {
                    Status = JobStatusConstants.Pending,
                    Progress = 0,
                    ErrorMessage = null
                });

                _logger.LogInformation("Job {JobId} marcado para reprocessamento", jobId);

                var okMsg = ApiMessages.JobMarkedForReprocessing(HttpContext);

                return Ok(new
                {
                    message = okMsg.Message,
                    jobId,
                    status = JobStatusConstants.Pending
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao retomar job {JobId}", jobId);
                var msg = ApiMessages.JobResumeFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        /// <summary>
        /// Cancela um job em execução
        /// </summary>
        [HttpPost("{jobId}/cancel")]
        [Authorize]
        [Authorize(Roles = "admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> CancelJob(string jobId)
        {
            try
            {
                var job = await _persistenceService.GetJobAsync(jobId);
                if (job == null)
                {
                    var msg = ApiMessages.JobNotFound(HttpContext, jobId);
                    return NotFound(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }

                // Só pode cancelar jobs pending ou processing
                if (job.Status == JobStatusConstants.Completed || job.Status == JobStatusConstants.Failed)
                {
                    var msg = ApiMessages.JobAlreadyFinalized(HttpContext, job.Status);
                    return BadRequest(new ErrorResponse { Error = msg.Error, Details = msg.Details });
                }

                await _persistenceService.UpdateJobAsync(jobId, new UpdateJobDto
                {
                    Status = JobStatusConstants.Failed,
                    ErrorMessage = "Cancelado pelo usuário",
                    CompletedAt = DateTime.UtcNow
                });

                _logger.LogInformation("Job {JobId} cancelado pelo usuário", jobId);

                var okMsg = ApiMessages.JobCanceled(HttpContext);

                return Ok(new
                {
                    message = okMsg.Message,
                    jobId,
                    status = JobStatusConstants.Failed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao cancelar job {JobId}", jobId);
                var msg = ApiMessages.JobCancelFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        /// <summary>
        /// Busca jobs por status (admin)
        /// </summary>
        [HttpGet("by-status/{status}")]
        [Authorize(Roles = "admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IEnumerable<JobDto>>> GetJobsByStatus(string status)
        {
            try
            {
                var jobs = await _persistenceService.GetJobsByStatusAsync(status);

                return Ok(jobs.Select(j => new JobDto
                {
                    JobId = j.JobId,
                    FileName = j.FileName,
                    Status = j.Status,
                    Progress = j.Progress,
                    ErrorMessage = j.ErrorMessage,
                    CreatedAt = j.CreatedAt,
                    CompletedAt = j.CompletedAt,
                    UserId = j.UserId
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar jobs por status {Status}", status);
                var msg = ApiMessages.JobsFetchFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }


        /// <summary>
        /// Remove jobs antigos (executar manualmente ou via cron)
        /// </summary>
        /// <param name="hoursOld">Remover jobs com mais de X horas (padrão: 24h)</param>
        /// <returns>Número de jobs removidos</returns>
        /// <response code="200">Limpeza concluída</response>
        [HttpPost("cleanup")]
        [Authorize(Roles = "admin")]
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
                Message = ApiMessages.CleanupRemovedMessage(HttpContext, removed, hoursOld)
            });
        }

        /// <summary>
        /// Obtém estatísticas gerais dos jobs
        /// </summary>
        /// <returns>Estatísticas de processamento</returns>
        /// <response code="200">Estatísticas obtidas</response>
        [HttpGet("stats")]
        [Authorize(Roles = "admin")]
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
    }// DTOs para responses
    public class JobDto
    {
        public string JobId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? UserId { get; set; }
    }

    public class JobsListResponse
    {
        public IEnumerable<JobDto> Jobs { get; set; } = Enumerable.Empty<JobDto>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }
}