using pdf_ocr.Models;
using System.Text.Json.Serialization;

namespace pdf_ocr.Services
{
    public interface IUserService
    {
        Task<UserProfile?> GetOrCreateUserAsync(string userId, string email, string name, string? avatar, string? user_metadata = null);
        Task<int> GetCreditsAsync(string userId);
        Task<bool> DeductCreditsAsync(string userId, int amount);
        Task<bool> AddCreditsAsync(string userId, int amount);
        Task<bool> UpdatePlanAsync(string userId, string plan, DateTime? expiresAt);
        Task<UsageStats?> GetUsageStatsAsync(string userId);
        Task<bool> RecordUsageAsync(string userId, int creditsUsed, string? fileName, long? fileSize);
        Task<Models.UserProfile?> UpdateUserAsync(string userId, string name, string? avatar);
    }

    public class UserService : IUserService
    {
        private readonly Dictionary<string, UserProfile> _users = new();
        private readonly ILogger<UserService> _logger;
        private readonly Dictionary<string, List<UsageHistoryRecord>> _usageHistory = new();

        public UserService(ILogger<UserService> logger)
        {
            _logger = logger;
        }

        public Task<UserProfile?> GetOrCreateUserAsync(string userId, string email, string name, string? avatar, string? user_metadata)
        {
            if (!_users.ContainsKey(userId))
            {
                _users[userId] = new UserProfile
                {
                    Id = userId,
                    Email = email,
                    Name = name,
                    User_metadata = user_metadata,
                    Avatar = avatar,
                    Credits = 2, // 2 créditos grátis
                    Plan = "free",
                    CreatedAt = DateTime.UtcNow
                };
                _logger.LogInformation("Novo usuário criado: {Email} ({UserId})", email, userId);
            }
            return Task.FromResult<UserProfile?>(_users[userId]);
        }

        public Task<int> GetCreditsAsync(string userId)
        {
            if (_users.TryGetValue(userId, out var user))
                return Task.FromResult(user.Credits);
            return Task.FromResult(0);
        }

        public async Task<bool> DeductCreditsAsync(string userId, int amount)
        {
            if (!_users.TryGetValue(userId, out var user))
                return false;

            if (user.Credits < amount)
                return false;

            user.Credits -= amount;
            _logger.LogInformation("Créditos deduzidos: {UserId} -{Amount} = {Remaining}",
                userId, amount, user.Credits);

            // Registrar no histórico de uso em memória
            await RecordUsageAsync(userId, amount, null, null);

            return true;
        }

        public Task<bool> AddCreditsAsync(string userId, int amount)
        {
            if (!_users.TryGetValue(userId, out var user))
                return Task.FromResult(false);

            user.Credits += amount;
            _logger.LogInformation("Créditos adicionados: {UserId} +{Amount} = {Total}",
                userId, amount, user.Credits);
            return Task.FromResult(true);
        }

        public Task<bool> UpdatePlanAsync(string userId, string plan, DateTime? expiresAt)
        {
            if (!_users.TryGetValue(userId, out var user))
                return Task.FromResult(false);

            user.Plan = plan;
            user.SubscriptionEndsAt = expiresAt;
            _logger.LogInformation("Plano atualizado: {UserId} → {Plan}", userId, plan);
            return Task.FromResult(true);
        }

        public Task<UsageStats?> GetUsageStatsAsync(string userId)
        {
            if (!_users.TryGetValue(userId, out var user))
                return Task.FromResult<UsageStats?>(new UsageStats { Today = 0, Week = 0, Month = 0, LimitValue = 3 });

            var limit = user.Plan switch
            {
                "free" => 3,
                "pro" => 50,
                "business" => 500,
                _ => 3
            };

            return Task.FromResult<UsageStats?>(new UsageStats { Today = 0, Week = 0, Month = 0, LimitValue = limit });
        }

        public Task<bool> RecordUsageAsync(string userId, int creditsUsed, string? fileName, long? fileSize)
        {
            var entry = new UsageHistoryRecord
            {
                Id = 0,
                UserId = userId,
                CreditsUsed = creditsUsed,
                FileName = fileName,
                FileSize = fileSize,
                CreatedAt = DateTime.UtcNow
            };

            if (!_usageHistory.ContainsKey(userId))
                _usageHistory[userId] = new List<UsageHistoryRecord>();

            _usageHistory[userId].Add(entry);
            _logger.LogInformation("Recorded usage in-memory: {UserId} -{Credits}", userId, creditsUsed);
            return Task.FromResult(true);
        }

        public Task<Models.UserProfile?> UpdateUserAsync(string userId, string name, string? avatar)
        {
            throw new NotImplementedException();
        }
    }

    public class UsageStats
    {
        [JsonPropertyName("today")]
        public int Today { get; set; }

        [JsonPropertyName("week")]
        public int Week { get; set; }

        [JsonPropertyName("month")]
        public int Month { get; set; }

        [JsonPropertyName("limit_value")]
        public int LimitValue { get; set; }
    }

    public class UsageHistoryRecord
    {
        public long Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public int CreditsUsed { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
