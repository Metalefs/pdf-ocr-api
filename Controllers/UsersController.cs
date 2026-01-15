// Controllers/UsersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;
using System.Security.Claims;

namespace pdf_ocr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IUserService userService, ILogger<UsersController> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Obtém perfil do usuário autenticado
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    var unauth = ApiMessages.UserNotAuthenticated(HttpContext);
                    return Unauthorized(new ErrorResponse { Error = unauth.Error, Details = unauth.Details });
                }
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

                var user = await _userService.GetOrCreateUserAsync(userId, email, name, null, null);

                if (user == null)
                {
                    var msg = ApiMessages.UserNotFound(HttpContext);
                    return NotFound(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter perfil do usuário");
                var msg = ApiMessages.InternalServerError(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        /// <summary>
        /// Atualiza perfil do usuário
        /// </summary>
        [HttpPut("me")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    var unauth = ApiMessages.UserNotAuthenticated(HttpContext);
                    return Unauthorized(new ErrorResponse { Error = unauth.Error, Details = unauth.Details });
                }
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";

                // Buscar usuário atual
                var user = await _userService.UpdateUserAsync(userId, request.Name, request.Avatar);

                if (user == null)
                {
                    var msg = ApiMessages.UserNotFound(HttpContext);
                    return NotFound(new ErrorResponse
                    {
                        Error = msg.Error,
                        Details = msg.Details
                    });
                }

                // Retornar usuário atualizado
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar perfil");
                var msg = ApiMessages.UpdateProfileFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        /// <summary>
        /// Obtém saldo de créditos
        /// </summary>
        [HttpGet("credits")]
        public async Task<IActionResult> GetCredits()
        {
            try
            {
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    var unauth = ApiMessages.UserNotAuthenticated(HttpContext);
                    return Unauthorized(new ErrorResponse { Error = unauth.Error, Details = unauth.Details });
                }
                var credits = await _userService.GetCreditsAsync(userId);
                var plan = await GetUserPlan(userId);

                return Ok(new
                {
                    credits,
                    resetDate = GetNextResetDate(),
                    plan
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter créditos");
                var msg = ApiMessages.GetCreditsFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        /// <summary>
        /// Histórico de uso
        /// </summary>
        [HttpGet("usage")]
        public async Task<IActionResult> GetUsage()
        {
            try
            {
                var userId = GetUserIdOrNull();
                if (string.IsNullOrEmpty(userId))
                {
                    var unauth = ApiMessages.UserNotAuthenticated(HttpContext);
                    return Unauthorized(new ErrorResponse { Error = unauth.Error, Details = unauth.Details });
                }
                var plan = await GetUserPlan(userId);

                var usageStats = await _userService.GetUsageStatsAsync(userId);

                if (usageStats != null)
                {
                    return Ok(new
                    {
                        today = usageStats.Today,
                        week = usageStats.Week,
                        month = usageStats.Month,
                        limit = usageStats.LimitValue
                    });
                }

                // Fallback caso a consulta falhe
                return Ok(new
                {
                    today = 0,
                    week = 0,
                    month = 0,
                    limit = GetDailyLimit(plan)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter uso");
                var msg = ApiMessages.GetUsageFailed(HttpContext);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }

        private string? GetUserIdOrNull()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private async Task<string> GetUserPlan(string userId)
        {
            try
            {
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
                var user = await _userService.GetOrCreateUserAsync(userId, email, name, null, null);
                return user?.Plan ?? "free";
            }
            catch
            {
                return "free";
            }
        }

        private static int GetDailyLimit(string plan)
        {
            return plan switch
            {
                "free" => 3,
                "pro" => 50,
                "business" => 500,
                _ => 3
            };
        }

        private static DateTime GetNextResetDate()
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc)
                .AddDays(1);
        }
    }

    public class UpdateProfileRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Avatar { get; set; }
    }
}