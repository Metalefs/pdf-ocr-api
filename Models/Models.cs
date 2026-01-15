using System.ComponentModel.DataAnnotations;

namespace pdf_ocr.Models
{
    // ========================================
    // ENTIDADE PRINCIPAL - JOB
    // ========================================

    /// <summary>
    /// Representa um job de processamento de PDF
    /// </summary>
    public class JobInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = "queued";
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<string> Logs { get; set; } = new();
        public string Error { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int ProgressPercent { get; set; }
        public string? JobId { get; set; }
        public string? UserId { get; set; }
        public JobProgressInfo? ProgressInfo { get; set; }
    }

    /// <summary>
    /// Progresso detalhado e amigável do job
    /// </summary>
    public class JobProgressInfo
    {
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int? TotalPages { get; set; }
        public int? ProcessedPages { get; set; }
        public List<int>? ActivePages { get; set; }
        public int? Percent { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ========================================
    // REQUEST DTOs
    // ========================================

    /// <summary>
    /// Request para upload de PDF (via FormFile)
    /// </summary>
    public class PdfUploadRequest
    {
        [Required(ErrorMessage = "Arquivo PDF é obrigatório")]
        public IFormFile File { get; set; } = null!;
    }

    // ========================================
    // RESPONSE DTOs
    // ========================================

    /// <summary>
    /// Resposta para requisição de processamento assíncrono
    /// </summary>
    public class ProcessResponse
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StatusUrl { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public int CreditsRemaining { get; set; }
        public string UpgradeMessage { get; set; }
    }

    /// <summary>
    /// Resposta detalhada de status do job
    /// </summary>
    public class JobStatusResponse
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<string> Logs { get; set; } = new();
        public string Error { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public int Progress { get; set; }
        public string? EstimatedTimeRemaining { get; set; }
        public string? Message { get; set; }
        public string? Stage { get; set; }
        public int? TotalPages { get; set; }
        public int? ProcessedPages { get; set; }
        public List<int>? ActivePages { get; set; }
    }

    /// <summary>
    /// Resumo de um job para listagem
    /// </summary>
    public class JobSummary
    {
        public string JobId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long FileSize { get; set; }
    }

    /// <summary>
    /// Resposta paginada de lista de jobs
    /// </summary>
    public class JobListResponse
    {
        public List<JobSummary> Jobs { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalJobs { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// Resposta de cancelamento de job
    /// </summary>
    public class CancelJobResponse
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resposta de limpeza de jobs antigos
    /// </summary>
    public class CleanupResponse
    {
        public int RemovedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Estatísticas gerais dos jobs
    /// </summary>
    public class JobStatsResponse
    {
        public int TotalJobs { get; set; }
        public int QueuedJobs { get; set; }
        public int ProcessingJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }
        public int CancelledJobs { get; set; }
        public long TotalBytesProcessed { get; set; }
        public double AverageProcessingTime { get; set; }
        public DateTime? OldestJobDate { get; set; }
        public DateTime? NewestJobDate { get; set; }
    }

    /// <summary>
    /// Resposta de erro padronizada
    /// </summary>
    public class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string? UpgradeUrl { get; set; }
        public List<string>? Logs { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Resposta de health check
    /// </summary>
    public class HealthResponse
    {
        public string Status { get; set; } = "online";
        public string Service { get; set; } = "TextLayer OCR API";
        public string Version { get; set; } = "1.0.0";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<string> Features { get; set; } = new();
    }

    // ========================================
    // ENUMS
    // ========================================

    /// <summary>
    /// Status possíveis de um job
    /// </summary>
    public enum JobStatus
    {
        Queued,
        Processing,
        Completed,
        Failed,
        Cancelled
    }
}