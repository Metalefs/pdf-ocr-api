// Services/SupabaseUserService.cs
using Microsoft.AspNetCore.Mvc.RazorPages;
using Postgrest.Models;
using Supabase;
using Postgrest;
using Postgrest.Attributes;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TableAttribute = Postgrest.Attributes.TableAttribute;
using ColumnAttribute = Postgrest.Attributes.ColumnAttribute;

namespace pdf_ocr.Services
{
    // ========================================
    // MODELO DE TABELA SUPABASE
    // ========================================
    [Table("users")]
    public class UserRecord : BaseModel
    {
        [PrimaryKey("id", true)]
        [JsonPropertyName("id")]
        public string Id { get; set; } = default!;

        [Column("email")]
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [Column("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Column("avatar")]
        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [Column("credits")]
        [JsonPropertyName("credits")]
        public int Credits { get; set; }

        [Column("plan")]
        [JsonPropertyName("plan")]
        public string Plan { get; set; } = "free";

        [Column("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("subscription_ends_at")]
        [JsonPropertyName("subscription_ends_at")]
        public DateTime? SubscriptionEndsAt { get; set; }

        [Column("stripe_customer_id")]
        [JsonPropertyName("stripe_customer_id")]
        public string? StripeCustomerId { get; set; }
    }

    // ========================================
    // SERVIÇO COM SUPABASE
    // ========================================
    public class SupabaseUserService : IUserService
    {
        private readonly Supabase.Client _supabase;
        private readonly ILogger<SupabaseUserService> _logger;

        public SupabaseUserService(IConfiguration config, ILogger<SupabaseUserService> logger)
        {
            _logger = logger;

            var url = config["Supabase:Url"];
            var key = config["Supabase:AnonKey"];

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
                throw new InvalidOperationException("Supabase URL/Key não configurados");

            var options = new SupabaseOptions
            {
                AutoConnectRealtime = false
            };

            _supabase = new Supabase.Client(url, key, options);
            _logger.LogInformation("SupabaseUserService inicializado");
        }

        public async Task<Models.UserProfile?> GetOrCreateUserAsync(
            string userId,
            string email,
            string name,
            string? avatar,
            string? user_metadata = null)
        {
            try
            {
                // Buscar usuário existente
                var response = await _supabase
                    .From<UserRecord>()
                    .Where(x => x.Id == userId)
                    .Single();

                if (response != null)
                {
                    _logger.LogDebug("Usuário encontrado: {UserId}", userId);
                    return MapToUserProfile(response);
                }

                // Criar novo usuário
                var newUser = new UserRecord
                {
                    Id = userId,
                    Email = email,
                    Name = string.IsNullOrWhiteSpace(name) ? email : name,
                    Avatar = avatar,
                    Credits = 2,
                    Plan = "free",
                    CreatedAt = DateTime.UtcNow
                };

                var inserted = await _supabase
                    .From<UserRecord>()
                    .Insert(newUser);

                _logger.LogInformation("Novo usuário criado: {Email} ({UserId})", email, userId);
                return MapToUserProfile(inserted.Models.First());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter/criar usuário: {UserId}", userId);
                throw;
            }
        }

        public async Task<int> GetCreditsAsync(string userId)
        {
            try
            {
                var user = await _supabase
                    .From<UserRecord>()
                    .Where(x => x.Id == userId)
                    .Single();

                return user?.Credits ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter créditos: {UserId}", userId);
                return 0;
            }
        }

        public async Task<bool> DeductCreditsAsync(string userId, int amount)
        {
            try
            {
                var user = await _supabase
                    .From<UserRecord>()
                    .Where(x => x.Id == userId)
                    .Single();

                if (user == null || user.Credits < amount)
                    return false;

                user.Credits -= amount;

                await _supabase
                    .From<UserRecord>()
                    .Update(user);

                _logger.LogInformation(
                    "Créditos deduzidos: {UserId} -{Amount} = {Remaining}",
                    userId, amount, user.Credits
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao deduzir créditos: {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> AddCreditsAsync(string userId, int amount)
        {
            try
            {
                var user = await _supabase
                    .From<UserRecord>()
                    .Where(x => x.Id == userId)
                    .Single();

                if (user == null)
                    return false;

                user.Credits += amount;

                await _supabase
                    .From<UserRecord>()
                    .Update(user);

                _logger.LogInformation(
                    "Créditos adicionados: {UserId} +{Amount} = {Total}",
                    userId, amount, user.Credits
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao adicionar créditos: {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> UpdatePlanAsync(string userId, string plan, DateTime? expiresAt)
        {
            try
            {
                var user = await _supabase
                    .From<UserRecord>()
                    .Where(x => x.Id == userId)
                    .Single();

                if (user == null)
                    return false;

                user.Plan = plan;
                user.SubscriptionEndsAt = expiresAt;

                await _supabase
                    .From<UserRecord>()
                    .Update(user);

                _logger.LogInformation("Plano atualizado: {UserId} → {Plan}", userId, plan);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar plano: {UserId}", userId);
                return false;
            }
        }

        private static Models.UserProfile MapToUserProfile(UserRecord record)
        {
            return new Models.UserProfile
            {
                Id = record.Id,
                Email = record.Email,
                Name = record.Name,
                Avatar = record.Avatar,
                Credits = record.Credits,
                Plan = record.Plan,
                CreatedAt = record.CreatedAt,
                SubscriptionEndsAt = record.SubscriptionEndsAt
            };
        }
    }
}