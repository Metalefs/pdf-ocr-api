using Stripe;

namespace pdf_ocr.Services;

public interface IStripePlansService
{
    Task<List<PlanDto>> GetPlansAsync();
}

public class StripePlansService : IStripePlansService
{
    private readonly ILogger<StripePlansService> _logger;
    private readonly string _stripeSecretKey;

    public StripePlansService(IConfiguration config, ILogger<StripePlansService> logger)
    {
        _logger = logger;
        _stripeSecretKey = config["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey não configurada");

        StripeConfiguration.ApiKey = _stripeSecretKey;
    }

    public async Task<List<PlanDto>> GetPlansAsync()
    {
        try
        {
            var productService = new ProductService();
            var priceService = new PriceService();

            // Buscar produtos ativos
            var products = await productService.ListAsync(new ProductListOptions
            {
                Active = true,
                Expand = new List<string> { "data.default_price" }
            });

            var plans = new List<PlanDto>();

            foreach (var product in products)
            {
                // Buscar preços do produto
                var prices = await priceService.ListAsync(new PriceListOptions
                {
                    Product = product.Id,
                    Active = true
                });

                var price = prices.FirstOrDefault();
                if (price == null) continue;

                // Normalizar plano
                plans.Add(new PlanDto
                {
                    Id = product.Id,
                    Name = product.Name,
                    Description = product.Description ?? "",
                    PriceId = price.Id,
                    Price = price.UnitAmount.HasValue ? price.UnitAmount.Value / 100m : 0,
                    Currency = price.Currency?.ToUpper() ?? "USD",
                    Interval = price.Recurring?.Interval ?? "month",
                    Credits = ExtractCredits(product),
                    Features = ExtractFeatures(product),
                    Popular = IsPopular(product),
                    Order = GetOrder(product)
                });
            }

            // Ordenar planos
            return plans.OrderBy(p => p.Order).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao buscar planos do Stripe");
            return GetFallbackPlans();
        }
    }

    private static int ExtractCredits(Product product)
    {
        // Buscar em metadata primeiro
        if (product.Metadata.TryGetValue("credits", out var creditsStr)
            && int.TryParse(creditsStr, out var credits))
        {
            return credits;
        }

        // Fallback: tentar extrair do nome
        var name = product.Name.ToLower();
        if (name.Contains("free")) return 10;
        if (name.Contains("pro")) return 100;
        if (name.Contains("business")) return 500;

        return 0;
    }

    private static PlanFeatures ExtractFeatures(Product product)
    {
        var features = new PlanFeatures();

        // Buscar features em metadata
        if (product.Metadata.TryGetValue("basicProcessing", out var basicProcessing))
            features.BasicProcessing = bool.Parse(basicProcessing);

        if (product.Metadata.TryGetValue("priorityProcessing", out var priorityProcessing))
            features.PriorityProcessing = bool.Parse(priorityProcessing);

        if (product.Metadata.TryGetValue("maxProcessing", out var maxProcessing))
            features.MaxProcessing = bool.Parse(maxProcessing);

        if (product.Metadata.TryGetValue("emailSupport", out var emailSupport))
            features.EmailSupport = bool.Parse(emailSupport);

        if (product.Metadata.TryGetValue("prioritySupport", out var prioritySupport))
            features.PrioritySupport = bool.Parse(prioritySupport);

        if (product.Metadata.TryGetValue("support24x7", out var support24x7))
            features.Support24x7 = bool.Parse(support24x7);

        if (product.Metadata.TryGetValue("apiAccess", out var apiAccess))
            features.ApiAccess = bool.Parse(apiAccess);

        if (product.Metadata.TryGetValue("unlimitedApi", out var unlimitedApi))
            features.UnlimitedApi = bool.Parse(unlimitedApi);

        if (product.Metadata.TryGetValue("webhooks", out var webhooks))
            features.Webhooks = bool.Parse(webhooks);

        if (product.Metadata.TryGetValue("advancedDashboard", out var advancedDashboard))
            features.AdvancedDashboard = bool.Parse(advancedDashboard);

        if (product.Metadata.TryGetValue("customReports", out var customReports))
            features.CustomReports = bool.Parse(customReports);

        // Fallback: features padrão baseado no plano
        if (!features.BasicProcessing && !features.PriorityProcessing && !features.MaxProcessing)
        {
            var name = product.Name.ToLower();
            if (name.Contains("free"))
            {
                features.BasicProcessing = true;
                features.EmailSupport = true;
            }
            else if (name.Contains("pro"))
            {
                features.PriorityProcessing = true;
                features.PrioritySupport = true;
                features.ApiAccess = true;
            }
            else if (name.Contains("business"))
            {
                features.MaxProcessing = true;
                features.Support24x7 = true;
                features.UnlimitedApi = true;
                features.Webhooks = true;
            }
        }

        return features;
    }

