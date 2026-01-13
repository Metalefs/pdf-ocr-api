using PDFiumCore;
using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using SkiaSharp;
using System.Diagnostics;
using System.Text;
using PdfPage = iText.Kernel.Pdf.PdfPage;

namespace pdf_ocr
{
    public class OcrPipelineService
    {
        public record PipelineResult(bool Success, string OutputPdf, List<string> Logs, string Error);

        // Uses JobProgressInfo directly to avoid duplicate DTO shapes.
        // This keeps JobService updates simple.
        public static PipelineResult Run(string jobDir)
        {
            return Run(jobDir, onLog: null, onProgress: null);
        }

        private static readonly bool IsDebug = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        private static readonly string DebugRoot =
            Path.Combine(AppContext.BaseDirectory, "debug_jobs");

        private static void DebugCopy(string sourcePath, string debugJobDir, string label)
        {
            if (!IsDebug || debugJobDir == null || !File.Exists(sourcePath))
                return;

            string name = $"{label}_{Path.GetFileName(sourcePath)}";
            string dest = Path.Combine(debugJobDir, name);

            File.Copy(sourcePath, dest, overwrite: true);
        }
        private static void DebugCopyDir(string sourceDir, string debugJobDir, string label)
        {
            if (!IsDebug || debugJobDir == null || !Directory.Exists(sourceDir))
                return;

            string destDir = Path.Combine(debugJobDir, label);
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(
                    file,
                    Path.Combine(destDir, Path.GetFileName(file)),
                    overwrite: true
                );
            }
        }
        public static void SetupLinuxFonts()
        {
            if (OperatingSystem.IsLinux())
            {
                // Inicializa o sistema de fontes do PDFium para procurar nos diretórios padrão do Linux
                fpdfview.FPDF_InitLibrary();

                // Esta é a "mágica": o PDFium passará a usar as fontes instaladas via apt-get
                // (como fonts-liberation ou mscorefonts) para substituir as do Windows.
            }
        }

