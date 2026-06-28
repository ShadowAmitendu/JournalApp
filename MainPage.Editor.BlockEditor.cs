// MainPage.Editor.BlockEditor.cs
// 100% Native C# Block Editor — parses markdown, builds WinUI control stacks, and manages block editing.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JournalApp
{
    public class EditorBlock
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; } = "paragraph"; // paragraph, h1, h2, h3, bullet, numbered, todo, quote
        public string Content { get; set; } = "";
        public bool IsChecked { get; set; } = false; // For todo items
    }

    public sealed partial class MainPage
    {
        // ── Native Block Editor Fields ───────────────────────────────────────
        private List<EditorBlock> _nativeBlocks = new();
        private TextBox _activeBlockTextBox = null;
        private string _currentMarkdownContent = "";


        // ── Load Markdown note into Native Editor ───────────────────────────
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
                System.Diagnostics.Debug.WriteLine($"[NativeBlockEditor] Load error: {ex.Message}");
            }

            _currentMarkdownContent = markdown;

            // Show Native Scroll, hide WebView2 and RichEditBox
            if (NoteRichEditBox != null) NoteRichEditBox.Visibility = Visibility.Collapsed;
            if (NoteEditorWebView != null) NoteEditorWebView.Visibility = Visibility.Collapsed;
            if (NativeBlockEditorScroll != null) NativeBlockEditorScroll.Visibility = Visibility.Visible;

            // Import markdown into block list
            ImportMarkdownToBlocks(markdown);

            // Dynamically build and render the UI elements in the container
            RenderNativeBlocks();

            // Auto-focus the first block so cursor/keyboard is shown immediately
            if (_nativeBlocks.Count > 0)
            {
                FocusBlock(0);
            }

            _disableSavingCurrentNote = false;
            await Task.CompletedTask;
        }

        // ── Save Native Editor Content to file ──────────────────────────────
        private void SaveMarkdownNoteContent()
        {
            if (SelectedNote == null || _disableSavingCurrentNote) return;

            try
            {
                _currentMarkdownContent = ExportBlocksToMarkdown();
                string mdPath = GetMarkdownFilePath(SelectedNote);
                byte[] mdBytes = System.Text.Encoding.UTF8.GetBytes(_currentMarkdownContent);

                if (_lockedCategories.Contains(SelectedNote.Category) && !string.IsNullOrEmpty(_masterPassword))
                    mdBytes = EncryptionHelper.Encrypt(mdBytes, _masterPassword);

                File.WriteAllBytes(mdPath, mdBytes);

                // Update snippet from first non-empty block content
                var firstTextBlock = _nativeBlocks.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Content));
                string plainText = firstTextBlock?.Content ?? "";
                SelectedNote.Snippet = string.IsNullOrWhiteSpace(plainText) ? "No additional text"
                    : (plainText.Length > 80 ? plainText.Substring(0, 80).Replace("\n", " ").Trim() : plainText.Replace("\n", " ").Trim());

                // Extract hashtags
                var matches = System.Text.RegularExpressions.Regex.Matches(_currentMarkdownContent, @"\B#([a-zA-Z0-9_]+)");
                var tags = SelectedNote.Tags ?? new List<string>();
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
                System.Diagnostics.Debug.WriteLine($"[NativeBlockEditor] Save error: {ex.Message}");
            }
        }

        // ── Dynamic Rendering of Blocks ─────────────────────────────────────
        private void RenderNativeBlocks()
        {
            if (NativeBlockEditorContainer == null) return;
            NativeBlockEditorContainer.Children.Clear();

            for (int i = 0; i < _nativeBlocks.Count; i++)
            {
                var block = _nativeBlocks[i];
                var blockUI = CreateBlockControl(block, i);
                NativeBlockEditorContainer.Children.Add(blockUI);
            }
        }

        private UIElement CreateBlockControl(EditorBlock block, int index)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left Side: Type indicators or checkmarks
            FrameworkElement prefixElement = null;
            double fontSize = 16;
            var fontWeight = Microsoft.UI.Text.FontWeights.Normal;
            var fontStyle = Windows.UI.Text.FontStyle.Normal;
            var padding = new Thickness(6, 4, 6, 4);
            Brush foregroundBrush = GetThemeBrush("TextFillColorPrimaryBrush", "#1a1a1a");

            if (block.Type == "h1")
            {
                fontSize = 24;
                fontWeight = Microsoft.UI.Text.FontWeights.Bold;
                prefixElement = new TextBlock
                {
                    Text = "H1",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#808080"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
            }
            else if (block.Type == "h2")
            {
                fontSize = 20;
                fontWeight = Microsoft.UI.Text.FontWeights.Bold;
                prefixElement = new TextBlock
                {
                    Text = "H2",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#808080"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
            }
            else if (block.Type == "h3")
            {
                fontSize = 18;
                fontWeight = Microsoft.UI.Text.FontWeights.Bold;
                prefixElement = new TextBlock
                {
                    Text = "H3",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#808080"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
            }
            else if (block.Type == "bullet")
            {
                prefixElement = new FontIcon
                {
                    Glyph = "\uE7C8", // Bullet dot icon
                    FontSize = 8,
                    Foreground = GetThemeBrush("TextFillColorPrimaryBrush", "#1a1a1a"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                };
            }
            else if (block.Type == "numbered")
            {
                prefixElement = new TextBlock
                {
                    Text = $"{index + 1}.",
                    FontSize = 14,
                    Foreground = GetThemeBrush("TextFillColorPrimaryBrush", "#1a1a1a"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 8, 0)
                };
            }
            else if (block.Type == "todo")
            {
                var chk = new CheckBox
                {
                    IsChecked = block.IsChecked,
                    MinWidth = 0,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chk.Checked += (s, e) => { block.IsChecked = true; MarkDirty(); SaveMarkdownNoteContent(); };
                chk.Unchecked += (s, e) => { block.IsChecked = false; MarkDirty(); SaveMarkdownNoteContent(); };
                prefixElement = chk;
            }
            else if (block.Type == "quote")
            {
                fontStyle = Windows.UI.Text.FontStyle.Italic;
                foregroundBrush = GetThemeBrush("TextFillColorSecondaryBrush", "#555555");
                prefixElement = new Border
                {
                    Width = 3,
                    Background = GetThemeBrush("AccentFillColorDefaultBrush", "#6366f1"),
                    Margin = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Stretch
                };
            }

            if (prefixElement != null)
            {
                Grid.SetColumn(prefixElement, 0);
                grid.Children.Add(prefixElement);
            }

            // Editable TextBox
            var tb = new TextBox
            {
                Text = block.Content,
                FontSize = fontSize,
                FontWeight = fontWeight,
                FontStyle = fontStyle,
                Foreground = foregroundBrush,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false, // Enter inserts next block
                BorderThickness = new Thickness(0),
                Padding = padding,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = index
            };

            if (index == 0 && string.IsNullOrEmpty(block.Content))
            {
                tb.PlaceholderText = "Start writing your thoughts here...";
            }
            else if (string.IsNullOrEmpty(block.Content))
            {
                tb.PlaceholderText = block.Type == "todo" ? "To-do item" :
                                     block.Type == "bullet" ? "List item" :
                                     block.Type == "numbered" ? "List item" :
                                     block.Type == "quote" ? "Quote" : "";
            }

            // Remove box background on hover and focus
            tb.Resources.Add("TextControlBackground", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
            tb.Resources.Add("TextControlBackgroundPointerOver", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
            tb.Resources.Add("TextControlBackgroundFocused", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
            tb.Resources.Add("TextControlBorderBrush", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
            tb.Resources.Add("TextControlBorderBrushPointerOver", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
            tb.Resources.Add("TextControlBorderBrushFocused", new SolidColorBrush(Microsoft.UI.Colors.Transparent));

            // Sync dynamic fonts
            ApplyFontToControl(tb);

            tb.GotFocus += (s, e) =>
            {
                _activeBlockTextBox = tb;
            };

            tb.TextChanged += (s, e) =>
            {
                if (tb.Tag is int idx && idx < _nativeBlocks.Count)
                {
                    _nativeBlocks[idx].Content = tb.Text;
                    _currentMarkdownContent = ExportBlocksToMarkdown();
                    MarkDirty();
                    UpdateWordCount();
                }
            };

            tb.KeyDown += (s, e) =>
            {
                int idx = (int)tb.Tag;

                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    e.Handled = true;
                    var newBlock = new EditorBlock { Type = "paragraph", Content = "" };
                    _nativeBlocks.Insert(idx + 1, newBlock);
                    _currentMarkdownContent = ExportBlocksToMarkdown();
                    MarkDirty();
                    RenderNativeBlocks();
                    FocusBlock(idx + 1);
                }
                else if (e.Key == Windows.System.VirtualKey.Back)
                {
                    if (tb.SelectionStart == 0 && tb.SelectionLength == 0)
                    {
                        e.Handled = true;
                        if (idx > 0)
                        {
                            var prevBlock = _nativeBlocks[idx - 1];
                            int originalLength = prevBlock.Content.Length;
                            prevBlock.Content += tb.Text;
                            _nativeBlocks.RemoveAt(idx);
                            _currentMarkdownContent = ExportBlocksToMarkdown();
                            MarkDirty();
                            RenderNativeBlocks();
                            FocusBlock(idx - 1, originalLength);
                        }
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.Up)
                {
                    if (idx > 0)
                    {
                        e.Handled = true;
                        FocusBlock(idx - 1);
                    }
                }
                else if (e.Key == Windows.System.VirtualKey.Down)
                {
                    if (idx < _nativeBlocks.Count - 1)
                    {
                        e.Handled = true;
                        FocusBlock(idx + 1);
                    }
                }
            };

            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            return grid;
        }

        private void FocusBlock(int index, int selectionStart = 0)
        {
            if (NativeBlockEditorContainer == null || index < 0 || index >= NativeBlockEditorContainer.Children.Count) return;

            var grid = NativeBlockEditorContainer.Children[index] as Grid;
            if (grid != null)
            {
                foreach (var child in grid.Children)
                {
                    if (child is TextBox tb)
                    {
                        tb.Focus(FocusState.Programmatic);
                        tb.SelectionStart = Math.Min(selectionStart, tb.Text.Length);
                        tb.SelectionLength = 0;
                        break;
                    }
                }
            }
        }

        // ── Toolbar Formatting Helpers ──────────────────────────────────────
        private void FormatActiveTextBoxSelection(string prefix, string suffix)
        {
            if (_activeBlockTextBox == null) return;

            int start = _activeBlockTextBox.SelectionStart;
            int len = _activeBlockTextBox.SelectionLength;
            string text = _activeBlockTextBox.Text;

            string selectedText = text.Substring(start, len);
            string newText = text.Remove(start, len).Insert(start, prefix + selectedText + suffix);

            _activeBlockTextBox.Text = newText;
            _activeBlockTextBox.SelectionStart = start + prefix.Length;
            _activeBlockTextBox.SelectionLength = len;
            MarkDirty();
        }

        private void ChangeActiveBlockType(string newType)
        {
            if (_activeBlockTextBox == null) return;
            if (_activeBlockTextBox.Tag is int idx && idx < _nativeBlocks.Count)
            {
                _nativeBlocks[idx].Type = newType;
                _currentMarkdownContent = ExportBlocksToMarkdown();
                MarkDirty();
                RenderNativeBlocks();
                FocusBlock(idx);
            }
        }

        // ── Markdown Parser / Serializer ────────────────────────────────────
        private void ImportMarkdownToBlocks(string markdown)
        {
            _nativeBlocks.Clear();
            if (string.IsNullOrEmpty(markdown))
            {
                _nativeBlocks.Add(new EditorBlock { Type = "paragraph", Content = "" });
                return;
            }

            var lines = markdown.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd('\r');
                var block = new EditorBlock();

                if (trimmed.StartsWith("# "))
                {
                    block.Type = "h1";
                    block.Content = trimmed.Substring(2);
                }
                else if (trimmed.StartsWith("## "))
                {
                    block.Type = "h2";
                    block.Content = trimmed.Substring(3);
                }
                else if (trimmed.StartsWith("### "))
                {
                    block.Type = "h3";
                    block.Content = trimmed.Substring(4);
                }
                else if (trimmed.StartsWith("> "))
                {
                    block.Type = "quote";
                    block.Content = trimmed.Substring(2);
                }
                else if (trimmed.StartsWith("- [ ] ") || trimmed.StartsWith("* [ ] "))
                {
                    block.Type = "todo";
                    block.IsChecked = false;
                    block.Content = trimmed.Substring(6);
                }
                else if (trimmed.StartsWith("- [x] ") || trimmed.StartsWith("* [x] ") ||
                         trimmed.StartsWith("- [X] ") || trimmed.StartsWith("* [X] "))
                {
                    block.Type = "todo";
                    block.IsChecked = true;
                    block.Content = trimmed.Substring(6);
                }
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    block.Type = "bullet";
                    block.Content = trimmed.Substring(2);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\.\s+"))
                {
                    block.Type = "numbered";
                    int idx = trimmed.IndexOf('.');
                    block.Content = trimmed.Substring(idx + 1).TrimStart();
                }
                else
                {
                    block.Type = "paragraph";
                    block.Content = trimmed;
                }

                _nativeBlocks.Add(block);
            }

            if (_nativeBlocks.Count == 0)
            {
                _nativeBlocks.Add(new EditorBlock { Type = "paragraph", Content = "" });
            }
        }

        private string ExportBlocksToMarkdown()
        {
            var lines = new List<string>();
            foreach (var block in _nativeBlocks)
            {
                string line = "";
                if (block.Type == "h1")
                    line = $"# {block.Content}";
                else if (block.Type == "h2")
                    line = $"## {block.Content}";
                else if (block.Type == "h3")
                    line = $"### {block.Content}";
                else if (block.Type == "quote")
                    line = $"> {block.Content}";
                else if (block.Type == "todo")
                    line = $"- [{(block.IsChecked ? "x" : " ")}] {block.Content}";
                else if (block.Type == "bullet")
                    line = $"- {block.Content}";
                else if (block.Type == "numbered")
                    line = $"1. {block.Content}";
                else
                    line = block.Content;

                lines.Add(line);
            }
            return string.Join("\n", lines);
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

        // ── Theme / Font Sync hooks (keep stubs or adapt) ────────────────────
        private void SyncBlockEditorTheme()
        {
            RenderNativeBlocks();
        }

        private void SyncBlockEditorFont()
        {
            if (NativeBlockEditorContainer == null) return;
            foreach (var child in NativeBlockEditorContainer.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var gc in grid.Children)
                    {
                        if (gc is TextBox tb)
                        {
                            ApplyFontToControl(tb);
                        }
                    }
                }
            }
        }

        private void ApplyFontToControl(Control control)
        {
            string fontName = GetSetting("EditorFontFamily", "Segoe UI");
            if (EditorFontComboBox != null && EditorFontComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                fontName = selectedItem.Tag?.ToString() ?? fontName;
            }
            control.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontName);
        }

        private void NativeBlockEditorScroll_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_nativeBlocks.Count > 0)
            {
                FocusBlock(_nativeBlocks.Count - 1);
            }
        }
    }

    // Stub message class to avoid reference breaks if needed
    internal class BlockEditorMessage
    {
        public string type { get; set; }
        public string markdown { get; set; }
    }
}
