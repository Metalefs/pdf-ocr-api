using Supabase;
using pdf_ocr.Models;
using System.Collections.Concurrent;

namespace pdf_ocr.Services;

/// <summary>
/// Interface para o serviço de persistência de jobs no Supabase
/// </summary>
public interface IJobPersistenceService
{
    Task<Job> CreateJobAsync(CreateJobDto createDto);
    Task<Job?> GetJobAsync(string jobId);
    Task<Job?> UpdateJobAsync(string jobId, UpdateJobDto updateDto);
    Task<bool> DeleteJobAsync(string jobId);
    Task<IEnumerable<Job>> GetUserJobsAsync(string userId, int page = 1, int pageSize = 10);
    Task<IEnumerable<Job>> GetJobsByStatusAsync(string status);
    Task<int> CleanupOldJobsAsync(int hoursOld);
}

/// <summary>
/// Serviço de persistência de jobs no Supabase
/// </summary>
public class SupabaseJobPersistenceService : IJobPersistenceService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<SupabaseJobPersistenceService> _logger;

    public SupabaseJobPersistenceService(Client supabase, ILogger<SupabaseJobPersistenceService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    /// <summary>
    /// Cria um novo job no Supabase
    /// </summary>
    public async Task<Job> CreateJobAsync(CreateJobDto createDto)
    {
        try
        {
            var job = new Job
            {
                Id = Guid.NewGuid(),
                JobId = createDto.JobId,
                UserId = createDto.UserId,
                FileName = createDto.FileName,
                Status = JobStatusConstants.Pending,
                Progress = 0,
                FilePath = createDto.FilePath,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Metadata = createDto.Metadata ?? new Dictionary<string, object>()
            };

            var response = await _supabase
                .From<Job>()
                .Insert(job);

            var createdJob = response.Models.FirstOrDefault();
            if (createdJob == null)
            {
                throw new Exception("Falha ao criar job no Supabase");
            }

            _logger.LogInformation("Job {JobId} criado com sucesso no Supabase", createDto.JobId);
            return createdJob;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar job {JobId} no Supabase", createDto.JobId);
            throw;
        }
    }

    /// <summary>
    /// Busca um job pelo JobId
    /// </summary>
    public async Task<Job?> GetJobAsync(string jobId)
    {
        try
        {
            var response = await _supabase
                .From<Job>()
                .Where(x => x.JobId == jobId)
                .Single();

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId} não encontrado no Supabase", jobId);
            return null;
        }
    }

    /// <summary>
    /// Atualiza um job existente
    /// </summary>
    public async Task<Job?> UpdateJobAsync(string jobId, UpdateJobDto updateDto)
    {
        try
        {
            var existing = await GetJobAsync(jobId);
            if (existing == null)
            {
                _logger.LogWarning("Tentativa de atualizar job inexistente: {JobId}", jobId);
                return null;
            }

            // Atualizar apenas campos fornecidos
            if (updateDto.Status != null) existing.Status = updateDto.Status;
            if (updateDto.Progress.HasValue) existing.Progress = updateDto.Progress.Value;
            if (updateDto.ErrorMessage != null) existing.ErrorMessage = updateDto.ErrorMessage;
            if (updateDto.ResultPath != null) existing.ResultPath = updateDto.ResultPath;
            if (updateDto.CompletedAt.HasValue) existing.CompletedAt = updateDto.CompletedAt;
            if (updateDto.Metadata != null) existing.Metadata = updateDto.Metadata;

            existing.UpdatedAt = DateTime.UtcNow;

            var response = await _supabase
                .From<Job>()
                .Where(x => x.JobId == jobId)
                .Update(existing);

            var updatedJob = response.Models.FirstOrDefault();

            _logger.LogInformation("Job {JobId} atualizado: Status={Status}, Progress={Progress}",
                jobId, existing.Status, existing.Progress);

            return updatedJob;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar job {JobId} no Supabase", jobId);
            throw;
        }
    }

    /// <summary>
    /// Deleta um job
    /// </summary>
    public async Task<bool> DeleteJobAsync(string jobId)
    {
        try
        {
            await _supabase
                .From<Job>()
                .Where(x => x.JobId == jobId)
                .Delete();

            _logger.LogInformation("Job {JobId} deletado do Supabase", jobId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao deletar job {JobId} do Supabase", jobId);
            return false;
        }
    }

    /// <summary>
    /// Busca jobs de um usuário específico
    /// </summary>
    public async Task<IEnumerable<Job>> GetUserJobsAsync(string userId, int page = 1, int pageSize = 10)
    {
        try
        {
            var offset = (page - 1) * pageSize;

            var response = await _supabase
                .From<Job>()
                .Where(x => x.UserId == userId)
                .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Descending)
                .Range(offset, offset + pageSize - 1)
                .Get();

            return response.Models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar jobs do usuário {UserId}", userId);
            return Enumerable.Empty<Job>();
        }
    }

    /// <summary>
    /// Busca jobs por status
    /// </summary>
    public async Task<IEnumerable<Job>> GetJobsByStatusAsync(string status)
    {
        try
        {
            var response = await _supabase
                .From<Job>()
                .Where(x => x.Status == status)
                .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Descending)
                .Get();

            return response.Models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar jobs com status {Status}", status);
            return Enumerable.Empty<Job>();
        }
    }

    /// <summary>
    /// Remove jobs antigos
    /// </summary>
    public async Task<int> CleanupOldJobsAsync(int hoursOld)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddHours(-hoursOld);

            var oldJobs = await _supabase
                .From<Job>()
                .Where(x => x.CreatedAt < cutoffDate)
                .Get();

            foreach (var job in oldJobs.Models)
            {
                await DeleteJobAsync(job.JobId);
            }

            var count = oldJobs.Models.Count;
            _logger.LogInformation("Cleanup: {Count} jobs antigos removidos (> {Hours}h)", count, hoursOld);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no cleanup de jobs antigos");
            return 0;
        }
    }
}

