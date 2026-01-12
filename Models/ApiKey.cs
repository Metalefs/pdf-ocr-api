using Postgrest.Attributes;
using Postgrest.Models;
using System.Text.Json.Serialization;

namespace pdf_ocr.Models
{
    /// <summary>
    /// Modelo da tabela api_keys no Supabase
    /// </summary>
    [Table("api_keys")]
    public class ApiKeyRecord : BaseModel
    {
        [PrimaryKey("id")]
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [Column("key_hash")]
        [JsonPropertyName("key_hash")]
        public string KeyHash { get; set; } = string.Empty;

        [Column("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Column("last_used_at")]
        [JsonPropertyName("last_used_at")]
        public DateTime? LastUsedAt { get; set; }

        [Column("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("expires_at")]
        [JsonPropertyName("expires_at")]
        public DateTime? ExpiresAt { get; set; }

        [Column("is_active")]
        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("rate_limit_per_minute")]
        [JsonPropertyName("rate_limit_per_minute")]
        public int RateLimitPerMinute { get; set; } = 60;

        [Column("allowed_ips")]
        [JsonPropertyName("allowed_ips")]
        public string[]? AllowedIps { get; set; }
    }

    /// <summary>
    /// DTO para criação de chave
    /// </summary>
    public class CreateApiKeyRequest
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
        public int RateLimitPerMinute { get; set; } = 60;
        public string[]? AllowedIps { get; set; }
    }

    /// <summary>
    /// DTO de resposta (inclui a chave plain-text APENAS na criação)
    /// </summary>
    public class ApiKeyResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? PlainKey { get; set; } // Retornado APENAS na criação
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public bool IsActive { get; set; }
        public int RateLimitPerMinute { get; set; }
    }
}