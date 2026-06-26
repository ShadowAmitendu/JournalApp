using System;

namespace JournalApp
{
    public class JournalNote
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "Untitled Note";
        public string Category { get; set; } = "All Entries";
        public DateTime DateCreated { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public string HeroImagePath { get; set; } // Can be a URL, absolute local path, or filename under Media/
        public double CoverOffsetY { get; set; } = 0;
        public double CoverBrightness { get; set; } = 100;
        public double CoverBlur { get; set; } = 0;
        public string? CoverAttributionText { get; set; }
        public string? CoverAttributionUrl { get; set; }
        public string RtfFileName { get; set; } // e.g., "note_<id>.rtf"
        public bool IsFavorite { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsPinned { get; set; }
        public bool HasTime { get; set; } = true;
        public string EditorWidth { get; set; } = "Medium";
        public string AvatarImagePath => !string.IsNullOrEmpty(HeroImagePath) 
            ? HeroImagePath 
            : $"https://picsum.photos/seed/{Id}/100";

        // Non-persisted helper properties for UI bindings
        public string DateModifiedFormatted => DateModified.ToString("MMM d, yyyy h:mm tt");
        public string DateDayOfWeek => DateCreated.ToString("ddd").ToUpper();
        public string DateDayNumber => DateCreated.ToString("dd");
        public string DateMonthYear => DateCreated.ToString("MMM yyyy").ToUpper();
        
        // Return a snippet of content or placeholder. We will update this from the UI side.
        public string Snippet { get; set; } = "No additional text";

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsCategoryHeaderVisible { get; set; }
        
        [System.Text.Json.Serialization.JsonIgnore]
        public string CategoryHeaderName { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonIgnore]
        public string CategoryHeaderColor { get; set; } = "#808080";
        
        [System.Text.Json.Serialization.JsonIgnore]
        public string CategoryHeaderIcon { get; set; } = "\uE889";

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsBottomDividerVisible { get; set; } = true;
    }

    public class JournalCategory
    {
        public string Name { get; set; }
        public string Icon { get; set; } // Segoe MDL2 Assets glyph string (e.g. "\uE889")
        public string Color { get; set; } // Hex color string (e.g. "#FF0000")
    }
}
