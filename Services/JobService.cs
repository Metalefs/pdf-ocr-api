using System.Collections.Concurrent;
using pdf_ocr.Models;

namespace pdf_ocr.Services
{
    // ========================================
    // INTERFACE
    // ========================================

    /// <summary>
    /// Interface do serviço de gerenciamento de jobs
    /// </summary>
    public interface IJobService
    {
        Task<string> CreateJobAsync(IFormFile file);
        Task<JobInfo?> GetJobAsync(string jobId);
        Task<List<JobInfo>> ListJobsAsync(int page, int pageSize, string? status);
        Task<int> GetTotalJobsAsync(string? status);
        Task<bool> UpdateJobStatusAsync(string jobId, string status, string? outputPath = null,
            List<string>? logs = null, string? error = null);
        Task<bool> AppendJobLogAsync(string jobId, string logLine);
        Task<bool> UpdateJobProgressAsync(string jobId, JobProgressInfo progress);
        Task<bool> CancelJobAsync(string jobId);
        Task<int> CleanupOldJobsAsync(int hoursOld);
        Task<JobStatsResponse> GetStatsAsync();
    }

    // ========================================
    // IMPLEMENTAÇÃO
    // ========================================

    /// <summary>
    /// Implementação do serviço de gerenciamento de jobs usando ConcurrentDictionary
    /// Em produção, substituir por banco de dados (PostgreSQL, Redis, etc.)
    /// </summary>
    public class JobService : IJobService
    {
        private readonly ConcurrentDictionary<string, JobInfo> _jobs;
        private readonly ILogger<JobService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _jobsBasePath;

        public JobService(ILogger<JobService> logger, IConfiguration configuration)
        {
            _jobs = new ConcurrentDictionary<string, JobInfo>();
            _logger = logger;
            _configuration = configuration;

            // Diretório base para armazenar jobs
            _jobsBasePath = Path.Combine(Path.GetTempPath(), "ocr_jobs");
            Directory.CreateDirectory(_jobsBasePath);

            _logger.LogInformation("JobService inicializado. Base path: {Path}", _jobsBasePath);
        }