/// <summary>
/// Serviço híbrido que combina cache em memória com persistência no Supabase
/// Implementa IJobService mantendo compatibilidade total com o código existente
/// </summary>
public class HybridJobService : IJobService
{
    private readonly IJobPersistenceService _persistenceService;
    private readonly ILogger<HybridJobService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _jobsBasePath;

    // Cache em memória para acesso rápido
    private readonly ConcurrentDictionary<string, JobInfo> _cache = new();

    public HybridJobService(
        IJobPersistenceService persistenceService,
        ILogger<HybridJobService> logger,
        IConfiguration configuration)
    {
        _persistenceService = persistenceService;
        _logger = logger;
        _configuration = configuration;

        _jobsBasePath = Path.Combine(Path.GetTempPath(), "ocr_jobs");
        Directory.CreateDirectory(_jobsBasePath);
    }

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

            // Criar registro no cache
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

            _cache[jobId] = jobInfo;

            // Persistir no Supabase
            var createDto = new CreateJobDto
            {
                JobId = jobId,
                UserId = null, // Pode ser setado se tiver contexto de usuário
                FileName = file.FileName,
                FilePath = inputPath,
                Metadata = new Dictionary<string, object>
                {
                    { "file_size", file.Length },
                    { "original_name", file.FileName }
                }
            };

            await _persistenceService.CreateJobAsync(createDto);

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

