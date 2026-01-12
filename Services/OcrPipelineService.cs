using iText.Forms;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using PDFiumSharp;
using PDFiumSharp.Enums;
using PDFiumSharp.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using PdfPage = iText.Kernel.Pdf.PdfPage;

namespace pdf_ocr
{
    public class OcrPipelineService
    {
        public record PipelineResult(
            bool Success,
            string OutputPdf,
            List<string> Logs,
            string Error
        );
        /// <summary>
        /// Executa o pipeline completo de OCR em um PDF
        /// </summary>
        /// <param name="jobDir">Diretório de trabalho que deve conter um arquivo 'input.pdf'</param>
        /// <returns>Resultado do processamento com caminho do PDF final</returns>
        public static PipelineResult Run(string jobDir)
        {
            var logs = new List<string>();

            try
            {
                // Validações iniciais
                if (!Directory.Exists(jobDir))
                {
                    return new PipelineResult(false, "", logs, $"Diretório não encontrado: {jobDir}");
                }

                string inputPdf = Path.Combine(jobDir, "input.pdf");
                if (!File.Exists(inputPdf))
                {
                    return new PipelineResult(false, "", logs, $"Arquivo input.pdf não encontrado em: {jobDir}");
                }

                logs.Add($"[INÍCIO] Pipeline iniciado");
                logs.Add($"[INPUT] {inputPdf} ({new FileInfo(inputPdf).Length:N0} bytes)");

                // Definir caminhos dos arquivos intermediários
                string processedPdf = Path.Combine(jobDir, "1_processed.pdf");
                string noFormsPdf = Path.Combine(jobDir, "1.5_no_forms.pdf");
                string imagesDir = Path.Combine(jobDir, "3_images");
                string tesseractOcr = Path.Combine(jobDir, "4_tesseract_ocr.pdf");
                string outputPdf = Path.Combine(jobDir, "output.pdf");

                // ETAPA 1: Limpar texto vetorial
                logs.Add("[ETAPA 1/5] Limpando texto vetorial do PDF...");
                ProcessPdfWithVectorText(inputPdf, processedPdf);
                logs.Add($"[OK] Processado: {new FileInfo(processedPdf).Length:N0} bytes");

                // ETAPA 2: Remover formulários visualmente
                logs.Add("[ETAPA 2/5] Removendo formulários visualmente...");
                RemoveFormVisualsOnly(processedPdf, noFormsPdf);
                logs.Add($"[OK] Sem formulários: {new FileInfo(noFormsPdf).Length:N0} bytes");

                // ETAPA 3: Converter para imagens
                logs.Add("[ETAPA 3/5] Convertendo PDF em imagens...");
                Directory.CreateDirectory(imagesDir);
                int imageCount = RenderPdfToImages(noFormsPdf, imagesDir, logs);
                logs.Add($"[OK] {imageCount} imagem(ns) gerada(s)");

                // ETAPA 4: OCR com Tesseract
                logs.Add("[ETAPA 4/5] Executando OCR com Tesseract...");
                RunTesseractPerPage(imagesDir, jobDir, tesseractOcr, logs);
                logs.Add($"[OK] OCR concluído: {new FileInfo(tesseractOcr).Length:N0} bytes");

                // ETAPA 5: Mesclar OCR com formulário original
                logs.Add("[ETAPA 5/5] Mesclando OCR com formulário original...");
                MergeOcrPdfWithOriginalForm(tesseractOcr, inputPdf, outputPdf);
                logs.Add($"[OK] PDF final: {new FileInfo(outputPdf).Length:N0} bytes");

                logs.Add("[SUCESSO] Pipeline concluído!");

                return new PipelineResult(true, outputPdf, logs, "");
            }
            catch (Exception ex)
            {
                logs.Add($"[ERRO] {ex.GetType().Name}: {ex.Message}");
                logs.Add($"[STACK] {ex.StackTrace}");
                return new PipelineResult(false, "", logs, ex.Message);
            }
        }

        // =========================================================================
        // ETAPA 1: Tornar texto não selecionável
        // =========================================================================
        private static void ProcessPdfWithVectorText(string inputPdf, string outputPdf)
        {
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(new PdfReader(inputPdf), new PdfWriter(outputPdf));
            int totalPages = pdfDoc.GetNumberOfPages();

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdfDoc.GetPage(i);
                RenderPageAsNonSelectableContent(page);
            }
        }

