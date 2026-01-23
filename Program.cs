using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using pdf_ocr.BackgroundServices;
using pdf_ocr.Middleware;
using pdf_ocr.Models;
using pdf_ocr.Services;
using StackExchange.Redis;
using Stripe;
using Supabase;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// CORS Configuration - Allow both local dev and production frontend URLs
builder.Services.AddCors(options =>
{
    var isDevelopment = builder.Environment.IsDevelopment();
    
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (isDevelopment)
        {
            // Local development - allow localhost on any port
            policy
                .WithOrigins("http://localhost:54336", "http://localhost:5173", "http://127.0.0.1:54336")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
        else
        {
            // Production - allow Render frontend URL
            policy
                .WithOrigins("https://textlayerocr.com", "https://textlayerocr.up.railway.app", "https://pdf-ocr-frontend.netlify.app")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    });
    
    // Legacy AllowAll policy (deprecated, use AllowFrontend instead)
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configuraï¿½ï¿½o Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TextLayer OCR API",
        Version = "v1.0",
        Description = "API "
    });

    // Hide admin-only endpoints from the public Swagger document.
    // Any action/controller decorated with [Authorize(Roles = "admin")] will be omitted.
    options.DocInclusionPredicate((_, apiDesc) =>
    {
        if (!apiDesc.TryGetMethodInfo(out var methodInfo))
            return true;

        if (HasAdminRoleRequirement(methodInfo))
            return false;

        var declaringType = methodInfo.DeclaringType;
        if (declaringType != null && HasAdminRoleRequirement(declaringType))
            return false;

        return true;
    });

    // Adicionar autenticaÃ§Ã£o JWT no Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using Bearer scheme",
        Name = "Authorization",
        Scheme = "bearer",
        BearerFormat = "JWT",
        Type = SecuritySchemeType.Http
    });

    // Incluir comentï¿½rios XML na documentaï¿½ï¿½o
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (System.IO.File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Configurar suporte para upload de arquivos
    options.OperationFilter<FileUploadOperationFilter>();
});

static bool HasAdminRoleRequirement(MemberInfo member)
{
    var authorizeAttributes = member
        .GetCustomAttributes(inherit: true)
        .OfType<AuthorizeAttribute>();

    foreach (var authorize in authorizeAttributes)
    {
        if (string.IsNullOrWhiteSpace(authorize.Roles))
            continue;

        var roles = authorize.Roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (roles.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)))
            return true;
    }

    return false;
}

// Health Checks
builder.Services.AddHealthChecks();

// ============================================
// SERVIÃ‡OS CUSTOMIZADOS
// ============================================
// ============================================
// CONFIGURAÇÃO DO SUPABASE CLIENT
// Adicionar ANTES do registro dos serviços
// ============================================


// Configurar e registrar o Supabase Client como singleton

var supabaseUrl = builder.Configuration["Supabase:Url"]?.TrimEnd('/');
var supabaseKey = builder.Configuration["Supabase:AnonKey"]
    ?? throw new InvalidOperationException("Supabase:AnonKey não configurado");

var options = new SupabaseOptions
{
    AutoConnectRealtime = false, // Desabilitar realtime se não usar
    AutoRefreshToken = true,
    SessionHandler = new DefaultSupabaseSessionHandler()
};

// Criar e registrar client como singleton
builder.Services.AddSingleton(provider =>
{
    var client = new Supabase.Client(supabaseUrl, supabaseKey, options);
    // Inicializar o client de forma síncrona
    client.InitializeAsync().GetAwaiter().GetResult();
    return client;
});
builder.Services.AddSingleton<IStripePlansService, StripePlansService>();
builder.Services.AddSingleton<IJobPersistenceService, SupabaseJobPersistenceService>();
builder.Services.AddSingleton<IJobService, HybridJobService>();
builder.Services.AddHostedService<JobRecoveryService>();
if (builder.Environment.IsDevelopment())
{
    //builder.Services.AddSingleton<IUserService, UserService>();
    builder.Services.AddSingleton<IUserService, SupabaseUserService>();
}
else
{
    // ProduÃ§Ã£o: usar Supabase
    builder.Services.AddSingleton<IUserService, SupabaseUserService>();
}
// ============================================
// AUTENTICAÇÃO: JWT + API Key
// ============================================

// Registrar serviço de API Keys
builder.Services.AddSingleton<IApiKeyService, ApiKeyService>();

