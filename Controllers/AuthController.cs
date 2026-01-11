// Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using pdf_ocr.Models;
using pdf_ocr.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace pdf_ocr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _config;
        private readonly IUserService _userService;

        public AuthController(
            ILogger<AuthController> logger,
            IConfiguration config,
            IUserService userService)
        {
            _logger = logger;
            _config = config;
            _userService = userService;
        }

        /// <summary>
        /// Sincroniza usu�rio do Supabase com banco local
        /// </summary>
        [HttpPost("sync")]
        [AllowAnonymous]
        public async Task<IActionResult> SyncUser([FromBody] SyncUserRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.AccessToken))
                {
                    return BadRequest(new { error = "AccessToken � obrigat�rio" });
                }

                // Validar token JWT do Supabase
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(request.AccessToken);

                // Extrair informa��es do usu�rio
                var userId = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                var email = token.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
                var name = JsonSerializer.Deserialize<UserMetadata>(token.Claims.FirstOrDefault(c => c.Type == "user_metadata")?.Value)?.name;
                var user_metadata = token.Claims.FirstOrDefault(c => c.Type == "user_metadata")?.Value;
                var avatarUrl = token.Claims.FirstOrDefault(c => c.Type == "picture")?.Value;

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
                {
                    return BadRequest(new { error = "Token inv�lido" });
                }

                // Criar ou atualizar usu�rio no sistema
                var user = await _userService.GetOrCreateUserAsync(
                    userId,
                    email,
                    name ?? email.Split('@')[0],
                    avatarUrl,
                    user_metadata
                );

                _logger.LogInformation("Usuário sincronizado: {Email} ({UserId})", email, userId);

                return Ok(new
                {
                    success = true,
                    user = new
                    {
                        id = user.Id,
                        email = user.Email,
                        name = user.Name,
                        avatar = user.Avatar,
                        credits = user.Credits,
                        plan = user.Plan,
                        createdAt = user.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao sincronizar usu�rio");
                return StatusCode(500, new { error = "Erro ao processar autentica��o" });
            }
        }
        /// <summary>
        /// OAuth callback endpoint - receives code from Supabase OAuth provider
        /// Redirects to frontend with the OAuth code so frontend can establish session
        /// </summary>
        [HttpGet("callback")]
        [AllowAnonymous]
        public IActionResult OAuthCallback(
            [FromQuery] string? data = null,
            [FromQuery] string? code = null,
            [FromQuery] string? error = null,
            [FromQuery] string? error_description = null)
        {
            try
            {
                // Log callback attempt
                _logger.LogInformation("OAuth callback received. Error: {Error}", error ?? "None");

                // Determine redirect URL based on environment
                string redirectUrl = GetFrontendCallbackUrl();

                // If there's an error, redirect with error message
                if (!string.IsNullOrEmpty(error))
                {
                    var errorUrl = $"{redirectUrl}?error={error}&error_description={Uri.EscapeDataString(error_description ?? "Unknown error")}";
                    _logger.LogWarning("OAuth error: {Error} - {Description}", error, error_description);
                    return Redirect(errorUrl);
                }

                // If there's no code, something went wrong
                if (string.IsNullOrEmpty(code))
                {
                    var errorUrl = $"{redirectUrl}?error=missing_code&error_description=No authorization code received";
                    _logger.LogWarning("No authorization code received from OAuth provider");
                    return Redirect(errorUrl);
                }

                // Success - redirect to frontend with code
                // The frontend will call /api/auth/verify-code to complete the session setup
                var successUrl = $"{redirectUrl}?code={Uri.EscapeDataString(code)}";
                _logger.LogInformation("OAuth callback successful, redirecting to frontend with code");
                return Redirect(successUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OAuth callback");
                string redirectUrl = GetFrontendCallbackUrl();
                var errorUrl = $"{redirectUrl}?error=server_error&error_description={Uri.EscapeDataString(ex.Message)}";
                return Redirect(errorUrl);
            }
        }

        /// <summary>
        /// Retorna dados do usu�rio autenticado
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { error = "Usu�rio n�o autenticado" });
                }

                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

                var user = await _userService.GetOrCreateUserAsync(userId, email, name, null, null);

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    name = user.Name,
                    user = user.User_metadata,
                    avatar = user.Avatar,
                    credits = user.Credits,
                    plan = user.Plan,
                    createdAt = user.CreatedAt,
                    subscriptionEndsAt = user.SubscriptionEndsAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter dados do usu�rio");
                return StatusCode(500, new { error = "Erro ao obter dados" });
            }
        }

        /// <summary>
        /// Health check
        /// </summary>
        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "auth"
            });
        }

        /// <summary>
        /// Get the appropriate frontend callback URL based on environment
        /// </summary>
        private string GetFrontendCallbackUrl()
        {
            var isDevelopment = _config["ASPNETCORE_ENVIRONMENT"] == "Development";

            if (isDevelopment)
            {
                // Local development redirect to localhost:54336
                return "http://localhost:54336/auth/callback";
            }
            else
            {
                // Production redirect to Render
                return "https://pdf-ocr-frontend.onrender.com/auth/callback";
            }
        }
    }

    public class SyncUserRequest
    {
        public string AccessToken { get; set; } = string.Empty;
    }
}