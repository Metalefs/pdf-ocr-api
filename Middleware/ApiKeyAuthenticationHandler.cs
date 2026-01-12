using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using pdf_ocr.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace pdf_ocr.Middleware
{
    /// <summary>
    /// Handler de autenticação via API Key (header X-API-Key)
    /// </summary>
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IApiKeyService _apiKeyService;
        private readonly IUserService _userService;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IApiKeyService apiKeyService,
            IUserService userService)
            : base(options, logger, encoder)
        {
            _apiKeyService = apiKeyService;
            _userService = userService;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Verificar se o header X-API-Key está presente
            if (!Request.Headers.TryGetValue("X-API-Key", out var apiKeyValues))
            {
                return AuthenticateResult.NoResult();
            }

            var apiKey = apiKeyValues.FirstOrDefault();
            if (string.IsNullOrEmpty(apiKey))
            {
                return AuthenticateResult.NoResult();
            }

            try
            {
                // Validar chave
                var userId = await _apiKeyService.ValidateKeyAsync(apiKey);
                if (userId == null)
                {
                    Logger.LogWarning("API Key inválida ou expirada");
                    return AuthenticateResult.Fail("Invalid or expired API key");
                }

                // Buscar informações do usuário
                var user = await _userService.GetOrCreateUserAsync(userId, "", "", null, null);
                if (user == null)
                {
                    return AuthenticateResult.Fail("User not found");
                }

                // Atualizar last_used (fire-and-forget)
                _ = Task.Run(() => _apiKeyService.UpdateLastUsedAsync(
                    HashKey(apiKey)
                ));

                // Criar claims (mesmos do JWT)
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim("plan", user.Plan),
                    new Claim("auth_method", "api_key")
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                Logger.LogInformation("Autenticado via API Key: {UserId} ({Email})",
                    user.Id, user.Email);

                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Erro ao validar API Key");
                return AuthenticateResult.Fail("Authentication error");
            }
        }

        private static string HashKey(string plainKey)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(plainKey));
            return Convert.ToBase64String(hashBytes);
        }
    }

    /// <summary>
    /// Extensão para registrar o handler
    /// </summary>
    public static class ApiKeyAuthenticationExtensions
    {
        public static AuthenticationBuilder AddApiKeySupport(
            this AuthenticationBuilder builder)
        {
            return builder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                "ApiKey",
                options => { }
            );
        }
    }
}