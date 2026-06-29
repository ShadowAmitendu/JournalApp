using System;
using System.Collections.Generic;

namespace JournalApp
{
    public class AIChatMessage
    {
        public string Text { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public DateTime DateSent { get; set; } = DateTime.Now;
    }

    public class AIChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Chat";
        public DateTime DateCreated { get; set; } = DateTime.Now;
        public List<AIChatMessage> Messages { get; set; } = new List<AIChatMessage>();
        public string DisplayDate => DateCreated.ToString("yyyy-MM-dd HH:mm");
    }
}
