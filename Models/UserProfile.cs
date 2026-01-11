namespace pdf_ocr.Models
{
    public class UserProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public int Credits { get; set; }
        public string Plan { get; set; } = "free"; // free, pro, business
        public DateTime CreatedAt { get; set; }
        public DateTime? SubscriptionEndsAt { get; set; }
    }

}