        private static void RenderPageAsNonSelectableContent(PdfPage page, Boolean makeInvisible = false)
        {
            // Remove annotations
            page.GetAnnotations()?.Clear();

            var contentBytes = page.GetContentBytes();
            if (contentBytes != null && contentBytes.Length > 0)
            {
                var canvas = new PdfCanvas(page);
                canvas.SaveState();

                var stream = canvas.GetContentStream().GetOutputStream();

                // Tornar texto invisível (modo 3 = invisível)
                if (makeInvisible) stream.Write(System.Text.Encoding.ASCII.GetBytes("3 Tr\n"));
                stream.Write(contentBytes);
                stream.Write(Encoding.ASCII.GetBytes("0 Tr\n"));

                canvas.RestoreState();
            }
        }

        // =========================================================================
        // ETAPA 2: Remover formulários visualmente
        // =========================================================================
        private static void RemoveFormVisualsOnly(string inputPdf, string outputPdf)
        {
            using var src = new iText.Kernel.Pdf.PdfDocument(new PdfReader(inputPdf));
            using var dest = new iText.Kernel.Pdf.PdfDocument(new PdfWriter(outputPdf));

            int pages = src.GetNumberOfPages();

            for (int i = 1; i <= pages; i++)
            {
                var srcPage = src.GetPage(i);
                var destPage = srcPage.CopyTo(dest);
                dest.AddPage(destPage);

                // Remove apenas annotations de formulário (Widget)
                var annots = destPage.GetAnnotations();
                if (annots != null)
                {
                    for (int a = annots.Count - 1; a >= 0; a--)
                    {
                        var annot = annots[a];
                        if (PdfName.Widget.Equals(annot.GetSubtype()))
                        {
                            destPage.RemoveAnnotation(annot);
                        }
                    }
                }
            }

            // Remove o AcroForm do catálogo
            var form = PdfAcroForm.GetAcroForm(dest, false);
            if (form != null)
            {
                dest.GetCatalog().Remove(PdfName.AcroForm);
            }
        }

        // =========================================================================
        // ETAPA 3: Renderizar PDF para imagens PNG
        // =========================================================================
        private static int RenderPdfToImages(string inputPdf, string imagesDir, List<string> logs)
        {
            using var document = new PDFiumSharp.PdfDocument(inputPdf);

            const int TARGET_DPI = 300;
            const int MAX_DIMENSION = 8000;

            int pageCount = document.Pages.Count;

            for (int i = 0; i < pageCount; i++)
            {
                var page = document.Pages[i];

                // Calcular dimensões respeitando limite máximo
                double rawWidth = page.Width * TARGET_DPI / 72.0;
                double rawHeight = page.Height * TARGET_DPI / 72.0;

                double scale = Math.Min(1.0, Math.Min(
                    MAX_DIMENSION / rawWidth,
                    MAX_DIMENSION / rawHeight
                ));

                int width = (int)Math.Round(rawWidth * scale);
                int height = (int)Math.Round(rawHeight * scale);
                int effectiveDpi = (int)Math.Round(TARGET_DPI * scale);

                logs.Add($"  → Página {i + 1}: {width}x{height}px @ {effectiveDpi} DPI");

                using var bitmap = new PDFiumBitmap(width, height, true);

                // Fundo branco
                bitmap.FillRectangle(0, 0, width, height, new FPDF_COLOR(255, 255, 255));

                // Renderizar página
                page.Render(bitmap, PageOrientations.Normal,
                    RenderingFlags.LcdText | RenderingFlags.Annotations);

                string outputPath = Path.Combine(imagesDir, $"page_{i + 1:000}.png");
                SaveBitmapAsPng(bitmap, outputPath);
            }

            int generatedCount = Directory.GetFiles(imagesDir, "*.png").Length;

            if (generatedCount == 0)
            {
                throw new Exception("PDFium não gerou nenhuma imagem");
            }

            return generatedCount;
        }

