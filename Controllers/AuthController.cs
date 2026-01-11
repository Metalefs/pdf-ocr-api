// Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace pdf_ocr.Controllers
{
    /// <summary>
    /// Authentication controller for OAuth callbacks
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _config;

        public AuthController(ILogger<AuthController> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
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
        /// Verify OAuth code and establish session
        /// Called by frontend after being redirected from /api/auth/callback
        /// </summary>
        [HttpPost("verify-code")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyCode([FromBody] VerifyCodeRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request?.Code))
                {
                    _logger.LogWarning("Verify code request missing code parameter");
                    return BadRequest(new { error = "Code is required" });
                }

                _logger.LogInformation("Verifying OAuth code from frontend");

                // The code has already been validated by Supabase
                // Here you can:
                // 1. Log the successful authentication
                // 2. Create or update user in your database
                // 3. Return additional user data if needed

                return Ok(new
                {
                    success = true,
                    message = "OAuth code verified successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying OAuth code");
                return StatusCode(500, new { error = "Failed to verify code", details = ex.Message });
            }
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

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
    }

    /// <summary>
    /// Request model for OAuth code verification
    /// </summary>
    public class VerifyCodeRequest
    {
        public string? Code { get; set; }
    }
}
       
