using pdf_ocr.Models;
using pdf_ocr.Services;

namespace pdf_ocr.BackgroundServices;

/// <summary>
/// Serviço de background para recuperar jobs pendentes após restart
/// </summary>
public class JobRecoveryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobRecoveryService> _logger;

    public JobRecoveryService(IServiceProvider serviceProvider, ILogger<JobRecoveryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Aguardar 10 segundos após o startup antes de recuperar jobs
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        await RecoverPendingJobsAsync(stoppingToken);
    }

    private async Task RecoverPendingJobsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistenceService = scope.ServiceProvider.GetRequiredService<IJobPersistenceService>();
            var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

            _logger.LogInformation("Iniciando recuperação de jobs pendentes...");

            // Buscar jobs que estavam processing ou pending
            var processingJobs = await persistenceService.GetJobsByStatusAsync(JobStatusConstants.Processing);
            var pendingJobs = await persistenceService.GetJobsByStatusAsync(JobStatusConstants.Pending);

            var jobsToRecover = processingJobs.Concat(pendingJobs).ToList();

            if (!jobsToRecover.Any())
            {
                _logger.LogInformation("Nenhum job pendente encontrado para recuperação");
                return;
            }

            _logger.LogInformation("Encontrados {Count} jobs para recuperação", jobsToRecover.Count);

            foreach (var job in jobsToRecover)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    // Marcar como failed se estava processing (foi interrompido)
                    if (job.Status == JobStatusConstants.Processing)
                    {
                        await persistenceService.UpdateJobAsync(job.JobId, new UpdateJobDto
                        {
                            Status = JobStatusConstants.Failed,
                            ErrorMessage = "Job interrompido devido a reinício da aplicação",
                            CompletedAt = DateTime.UtcNow
                        });

                        _logger.LogWarning("Job {JobId} marcado como failed (estava em processamento)", job.JobId);
                    }
                    // Jobs pending permanecem pending para serem reprocessados
                    else
                    {
                        _logger.LogInformation("Job {JobId} permanece pending para reprocessamento", job.JobId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao recuperar job {JobId}", job.JobId);
                }
            }

            _logger.LogInformation("Recuperação de jobs concluída");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro crítico na recuperação de jobs");
        }
    }
}

// ============================================
// ADICIONAR AO Program.cs:
// ============================================
// builder.Services.AddHostedService<JobRecoveryService>();