        public static PipelineResult Run(
            string jobDir,
            Action<string>? onLog,
            Action<pdf_ocr.Models.JobProgressInfo>? onProgress)
        {
            SetupLinuxFonts();
            var logs = new List<string>();
            try
            {
                if (!Directory.Exists(jobDir))
                    return new PipelineResult(false, "", logs, $"Diretório não encontrado: {jobDir}");


                string debugJobDir = null;

                if (IsDebug)
                {
                    Directory.CreateDirectory(DebugRoot);

                    string jobName = Path.GetFileName(jobDir.TrimEnd(Path.DirectorySeparatorChar));
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

                    debugJobDir = Path.Combine(DebugRoot, $"{jobName}_{stamp}");
                    Directory.CreateDirectory(debugJobDir);

                    logs.Add($"[DEBUG] Salvando artefatos em: {debugJobDir}");
                }


                string inputPdf = Path.Combine(jobDir, "input.pdf");
                if (!File.Exists(inputPdf))
                    return new PipelineResult(false, "", logs, "Arquivo input.pdf não encontrado");

                void AddLog(string line)
                {
                    logs.Add(line);
                    onLog?.Invoke(line);
                }

                void ReportProgress(string stage, string message, int? percent = null, int? totalPages = null, int? processedPages = null, List<int>? activePages = null)
                {
                    onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
                    {
                        Stage = stage,
                        Message = message,
                        Percent = percent,
                        TotalPages = totalPages,
                        ProcessedPages = processedPages,
                        ActivePages = activePages
                    });
                }

                AddLog($"[INÍCIO] Pipeline iniciado. DebugMode: {IsDebug}");
                ReportProgress("pipeline", "Preparando pipeline...", percent: 2);

                // Caminhos dos arquivos intermediários
                string processedPdf = Path.Combine(jobDir, "1_vector_cleaned.pdf");
                string noFormsPdf = Path.Combine(jobDir, "2_no_forms_visuals.pdf");
                string imagesDir = Path.Combine(jobDir, "3_images_render");
                string tesseractOcr = Path.Combine(jobDir, "4_tesseract_text_layer.pdf");
                string outputPdf = Path.Combine(jobDir, "output_final.pdf");

                // ETAPA 1: Limpeza Vetorial
                AddLog("[ETAPA 1] Removendo texto selecionável original...");
                ReportProgress("vector-clean", "Removendo texto selecionável original...", percent: 8);
                ProcessPdfWithVectorText(inputPdf, processedPdf);
                DebugCopy(processedPdf, debugJobDir, "01_vector_cleaned");

                // ETAPA 2: Remoção de Visuais de Formulário
                AddLog("[ETAPA 2] Isolando base para OCR...");
                ReportProgress("forms-base", "Preparando PDF para OCR (preservando formulários)...", percent: 15);
                RemoveFormVisualsOnly(processedPdf, noFormsPdf);
                DebugCopy(noFormsPdf, debugJobDir, "02_no_forms_visuals");

                // ETAPA 3: Renderização para Imagem
                AddLog("[ETAPA 3] Renderizando PDF para JPG (Alta Definição)...");
                Directory.CreateDirectory(imagesDir);
                int imageCount = RenderPdfToImages(noFormsPdf, imagesDir, AddLog, onProgress);
                DebugCopyDir(imagesDir, debugJobDir, "03_images_render");

                // ETAPA 4: OCR
                AddLog("[ETAPA 4] Tesseract: Gerando camada de texto invisível...");
                RunTesseractParallel(imagesDir, jobDir, AddLog, onProgress);
                DebugCopy(tesseractOcr, debugJobDir, "04_tesseract_text_layer");
                DebugCopyDir(
                    Path.Combine(jobDir, "ocr_debug_temp"),
                    debugJobDir,
                    "04b_ocr_internal"
                );
                // ETAPA 5: Recomposição Final
                AddLog("[ETAPA 5] Mesclando elementos: Imagem + Texto OCR + Formulários Originais...");
                MergeOcrPdfWithOriginalForm(jobDir, inputPdf, outputPdf, imagesDir, AddLog, onProgress);
                DebugCopy(outputPdf, debugJobDir, "05_output_final");

                // Se falhar em debug, os arquivos acima estarão no jobDir para inspeção
                AddLog("[SUCESSO] Pipeline finalizado com êxito.");
                ReportProgress("completed", "Concluído. Seu PDF está pronto para download.", percent: 100, totalPages: imageCount, processedPages: imageCount);
                return new PipelineResult(true, outputPdf, logs, "");
            }
            catch (Exception ex)
            {
                logs.Add($"[FALHA CRÍTICA] Erro: {ex.Message}");
                onLog?.Invoke($"[FALHA CRÍTICA] Erro: {ex.Message}");
                onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
                {
                    Stage = "failed",
                    Message = "Falha no processamento.",
                    Percent = 0
                });
                // Em modo Debug, não deletamos os arquivos temporários em caso de erro
                return new PipelineResult(false, "", logs, ex.Message);
            }
        }

        private static void ProcessPdfWithVectorText(string inputPdf, string outputPdf)
        {
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(new PdfReader(inputPdf), new PdfWriter(outputPdf));
            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                RenderPageAsNonSelectableContent(pdfDoc.GetPage(i));
        }

        private static void RenderPageAsNonSelectableContent(PdfPage page, bool makeInvisible = false)
        {
            page.GetAnnotations()?.Clear();
            var contentBytes = page.GetContentBytes();
            if (contentBytes == null || contentBytes.Length == 0) return;

            var canvas = new PdfCanvas(page);
            canvas.SaveState();
            var stream = canvas.GetContentStream().GetOutputStream();
            if (makeInvisible) stream.Write(Encoding.ASCII.GetBytes("3 Tr\n"));
            stream.Write(contentBytes);
            stream.Write(Encoding.ASCII.GetBytes("0 Tr\n"));
            canvas.RestoreState();
        }

        private static void RemoveFormVisualsOnly(string inputPdf, string outputPdf)
        {
            using var src = new iText.Kernel.Pdf.PdfDocument(new PdfReader(inputPdf));
            using var dest = new iText.Kernel.Pdf.PdfDocument(new PdfWriter(outputPdf));
            for (int i = 1; i <= src.GetNumberOfPages(); i++)
            {
                var destPage = src.GetPage(i).CopyTo(dest);
                dest.AddPage(destPage);
                var annots = destPage.GetAnnotations();
                if (annots == null) continue;
                for (int a = annots.Count - 1; a >= 0; a--)
                {
                    if (PdfName.Widget.Equals(annots[a].GetSubtype()))
                        destPage.RemoveAnnotation(annots[a]);
                }
            }
            dest.GetCatalog().Remove(PdfName.AcroForm);
        }

        // =========================================================================
        // ETAPA 3: Renderizar PDF para JPEG (Otimizado para PDF.js)
        // =========================================================================
        private static int RenderPdfToImages(
            string inputPdf,
            string imagesDir,
            Action<string> addLog,
            Action<pdf_ocr.Models.JobProgressInfo>? onProgress)
        {
            if (OperatingSystem.IsLinux()) fpdfview.FPDF_InitLibrary();

            // We load a temporary document to get the page count
            int pageCount;
            using (var doc = DtronixPdf.PdfDocument.Load(inputPdf, null))
            {
                pageCount = doc.Pages;
            }

            // Adobe Reader High Quality Heuristic: 200 DPI
            const int TARGET_DPI = 250;
            float scale = TARGET_DPI / 72f;

            // Use Parallel.For to render pages across all CPU cores
            // We limit MaxDegreeOfParallelism slightly to prevent RAM exhaustion from large bitmaps
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            };

            int completed = 0;
            var activePages = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
            var lastReport = 0L;
            var reportLock = new object();

            void Report(string message)
            {
                // Throttle progress reporting to avoid spamming the status endpoint.
                var now = Stopwatch.GetTimestamp();
                bool shouldReport;
                lock (reportLock)
                {
                    var elapsedMs = (now - lastReport) * 1000.0 / Stopwatch.Frequency;
                    shouldReport = elapsedMs >= 250;
                    if (shouldReport) lastReport = now;
                }

                if (!shouldReport && completed < pageCount) return;

                var active = activePages.Keys.OrderBy(x => x).Take(8).ToList();
                var percent = pageCount > 0
                    ? 15 + (int)Math.Round((completed / (double)pageCount) * 30)
                    : 25;

                onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
                {
                    Stage = "render",
                    Message = message,
                    TotalPages = pageCount,
                    ProcessedPages = completed,
                    ActivePages = active,
                    Percent = Math.Min(45, Math.Max(15, percent))
                });
            }

            Report("Renderizando páginas para imagem...");

            Parallel.For(0, pageCount, parallelOptions, i =>
            {
                int pageNumber = i + 1;
                activePages.TryAdd(pageNumber, 0);
                Report($"Renderizando páginas... ({completed}/{pageCount})");

                // Each thread must load its own instance of the document/page 
                // to ensure thread safety within PDFium
                using var threadDoc = DtronixPdf.PdfDocument.Load(inputPdf, null);
                using var page = threadDoc.GetPage(i);
                using var bmp = page.Render(scale);

                string outputPath = Path.Combine(imagesDir, $"page_{i + 1:000}.jpg");

                // Save using SkiaSharp (Same logic as before, but executing in parallel)
                SavePdfBitmapAsJpeg(bmp, outputPath, 95);

                activePages.TryRemove(pageNumber, out _);
                System.Threading.Interlocked.Increment(ref completed);
                Report($"Renderizando páginas... ({completed}/{pageCount})");
            });

            addLog($"[RENDER] {pageCount} páginas renderizadas em paralelo a 300 DPI.");

            onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
            {
                Stage = "render",
                Message = "Renderização concluída.",
                TotalPages = pageCount,
                ProcessedPages = pageCount,
                ActivePages = new List<int>(),
                Percent = 45
            });
            return pageCount;
        }

        private static void SavePdfBitmapAsJpeg(DtronixPdf.PdfBitmap bmp, string outputPath, int quality)
        {
            var srcInfo = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            // Alterado para Rgb888x para garantir compatibilidade nativa de encoding JPEG no Linux
            var destInfo = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Rgb888x, SKAlphaType.Opaque);

            using var destBitmap = new SKBitmap(destInfo);
            using var canvas = new SKCanvas(destBitmap);

            canvas.Clear(SKColors.White);
            using (var srcBitmap = new SKBitmap())
            {
                if (!srcBitmap.InstallPixels(srcInfo, bmp.Pointer, bmp.Stride))
                    throw new Exception("Falha ao mapear pixels do bitmap PDFium para SkiaSharp.");

                using var paint = new SKPaint
                {
                    FilterQuality = SKFilterQuality.High,
                    IsAntialias = true
                };

                canvas.DrawBitmap(srcBitmap, 0, 0, paint);
            }

            using var image = SKImage.FromBitmap(destBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);

            if (data == null)
            {
                using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                if (pngData == null)
                {
                    throw new Exception(
                        "SkiaSharp falhou ao codificar a imagem (JPEG e PNG). " +
                        "As dependências nativas estão carregadas, mas o encoder não respondeu."
                    );
                }

                // Save PNG as JPG-compatible name only if caller expects JPG
                using var fallbackStream = File.OpenWrite(outputPath);
                pngData.SaveTo(fallbackStream);
                return;
            }
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);
        }

        // =========================================================================
        // ETAPA 4: OCR (Comando textonly para evitar PDFs gigantes)
        // =========================================================================
        private static string DetectBestLanguages(string imagePath)
        {
            // Executa o Tesseract apenas para detecção (PSM 0 = OSD Only)
            // Isso retorna qual o Script predominante (ex: Latin, Arabic, Han)
            var psi = new ProcessStartInfo("tesseract", $"\"{imagePath}\" stdout --psm 0")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            // Lógica de decisão baseada no Script detectado
            // Padrão: Português e Inglês sempre presentes
            string baseLangs = "por+eng";

            if (output.Contains("Arabic")) return baseLangs + "+ara";
            if (output.Contains("Han")) return baseLangs + "+chi_sim+chi_tra";
            if (output.Contains("Japanese")) return baseLangs + "+jpn";
            if (output.Contains("Korean")) return baseLangs + "+kor";
            if (output.Contains("Cyrillic")) return baseLangs + "+rus";

            return baseLangs;
        }
        private static void RunTesseractParallel(
            string imagesDir,
            string workDir,
            Action<string> addLog,
            Action<pdf_ocr.Models.JobProgressInfo>? onProgress)
        {
            var images = Directory.GetFiles(imagesDir, "page_*.jpg");
            Array.Sort(images);

            string ocrPagesDir = Path.Combine(workDir, "ocr_debug_temp");
            Directory.CreateDirectory(ocrPagesDir);

            // Detect language once using the first page to avoid redundant OSD overhead
            string detectedLangs = images.Length > 0 ? DetectBestLanguages(images[0]) : "por+eng";
            addLog($"[OCR] Idioma detectado: {detectedLangs}. Iniciando processamento paralelo...");

            int totalPages = images.Length;
            int completed = 0;
            var activePages = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
            var lastReport = 0L;
            var reportLock = new object();

            void Report(string message)
            {
                // Throttle progress reporting to avoid spamming the status endpoint.
                var now = Stopwatch.GetTimestamp();
                bool shouldReport;
                lock (reportLock)
                {
                    var elapsedMs = (now - lastReport) * 1000.0 / Stopwatch.Frequency;
                    shouldReport = elapsedMs >= 250;
                    if (shouldReport) lastReport = now;
                }

                if (!shouldReport && completed < totalPages) return;

                var active = activePages.Keys.OrderBy(x => x).Take(8).ToList();
                var percent = totalPages > 0
                    ? 45 + (int)Math.Round((completed / (double)totalPages) * 40)
                    : 60;

                onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
                {
                    Stage = "ocr",
                    Message = message,
                    TotalPages = totalPages,
                    ProcessedPages = completed,
                    ActivePages = active,
                    Percent = Math.Min(85, Math.Max(45, percent))
                });
            }

            Report("Iniciando OCR (Tesseract)...");

            // Parallelize across CPU cores. 
            // MaxDegreeOfParallelism should be roughly your CPU core count.
            Parallel.ForEach(images, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, imagePath =>
            {
                string pageNum = Path.GetFileNameWithoutExtension(imagePath).Replace("page_", "");
                int pageIndex = 0;
                int.TryParse(pageNum, out pageIndex);
                if (pageIndex > 0) activePages.TryAdd(pageIndex, 0);

                Report($"OCR em andamento... ({completed}/{totalPages})");
                string outBase = Path.Combine(ocrPagesDir, $"ocr_page_{pageNum}");

                // Optimized arguments: textonly_pdf=1 is essential for speed/size
                string args = $"\"{imagePath}\" \"{outBase}\" -l {detectedLangs} --psm 1 -c textonly_pdf=1 -c dpi=300 pdf";

                RunProcess("tesseract", args, $"Tesseract Page {pageNum}");

                if (pageIndex > 0) activePages.TryRemove(pageIndex, out _);
                System.Threading.Interlocked.Increment(ref completed);
                Report($"OCR em andamento... ({completed}/{totalPages})");
            });

            onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
            {
                Stage = "ocr",
                Message = "OCR concluído.",
                TotalPages = totalPages,
                ProcessedPages = totalPages,
                ActivePages = new List<int>(),
                Percent = 85
            });
        }

        // =========================================================================
        // ETAPA 5: Mesclagem Robusta (Injeta imagem JPG como fundo)
        // =========================================================================
        private static void MergeOcrPdfWithOriginalForm(
            string jobDir,
            string originalPdf,
            string outputPdf,
            string imagesDir,
            Action<string> addLog,
            Action<pdf_ocr.Models.JobProgressInfo>? onProgress)
        {
            string ocrPagesDir = Path.Combine(jobDir, "ocr_debug_temp");
            using var srcOriginal = new iText.Kernel.Pdf.PdfDocument(new PdfReader(originalPdf));

            var writerProps = new WriterProperties().SetFullCompressionMode(true).SetCompressionLevel(9);
            using var writer = new PdfWriter(outputPdf, writerProps);
            using var dest = new iText.Kernel.Pdf.PdfDocument(writer);

            var formCopier = new PdfPageFormCopier();
            int pageCount = srcOriginal.GetNumberOfPages();

            addLog($"[MERGE] Mesclando {pageCount} página(s)...");

            for (int i = 1; i <= pageCount; i++)
            {
                onProgress?.Invoke(new pdf_ocr.Models.JobProgressInfo
                {
                    Stage = "merge",
                    Message = $"Mesclando página {i} de {pageCount}...",
                    TotalPages = pageCount,
                    ProcessedPages = i,
                    ActivePages = new List<int> { i },
                    Percent = 85 + (int)Math.Round((i / (double)pageCount) * 13)
                });

                var destPage = srcOriginal.GetPage(i).CopyTo(dest, formCopier);
                dest.AddPage(destPage);

                // Clear vector content
                destPage.GetPdfObject().Put(PdfName.Contents, new iText.Kernel.Pdf.PdfArray());

                var canvas = new PdfCanvas(destPage);
                var rect = destPage.GetMediaBox();

                // Background: Image
                string imgPath = Path.Combine(imagesDir, $"page_{i:000}.jpg");
                if (File.Exists(imgPath))
                {
                    var imgData = iText.IO.Image.ImageDataFactory.Create(imgPath);
                    canvas.AddImageFittedIntoRectangle(imgData, rect, false);
                }

                // Overlay: Individual Page OCR Layer
                string ocrPagePath = Path.Combine(ocrPagesDir, $"ocr_page_{i:000}.pdf");
                if (File.Exists(ocrPagePath))
                {
                    using var ocrSubDoc = new iText.Kernel.Pdf.PdfDocument(new PdfReader(ocrPagePath));
                    var ocrPage = ocrSubDoc.GetPage(1);
                    var ocrXObject = ocrPage.CopyAsFormXObject(dest);
                    canvas.AddXObjectFittedIntoRectangle(ocrXObject, rect);
                }
            }
        }

        private static void RunProcess(string exe, string args, string toolName)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p.WaitForExit();
            if (p.ExitCode != 0) throw new Exception($"{toolName} erro: {p.StandardError.ReadToEnd()}");
        }
    }
}