        /// <summary>
        /// Cria um novo job e inicia o processamento em background
        /// </summary>
        public async Task<string> CreateJobAsync(IFormFile file)
        {
            string jobId = Guid.NewGuid().ToString("N");
            string jobDir = Path.Combine(_jobsBasePath, jobId);

            try
            {
                Directory.CreateDirectory(jobDir);

                string inputPath = Path.Combine(jobDir, "input.pdf");

                // Salvar arquivo
                using (var stream = File.Create(inputPath))
                {
                    await file.CopyToAsync(stream);
                }

                // Criar registro do job
                var jobInfo = new JobInfo
                {
                    Id = jobId,
                    Status = "queued",
                    FileName = file.FileName,
                    FileSize = file.Length,
                    CreatedAt = DateTime.UtcNow,
                    Logs = new List<string> { $"[{DateTime.UtcNow:HH:mm:ss}] Job criado" },
                    ProgressPercent = 0,
                    ProgressInfo = new JobProgressInfo
                    {
                        Stage = "queued",
                        Message = "Job criado. Aguardando processamento...",
                        Percent = 0,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                _jobs[jobId] = jobInfo;

                _logger.LogInformation("Job criado: {JobId}, Arquivo: {FileName}, Tamanho: {Size} bytes",
                    jobId, file.FileName, file.Length);

                // Processar em background
                _ = Task.Run(() => ProcessJobAsync(jobId, jobDir));

                return jobId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar job: {JobId}", jobId);

                // Limpar em caso de erro
                try
                {
                    if (Directory.Exists(jobDir))
                    {
                        Directory.Delete(jobDir, true);
                    }
                }
                catch { }

                throw;
            }
        }

        /// <summary>
        /// Processa um job em background
        /// </summary>
        private async Task ProcessJobAsync(string jobId, string jobDir)
        {
            try
            {
                _logger.LogInformation("Iniciando processamento do job: {JobId}", jobId);

                // Atualizar status
                await UpdateJobStatusAsync(jobId, "processing");

                await UpdateJobProgressAsync(jobId, new JobProgressInfo
                {
                    Stage = "starting",
                    Message = "Iniciando processamento...",
                    Percent = 1,
                    UpdatedAt = DateTime.UtcNow
                });

                // Executar pipeline
                var result = await Task.Run(() => OcrPipelineService.Run(
                    jobDir,
                    onLog: line => _ = AppendJobLogAsync(jobId, line),
                    onProgress: p => _ = UpdateJobProgressAsync(jobId, p)
                ));

                if (result.Success)
                {
                    _logger.LogInformation("Job concluído com sucesso: {JobId}", jobId);
                    await UpdateJobStatusAsync(jobId, "completed", result.OutputPdf, result.Logs);

                    await UpdateJobProgressAsync(jobId, new JobProgressInfo
                    {
                        Stage = "completed",
                        Message = "Concluído. Seu PDF está pronto para download.",
                        Percent = 100,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogError("Job falhou: {JobId}, Erro: {Error}", jobId, result.Error);
                    await UpdateJobStatusAsync(jobId, "failed", null, result.Logs, result.Error);

                    await UpdateJobProgressAsync(jobId, new JobProgressInfo
                    {
                        Stage = "failed",
                        Message = "Falha no processamento.",
                        Percent = 0,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção durante processamento do job: {JobId}", jobId);
                await UpdateJobStatusAsync(jobId, "failed", null, null, ex.Message);

                await UpdateJobProgressAsync(jobId, new JobProgressInfo
                {
                    Stage = "failed",
                    Message = "Falha no processamento.",
                    Percent = 0,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        public Task<bool> AppendJobLogAsync(string jobId, string logLine)
        {
            if (string.IsNullOrWhiteSpace(logLine))
            {
                return Task.FromResult(false);
            }

            if (!_jobs.TryGetValue(jobId, out var job))
            {
                return Task.FromResult(false);
            }

            var newLogs = new List<string>(job.Logs);
            newLogs.Add(logLine);

            // Avoid unbounded growth for long jobs
            const int maxLogs = 500;
            if (newLogs.Count > maxLogs)
            {
                newLogs = newLogs.Skip(Math.Max(0, newLogs.Count - maxLogs)).ToList();
            }

            var updatedJob = new JobInfo
            {
                Id = job.Id,
                Status = job.Status,
                FileName = job.FileName,
                FileSize = job.FileSize,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                OutputPath = job.OutputPath,
                Error = job.Error,
                Logs = newLogs,
                ProgressPercent = job.ProgressPercent,
                ProgressInfo = job.ProgressInfo
            };

            _jobs[jobId] = updatedJob;
            return Task.FromResult(true);
        }

        public Task<bool> UpdateJobProgressAsync(string jobId, JobProgressInfo progress)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                return Task.FromResult(false);
            }

            progress.UpdatedAt = DateTime.UtcNow;

            var percent = progress.Percent ?? job.ProgressPercent;

            var updatedJob = new JobInfo
            {
                Id = job.Id,
                Status = job.Status,
                FileName = job.FileName,
                FileSize = job.FileSize,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                OutputPath = job.OutputPath,
                Logs = job.Logs,
                Error = job.Error,
                ProgressPercent = percent,
                ProgressInfo = progress
            };

            _jobs[jobId] = updatedJob;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Obtém um job pelo ID
        /// </summary>
        public Task<JobInfo?> GetJobAsync(string jobId)
        {
            _jobs.TryGetValue(jobId, out var job);
            return Task.FromResult(job);
        }

        /// <summary>
        /// Lista jobs com paginação e filtro opcional
        /// </summary>
        public Task<List<JobInfo>> ListJobsAsync(int page, int pageSize, string? status)
        {
            var query = _jobs.Values.AsEnumerable();

            // Filtrar por status se especificado
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(j => j.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            var jobs = query
                .OrderByDescending(j => j.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult(jobs);
        }

        /// <summary>
        /// Conta total de jobs (com filtro opcional)
        /// </summary>
        public Task<int> GetTotalJobsAsync(string? status)
        {
            var query = _jobs.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(j => j.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult(query.Count());
        }

        /// <summary>
        /// Atualiza o status de um job
        /// </summary>
        public Task<bool> UpdateJobStatusAsync(string jobId, string status, string? outputPath = null,
            List<string>? logs = null, string? error = null)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                _logger.LogWarning("Tentativa de atualizar job inexistente: {JobId}", jobId);
                return Task.FromResult(false);
            }

            var updatedJob = new JobInfo
            {
                Id = job.Id,
                Status = status,
                FileName = job.FileName,
                FileSize = job.FileSize,
                CreatedAt = job.CreatedAt,
                CompletedAt = (status == "completed" || status == "failed" || status == "cancelled")
                    ? DateTime.UtcNow
                    : job.CompletedAt,
                OutputPath = outputPath ?? job.OutputPath,
                Logs = logs ?? job.Logs,
                Error = error ?? job.Error,
                ProgressPercent = job.ProgressPercent,
                ProgressInfo = job.ProgressInfo
            };

            _jobs[jobId] = updatedJob;

            _logger.LogDebug("Job atualizado: {JobId}, Status: {Status}", jobId, status);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Cancela um job em processamento
        /// </summary>
        public Task<bool> CancelJobAsync(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                return Task.FromResult(false);
            }

            if (job.Status == "completed" || job.Status == "failed")
            {
                return Task.FromResult(false);
            }

            var updatedJob = new JobInfo
            {
                Id = job.Id,
                Status = "cancelled",
                FileName = job.FileName,
                FileSize = job.FileSize,
                CreatedAt = job.CreatedAt,
                CompletedAt = DateTime.UtcNow,
                Logs = job.Logs,
                Error = "Job cancelado pelo usuário",
                OutputPath = job.OutputPath
            };

            _jobs[jobId] = updatedJob;

            _logger.LogInformation("Job cancelado: {JobId}", jobId);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Remove jobs antigos e seus arquivos
        /// </summary>
        public Task<int> CleanupOldJobsAsync(int hoursOld)
        {
            var cutoff = DateTime.UtcNow.AddHours(-hoursOld);
            var removed = 0;

            foreach (var job in _jobs.Values.Where(j => j.CreatedAt < cutoff).ToList())
            {
                if (_jobs.TryRemove(job.Id, out _))
                {
                    // Deletar diretório do job
                    string jobDir = Path.Combine(_jobsBasePath, job.Id);
                    try
                    {
                        if (Directory.Exists(jobDir))
                        {
                            Directory.Delete(jobDir, true);
                            removed++;
                            _logger.LogDebug("Job removido: {JobId}, Criado em: {CreatedAt}",
                                job.Id, job.CreatedAt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erro ao remover diretório do job: {JobId}", job.Id);
                    }
                }
            }

            if (removed > 0)
            {
                _logger.LogInformation("Limpeza concluída: {Count} job(s) removido(s)", removed);
            }

            return Task.FromResult(removed);
        }

        /// <summary>
        /// Obtém estatísticas dos jobs
        /// </summary>
        public Task<JobStatsResponse> GetStatsAsync()
        {
            var allJobs = _jobs.Values.ToList();

            var stats = new JobStatsResponse
            {
                TotalJobs = allJobs.Count,
                QueuedJobs = allJobs.Count(j => j.Status == "queued"),
                ProcessingJobs = allJobs.Count(j => j.Status == "processing"),
                CompletedJobs = allJobs.Count(j => j.Status == "completed"),
                FailedJobs = allJobs.Count(j => j.Status == "failed"),
                CancelledJobs = allJobs.Count(j => j.Status == "cancelled"),
                TotalBytesProcessed = allJobs.Sum(j => j.FileSize),
                OldestJobDate = allJobs.Any() ? allJobs.Min(j => j.CreatedAt) : null,
                NewestJobDate = allJobs.Any() ? allJobs.Max(j => j.CreatedAt) : null
            };

            // Calcular tempo médio de processamento
            var completedJobs = allJobs
                .Where(j => j.Status == "completed" && j.CompletedAt.HasValue)
                .ToList();

            if (completedJobs.Any())
            {
                var avgSeconds = completedJobs
                    .Average(j => (j.CompletedAt!.Value - j.CreatedAt).TotalSeconds);
                stats.AverageProcessingTime = Math.Round(avgSeconds, 2);
            }

            return Task.FromResult(stats);
        }
    }
}