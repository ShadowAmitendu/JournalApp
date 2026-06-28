using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JournalApp
{
    public class JournalManager
    {
        private static JournalManager _instance;
        public static JournalManager Instance => _instance ??= new JournalManager();

        public string DataDir { get; }
        public string NotesDir { get; }
        public string MediaDir { get; }

        public List<JournalNote> Notes { get; private set; } = new List<JournalNote>();
        public List<JournalCategory> Categories { get; private set; } = new List<JournalCategory>();

        private JournalManager()
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JournalApp");
            NotesDir = Path.Combine(DataDir, "Notes");
            MediaDir = Path.Combine(DataDir, "Media");

            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(NotesDir);
            Directory.CreateDirectory(MediaDir);

            LoadCategories();
            LoadNotes();
        }

        private void LoadCategories()
        {
            string path = Path.Combine(DataDir, "categories.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    Categories = JsonSerializer.Deserialize<List<JournalCategory>>(json) ?? new List<JournalCategory>();

                    // Migrate legacy incorrect icons and assign default colors if missing
                    bool migrated = false;
                    foreach (var category in Categories)
                    {
                        if (category.Name == "All Entries")
                        {
                            if (category.Icon == "\uE889") { category.Icon = "\uE80F"; migrated = true; }
                            if (string.IsNullOrEmpty(category.Color)) { category.Color = "#0078D4"; migrated = true; }
                        }
                        else if (category.Name == "Personal")
                        {
                            if (category.Icon == "\uE8B7") { category.Icon = "\uE77B"; migrated = true; }
                            if (string.IsNullOrEmpty(category.Color)) { category.Color = "#E3008C"; migrated = true; }
                        }
                        else if (category.Name == "Work")
                        {
                            if (category.Icon == "\uE8A1") { category.Icon = "\uE821"; migrated = true; }
                            if (string.IsNullOrEmpty(category.Color)) { category.Color = "#107C41"; migrated = true; }
                        }
                        else if (category.Name == "Ideas")
                        {
                            if (category.Icon == "\uE9A1") { category.Icon = "\uEA80"; migrated = true; }
                            if (string.IsNullOrEmpty(category.Color)) { category.Color = "#FFB900"; migrated = true; }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(category.Color)) { category.Color = "#8A8886"; migrated = true; } // Default gray
                        }
                    }
                    if (migrated)
                    {
                        SaveCategories();
                    }
                }
                catch
                {
                    InitDefaultCategories();
                }
            }
            else
            {
                InitDefaultCategories();
                SaveCategories();
            }
        }

        private void InitDefaultCategories()
        {
            Categories = new List<JournalCategory>
            {
                new JournalCategory { Name = "All Entries", Icon = "\uE80F", Color = "#0078D4" }, // Home
                new JournalCategory { Name = "Personal", Icon = "\uE77B", Color = "#E3008C" },    // Contact
                new JournalCategory { Name = "Work", Icon = "\uE821", Color = "#107C41" },        // Work / Briefcase
                new JournalCategory { Name = "Ideas", Icon = "\uEA80", Color = "#FFB900" }        // Lightbulb
            };
        }

        public void SaveCategories()
        {
            string path = Path.Combine(DataDir, "categories.json");
            string json = JsonSerializer.Serialize(Categories, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public void AddCategory(string name, string icon = "\uE8B7", string color = "#8A8886") // Default Folder icon, gray color
        {
            if (Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return;

            Categories.Add(new JournalCategory { Name = name, Icon = icon, Color = color });
            SaveCategories();
        }

        private void LoadNotes()
        {
            string path = Path.Combine(DataDir, "notes.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    Notes = JsonSerializer.Deserialize<List<JournalNote>>(json) ?? new List<JournalNote>();
                }
                catch
                {
                    Notes = new List<JournalNote>();
                }
            }
            else
            {
                Notes = new List<JournalNote>();
            }
        }

        public void SaveNotesMetadata()
        {
            string path = Path.Combine(DataDir, "notes.json");
            string json = JsonSerializer.Serialize(Notes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public JournalNote CreateNote(string category = "All Entries")
        {
            // If category is "All Entries", default to the first custom one or just "Personal"
            string targetCategory = (category == "All Entries") ? "Personal" : category;

            string noteId = Guid.NewGuid().ToString();

            // Read default editor style from settings
            string defaultStyle = "rtf";
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (localSettings.Values.TryGetValue("DefaultEditorStyle", out object val) && val is string str)
                {
                    defaultStyle = str;
                }
                else
                {
                    string path = Path.Combine(DataDir, "appsettings.json");
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null && dict.TryGetValue("DefaultEditorStyle", out string fallbackVal))
                        {
                            defaultStyle = fallbackVal;
                        }
                    }
                }
            }
            catch {}

            var note = new JournalNote
            {
                Id = noteId,
                Title = "New Journal Entry",
                Category = targetCategory,
                RtfFileName = $"note_{noteId}.rtf",          // kept for legacy compat
                ContentFormat = defaultStyle,
                MarkdownContentFileName = $"note_{noteId}.md"
            };

            Notes.Insert(0, note); // Newest at top
            SaveNotesMetadata();
            return note;
        }

        public void SoftDeleteNote(JournalNote note)
        {
            if (note == null) return;
            note.IsDeleted = true;
            SaveNotesMetadata();
        }

        public void RestoreNote(JournalNote note)
        {
            if (note == null) return;
            note.IsDeleted = false;
            SaveNotesMetadata();
        }

        public void PermanentlyDeleteNote(JournalNote note)
        {
            if (note == null) return;

            Notes.Remove(note);
            SaveNotesMetadata();

            // Delete RTF file
            string rtfPath = Path.Combine(NotesDir, note.RtfFileName);
            if (File.Exists(rtfPath))
            {
                try { File.Delete(rtfPath); } catch {}
            }

            // Delete attached media files (photos + audio)
            DeleteNoteMediaFiles(note);
        }

        public void EmptyTrash()
        {
            var deletedNotes = Notes.Where(n => n.IsDeleted).ToList();
            foreach (var note in deletedNotes)
            {
                Notes.Remove(note);
                // Delete RTF file
                string rtfPath = Path.Combine(NotesDir, note.RtfFileName);
                if (File.Exists(rtfPath))
                {
                    try { File.Delete(rtfPath); } catch {}
                }

                // Delete attached media files (photos + audio)
                DeleteNoteMediaFiles(note);
            }
            SaveNotesMetadata();
        }

        private void DeleteNoteMediaFiles(JournalNote note)
        {
            if (note.AttachedPhotoPaths != null)
            {
                foreach (var photoPath in note.AttachedPhotoPaths)
                {
                    string absPath = GetAbsoluteMediaPath(photoPath);
                    if (absPath != null && File.Exists(absPath))
                    {
                        try { File.Delete(absPath); } catch {}
                    }
                }
            }
            if (note.AttachedAudioPaths != null)
            {
                foreach (var audioPath in note.AttachedAudioPaths)
                {
                    string absPath = GetAbsoluteMediaPath(audioPath);
                    if (absPath != null && File.Exists(absPath))
                    {
                        try { File.Delete(absPath); } catch {}
                    }
                }
            }
        }

        public string CopyImageToLocalMedia(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return null;

            try
            {
                string ext = Path.GetExtension(sourcePath);
                string uniqueName = $"{Guid.NewGuid()}{ext}";
                string destinationPath = Path.Combine(MediaDir, uniqueName);
                File.Copy(sourcePath, destinationPath, true);
                
                // Return just the filename or relative path so that the app doesn't break if base path changes
                return uniqueName; 
            }
            catch
            {
                return null;
            }
        }

        public string GetAbsoluteMediaPath(string mediaFileName)
        {
            if (string.IsNullOrEmpty(mediaFileName)) return null;
            
            // If it is a web URL, return it as is
            if (mediaFileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                mediaFileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return mediaFileName;
            }

            // If it is already an absolute path, check and return
            if (Path.IsPathRooted(mediaFileName))
                return mediaFileName;

            return Path.Combine(MediaDir, mediaFileName);
        }

        public string GetAbsoluteRtfPath(string rtfFileName)
        {
            if (string.IsNullOrEmpty(rtfFileName)) return null;
            return Path.Combine(NotesDir, rtfFileName);
        }
    }
}
