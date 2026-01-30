using Microsoft.AspNetCore.Mvc;
using pdf_ocr.Models;
using pdf_ocr.Services;
using System.Diagnostics;

namespace pdf_ocr.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)] // Oculta endpoints de diagnóstico no Swagger
    public class DiagnosticsController : Controller
    {
        [HttpGet("ocr-status")]
        public IActionResult GetOcrStatus()
        {
            try
            {
                var psi = new ProcessStartInfo("tesseract", "--list-langs")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                // Processa a lista (pula a primeira linha que é informativa)
                var langs = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                                  .Skip(1)
                                  .Select(l => l.Trim())
                                  .ToList();

                var status = new
                {
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    GlobalizationInvariant = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"),
                    OCR = true,
                    AvailableLanguages = langs,
                    SupportFullGlobal = langs.Contains("por") && langs.Contains("ara") && langs.Contains("chi_sim")
                };

                return Ok(status);
            }
            catch (Exception ex)
            {
                var msg = ApiMessages.TesseractUnavailable(HttpContext, ex.Message);
                return StatusCode(500, new ErrorResponse { Error = msg.Error, Details = msg.Details });
            }
        }
    }
}
