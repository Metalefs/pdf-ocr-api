using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using pdf_ocr.Middleware;
using pdf_ocr.Models;
using pdf_ocr.Services;
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
                .WithOrigins("https://pdf-ocr-frontend.onrender.com")
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

// Add custom services
builder.Services.AddSingleton<IJobService, JobService>();
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Configura��o Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PDF OCR API",
        Version = "v1.0",
        Description = "SaaS de OCR para PDFs com preserva��o de formul�rios"
    });
    // Adicionar autentica��o JWT no Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using Bearer scheme",
        Name = "Authorization",
        Scheme = "bearer",
        BearerFormat = "JWT",
        Type = SecuritySchemeType.Http
    });

    // Incluir coment�rios XML na documenta��o
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Configurar suporte para upload de arquivos
    options.OperationFilter<FileUploadOperationFilter>();
});

// Health Checks
builder.Services.AddHealthChecks();

// ============================================
// SERVI�OS CUSTOMIZADOS
// ============================================

builder.Services.AddSingleton<IJobService, JobService>();
builder.Services.AddSingleton<IUserService, UserService>();
builder.Services.AddSupabaseAuth(builder.Configuration);

// Configura��o de logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();


var app = builder.Build();

// ========================================
// CONFIGURA��O DO PIPELINE
// ========================================

// Swagger
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PDF OCR API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "PDF OCR API - Documentação";
});

// CORS - Use AllowFrontend policy for OAuth callback and API requests
app.UseCors("AllowFrontend");

// Important: Correct order for authentication/authorization middleware
app.UseAuthentication();

// Roteamento (must come before UseAuthorization)
app.UseRouting();

// Authorization (must come after UseRouting)
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
    Service = "PDF OCR API",
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
// LIMPEZA AUTOM�TICA (Background)
// ============================================

var cleanupTimer = new System.Threading.Timer(async _ =>
{
    var jobService = app.Services.GetRequiredService<IJobService>();
    await jobService.CleanupOldJobsAsync(24); // Limpar jobs > 24h
}, null, TimeSpan.Zero, TimeSpan.FromHours(6));

// Informa��es da API
app.MapGet("/api/info", () => new
{
    service = "PDF OCR API",
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
// INICIALIZA��O
// ========================================

// Log de inicializa��o
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=".PadRight(60, '='));
logger.LogInformation("PDF OCR API - Iniciando");
logger.LogInformation("=".PadRight(60, '='));
logger.LogInformation("Ambiente: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("Swagger: /swagger");
logger.LogInformation("=".PadRight(60, '='));

app.Run();

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