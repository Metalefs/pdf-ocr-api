using System.Globalization;

namespace pdf_ocr.Middleware;

public sealed class RequestLanguageMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLanguageMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var lang = DetermineLanguage(context);

        // Store for downstream usage (controllers/services)
        context.Items[ApiLanguage.ItemKey] = lang;

        // Inform clients what language was used
        context.Response.Headers["Content-Language"] = lang == ApiLanguage.Pt ? "pt-BR" : "en";

        await _next(context);
    }

    private static string DetermineLanguage(HttpContext context)
    {
        // Prefer standard header
        var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
        var raw = !string.IsNullOrWhiteSpace(acceptLanguage)
            ? acceptLanguage
            : (context.Request.Headers["X-Language"].ToString() ?? context.Request.Headers["X-Locale"].ToString());

        return ApiLanguage.Normalize(raw);
    }
}

public static class ApiLanguage
{
    public const string ItemKey = "api.lang";
    public const string En = "en";
    public const string Pt = "pt";

    public static string Normalize(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return En;

        // Accept-Language example: "pt-BR,pt;q=0.9,en;q=0.8"
        var first = headerValue.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(first))
            return En;

        // Remove quality values if they appear in the first part
        var semicolonIdx = first.IndexOf(';');
        if (semicolonIdx >= 0)
            first = first.Substring(0, semicolonIdx);

        first = first.Trim().ToLowerInvariant();

        if (first.StartsWith("pt"))
            return Pt;

        return En;
    }

    public static string Current(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var value) && value is string lang && !string.IsNullOrWhiteSpace(lang))
            return lang;

        return En;
    }
}
