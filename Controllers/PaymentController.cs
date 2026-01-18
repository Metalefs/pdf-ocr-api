using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Services;
using StackExchange.Redis;
using Stripe;
using Stripe.Checkout;
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
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        { "userId", userId },
                        { "email", user.Email },
                        { "plan", req.PlanId }
                    }
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
    /// Cancela assinatura do Stripe (por padrão: cancelamento ao fim do período)
    /// </summary>
    [HttpPost("cancel")]
    [Authorize]
    [ProducesResponseType(typeof(CancelSubscriptionResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelSubscriptionRequest req)
    {
        try
        {
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

            if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
            {
                var msg = ApiMessages.SubscriptionNotFound(HttpContext);
                return Conflict(new pdf_ocr.Models.ErrorResponse
                {
                    Error = msg.Error,
                    Details = msg.Details
                });
            }

            StripeConfiguration.ApiKey = _stripeSecretKey;
            var subService = new SubscriptionService();

            Subscription subscription;
            if (req.Immediate)
            {
                subscription = await subService.CancelAsync(user.StripeSubscriptionId, new SubscriptionCancelOptions
                {
                    InvoiceNow = false,
                    Prorate = false
                });

                var freeCredits = await GetFreeCreditsAsync();
                await _userService.UpdateUserPlanAsync(userId, "free", freeCredits, null);
                await _userService.UpdatePlanAsync(userId, "free", null);
            }
            else
            {
                subscription = await subService.UpdateAsync(user.StripeSubscriptionId, new SubscriptionUpdateOptions
                {
                    CancelAtPeriodEnd = true
                });
                // Atualiza status para canceled mas mantém plano até o fim do período
                await _userService.UpdateSubscriptionStatusAsync(userId, "canceled");

                // Guardar data de fim do ciclo (para UI: "Renews" / "Ends")
                await _userService.UpdatePlanAsync(userId, user.Plan, GetSubscriptionPeriodEnd(subscription));
            }

            return Ok(new CancelSubscriptionResponse
            {
                SubscriptionId = subscription.Id,
                Status = subscription.Status,
                CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                CurrentPeriodEnd = GetSubscriptionPeriodEnd(subscription)
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Erro no Stripe ao cancelar assinatura: {Message}", ex.Message);
            var msg = ApiMessages.StripeError(HttpContext, ex.Message);
            return BadRequest(new pdf_ocr.Models.ErrorResponse
            {
                Error = msg.Error,
                Details = msg.Details
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao cancelar assinatura");
            var msg = ApiMessages.SubscriptionCancelFailed(HttpContext);
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
            var subscription = stripeEvent.Data.Object as Stripe.Subscription;
            // Processar eventos
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    var sessionObj = stripeEvent.Data.Object as Session;
                    if (sessionObj == null || string.IsNullOrWhiteSpace(sessionObj.Id)) break;
                    var session = await this.RetrieveCheckoutSession(sessionObj.Id);
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

    async Task<Session?> RetrieveCheckoutSession(string sessionId)
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

        // Atualizar usuário (persistir StripeSubscriptionId quando disponível)
        var subscriptionId = session.SubscriptionId;
        await _userService.UpdateUserPlanAsync(userId, plan.Name.ToLower(), plan.Credits, subscriptionId);

        // Salvar data de renovação/fim do período atual
        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            try
            {
                var subService = new SubscriptionService();
                var subscription = await subService.GetAsync(subscriptionId);
                await _userService.UpdateSubscriptionAsync(
                   userId,
                   plan.Name.ToLower(),
                   subscriptionId,
                   "active",
                   GetSubscriptionPeriodEnd(subscription));
                await _userService.UpdatePlanAsync(userId, plan.Name.ToLower(), GetSubscriptionPeriodEnd(subscription));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao buscar subscription para CurrentPeriodEnd: {SubId}", subscriptionId);
            }
        }

        _logger.LogInformation(
                 "Assinatura ativada: {UserId} → {Plan} (+{Credits} créditos)",
                 userId, plan.Name, plan.Credits);
    }

    private async Task HandleSubscriptionUpdated(Subscription? subscription)
    {
        if (subscription?.CustomerId == null) return;

        // Atualizar subscriptionEndsAt usando metadata (userId)
        if (subscription.Metadata != null && subscription.Metadata.TryGetValue("userId", out var userId)
            && !string.IsNullOrWhiteSpace(userId))
        {
            try
            {
                var user = await _userService.GetUserAsync(userId);
                var plan = user?.Plan ?? "free";
                string? status = subscription.Status switch
                {
                    "active" => "active",
                    "canceled" => "canceled",
                    "past_due" => "past_due",
                    "unpaid" => "unpaid",
                    "trialing" => "trialing",
                    _ => null
                };

                await _userService.UpdateSubscriptionAsync(
                    userId,
                    plan,
                    subscription.Id,
                    status,
                    GetSubscriptionPeriodEnd(subscription));

                await _userService.UpdatePlanAsync(userId, plan, GetSubscriptionPeriodEnd(subscription));
                _logger.LogInformation("Assinatura atualizada: {SubId} (User: {UserId})", subscription.Id, userId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar SubscriptionEndsAt via webhook (SubId: {SubId})", subscription.Id);
            }
        }

        _logger.LogInformation("Assinatura atualizada (sem metadata userId): {SubId}", subscription.Id);
    }

    private async Task HandleSubscriptionCancelled(Subscription? subscription)
    {
        if (subscription?.CustomerId == null) return;

        // Cancelamento definitivo (subscription.deleted)
        if (subscription.Metadata != null && subscription.Metadata.TryGetValue("userId", out var userId)
            && !string.IsNullOrWhiteSpace(userId))
        {
            try
            {
                var freeCredits = await GetFreeCreditsAsync();
                await _userService.UpdateSubscriptionAsync(
                userId,
                "free",
                null,
                "canceled",
                null);
                await _userService.UpdateUserPlanAsync(userId, "free", freeCredits, null);
                await _userService.UpdatePlanAsync(userId, "free", null);
                _logger.LogInformation("Assinatura cancelada: {SubId} (User: {UserId})", subscription.Id, userId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar cancelamento via webhook (SubId: {SubId})", subscription.Id);
            }
        }

        _logger.LogInformation("Assinatura cancelada (sem metadata userId): {SubId}", subscription.Id);
    }

    private async Task<int> GetFreeCreditsAsync()
    {
        try
        {
            var plans = await _plansService.GetPlansAsync();
            var free = plans.FirstOrDefault(p =>
                string.Equals(p.Id, "free", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Name, "free", StringComparison.OrdinalIgnoreCase));

            return free?.Credits ?? 2;
        }
        catch
        {
            return 2;
        }
    }

    private static DateTime? GetSubscriptionPeriodEnd(Stripe.Subscription subscription)
    {
        return subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
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

public class CancelSubscriptionRequest
{
    /// <summary>
    /// Se true: cancela imediatamente. Se false: cancela ao fim do período atual.
    /// </summary>
    public bool Immediate { get; set; } = false;
}

public class CancelSubscriptionResponse
{
    public string SubscriptionId { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
}