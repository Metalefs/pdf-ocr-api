using pdf_ocr.Models;
using Supabase;
using System.Security.Cryptography;
using System.Text;

namespace pdf_ocr.Services
{
    public interface IApiKeyService
    {
        Task<ApiKeyResponse> CreateKeyAsync(string userId, CreateApiKeyRequest request);
        Task<List<ApiKeyResponse>> GetUserKeysAsync(string userId);
        Task<bool> RevokeKeyAsync(string userId, Guid keyId);
        Task<string?> ValidateKeyAsync(string plainKey);
        Task UpdateLastUsedAsync(string keyHash);
    }

    public class ApiKeyService : IApiKeyService
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<ApiKeyService> _logger;

        // Prefixo para identificar chaves visualmente (ex: sk_live_abc123...)
        private const string KEY_PREFIX = "sk_live_";

        public ApiKeyService(IConfiguration config, ILogger<ApiKeyService> logger)
        {
            _logger = logger;

            var url = config["Supabase:Url"] ?? throw new Exception("Supabase URL não configurada");
            var key = config["Supabase:AnonKey"] ?? throw new Exception("Supabase AnonKey não configurada");

            _supabase = new Supabase.Client(url, key, new SupabaseOptions { AutoConnectRealtime = false });
        }

        /// <summary>
        /// Cria uma nova chave de API
        /// </summary>
        public async Task<ApiKeyResponse> CreateKeyAsync(string userId, CreateApiKeyRequest request)
        {
            // 1. Gerar chave segura (32 bytes = 256 bits)
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            var plainKey = KEY_PREFIX + Convert.ToBase64String(randomBytes)
                .Replace("+", "").Replace("/", "").Replace("=", "")[..40];

            // 2. Hash da chave (SHA256) para armazenamento
            var keyHash = HashKey(plainKey);

            // 3. Inserir no banco
            var record = new ApiKeyRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                KeyHash = keyHash,
                Name = request.Name,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresAt,
                IsActive = true,
                RateLimitPerMinute = request.RateLimitPerMinute,
                AllowedIps = request.AllowedIps
            };

            await _supabase.From<ApiKeyRecord>().Insert(record);

            _logger.LogInformation("API Key criada: {Name} para {UserId}", request.Name, userId);

            // 4. Retornar DTO (inclui plainKey APENAS aqui)
            return new ApiKeyResponse
            {
                Id = record.Id,
                Name = record.Name,
                PlainKey = plainKey, // ⚠️ Mostrar APENAS na criação
                CreatedAt = record.CreatedAt,
                ExpiresAt = record.ExpiresAt,
                IsActive = record.IsActive,
                RateLimitPerMinute = record.RateLimitPerMinute
            };
        }

        /// <summary>
        /// Lista chaves do usuário (SEM mostrar plain key)
        /// </summary>
        public async Task<List<ApiKeyResponse>> GetUserKeysAsync(string userId)
        {
            var keys = await _supabase
                .From<ApiKeyRecord>()
                .Where(x => x.UserId == userId)
                .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Descending)
                .Get();

            return keys.Models.Select(k => new ApiKeyResponse
            {
                Id = k.Id,
                Name = k.Name,
                PlainKey = null, // Nunca retornar novamente
                CreatedAt = k.CreatedAt,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                IsActive = k.IsActive,
                RateLimitPerMinute = k.RateLimitPerMinute
            }).ToList();
        }

        /// <summary>
        /// Revoga (desativa) uma chave
        /// </summary>
        public async Task<bool> RevokeKeyAsync(string userId, Guid keyId)
        {
            var key = await _supabase
                .From<ApiKeyRecord>()
                .Where(x => x.Id == keyId && x.UserId == userId)
                .Single();

            if (key == null) return false;

            key.IsActive = false;
            await _supabase.From<ApiKeyRecord>().Update(key);

            _logger.LogWarning("API Key revogada: {KeyId} ({Name})", keyId, key.Name);
            return true;
        }

        /// <summary>
        /// Valida uma chave e retorna o user_id se válida
        /// </summary>
        public async Task<string?> ValidateKeyAsync(string plainKey)
        {
            if (string.IsNullOrEmpty(plainKey) || !plainKey.StartsWith(KEY_PREFIX))
                return null;

            var keyHash = HashKey(plainKey);

            var key = await _supabase
                .From<ApiKeyRecord>()
                .Where(x => x.KeyHash == keyHash && x.IsActive == true)
                .Single();

            if (key == null) return null;

            // Verificar expiração
            if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("API Key expirada: {KeyId}", key.Id);
                return null;
            }

            return key.UserId;
        }

        /// <summary>
        /// Atualiza timestamp de último uso
        /// </summary>
        public async Task UpdateLastUsedAsync(string keyHash)
        {
            try
            {
                var key = await _supabase
                    .From<ApiKeyRecord>()
                    .Where(x => x.KeyHash == keyHash)
                    .Single();

                if (key != null)
                {
                    key.LastUsedAt = DateTime.UtcNow;
                    await _supabase.From<ApiKeyRecord>().Update(key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar last_used_at");
            }
        }

        /// <summary>
        /// Hash SHA256 da chave
        /// </summary>
        private static string HashKey(string plainKey)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plainKey));
            return Convert.ToBase64String(hashBytes);
        }
    }
}