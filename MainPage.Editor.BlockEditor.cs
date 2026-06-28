// MainPage.Editor.BlockEditor.cs
// Block editor (WebView2 + editor.html) bridge — initialisation, load, save, messaging.
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── State ────────────────────────────────────────────────────────────
        private bool _blockEditorReady = false;
        private string _pendingMarkdownLoad = null;   // markdown to inject once WV2 is ready
        private string _currentMarkdownContent = "";  // last known markdown from WV2

        // ── Initialise WebView2 ──────────────────────────────────────────────
        private async Task InitBlockEditorAsync()
        {
            if (_blockEditorReady) return;

            try
            {
                await NoteEditorWebView.EnsureCoreWebView2Async();

                // Allow local file access (editor.html references no external resources)
                NoteEditorWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                NoteEditorWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                NoteEditorWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                NoteEditorWebView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                NoteEditorWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                NoteEditorWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

                // Subscribe to messages from the JS editor
                NoteEditorWebView.CoreWebView2.WebMessageReceived += BlockEditor_WebMessageReceived;

                // Navigate to the bundled editor.html
                string editorPath = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path,
                                                  "Assets", "editor.html");
                NoteEditorWebView.CoreWebView2.Navigate("file:///" + editorPath.Replace("\\", "/"));

                // Wait for navigation to complete before marking ready
                NoteEditorWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _blockEditorReady = true;

                    // Apply current theme
                    var theme = this.ActualTheme == ElementTheme.Dark ? "dark" : "light";
                    _ = NoteEditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editor.setTheme('{theme}')");

                    // Inject any pending content
                    if (_pendingMarkdownLoad != null)
                    {
                        var mdJson = JsonSerializer.Serialize(_pendingMarkdownLoad);
                        _ = NoteEditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editor.setContent({mdJson})");
                        _pendingMarkdownLoad = null;
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BlockEditor] Init failed: {ex.Message}");
            }
        }

        // ── Load a note into the block editor ───────────────────────────────
        private async Task LoadMarkdownNoteAsync(JournalNote note)
        {
            string markdown = "";
            try
            {
                string mdPath = GetMarkdownFilePath(note);
                if (File.Exists(mdPath))
                {
                    byte[] raw = File.ReadAllBytes(mdPath);

                    // Decrypt if needed
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
                System.Diagnostics.Debug.WriteLine($"[BlockEditor] Load error: {ex.Message}");
            }

            _currentMarkdownContent = markdown;

            // Show WebView2, hide RichEditBox
            NoteRichEditBox.Visibility = Visibility.Collapsed;
            NoteEditorWebView.Visibility = Visibility.Visible;

            await InitBlockEditorAsync();

            if (_blockEditorReady)
            {
                var mdJson = JsonSerializer.Serialize(markdown);
                await NoteEditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editor.setContent({mdJson})");
            }
            else
            {
                _pendingMarkdownLoad = markdown;
            }

            _disableSavingCurrentNote = false;
        }

        // ── Save the block editor content ────────────────────────────────────
        private void SaveMarkdownNoteContent()
        {
            if (SelectedNote == null || _disableSavingCurrentNote) return;
            if (string.IsNullOrEmpty(_currentMarkdownContent) && SelectedNote.ContentFormat == "markdown") return;

            try
            {
                string mdPath = GetMarkdownFilePath(SelectedNote);
                byte[] mdBytes = System.Text.Encoding.UTF8.GetBytes(_currentMarkdownContent);

                if (_lockedCategories.Contains(SelectedNote.Category) && !string.IsNullOrEmpty(_masterPassword))
                    mdBytes = EncryptionHelper.Encrypt(mdBytes, _masterPassword);

                File.WriteAllBytes(mdPath, mdBytes);

                // Update snippet from first non-empty line of markdown
                string plainText = _currentMarkdownContent
                    .Replace("#", "").Replace(">", "").Replace("- ", "").Replace("```", "")
                    .Trim();
                SelectedNote.Snippet = string.IsNullOrWhiteSpace(plainText) ? "No additional text"
                    : (plainText.Length > 80 ? plainText.Substring(0, 80).Replace("\n", " ").Trim() : plainText.Replace("\n", " ").Trim());

                // Extract hashtags
                var matches = System.Text.RegularExpressions.Regex.Matches(_currentMarkdownContent, @"\B#([a-zA-Z0-9_]+)");
                var tags = SelectedNote.Tags ?? new System.Collections.Generic.List<string>();
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string tag = m.Groups[1].Value.ToLowerInvariant();
                    if (!tags.Contains(tag)) tags.Add(tag);
                }
                SelectedNote.Tags = tags;

                SelectedNote.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "Untitled Note" : TitleTextBox.Text.Trim();
                SelectedNote.DateModified = DateTime.Now;
                JournalManager.Instance.SaveNotesMetadata();

                var current = SelectedNote;
                LoadCategoriesList();
                RefreshNotesList();
                SelectedNote = current;

                if (StatusMessageTextBlock != null)
                    StatusMessageTextBlock.Text = $"Saved at {DateTime.Now:h:mm:ss tt}";
                _isDirty = false;
                UpdateTitleBarBackupButtonState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BlockEditor] Save error: {ex.Message}");
            }
        }

        // ── Message from JS: content changed ────────────────────────────────
        private void BlockEditor_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<BlockEditorMessage>(e.WebMessageAsJson);
                if (msg?.type == "contentChanged")
                {
                    _currentMarkdownContent = msg.markdown ?? "";
                    _isDirty = true;
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        SaveMarkdownNoteContent();
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BlockEditor] Message parse error: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private string GetMarkdownFilePath(JournalNote note)
        {
            if (string.IsNullOrEmpty(note.MarkdownContentFileName))
                note.MarkdownContentFileName = $"note_{note.Id}.md";

            return Path.Combine(JournalManager.Instance.NotesDir, note.MarkdownContentFileName);
        }

        private void SwitchToBlockEditorForNewNote(JournalNote note)
        {
            note.ContentFormat = "markdown";
            note.MarkdownContentFileName = $"note_{note.Id}.md";
        }

        // ── Theme sync ───────────────────────────────────────────────────────
        private async void SyncBlockEditorTheme()
        {
            if (!_blockEditorReady) return;
            var theme = this.ActualTheme == ElementTheme.Dark ? "dark" : "light";
            await NoteEditorWebView.CoreWebView2.ExecuteScriptAsync($"window.editor.setTheme('{theme}')");
        }
    }

    // ── Message model ────────────────────────────────────────────────────────
    internal class BlockEditorMessage
    {
        public string type { get; set; }
        public string markdown { get; set; }
    }
}
