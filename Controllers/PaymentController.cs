using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Services;
using StackExchange.Redis;
using Stripe;
using Stripe.Checkout;
using Stripe.V2.Core;
using System.Security.Claims;
using Event = Stripe.Event;

namespace pdf_ocr.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PaymentController : ControllerBase
{
    private readonly ILogger<PaymentController> _logger;
    private readonly IConfiguration _config;
    private readonly IUserService _userService;
    private readonly IStripePlansService _plansService;
    private readonly string _stripeSecretKey;
    private readonly string _webhookSecret;

    public PaymentController(
        ILogger<PaymentController> logger,
        IConfiguration config,
        IUserService userService,
        IStripePlansService plansService)
    {
        _logger = logger;
        _config = config;
        _userService = userService;
        _plansService = plansService;

        _stripeSecretKey = config["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey não configurada");
        _webhookSecret = config["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret não configurada");

        StripeConfiguration.ApiKey = _stripeSecretKey;
    }

    /// <summary>
    /// Retorna planos disponíveis dinamicamente do Stripe
    /// </summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<PlanDto>), 200)]
    public async Task<IActionResult> GetPlans()
    {
        try
        {
            var plans = await _plansService.GetPlansAsync();
            return Ok(plans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar planos");
            var msg = ApiMessages.PlansFetchFailed(HttpContext);
            return StatusCode(500, new pdf_ocr.Models.ErrorResponse
            {
                Error = msg.Error,
                Details = msg.Details
            });
        }
    }

    /// <summary>
    /// Cria sessão de checkout do Stripe
    /// </summary>
    [HttpPost("checkout")]
    [Authorize]
    [ProducesResponseType(typeof(CheckoutResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest req)
    {
        try
        {
            // Validar request
            if (string.IsNullOrWhiteSpace(req.PriceId))
            {
                var msg = ApiMessages.PriceIdRequired(HttpContext);
                return BadRequest(new pdf_ocr.Models.ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            // Obter usuário do token
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var msg = ApiMessages.InvalidToken(HttpContext);
                return Unauthorized(new pdf_ocr.Models.ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            var user = await _userService.GetUserAsync(userId);
            if (user == null)
            {
                var msg = ApiMessages.UserNotFound(HttpContext);
                return NotFound(new pdf_ocr.Models.ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            // Determinar URLs baseado no ambiente
            var isDev = _config.GetValue<bool>("IsDevelopment");
            var frontendUrl = _config["FrontendUrl"];

            // Criar sessão do Stripe
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = req.PriceId,
                        Quantity = 1
                    }
                },
                Mode = "subscription",
                SuccessUrl = $"{frontendUrl}/account?payment=success",
                CancelUrl = $"{frontendUrl}/plans?payment=cancelled",
                CustomerEmail = user.Email,
                ClientReferenceId = userId,
                Metadata = new Dictionary<string, string>
                {
                    { "userId", userId },
                    { "email", user.Email },
                    { "plan", req.PlanId }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation("Checkout criado: {SessionId} para usuário {UserId}",
                session.Id, userId);

            return Ok(new CheckoutResponse
            {
                SessionId = session.Id,
                Url = session.Url
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Erro no Stripe: {Message}", ex.Message);
            var msg = ApiMessages.StripeError(HttpContext, ex.Message);
            return BadRequest(new pdf_ocr.Models.ErrorResponse
            {
                Error = msg.Error,
                Details = msg.Details
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar checkout");
            var msg = ApiMessages.CheckoutCreateFailed(HttpContext);
            return StatusCode(500, new pdf_ocr.Models.ErrorResponse
            {
                Error = msg.Error,
                Details = msg.Details
            });
        }
    }

    /// <summary>
    /// Webhook do Stripe (eventos de pagamento)
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> StripeWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        try
        {
            var signature = Request.Headers["Stripe-Signature"];
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                _webhookSecret
            );

            _logger.LogInformation("Webhook recebido: {Type}", stripeEvent.Type);
            var subscription = stripeEvent.Data.Object as Subscription;
            // Processar eventos
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    var session = await this.RetrieveCheckoutSession((stripeEvent.Data.Object as Session).Id);
                    await HandleCheckoutCompleted(session);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(subscription);
                    break;
                case "customer.subscription.deleted":
                    await HandleSubscriptionCancelled(subscription);
                    break;
            }

            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Erro no webhook: {Message}", ex.Message);
            return BadRequest();
        }
    }

    async Task<Session> RetrieveCheckoutSession(string sessionId)
    {
        StripeConfiguration.ApiKey = _stripeSecretKey;

        var service = new SessionService();

        try
        {
            var options = new SessionGetOptions
            {
                Expand = new List<string> { "line_items" }
            };
            
            Session session = await service.GetAsync(sessionId, options);
            Console.WriteLine($"Successfully retrieved session: {session.Id}");
            return session;
        }
        catch (StripeException e)
        {
            Console.WriteLine($"Error retrieving session: {e.Message}");
            return null;
        }
    }

    private async Task HandleCheckoutCompleted(Session? session)
    {
        if (session?.ClientReferenceId == null) return;

        var userId = session.ClientReferenceId;
        var priceId = session.LineItems?.Data[0]?.Price?.Id;

        if (string.IsNullOrEmpty(priceId))
        {
            _logger.LogError("PriceId não encontrado no checkout");
            return;
        }

        // Buscar plano correspondente
        var plans = await _plansService.GetPlansAsync();
        var plan = plans.FirstOrDefault(p => p.PriceId == priceId);

        if (plan == null)
        {
            _logger.LogError("Plano não encontrado para priceId: {PriceId}", priceId);
            return;
        }

        // Atualizar usuário
        await _userService.UpdateUserPlanAsync(userId, plan.Name.ToLower(), plan.Credits);

        _logger.LogInformation(
                 "Assinatura ativada: {UserId} → {Plan} (+{Credits} créditos)",
                 userId, plan.Name, plan.Credits);
    }

    private async Task HandleSubscriptionUpdated(Subscription? subscription)
    {
        if (subscription?.CustomerId == null) return;

        // Lógica de atualização de assinatura
        _logger.LogInformation("Assinatura atualizada: {SubId}", subscription.Id);
        await Task.CompletedTask;
    }

    private async Task HandleSubscriptionCancelled(Subscription? subscription)
    {
        if (subscription?.CustomerId == null) return;

        // Lógica de cancelamento de assinatura
        // TODO: Implementar lookup de customer_id → user_id
        _logger.LogInformation("Assinatura cancelada: {SubId}", subscription.Id);
        await Task.CompletedTask;
    }

    private string GetUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException();
    }
}

// ============================================
// DTOs
// ============================================

public class CheckoutRequest
{
    public string PlanId { get; set; } = "";
    public string PriceId { get; set; } = "";
    public string SuccessUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";
}

public class CheckoutResponse
{
    public string SessionId { get; set; } = "";
    public string Url { get; set; } = "";
}