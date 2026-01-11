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
        [HttpPost("sync")]
        [AllowAnonymous]
        public IActionResult OAuthCallback(
            [FromBody] string? accessToken = null)
        {
            try
            {
                // Log callback attempt
                _logger.LogInformation("OAuth sync received.");

                
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling OAuth callback");
                return BadRequest(ex.Message);
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
       
