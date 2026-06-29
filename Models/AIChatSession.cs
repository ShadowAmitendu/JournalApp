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

    public class AIChatSession : System.ComponentModel.INotifyPropertyChanged
    {
        private string _title = "New Chat";
        private DateTime _dateCreated = DateTime.Now;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime DateCreated
        {
            get => _dateCreated;
            set
            {
                if (_dateCreated != value)
                {
                    _dateCreated = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayDate));
                }
            }
        }

        public List<AIChatMessage> Messages { get; set; } = new List<AIChatMessage>();
        public string DisplayDate => DateCreated.ToString("yyyy-MM-dd HH:mm");

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
