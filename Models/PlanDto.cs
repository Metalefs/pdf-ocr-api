// Controllers/PaymentController.cs
namespace pdf_ocr.Models
{
    internal class PlanDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Price { get; set; }
        public int Credits { get; set; }
        public string[] Features { get; set; }
    }
}