namespace pdf_ocr.Services;

using pdf_ocr.Middleware;
using pdf_ocr.Models;

public static class ApiMessages
{
    private static bool IsPt(HttpContext ctx) => ApiLanguage.Current(ctx) == ApiLanguage.Pt;

    public static string JobProgressMessage(HttpContext ctx, JobProgressInfo? progress, string? status = null)
    {
        if (progress == null)
        {
            return StatusFallback(ctx, status);
        }

        var stage = (progress.Stage ?? string.Empty).Trim().ToLowerInvariant();

        switch (stage)
        {
            case "queued":
                return IsPt(ctx)
                    ? "Job criado. Aguardando processamento..."
                    : "Job created. Waiting for processing...";
            case "starting":
                return IsPt(ctx)
                    ? "Iniciando processamento..."
                    : "Starting processing...";
            case "pipeline":
                return IsPt(ctx)
                    ? "Preparando pipeline..."
                    : "Preparing pipeline...";
            case "vector-clean":
                return IsPt(ctx)
                    ? "Removendo texto selecionável original..."
                    : "Removing original selectable text...";
            case "forms-base":
                return IsPt(ctx)
                    ? "Preparando PDF para OCR (preservando formulários)..."
                    : "Preparing PDF for OCR (preserving forms)...";
            case "render":
                return IsPt(ctx)
                    ? RenderCountMessage("Renderizando páginas para imagem", progress.ProcessedPages, progress.TotalPages)
                    : RenderCountMessage("Rendering pages to images", progress.ProcessedPages, progress.TotalPages);
            case "ocr":
                return IsPt(ctx)
                    ? RenderCountMessage("Executando OCR", progress.ProcessedPages, progress.TotalPages)
                    : RenderCountMessage("Running OCR", progress.ProcessedPages, progress.TotalPages);
            case "merge":
                return IsPt(ctx)
                    ? MergeMessage(progress.ProcessedPages, progress.TotalPages)
                    : MergeMessage(progress.ProcessedPages, progress.TotalPages, english: true);
            case "completed":
                return IsPt(ctx)
                    ? "Concluído. Seu PDF está pronto para download."
                    : "Done. Your PDF is ready for download.";
            case "failed":
                return IsPt(ctx)
                    ? "Falha no processamento."
                    : "Processing failed.";
            case "cancelled":
                return IsPt(ctx)
                    ? "Cancelado pelo usuário."
                    : "Cancelled by the user.";
            default:
                // Fallback to the stored message for unknown/custom stages
                if (!string.IsNullOrWhiteSpace(progress.Message))
                {
                    return progress.Message;
                }

                return StatusFallback(ctx, status);
        }
    }

    private static string RenderCountMessage(string baseText, int? processed, int? total)
    {
        if (processed.HasValue && total.HasValue && total.Value > 0)
        {
            return $"{baseText}...";
        }

        return $"{baseText}...";
    }

    private static string MergeMessage(int? currentPage, int? totalPages, bool english = false)
    {
        if (currentPage.HasValue && totalPages.HasValue && totalPages.Value > 0)
        {
            return english
                ? $"Merging page {currentPage.Value} of {totalPages.Value}..."
                : $"Mesclando página {currentPage.Value} de {totalPages.Value}...";
        }

        return english ? "Merging pages..." : "Mesclando páginas...";
    }

