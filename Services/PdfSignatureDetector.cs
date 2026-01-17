using iText.Kernel.Pdf;
using iText.Signatures;

namespace pdf_ocr.Services
{
    public static class PdfSignatureDetector
    {
        public static bool HasDigitalSignatures(string pdfPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                    return false;

                using var reader = new PdfReader(pdfPath);
                using var doc = new PdfDocument(reader);

                var util = new SignatureUtil(doc);
                var names = util.GetSignatureNames();
                return names != null && names.Count > 0;
            }
            catch
            {
                // If the PDF is encrypted/corrupt or iText can't parse signatures,
                // we fail closed (treat as unsigned) to avoid breaking processing.
                return false;
            }
        }
    }
}
