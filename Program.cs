using Microsoft.OpenApi;
using pdf_ocr.Models;
using pdf_ocr.Services;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
// Configuração CORS para permitir frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});
// Adicionar serviços customizados
builder.Services.AddSingleton<IJobService, JobService>();
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Configuração Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PDF OCR API",
        Version = "v1.0",
        Description = "API REST para processamento de PDFs com OCR preservando formulários",
        Contact = new OpenApiContact
        {
            Name = "Suporte",
            Email = "contato@exemplo.com"
        },
        License = new OpenApiLicense
        {
            Name = "MIT License",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });

    // Incluir comentários XML na documentação
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

// Configuração de logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// ========================================
// CONFIGURAÇÃO DO PIPELINE
// ========================================

// Swagger em todos os ambientes (útil para debug em produção)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PDF OCR API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "PDF OCR API - Documentação";
});

// CORS
app.UseCors();

// Roteamento
app.UseRouting();

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
    Dependencies = new Dictionary<string, string>
    {
        { ".NET", Environment.Version.ToString() },
        { "OS", Environment.OSVersion.ToString() }
    }
})
.WithName("HealthCheck")
.WithTags("Health")
.Produces<HealthResponse>(StatusCodes.Status200OK);

// Health Check detalhado
app.MapHealthChecks("/health");

// Informações da API
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
// INICIALIZAÇÃO
// ========================================

// Log de inicialização
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