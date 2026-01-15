using Postgrest.Attributes;
using Postgrest.Models;
using System.Text.Json.Serialization;

namespace pdf_ocr.Models;

/// <summary>
/// Representa um job de processamento OCR persistido no Supabase
/// </summary>
[Table("jobs")]
public class Job : BaseModel
{
    [PrimaryKey("id")]
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [Column("job_id")]
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [Column("file_name")]
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [Column("progress")]
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [Column("error_message")]
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("file_path")]
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [Column("result_path")]
    [JsonPropertyName("result_path")]
    public string? ResultPath { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("completed_at")]
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("metadata")]
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// DTO para criação de novo job
/// </summary>
public class CreateJobDto
{
    public string JobId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// DTO para atualização de job
/// </summary>
public class UpdateJobDto
{
    public string? Status { get; set; }
    public int? Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultPath { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Constantes de status de job
/// </summary>
public static class JobStatusConstants
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Queued = "queued";
    public const string Cancelled = "cancelled";
}