using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using System.Diagnostics;
using System.Text;
using SkiaSharp;
using PdfPage = iText.Kernel.Pdf.PdfPage;

namespace pdf_ocr
{
    public class OcrPipelineService
    {
        public record PipelineResult(bool Success, string OutputPdf, List<string> Logs, string Error);

        public static PipelineResult Run(string jobDir)
        {
            var logs = new List<string>();
            try
            {
                if (!Directory.Exists(jobDir))
                    return new PipelineResult(false, "", logs, $"Diretório não encontrado: {jobDir}");

                string inputPdf = Path.Combine(jobDir, "input.pdf");
                if (!File.Exists(inputPdf))
                    return new PipelineResult(false, "", logs, "Arquivo input.pdf não encontrado");

                logs.Add($"[INÍCIO] Pipeline iniciado. Input: {new FileInfo(inputPdf).Length:N0} bytes");

                string processedPdf = Path.Combine(jobDir, "1_processed.pdf");
                string noFormsPdf = Path.Combine(jobDir, "1.5_no_forms.pdf");
                string imagesDir = Path.Combine(jobDir, "3_images");
                string tesseractOcr = Path.Combine(jobDir, "4_tesseract_ocr.pdf");
                string outputPdf = Path.Combine(jobDir, "output.pdf");

                // ETAPA 1 e 2: Limpeza e Preparação
                ProcessPdfWithVectorText(inputPdf, processedPdf);
                RemoveFormVisualsOnly(processedPdf, noFormsPdf);

                // ETAPA 3: Renderizar para JPEG Otimizado
                Directory.CreateDirectory(imagesDir);
                int imageCount = RenderPdfToImages(noFormsPdf, imagesDir, logs);
                logs.Add($"[OK] {imageCount} imagens JPG geradas.");

                // ETAPA 4: OCR (Apenas Texto)
                RunTesseractPerPage(imagesDir, jobDir, tesseractOcr, logs);

                // ETAPA 5: Mesclagem com Injeção de Imagem
                MergeOcrPdfWithOriginalForm(tesseractOcr, inputPdf, outputPdf, imagesDir, logs);

                return new PipelineResult(true, outputPdf, logs, "");
            }
            catch (Exception ex)
            {
                logs.Add($"[ERRO] {ex.Message}");
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
            using var document = DtronixPdf.PdfDocument.Load(inputPdf, null);

            // Configurações equilibradas para PDF.js e OCR
            const int TARGET_DPI = 300; // Aumentado para 200 para melhor nitidez visual
            
            for (int i = 0; i < document.Pages; i++)
            {
                using var page = document.GetPage(i);
                float scale = TARGET_DPI / 72f;
                using var bmp = page.Render(scale);
                string outputPath = Path.Combine(imagesDir, $"page_{i + 1:000}.jpg");

                // NOVO: Método de salvamento corrigido para evitar fundo preto
                SavePdfBitmapAsJpeg(bmp, outputPath, 95); // Qualidade 85 é excelente para texto
            }
            return Directory.GetFiles(imagesDir, "*.jpg").Length;
        }

        private static void SavePdfBitmapAsJpeg(DtronixPdf.PdfBitmap bmp, string outputPath, int quality)
        {
            // Source bitmap from PDFium (BGRA)
            var srcInfo = new SKImageInfo(
                bmp.Width,
                bmp.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul
            );

            // Destination bitmap — MUST be encodable
            var destInfo = new SKImageInfo(
                bmp.Width,
                bmp.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque
            );

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

            // Primary encode attempt
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);

            if (data == null)
            {
                // Fallback: try PNG (always available)
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
        private static void RunTesseractPerPage(string imagesDir, string workDir, string outputPdf, List<string> logs)
        {
            string ocrPagesDir = Path.Combine(workDir, "ocr_pages");
            Directory.CreateDirectory(ocrPagesDir);

            var images = Directory.GetFiles(imagesDir, "page_*.jpg");
            Array.Sort(images);

            string manifestPath = Path.Combine(ocrPagesDir, "tesseract_input_list.txt");
            File.WriteAllLines(manifestPath, images);

            string outBase = Path.Combine(ocrPagesDir, "tesseract_output_merged");
            // textonly_pdf=1 garante que o PDF do tesseract não contenha imagens, apenas a camada de texto
            string args = $"\"{manifestPath}\" \"{outBase}\" -l por -c textonly_pdf=1 pdf";

            RunProcess("tesseract", args, "Tesseract Batch");

            string result = outBase + ".pdf";
            if (File.Exists(outputPdf)) File.Delete(outputPdf);
            File.Move(result, outputPdf);
        }

        // =========================================================================
        // ETAPA 5: Mesclagem Robusta (Injeta imagem JPG como fundo)
        // =========================================================================
        private static void MergeOcrPdfWithOriginalForm(string ocrPdf, string originalPdf, string outputPdf, string imagesDir, List<string> logs)
        {
            using var srcOriginal = new iText.Kernel.Pdf.PdfDocument(new PdfReader(originalPdf));
            using var srcOcr = new iText.Kernel.Pdf.PdfDocument(new PdfReader(ocrPdf));

            var writerProps = new WriterProperties()
                .SetFullCompressionMode(true)
                .SetCompressionLevel(9);

            using var writer = new PdfWriter(outputPdf, writerProps);
            using var dest = new iText.Kernel.Pdf.PdfDocument(writer);

            var formCopier = new PdfPageFormCopier();
            int pageCount = Math.Min(srcOriginal.GetNumberOfPages(), srcOcr.GetNumberOfPages());

            for (int i = 1; i <= pageCount; i++)
            {
                var destPage = srcOriginal.GetPage(i).CopyTo(dest, formCopier);
                dest.AddPage(destPage);

                // Limpa o conteúdo vetorial original
                destPage.GetPdfObject().Put(PdfName.Contents, new PdfArray());

                var canvas = new PdfCanvas(destPage);
                var rect = destPage.GetMediaBox();

                // 1. Desenha a imagem JPG comprimida (Fundo)
                string imgPath = Path.Combine(imagesDir, $"page_{i:000}.jpg");
                if (File.Exists(imgPath))
                {
                    var imgData = iText.IO.Image.ImageDataFactory.Create(imgPath);
                    canvas.AddImageFittedIntoRectangle(imgData, rect, false);
                }

                // 2. Desenha a camada de texto do OCR por cima
                var ocrXObject = srcOcr.GetPage(i).CopyAsFormXObject(dest);
                canvas.AddXObjectFittedIntoRectangle(ocrXObject, rect);
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