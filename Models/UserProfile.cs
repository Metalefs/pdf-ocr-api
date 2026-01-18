using System.Text.Json.Serialization;

namespace pdf_ocr.Models
{
    public class UserProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string User_metadata { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public int Credits { get; set; }
        public string Plan { get; set; } = "free"; // free, pro, business
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? SubscriptionEndsAt { get; set; }
        public string? SubscriptionStatus { get; set; }
    }
    public class UserMetadata
    {
        public string avatar_url {get;set;}
        public string email {get;set;}
        public bool email_verified {get;set;}
        public string full_name {get;set;}
        public string iss {get;set;}
        public string name {get;set;}
        public bool phone_verified {get;set;}
        public string picture {get;set;}
        public string preferred_username {get;set;}
        public string provider_id {get;set;}
        public string sub {get;set;}
        public string user_name { get; set; }
    }
}
