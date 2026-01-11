// Controllers/PaymentController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Middleware;
using pdf_ocr.Models;
using pdf_ocr.Services;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace pdf_ocr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _config;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IUserService userService,
            IConfiguration config,
            ILogger<PaymentController> logger)
        {
            _userService = userService;
            _config = config;
            _logger = logger;

            StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
        }

        /// <summary>
        /// Planos disponíveis
        /// </summary>
        [HttpGet("plans")]
        [AllowAnonymous]
        public IActionResult GetPlans()
        {
            var plans = new[]
            {
                new PlanDto
                {
                    Id = "free",
                    Name = "Free",
                    Price = 0,
                    Credits = 10,
                    Features = new[]
                    {
                        "10 créditos/mês",
                        "PDFs até 5MB",
                        "Processamento padrão"
                    }
                },
                new PlanDto
                {
                    Id = "pro",
                    Name = "Pro",
                    Price = 19,
                    Credits = 100,
                    PriceId = "price_1SoLFZFKr62FCO6S5CiQOHbH", // Criar no Stripe Dashboard
                    Features = new[]
                    {
                        "100 créditos/mês",
                        "PDFs até 20MB",
                        "Processamento prioritário",
                        "Suporte por email"
                    }
                },
                new PlanDto
                {
                    Id = "business",
                    Name = "Business",
                    Price = 49,
                    Credits = 500,
                    PriceId = "price_1SoLGGFKr62FCO6Sw7RQ3h1C",
                    Features = new[]
                    {
                        "500 créditos/mês",
                        "PDFs ilimitados",
                        "API access",
                        "Suporte dedicado"
                    }
                }
            };
            return Ok(plans);
        }

        /// <summary>
        /// Criar sessão de checkout Stripe
        /// </summary>
        [HttpPost("checkout")]
        public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
        {
            var userId = GetUserId();
            var user = await _userService.GetOrCreateUserAsync(userId, "", "", null);

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Price = request.PriceId,
                        Quantity = 1,
                    }
                },
                Mode = "subscription",
                SuccessUrl = $"{request.SuccessUrl}?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = request.CancelUrl,
                ClientReferenceId = userId,
                CustomerEmail = user?.Email,
                Metadata = new Dictionary<string, string>
                {
                    { "user_id", userId },
                    { "plan", request.PlanId }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation("Checkout criado: {SessionId} para {UserId}", session.Id, userId);

            return Ok(new { sessionId = session.Id, url = session.Url });
        }

        /// <summary>
        /// Webhook Stripe - processa eventos de pagamento
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var stripeSignature = Request.Headers["Stripe-Signature"];

            try
            {
                var webhookSecret = _config["Stripe:WebhookSecret"];
                var stripeEvent = EventUtility.ConstructEvent(
                    json, stripeSignature, webhookSecret);

                _logger.LogInformation("Webhook recebido: {Type}", stripeEvent.Type);

                // Processar eventos
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        await HandleCheckoutCompleted(stripeEvent);
                        break;

                    case "customer.subscription.updated":
                    case "customer.subscription.deleted":
                        await HandleSubscriptionChange(stripeEvent);
                        break;
                }

                return Ok();
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Erro no webhook Stripe");
                return BadRequest();
            }
        }

        private async Task HandleCheckoutCompleted(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Session;
            if (session == null) return;

            var userId = session.ClientReferenceId;
            var plan = session.Metadata["plan"];

            // Atualizar plano do usuário
            var expiresAt = DateTime.UtcNow.AddMonths(1);
            await _userService.UpdatePlanAsync(userId, plan, expiresAt);

            // Adicionar créditos
            var credits = plan switch
            {
                "pro" => 100,
                "business" => 500,
                _ => 10
            };
            await _userService.AddCreditsAsync(userId, credits);

            _logger.LogInformation(
                "Assinatura ativada: {UserId} → {Plan} (+{Credits} créditos)",
                userId, plan, credits);
        }

        private async Task HandleSubscriptionChange(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            if (subscription == null) return;

            // Buscar usuário pelo customer ID
            // TODO: Implementar lookup de customer_id → user_id

            _logger.LogInformation("Assinatura atualizada: {Status}", subscription.Status);
        }

        private string GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new UnauthorizedAccessException();
        }
    }

    public class CheckoutRequest
    {
        public string PlanId { get; set; } = "";
        public string PriceId { get; set; } = "";
        public string SuccessUrl { get; set; } = "";
        public string CancelUrl { get; set; } = "";
    }
}