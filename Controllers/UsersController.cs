// Controllers/UsersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
                var userId = GetUserId();
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

                var user = await _userService.GetOrCreateUserAsync(userId, email, name, null, null);

                if (user == null)
                {
                    return NotFound(new { error = "Usuário não encontrado" });
                }

                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter perfil do usuário");
                return StatusCode(500, new { error = "Erro interno do servidor" });
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
                var userId = GetUserId();
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";

                // Buscar usuário atual
                var user = await _userService.UpdateUserAsync(userId, request.Name, request.Avatar);

                if (user == null)
                {
                    return NotFound(new { error = "Usuário não encontrado" });
                }

                // Retornar usuário atualizado
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar perfil");
                return StatusCode(500, new { error = "Erro ao atualizar perfil" });
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
                var userId = GetUserId();
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
                return StatusCode(500, new { error = "Erro ao obter créditos" });
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
                var userId = GetUserId();
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
                return StatusCode(500, new { error = "Erro ao obter uso" });
            }
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException("Usuário não autenticado");
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