    private static string StatusFallback(HttpContext ctx, string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "queued" => IsPt(ctx) ? "Aguardando processamento..." : "Queued for processing...",
            "processing" => IsPt(ctx) ? "Processando..." : "Processing...",
            "completed" => IsPt(ctx) ? "Concluído." : "Completed.",
            "failed" => IsPt(ctx) ? "Falha no processamento." : "Processing failed.",
            "cancelled" => IsPt(ctx) ? "Cancelado pelo usuário." : "Cancelled by the user.",
            _ => IsPt(ctx) ? "Processando..." : "Processing..."
        };
    }

    public static (string Error, string Details) AccessTokenRequired(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("AccessToken é obrigatório", "Envie o campo 'accessToken' no body")
            : ("AccessToken is required", "Send the 'accessToken' field in the request body");
    }

    public static (string Error, string Details) InvalidToken(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Token inválido", "O token fornecido é inválido ou está incompleto")
            : ("Invalid token", "The provided token is invalid or incomplete");
    }

    public static (string Error, string Details) AuthProcessingFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao processar autenticação", "Não foi possível sincronizar o usuário")
            : ("Authentication processing failed", "Unable to sync user");
    }

    public static (string Error, string Details) UserNotAuthenticated(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Usuário não autenticado", "Faça login e tente novamente")
            : ("User not authenticated", "Please sign in and try again");
    }

    public static (string Error, string Details) GetUserDataFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao obter dados", "Não foi possível obter os dados do usuário")
            : ("Failed to fetch user data", "Unable to retrieve user data");
    }

    public static (string Error, string Details) InternalServerError(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro interno do servidor", "Ocorreu um erro inesperado")
            : ("Internal server error", "An unexpected error occurred");
    }

    public static (string Error, string Details) UpdateProfileFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao atualizar perfil", "Não foi possível atualizar o perfil")
            : ("Failed to update profile", "Unable to update the profile");
    }

    public static (string Error, string Details) GetCreditsFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao obter créditos", "Não foi possível obter o saldo de créditos")
            : ("Failed to fetch credits", "Unable to retrieve credit balance");
    }

    public static (string Error, string Details) GetUsageFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao obter uso", "Não foi possível obter o histórico de uso")
            : ("Failed to fetch usage", "Unable to retrieve usage history");
    }

    public static (string Error, string Details, string? UpgradeUrl) DemoFileTooLarge(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Demo limitado a 1MB", "Crie uma conta gratuita para processar PDFs maiores", "/plans")
            : ("Demo limited to 1MB", "Create a free account to process larger PDFs", "/plans");
    }

    public static (string Error, string Details, string? UpgradeUrl) DemoLimitExceeded(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Limite da demo excedido", "Você atingiu o limite de demonstração. Crie uma conta ou assine um plano para continuar.", "/plans")
            : ("Demo limit exceeded", "You have reached the demo limit. Create an account or purchase a plan to continue.", "/plans");
    }

    public static (string Error, string Details) JobNotFound(HttpContext ctx, string jobId)
    {
        return IsPt(ctx)
            ? ("Job não encontrado", $"Nenhum job foi encontrado com o ID: {jobId}")
            : ("Job not found", $"No job was found with ID: {jobId}");
    }

    public static (string Error, string Details) JobNotCompleted(HttpContext ctx, string status)
    {
        return IsPt(ctx)
            ? ("Job ainda não concluído", $"Status atual: {status}. Aguarde a conclusão do processamento.")
            : ("Job not completed yet", $"Current status: {status}. Please wait for processing to complete.");
    }

    public static (string Error, string Details) JobOutputMissing(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Arquivo processado não encontrado", "O arquivo pode ter sido removido por limpeza automática")
            : ("Processed file not found", "The file may have been removed by automatic cleanup");
    }

    public static (string Error, string Details) DownloadFailed(HttpContext ctx, string details)
    {
        return IsPt(ctx)
            ? ("Erro ao processar download", details)
            : ("Failed to process download", details);
    }

    public static (string Message, string UpgradeMessage) DemoQueued(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Demo - Processamento iniciado", "Crie uma conta para mais recursos")
            : ("Demo - Processing started", "Create an account for more features");
    }

    public static (string Error, string Details) UserNotFound(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Usuário não encontrado", "Nenhum usuário foi encontrado para esta requisição")
            : ("User not found", "No user was found for this request");
    }

    public static (string Error, string Details) ApiKeyNotFound(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Chave não encontrada", "Nenhuma chave de API foi encontrada")
            : ("API key not found", "No API key was found");
    }

    public static (string Error, string Details) ApiKeyNameRequired(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Nome da chave é obrigatório", "Informe um nome para a chave")
            : ("Key name is required", "Provide a name for the API key");
    }

    public static (string Error, string Details) ApiKeysCreateFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao criar chave", "Não foi possível criar a chave de API")
            : ("Failed to create API key", "Unable to create API key");
    }

    public static (string Error, string Details) ApiKeysListFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao listar chaves", "Não foi possível listar as chaves de API")
            : ("Failed to list API keys", "Unable to list API keys");
    }

    public static (string Error, string Details) ApiKeyRevokeFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao revogar chave", "Não foi possível revogar a chave de API")
            : ("Failed to revoke API key", "Unable to revoke API key");
    }

    public static (string Error, string Details) ApiKeyInvalidOrExpired(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Chave inválida ou expirada", "Verifique a chave e tente novamente")
            : ("API key invalid or expired", "Check the key and try again");
    }

    public static (string Error, string Details) ApiKeyValidateFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao validar", "Não foi possível validar a chave de API")
            : ("Validation failed", "Unable to validate API key");
    }

    public static (string Error, string Details) PlansFetchFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao buscar planos", "Não foi possível buscar os planos")
            : ("Failed to fetch plans", "Unable to fetch plans");
    }

    public static (string Error, string Details) PriceIdRequired(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("PriceId é obrigatório", "Informe o PriceId do plano")
            : ("PriceId is required", "Provide the plan PriceId");
    }

    public static (string Error, string Details) CheckoutCreateFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao criar checkout", "Não foi possível iniciar o checkout")
            : ("Failed to create checkout", "Unable to start checkout");
    }

    public static (string Error, string Details) StripeError(HttpContext ctx, string details)
    {
        return IsPt(ctx)
            ? ("Erro no Stripe", details)
            : ("Stripe error", details);
    }

    public static (string Error, string Details) SubscriptionNotFound(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Assinatura não encontrada", "Nenhuma assinatura ativa foi encontrada para este usuário")
            : ("Subscription not found", "No active subscription was found for this user");
    }

    public static (string Error, string Details) SubscriptionCancelFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao cancelar assinatura", "Não foi possível cancelar a assinatura")
            : ("Failed to cancel subscription", "Unable to cancel the subscription");
    }

    public static (string Error, string Details) JobsFetchFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao buscar jobs", "Não foi possível buscar jobs")
            : ("Failed to fetch jobs", "Unable to fetch jobs");
    }

    public static string CleanupRemovedMessage(HttpContext ctx, int removed, int hoursOld)
    {
        return IsPt(ctx)
            ? $"Removidos {removed} job(s) com mais de {hoursOld} hora(s)"
            : $"Removed {removed} job(s) older than {hoursOld} hour(s)";
    }

    public static (string Error, string Details) JobResumeNotAllowed(HttpContext ctx, string status)
    {
        return IsPt(ctx)
            ? ("Job não pode ser retomado", $"Status atual: {status}")
            : ("Job cannot be resumed", $"Current status: {status}");
    }

    public static (string Error, string Details) JobResumeFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao retomar job", "Não foi possível retomar o job")
            : ("Failed to resume job", "Unable to resume job");
    }

    public static (string Message, string Status) JobMarkedForReprocessing(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Job marcado para reprocessamento", "pending")
            : ("Job marked for reprocessing", "pending");
    }

    public static (string Error, string Details) JobAlreadyFinalized(HttpContext ctx, string status)
    {
        return IsPt(ctx)
            ? ("Job já finalizado", $"Status: {status}")
            : ("Job already finished", $"Status: {status}");
    }

    public static (string Message, string Status) JobCanceled(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Job cancelado", "failed")
            : ("Job canceled", "failed");
    }

    public static (string Error, string Details) JobCancelFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao cancelar job", "Não foi possível cancelar o job")
            : ("Failed to cancel job", "Unable to cancel the job");
    }

    public static (string Error, string Details) TesseractUnavailable(HttpContext ctx, string details)
    {
        return IsPt(ctx)
            ? ("Tesseract não encontrado ou falha no sistema nativo.", details)
            : ("Tesseract not found or native system failure.", details);
    }

    public static (string Error, string Details, string? UpgradeUrl) InsufficientCredits(HttpContext ctx, int cost, int credits)
    {
        return IsPt(ctx)
            ? ("Créditos insuficientes", $"Você precisa de {cost} crédito(s). Saldo: {credits}", "/pricing")
            : ("Insufficient credits", $"You need {cost} credit(s). Balance: {credits}", "/pricing");
    }

    public static (string Error, string Details) DeductCreditsFailed(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Erro ao deduzir créditos", "Não foi possível deduzir créditos do usuário")
            : ("Failed to deduct credits", "Unable to deduct credits from the user");
    }

    public static string PdfQueued(HttpContext ctx)
    {
        return IsPt(ctx)
            ? "PDF recebido e aguardando processamento"
            : "PDF received and queued for processing";
    }

    public static (string Error, string Details) CreateJobFailed(HttpContext ctx, string details)
    {
        return IsPt(ctx)
            ? ("Erro ao criar job de processamento", details)
            : ("Failed to create processing job", details);
    }

    public static (string Error, string Details) OcrProcessingFailed(HttpContext ctx, string details)
    {
        return IsPt(ctx)
            ? ("Erro no processamento OCR", details)
            : ("OCR processing failed", details);
    }

    public static (string Error, string Details) NoFileProvided(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Nenhum arquivo enviado", "É necessário enviar um arquivo PDF")
            : ("No file provided", "You must upload a PDF file");
    }

    public static (string Error, string Details) FileTooLarge(HttpContext ctx, int maxMb)
    {
        return IsPt(ctx)
            ? ("Arquivo muito grande", $"O tamanho máximo permitido é {maxMb}MB")
            : ("File too large", $"The maximum allowed size is {maxMb}MB");
    }

    public static (string Error, string Details) InvalidFileType(HttpContext ctx)
    {
        return IsPt(ctx)
            ? ("Tipo de arquivo inválido", "Apenas arquivos PDF são aceitos")
            : ("Invalid file type", "Only PDF files are accepted");
    }
}