    private static bool IsPopular(Product product)
    {
        // Verificar metadata
        if (product.Metadata.TryGetValue("popular", out var popularStr))
        {
            return bool.TryParse(popularStr, out var popular) && popular;
        }

        // Fallback: Pro é popular
        return product.Name.ToLower().Contains("pro");
    }

    private static int GetOrder(Product product)
    {
        // Verificar metadata
        if (product.Metadata.TryGetValue("order", out var orderStr)
            && int.TryParse(orderStr, out var order))
        {
            return order;
        }

        // Fallback: ordenar por nome
        var name = product.Name.ToLower();
        if (name.Contains("free")) return 1;
        if (name.Contains("pro")) return 2;
        if (name.Contains("business")) return 3;

        return 99;
    }

    private List<PlanDto> GetFallbackPlans()
    {
        _logger.LogWarning("Usando planos fallback");

        return new List<PlanDto>
        {
            new PlanDto
            {
                Id = "free",
                Name = "Free",
                Description = "Para uso pessoal",
                PriceId = "price_free",
                Price = 0,
                Currency = "USD",
                Interval = "month",
                Credits = 10,
                Features = new PlanFeatures
                {
                    BasicProcessing = true,
                    EmailSupport = true
                },
                Popular = false,
                Order = 1
            },
            new PlanDto
            {
                Id = "pro",
                Name = "Pro",
                Description = "Para profissionais",
                PriceId = "price_pro",
                Price = 19,
                Currency = "USD",
                Interval = "month",
                Credits = 100,
                Features = new PlanFeatures
                {
                    PriorityProcessing = true,
                    PrioritySupport = true,
                    ApiAccess = true
                },
                Popular = true,
                Order = 2
            },
            new PlanDto
            {
                Id = "business",
                Name = "Business",
                Description = "Para empresas",
                PriceId = "price_business",
                Price = 49,
                Currency = "USD",
                Interval = "month",
                Credits = 500,
                Features = new PlanFeatures
                {
                    MaxProcessing = true,
                    Support24x7 = true,
                    UnlimitedApi = true,
                    Webhooks = true
                },
                Popular = false,
                Order = 3
            }
        };
    }
}
public class PlanDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string PriceId { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string Interval { get; set; } = "month";
    public int Credits { get; set; }
    public PlanFeatures Features { get; set; } = new();
    public bool Popular { get; set; }
    public int Order { get; set; }
}

public class PlanFeatures
{
    public bool BasicProcessing { get; set; }
    public bool PriorityProcessing { get; set; }
    public bool MaxProcessing { get; set; }
    public bool EmailSupport { get; set; }
    public bool PrioritySupport { get; set; }
    public bool Support24x7 { get; set; }
    public bool ApiAccess { get; set; }
    public bool UnlimitedApi { get; set; }
    public bool Webhooks { get; set; }
    public bool AdvancedDashboard { get; set; }
    public bool CustomReports { get; set; }
}