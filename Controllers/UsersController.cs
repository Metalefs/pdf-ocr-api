// Controllers/UsersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Middleware;
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
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var name = User.FindFirst(ClaimTypes.Name)?.Value ?? "";

            var user = await _userService.GetOrCreateUserAsync(userId, email, name, null);
            return Ok(user);
        }

        /// <summary>
        /// Obtém saldo de créditos
        /// </summary>
        [HttpGet("credits")]
        public async Task<IActionResult> GetCredits()
        {
            var userId = GetUserId();
            var credits = await _userService.GetCreditsAsync(userId);

            return Ok(new
            {
                credits,
                resetDate = GetNextResetDate(),
                plan = await GetUserPlan(userId)
            });
        }

        /// <summary>
        /// Histórico de uso (simplificado)
        /// </summary>
        [HttpGet("usage")]
        public IActionResult GetUsage()
        {
            // TODO: Implementar histórico real
            return Ok(new
            {
                today = 3,
                week = 15,
                month = 42,
                limit = GetDailyLimit()
            });
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException();
        }

        private async Task<string> GetUserPlan(string userId)
        {
            var user = await _userService.GetOrCreateUserAsync(userId, "", "", null);
            return user?.Plan ?? "free";
        }

        private int GetDailyLimit()
        {
            var plan = GetUserPlan(GetUserId()).Result;
            return plan switch
            {
                "free" => 3,
                "pro" => 50,
                "business" => 500,
                _ => 3
            };
        }

        private DateTime GetNextResetDate()
        {
            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc)
                .AddDays(1);
        }
    }

    // Atualizar PdfController para verificar créditos
    public static class CreditCheckAttribute
    {
        public static async Task<IActionResult?> CheckCredits(
            HttpContext context,
            IUserService userService,
            ILogger logger)
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return new UnauthorizedResult();

            // Custo: 1 crédito por PDF
            const int COST = 1;

            var credits = await userService.GetCreditsAsync(userId);
            if (credits < COST)
            {
                logger.LogWarning("Créditos insuficientes: {UserId} tem {Credits}", userId, credits);
                return new ObjectResult(new
                {
                    error = "Créditos insuficientes",
                    details = $"Você precisa de {COST} crédito(s). Saldo: {credits}",
                    upgradeUrl = "/pricing"
                })
                { StatusCode = 402 }; // Payment Required
            }

            // Deduzir créditos
            var success = await userService.DeductCreditsAsync(userId, COST);
            if (!success)
            {
                return new ObjectResult(new { error = "Erro ao deduzir créditos" })
                { StatusCode = 500 };
            }

            return null; // OK, pode processar
        }
    }
}