        private static void SaveBitmapAsPng(PDFiumBitmap bitmap, string outputPath)
        {
        #if WINDOWS
            var data = bitmap.AsBmpStream();
            using var bmp = new System.Drawing.Bitmap(data);
            bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        #else
            // Alternativa multiplataforma usando ImageSharp
            var data = bitmap.AsBmpStream();
            data.Position = 0;
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(data);
            image.Save(outputPath, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        #endif
        }

        // =========================================================================
        // ETAPA 4: OCR com Tesseract
        // =========================================================================
        private static void RunTesseractPerPage(string imagesDir, string workDir,
            string outputPdf, List<string> logs)
        {
            string ocrPagesDir = Path.Combine(workDir, "ocr_pages");
            Directory.CreateDirectory(ocrPagesDir);

            var images = Directory.GetFiles(imagesDir, "page_*.png");
            Array.Sort(images);

            if (images.Length == 0)
            {
                throw new Exception("Nenhuma imagem encontrada para OCR");
            }

            var ocrPdfs = new List<string>();
            int index = 1;

            foreach (var img in images)
            {
                string outBase = Path.Combine(ocrPagesDir, $"ocr_{index:000}");
                string args = $"\"{img}\" \"{outBase}\" -l por pdf";

                logs.Add($"  → OCR página {index}/{images.Length}: {Path.GetFileName(img)}");

                RunProcess("tesseract", args, "Tesseract");

                string pdf = outBase + ".pdf";
                if (!File.Exists(pdf))
                {
                    throw new Exception($"OCR falhou para: {img}");
                }

                ocrPdfs.Add(pdf);
                logs.Add($"    ✓ Gerado: {new FileInfo(pdf).Length:N0} bytes");
                index++;
            }

            // Mesclar todas as páginas OCR
            MergePdfs(ocrPdfs, outputPdf);
        }

        private static void MergePdfs(List<string> inputPdfs, string outputPdf)
        {
            using var writer = new PdfWriter(outputPdf);
            using var destDoc = new iText.Kernel.Pdf.PdfDocument(writer);

            foreach (var pdf in inputPdfs)
            {
                using var reader = new PdfReader(pdf);
                using var srcDoc = new iText.Kernel.Pdf.PdfDocument(reader);
                srcDoc.CopyPagesTo(1, srcDoc.GetNumberOfPages(), destDoc);
            }
        }

        // =========================================================================
        // ETAPA 5: Mesclar OCR com formulário original
        // =========================================================================
        private static void MergeOcrPdfWithOriginalForm(string ocrPdf, string originalPdf,
            string outputPdf)
        {
            using var srcOriginal = new iText.Kernel.Pdf.PdfDocument(new PdfReader(originalPdf));
            using var srcOcr = new iText.Kernel.Pdf.PdfDocument(new PdfReader(ocrPdf));
            using var writer = new PdfWriter(outputPdf);
            using var dest = new iText.Kernel.Pdf.PdfDocument(writer);

            var formCopier = new PdfPageFormCopier();
            int pageCount = Math.Min(srcOriginal.GetNumberOfPages(), srcOcr.GetNumberOfPages());

            for (int i = 1; i <= pageCount; i++)
            {
                // Copiar página original (traz layout + formulários)
                var destPage = srcOriginal.GetPage(i).CopyTo(dest, formCopier);
                dest.AddPage(destPage);

                // Limpar conteúdo visual antigo (mantém Resources para fontes/formulários)
                destPage.GetPdfObject().Put(PdfName.Contents, new PdfArray());

                // Obter página OCR como XObject
                var ocrPage = srcOcr.GetPage(i);
                var ocrXObject = ocrPage.CopyAsFormXObject(dest);

                // Desenhar OCR no fundo
                var canvas = new PdfCanvas(destPage);
                var origBox = destPage.GetMediaBox();
                canvas.AddXObjectFittedIntoRectangle(ocrXObject, origBox);
            }
        }

        // =========================================================================
        // Helper: Executar processo externo
        // =========================================================================
        private static void RunProcess(string exe, string args, string toolName)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception($"Falha ao iniciar processo: {exe}");
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string err = process.StandardError.ReadToEnd();
                throw new Exception($"{toolName} falhou (código {process.ExitCode}): {err}");
            }
        }
    }
}