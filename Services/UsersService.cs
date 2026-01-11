using pdf_ocr.Models;

namespace pdf_ocr.Services
{
    public interface IUserService
    {
        Task<UserProfile?> GetOrCreateUserAsync(string userId, string email, string name, string? avatar);
        Task<int> GetCreditsAsync(string userId);
        Task<bool> DeductCreditsAsync(string userId, int amount);
        Task<bool> AddCreditsAsync(string userId, int amount);
        Task<bool> UpdatePlanAsync(string userId, string plan, DateTime? expiresAt);
    }

    public class UserService : IUserService
    {
        private readonly Dictionary<string, UserProfile> _users = new();
        private readonly ILogger<UserService> _logger;

        public UserService(ILogger<UserService> logger)
        {
            _logger = logger;
        }

        public Task<UserProfile?> GetOrCreateUserAsync(string userId, string email, string name, string? avatar)
        {
            if (!_users.ContainsKey(userId))
            {
                _users[userId] = new UserProfile
                {
                    Id = userId,
                    Email = email,
                    Name = name,
                    Avatar = avatar,
                    Credits = 10, // 10 créditos grátis
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

        public Task<bool> DeductCreditsAsync(string userId, int amount)
        {
            if (!_users.TryGetValue(userId, out var user))
                return Task.FromResult(false);

            if (user.Credits < amount)
                return Task.FromResult(false);

            user.Credits -= amount;
            _logger.LogInformation("Créditos deduzidos: {UserId} -{Amount} = {Remaining}",
                userId, amount, user.Credits);
            return Task.FromResult(true);
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
    }
}