    private async Task ProcessJobAsync(string jobId, string jobDir)
    {
        try
        {
            _logger.LogInformation("Iniciando processamento do job: {JobId}", jobId);

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

    public async Task<JobInfo?> GetJobAsync(string jobId)
    {
        // Tentar cache primeiro
        if (_cache.TryGetValue(jobId, out var cached))
        {
            return cached;
        }

        // Buscar no Supabase e reconstruir cache
        var job = await _persistenceService.GetJobAsync(jobId);
        if (job == null) return null;

        var jobInfo = MapToJobInfo(job);
        _cache[jobId] = jobInfo;

        return jobInfo;
    }

    public Task<List<JobInfo>> ListJobsAsync(int page, int pageSize, string? status)
    {
        var query = _cache.Values.AsEnumerable();

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

    public Task<int> GetTotalJobsAsync(string? status)
    {
        var query = _cache.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(j => j.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(query.Count());
    }

    public async Task<bool> UpdateJobStatusAsync(string jobId, string status, string? outputPath = null,
        List<string>? logs = null, string? error = null)
    {
        // Atualizar cache
        if (!_cache.TryGetValue(jobId, out var job))
        {
            _logger.LogWarning("Tentativa de atualizar job inexistente: {JobId}", jobId);
            return false;
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
            ProgressInfo = job.ProgressInfo,
            JobId = job.JobId,
            UserId = job.UserId
        };

        _cache[jobId] = updatedJob;

        // Atualizar Supabase
        var updateDto = new UpdateJobDto
        {
            Status = status,
            Progress = updatedJob.ProgressPercent,
            ErrorMessage = error,
            ResultPath = outputPath,
            CompletedAt = updatedJob.CompletedAt
        };

        await _persistenceService.UpdateJobAsync(jobId, updateDto);

        _logger.LogDebug("Job atualizado: {JobId}, Status: {Status}", jobId, status);

        return true;
    }

    public Task<bool> AppendJobLogAsync(string jobId, string logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return Task.FromResult(false);
        }

        if (!_cache.TryGetValue(jobId, out var job))
        {
            return Task.FromResult(false);
        }

        var newLogs = new List<string>(job.Logs);
        newLogs.Add(logLine);

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
            ProgressInfo = job.ProgressInfo,
            JobId = job.JobId,
            UserId = job.UserId
        };

        _cache[jobId] = updatedJob;
        return Task.FromResult(true);
    }

    public Task<bool> UpdateJobProgressAsync(string jobId, JobProgressInfo progress)
    {
        if (!_cache.TryGetValue(jobId, out var job))
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
            ProgressInfo = progress,
            JobId = job.JobId,
            UserId = job.UserId
        };

        _cache[jobId] = updatedJob;

        // Atualizar progresso no Supabase também
        _ = _persistenceService.UpdateJobAsync(jobId, new UpdateJobDto
        {
            Progress = percent
        });

        return Task.FromResult(true);
    }

    public Task<bool> CancelJobAsync(string jobId)
    {
        if (!_cache.TryGetValue(jobId, out var job))
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
            OutputPath = job.OutputPath,
            ProgressPercent = job.ProgressPercent,
            ProgressInfo = job.ProgressInfo,
            JobId = job.JobId,
            UserId = job.UserId
        };

        _cache[jobId] = updatedJob;

        // Atualizar no Supabase
        _ = _persistenceService.UpdateJobAsync(jobId, new UpdateJobDto
        {
            Status = "cancelled",
            ErrorMessage = "Job cancelado pelo usuário",
            CompletedAt = DateTime.UtcNow
        });

        _logger.LogInformation("Job cancelado: {JobId}", jobId);

        return Task.FromResult(true);
    }

    public async Task<int> CleanupOldJobsAsync(int hoursOld)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hoursOld);
        var removed = 0;

        // Limpar cache
        foreach (var job in _cache.Values.Where(j => j.CreatedAt < cutoff).ToList())
        {
            if (_cache.TryRemove(job.Id, out _))
            {
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

        // Limpar Supabase
        await _persistenceService.CleanupOldJobsAsync(hoursOld);

        if (removed > 0)
        {
            _logger.LogInformation("Limpeza concluída: {Count} job(s) removido(s)", removed);
        }

        return removed;
    }

    public Task<JobStatsResponse> GetStatsAsync()
    {
        var allJobs = _cache.Values.ToList();

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

    private JobInfo MapToJobInfo(Job job)
    {
        return new JobInfo
        {
            Id = job.JobId,
            JobId = job.JobId,
            FileName = job.FileName,
            Status = job.Status,
            ProgressPercent = job.Progress,
            ErrorMessage = job.ErrorMessage,
            OutputPath = job.ResultPath,
            CreatedAt = job.CreatedAt,
            CompletedAt = job.CompletedAt,
            UserId = job.UserId,
            Logs = new List<string>(),
            FileSize = 0 // Pode ser recuperado dos metadados se necessário
        };
    }
}