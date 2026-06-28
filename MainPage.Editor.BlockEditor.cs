// MainPage.Editor.BlockEditor.cs
// Slim adapter that wires the MarkdownEditorBox control to the note load/save pipeline.
// All block-rendering logic now lives in Controls/MarkdownEditorBox.cs.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── Load markdown note into the MarkdownEditorBox ────────────────────────
        private async Task LoadMarkdownNoteAsync(JournalNote note)
        {
            _disableSavingCurrentNote = true;
            string markdown = "";

            try
            {
                string mdPath = GetMarkdownFilePath(note);
                if (File.Exists(mdPath))
                {
                    byte[] raw = File.ReadAllBytes(mdPath);

                    // Decrypt if the category is locked
                    if (_lockedCategories.Contains(note.Category) && !string.IsNullOrEmpty(_masterPassword))
                    {
                        try { raw = EncryptionHelper.Decrypt(raw, _masterPassword); }
                        catch { raw = System.Text.Encoding.UTF8.GetBytes("**[Decryption failed]**"); }
                    }

                    markdown = System.Text.Encoding.UTF8.GetString(raw);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownEditor] Load error: {ex.Message}");
            }

            _currentMarkdownContent = markdown;

            // Show the MarkdownEditorBox, hide the legacy RichEditBox and WebView2
            if (NoteRichEditBox   != null) NoteRichEditBox.Visibility   = Visibility.Collapsed;
            if (NoteEditorWebView != null) NoteEditorWebView.Visibility = Visibility.Collapsed;
            if (NativeBlockEditorScroll != null)
            {
                NativeBlockEditorScroll.Visibility = Visibility.Visible;
                NativeBlockEditorScroll.LoadMarkdown(markdown);
            }

            _disableSavingCurrentNote = false;
            await Task.CompletedTask;
        }

        // ── Save markdown note from the MarkdownEditorBox ────────────────────────
        private void SaveMarkdownNoteContent()
        {
            if (SelectedNote == null || _disableSavingCurrentNote) return;

            try
            {
                // Read raw markdown from the editor
                _currentMarkdownContent = NativeBlockEditorScroll?.RawMarkdown ?? "";

                string mdPath  = GetMarkdownFilePath(SelectedNote);
                byte[] mdBytes = System.Text.Encoding.UTF8.GetBytes(_currentMarkdownContent);

                if (_lockedCategories.Contains(SelectedNote.Category) && !string.IsNullOrEmpty(_masterPassword))
                    mdBytes = EncryptionHelper.Encrypt(mdBytes, _masterPassword);

                File.WriteAllBytes(mdPath, mdBytes);

                // Update snippet (first non-empty line, stripped of markdown markers)
                string firstLine = _currentMarkdownContent
                    .Split('\n')
                    .Select(l => l.TrimStart('#', '-', '*', '>', '0','1','2','3','4','5','6','7','8','9','.', ' '))
                    .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
                SelectedNote.Snippet = string.IsNullOrWhiteSpace(firstLine) ? "No additional text"
                    : (firstLine.Length > 80 ? firstLine.Substring(0, 80) + "…" : firstLine);

                // Extract hashtags
                var matches = System.Text.RegularExpressions.Regex.Matches(_currentMarkdownContent, @"\B#([a-zA-Z0-9_]+)");
                var tags    = SelectedNote.Tags ?? new System.Collections.Generic.List<string>();
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string tag = m.Groups[1].Value.ToLowerInvariant();
                    if (!tags.Contains(tag)) tags.Add(tag);
                }
                SelectedNote.Tags         = tags;
                SelectedNote.Title        = string.IsNullOrWhiteSpace(TitleTextBox?.Text) ? "Untitled Note" : TitleTextBox.Text.Trim();
                SelectedNote.DateModified = DateTime.Now;
                JournalManager.Instance.SaveNotesMetadata();

                var current = SelectedNote;
                _disableSavingCurrentNote = true;
                _isSelectingNote = true;
                _isNavigating = true;
                try
                {
                    LoadCategoriesList();
                    RefreshNotesList();
                    SelectedNote = current;
                }
                finally
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        _disableSavingCurrentNote = false;
                        _isSelectingNote = false;
                        _isNavigating = false;
                    });
                }

                if (StatusMessageTextBlock != null)
                    StatusMessageTextBlock.Text = $"Saved at {DateTime.Now:h:mm:ss tt}";

                _isDirty = false;
                UpdateTitleBarBackupButtonState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownEditor] Save error: {ex.Message}");
            }
        }

        // ── Wire TextChanged → autosave ──────────────────────────────────────────
        // Called from MainPage constructor / Loaded event after XAML tree is ready
        private void InitMarkdownEditorEvents()
        {
            if (NativeBlockEditorScroll == null) return;
            NativeBlockEditorScroll.TextChanged += (s, e) =>
            {
                if (_disableSavingCurrentNote || !_isDataLoaded) return;
                _currentMarkdownContent = NativeBlockEditorScroll.RawMarkdown ?? "";
                MarkDirty();
                UpdateWordCount();
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private string GetMarkdownFilePath(JournalNote note)
        {
            if (string.IsNullOrEmpty(note.MarkdownContentFileName))
                note.MarkdownContentFileName = $"note_{note.Id}.md";
            return Path.Combine(JournalManager.Instance.NotesDir, note.MarkdownContentFileName);
        }

        private void SwitchToBlockEditorForNewNote(JournalNote note)
        {
            note.ContentFormat            = "markdown";
            note.MarkdownContentFileName  = $"note_{note.Id}.md";
        }

        // Stub: font sync now handled by MarkdownEditorBox internally
        private void SyncBlockEditorFont() { }
        private void SyncBlockEditorTheme() { }

        // Stub: no longer needed — MarkdownEditorBox doesn't use per-block TextBoxes
        private void NativeBlockEditorScroll_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) { }

        // Stub class kept to avoid any lingering reference breaks
        private void FormatActiveTextBoxSelection(string prefix, string suffix)
        {
            NativeBlockEditorScroll?.WrapSelection(prefix, suffix);
        }

        private void ChangeActiveBlockType(string newType)
        {
            // Block types are expressed via inline markdown syntax in MarkdownEditorBox.
        }

        private void InsertLinePrefix(string marker)
        {
            NativeBlockEditorScroll?.ToggleLinePrefix(marker);
        }
    }

    // Stub — kept so any lingering references compile
    internal class BlockEditorMessage
    {
        public string type     { get; set; }
        public string markdown { get; set; }
    }
}
