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

        public static PipelineResult Run(string jobDir)
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

                logs.Add($"[INÍCIO] Pipeline iniciado. DebugMode: {IsDebug}");

                // Caminhos dos arquivos intermediários
                string processedPdf = Path.Combine(jobDir, "1_vector_cleaned.pdf");
                string noFormsPdf = Path.Combine(jobDir, "2_no_forms_visuals.pdf");
                string imagesDir = Path.Combine(jobDir, "3_images_render");
                string tesseractOcr = Path.Combine(jobDir, "4_tesseract_text_layer.pdf");
                string outputPdf = Path.Combine(jobDir, "output_final.pdf");

                // ETAPA 1: Limpeza Vetorial
                logs.Add("[ETAPA 1] Removendo texto selecionável original...");
                ProcessPdfWithVectorText(inputPdf, processedPdf);
                DebugCopy(processedPdf, debugJobDir, "01_vector_cleaned");

                // ETAPA 2: Remoção de Visuais de Formulário
                logs.Add("[ETAPA 2] Isolando base para OCR...");
                RemoveFormVisualsOnly(processedPdf, noFormsPdf);
                DebugCopy(noFormsPdf, debugJobDir, "02_no_forms_visuals");

                // ETAPA 3: Renderização para Imagem
                logs.Add("[ETAPA 3] Renderizando PDF para JPG (Alta Definição)...");
                Directory.CreateDirectory(imagesDir);
                int imageCount = RenderPdfToImages(noFormsPdf, imagesDir, logs);
                DebugCopyDir(imagesDir, debugJobDir, "03_images_render");

                // ETAPA 4: OCR
                logs.Add("[ETAPA 4] Tesseract: Gerando camada de texto invisível...");
                RunTesseractPerPage(imagesDir, jobDir, tesseractOcr, logs);
                DebugCopy(tesseractOcr, debugJobDir, "04_tesseract_text_layer");
                DebugCopyDir(
                    Path.Combine(jobDir, "ocr_debug_temp"),
                    debugJobDir,
                    "04b_ocr_internal"
                );
                // ETAPA 5: Recomposição Final
                logs.Add("[ETAPA 5] Mesclando elementos: Imagem + Texto OCR + Formulários Originais...");
                MergeOcrPdfWithOriginalForm(tesseractOcr, inputPdf, outputPdf, imagesDir, logs);
                DebugCopy(outputPdf, debugJobDir, "05_output_final");

                // Se falhar em debug, os arquivos acima estarão no jobDir para inspeção
                logs.Add("[SUCESSO] Pipeline finalizado com êxito.");
                return new PipelineResult(true, outputPdf, logs, "");
            }
            catch (Exception ex)
            {
                logs.Add($"[FALHA CRÍTICA] Erro: {ex.Message}");
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
        private static int RenderPdfToImages(string inputPdf, string imagesDir, List<string> logs)
        {
            // Garante que o PDFium conheça as fontes do Linux antes de carregar o PDF
            if (OperatingSystem.IsLinux()) fpdfview.FPDF_InitLibrary();

            using var document = DtronixPdf.PdfDocument.Load(inputPdf, null);

            // Adobe Reader High Quality Heuristic: 300 DPI para texto pequeno
            const int TARGET_DPI = 300;
            float scale = TARGET_DPI / 72f;

            for (int i = 0; i < document.Pages; i++)
            {
                using var page = document.GetPage(i);

                // Aumentar o scale é a única forma de forçar nitidez no DtronixPdf
                // sem acessar o ponteiro nativo da página
                using var bmp = page.Render(scale);

                string outputPath = Path.Combine(imagesDir, $"page_{i + 1:000}.jpg");

                // O método de salvamento SkiaSharp cuidará do fundo branco e compressão
                SavePdfBitmapAsJpeg(bmp, outputPath, 95);

                logs.Add($"[RENDER] Página {i + 1} processada (Heurística de 300 DPI aplicada).");
            }

            return Directory.GetFiles(imagesDir, "*.jpg").Length;
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
        private static void RunTesseractPerPage(string imagesDir, string workDir, string outputPdf, List<string> logs)
        {
            // Pega a primeira página para detectar o idioma predominante do lote
            var firstImage = Directory.GetFiles(imagesDir, "page_001.jpg").FirstOrDefault();
            string detectedLangs = firstImage != null ? DetectBestLanguages(firstImage) : "por+eng";

            logs.Add($"[OCR] Idiomas selecionados para este documento: {detectedLangs}");

            string ocrPagesDir = Path.Combine(workDir, "ocr_debug_temp");
            Directory.CreateDirectory(ocrPagesDir);

            var images = Directory.GetFiles(imagesDir, "page_*.jpg");
            Array.Sort(images);

            string manifestPath = Path.Combine(ocrPagesDir, "input_list.txt");
            File.WriteAllLines(manifestPath, images);

            string outBase = Path.Combine(ocrPagesDir, "ocr_result");

            // PSM 1 é ideal aqui pois lida com documentos multilingues e orientação
            string args = $"\"{manifestPath}\" \"{outBase}\" -l {detectedLangs} --psm 1 -c textonly_pdf=1 -c dpi=300 pdf";

            logs.Add($"[OCR GLOBAL] Iniciando processamento multilingue (PT/EN/AR/ZH/JP/KO).");
            RunProcess("tesseract", args, "Tesseract Global");

            string resultFile = outBase + ".pdf";
            if (File.Exists(outputPdf)) File.Delete(outputPdf);
            File.Move(resultFile, outputPdf);
        }

        // =========================================================================
        // ETAPA 5: Mesclagem Robusta (Injeta imagem JPG como fundo)
        // =========================================================================
        private static void MergeOcrPdfWithOriginalForm(string ocrPdf, string originalPdf, string outputPdf, string imagesDir, List<string> logs)
        {
            using var srcOriginal = new iText.Kernel.Pdf.PdfDocument(new PdfReader(originalPdf));
            using var srcOcr = new iText.Kernel.Pdf.PdfDocument(new PdfReader(ocrPdf));

            var writerProps = new WriterProperties().SetFullCompressionMode(true).SetCompressionLevel(9);
            using var writer = new PdfWriter(outputPdf, writerProps);
            using var dest = new iText.Kernel.Pdf.PdfDocument(writer);

            var formCopier = new PdfPageFormCopier();
            int pageCount = Math.Min(srcOriginal.GetNumberOfPages(), srcOcr.GetNumberOfPages());

            for (int i = 1; i <= pageCount; i++)
            {
                var destPage = srcOriginal.GetPage(i).CopyTo(dest, formCopier);
                dest.AddPage(destPage);

                // Limpa o conteúdo "sujo" original preservando o formulário
                destPage.GetPdfObject().Put(PdfName.Contents, new iText.Kernel.Pdf.PdfArray());

                var canvas = new PdfCanvas(destPage);
                var rect = destPage.GetMediaBox();

                // Fundo: Imagem processada
                string imgPath = Path.Combine(imagesDir, $"page_{i:000}.jpg");
                if (File.Exists(imgPath))
                {
                    var imgData = iText.IO.Image.ImageDataFactory.Create(imgPath);
                    canvas.AddImageFittedIntoRectangle(imgData, rect, false);
                }

                // Sobreposição: Camada de texto do OCR
                var ocrPage = srcOcr.GetPage(i);
                var ocrXObject = ocrPage.CopyAsFormXObject(dest);
                canvas.AddXObjectFittedIntoRectangle(ocrXObject, rect);

                if (IsDebug && i == 1) logs.Add("    > Camadas mescladas na página 1.");
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