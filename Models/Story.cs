namespace WebApplication1.Models
{
    public class Story
    {
        public int Id { get; set; }

        // Content bucket this story belongs to
        public string Bucket { get; set; } = "featuredSideStories"; // heroStory | featuredSideStories | trendingStories | tickerItems

        public string Category { get; set; } = string.Empty;   // category / tag
        public string Title { get; set; } = string.Empty;
        public string Excerpt { get; set; } = string.Empty;    // body summary / excerpt
        public string Author { get; set; } = string.Empty;
        public int ReadTimeMinutes { get; set; } = 1;           // read time in minutes
        public string Tone { get; set; } = string.Empty;        // tone / theme token

        // Pinning — used for trendingStories ordering (pinned first, then latest updated_at)
        public bool IsPinned { get; set; } = false;
        public int PinOrder { get; set; } = 0;                  // lower = higher in list when pinned

        // Status: draft | published | archived
        public string Status { get; set; } = "draft";

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Minimal DTO for ticker items (text-only entries)
    public class TickerItem
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Status { get; set; } = "published";
        public int SortOrder { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Request DTOs
    public class CreateStoryRequest
    {
        public string Bucket { get; set; } = "featuredSideStories";
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Excerpt { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public int ReadTimeMinutes { get; set; } = 1;
        public string Tone { get; set; } = string.Empty;
        public bool IsPinned { get; set; } = false;
        public int PinOrder { get; set; } = 0;
        public string Status { get; set; } = "draft";
        public DateTime? PublishedAt { get; set; }
    }

    public class UpdateStoryRequest : CreateStoryRequest { }

    public class CreateTickerItemRequest
    {
        public string Text { get; set; } = string.Empty;
        public string Status { get; set; } = "published";
        public int SortOrder { get; set; } = 0;
    }

    public class UpdateTickerItemRequest : CreateTickerItemRequest { }
}