// Registrar Redis (StackExchange.Redis) para rate-limiting de demo
try
{
    var redisConnection = builder.Configuration["Redis:Connection"];

    // Production platforms commonly provide REDIS_URL like:
    // - redis://:password@hostname:6379
    // - rediss://:password@hostname:6380
    if (string.IsNullOrWhiteSpace(redisConnection))
    {
        var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");
        if (!string.IsNullOrWhiteSpace(redisUrl))
        {
            try
            {
                if (redisUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
                    redisUrl.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(redisUrl);
                    var hostPort = $"{uri.Host}:{(uri.IsDefaultPort ? 6379 : uri.Port)}";

                    // Uri.UserInfo is typically ":password" or "user:password"
                    string? password = null;
                    if (!string.IsNullOrWhiteSpace(uri.UserInfo))
                    {
                        var userInfoParts = uri.UserInfo.Split(':', 2);
                        password = userInfoParts.Length == 2 ? userInfoParts[1] : userInfoParts[0];
                    }

                    var parts = new List<string> { hostPort };
                    if (!string.IsNullOrWhiteSpace(password))
                        parts.Add($"password={password}");
                    if (uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase))
                        parts.Add("ssl=true");

                    // Better behavior in container/cloud environments
                    parts.Add("abortConnect=false");

                    redisConnection = string.Join(",", parts);
                }
                else
                {
                    // If it's already in StackExchange.Redis format, just use it
                    redisConnection = redisUrl;
                }
            }
            catch
            {
                // Ignore parsing failures; fall back to defaults
            }
        }
    }

    redisConnection ??= "localhost:6379";

    // StackExchange.Redis expects a host:port (no URI scheme)
    if (redisConnection.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        redisConnection = redisConnection.Substring("http://".Length);
    else if (redisConnection.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        redisConnection = redisConnection.Substring("https://".Length);

    var multiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(multiplexer);
    Console.WriteLine($"✓ Redis conectado: {redisConnection}");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠ Redis não disponível (demo rate-limit desabilitado): {ex.Message}");
}

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services
    .AddAuthentication(options =>
    {
        // Esquema padrão: JWT Bearer
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var authority = supabaseUrl + "/auth/v1";
        options.Authority = authority;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true
        };
    })
    .AddApiKeySupport();

// Configurar política que aceita AMBOS os esquemas
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
        .RequireAuthenticatedUser()
        .Build();
});
// ConfiguraÃ§Ã£o de logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();


var app = builder.Build();

// ========================================
// CONFIGURAï¿½ï¿½O DO PIPELINE
// ========================================

// Swagger
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TextLayer OCR API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "TextLayer OCR API - Documentação";
});

// CORS - Use AllowFrontend policy for OAuth callback and API requests
app.UseCors("AllowFrontend");

// Important: Correct order for routing/authentication/authorization middleware
app.UseRouting();

// Language negotiation for API responses (Accept-Language / X-Language)
app.UseMiddleware<RequestLanguageMiddleware>();

app.UseAuthentication(); // Tenta JWT primeiro, depois API Key
app.UseAuthorization();

// Controllers
app.MapControllers();

// ========================================
// ENDPOINTS ADICIONAIS (Minimal API)
// ========================================

// Health Check na raiz
app.MapGet("/", () => new HealthResponse
{
    Status = "online",
    Service = "TextLayer OCR API",
    Version = "1.0.0",
    Timestamp = DateTime.UtcNow,
    Features = new List<string>
    {
        "OAuth Authentication (Supabase)",
        "Credit System",
        "Stripe Payments",
        "Rate Limiting"
    }
})
.WithName("HealthCheck")
.WithTags("Health")
.Produces<HealthResponse>(StatusCodes.Status200OK);

// Health Check detalhado
app.MapHealthChecks("/health");

// ============================================
// LIMPEZA AUTOMï¿½TICA (Background)
// ============================================

var cleanupTimer = new System.Threading.Timer(async _ =>
{
    var jobService = app.Services.GetRequiredService<IJobService>();
    await jobService.CleanupOldJobsAsync(24); // Limpar jobs > 24h
}, null, TimeSpan.Zero, TimeSpan.FromHours(6));

// Informaï¿½ï¿½es da API
app.MapGet("/api/info", () => new
{
    service = "TextLayer OCR API",
    version = "1.0.0",
    description = "API REST para processamento de PDFs com OCR",
    documentation = "/swagger",
    endpoints = new
    {
        processSync = "POST /api/pdf/process-sync",
        processAsync = "POST /api/pdf/process",
        jobStatus = "GET /api/jobs/{jobId}/status",
        jobDownload = "GET /api/jobs/{jobId}/download",
        jobsList = "GET /api/jobs",
        jobStats = "GET /api/jobs/stats",
        cleanup = "POST /api/jobs/cleanup"
    },
    limits = new
    {
        maxFileSize = "10 MB",
        supportedFormats = new[] { "PDF" }
    }
})
.WithName("ApiInfo")
.WithTags("Info")
.Produces(StatusCodes.Status200OK);

// ========================================
// INICIALIZAï¿½ï¿½O
// ========================================

// Log de inicializaï¿½ï¿½o
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=".PadRight(60, '='));
logger.LogInformation("TextLayer OCR API - Iniciando");
logger.LogInformation("=".PadRight(60, '='));
logger.LogInformation("Ambiente: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("Swagger: /swagger");
logger.LogInformation("=".PadRight(60, '='));

app.Run();

new System.Threading.Timer(async _ =>
{
    try
    {
        var jobService = app.Services.GetRequiredService<IJobService>();
        var count = await jobService.CleanupOldJobsAsync(24);
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Cleanup automático: {Count} jobs removidos", count);
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Erro no cleanup automático de jobs");
    }
}, null, TimeSpan.Zero, TimeSpan.FromHours(6));
// ========================================
// HELPERS
// ========================================

// ========================================
// OPERATION FILTER PARA SWAGGER
// ========================================

/// <summary>
/// Filtro para configurar upload de arquivos no Swagger
/// </summary>
public class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation,
        Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var fileParams = context.MethodInfo.GetParameters()
            .Where(p => p.ParameterType == typeof(IFormFile));

        if (fileParams.Any())
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["file"] = new OpenApiSchema
                                {
                                    Type = JsonSchemaType.String,
                                    Format = "binary"
                                }
                            },
                            Required = new HashSet<string> { "file" }
                        }
                    }
                }
            };
        }
    }
}