using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        // ── Find & Replace Fields ──────────────────────────────────────────────

        /// <summary>
        /// List of text ranges matching the active search query.
        /// </summary>
        private List<Microsoft.UI.Text.ITextRange> _findMatches = new();

        /// <summary>
        /// Zero-based index of the currently highlighted search match.
        /// </summary>
        private int _findMatchIndex = -1;

        // ── Text Formatting Click Handlers ─────────────────────────────────────

        /// <summary>
        /// Handler to toggle bold text formatting on the active editor selection.
        /// </summary>
        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("**", "**");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            format.Bold = (format.Bold == FormatEffect.On) ? FormatEffect.Off : FormatEffect.On;
            NoteRichEditBox.Document.Selection.CharacterFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to toggle italic text formatting on the active editor selection.
        /// </summary>
        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("*", "*");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            format.Italic = (format.Italic == FormatEffect.On) ? FormatEffect.Off : FormatEffect.On;
            NoteRichEditBox.Document.Selection.CharacterFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to toggle underline text formatting on the active editor selection.
        /// </summary>
        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("<u>", "</u>");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            format.Underline = (format.Underline == UnderlineType.Single) ? UnderlineType.None : UnderlineType.Single;
            NoteRichEditBox.Document.Selection.CharacterFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to apply custom highlighter backcolors onto selected text.
        /// </summary>
        private void HighlightColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item)
            {
                var hexColor = item.Tag?.ToString();
                if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
                {
                    string colorValue = (string.IsNullOrEmpty(hexColor) || hexColor == "None") ? "transparent" : hexColor;
                    if (colorValue == "transparent")
                        FormatActiveTextBoxSelection("<mark>", "</mark>");
                    else
                        FormatActiveTextBoxSelection($"<mark style=\"background-color: {colorValue}\">", "</mark>");
                    return;
                }
                var format = NoteRichEditBox.Document.Selection.CharacterFormat;
                
                if (string.IsNullOrEmpty(hexColor) || hexColor == "None")
                {
                    format.BackgroundColor = Microsoft.UI.Colors.Transparent;
                }
                else
                {
                    try
                    {
                        var color = ParseHexColor(hexColor);
                        format.BackgroundColor = color;
                    }
                    catch
                    {
                        format.BackgroundColor = Microsoft.UI.Colors.Transparent;
                    }
                }
                
                NoteRichEditBox.Document.Selection.CharacterFormat = format;
                MarkDirty();
            }
        }

        /// <summary>
        /// Parses a hex color string into a Windows.UI.Color object.
        /// </summary>
        private Windows.UI.Color ParseHexColor(string hex)
        {
            hex = hex.Replace("#", "");
            byte a = 255;
            byte r = 255;
            byte g = 255;
            byte b = 255;
            
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }
            return Windows.UI.Color.FromArgb(a, r, g, b);
        }

        /// <summary>
        /// Handler to format the selected paragraph as left-aligned.
        /// </summary>
        private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("<div align=\"left\">", "</div>");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Left;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to format the selected paragraph as center-aligned.
        /// </summary>
        private void AlignCenterButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("<div align=\"center\">", "</div>");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Center;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to format the selected paragraph as right-aligned.
        /// </summary>
        private void AlignRightButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("<div align=\"right\">", "</div>");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Right;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to format the selected paragraph with justified alignment.
        /// </summary>
        private void AlignJustifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                FormatActiveTextBoxSelection("<div align=\"justify\">", "</div>");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Justify;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to toggle bullet lists formatting on paragraph items.
        /// </summary>
        private void BulletListButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                ChangeActiveBlockType("bullet");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.ListType = (format.ListType == MarkerType.Bullet) ? MarkerType.None : MarkerType.Bullet;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        /// <summary>
        /// Handler to toggle numbered lists formatting on paragraph items.
        /// </summary>
        private async void NumberedListButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                ChangeActiveBlockType("numbered");
                return;
            }
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            if (format.ListType == MarkerType.Arabic)
            {
                format.ListType = MarkerType.None;
                NoteRichEditBox.Document.Selection.ParagraphFormat = format;
                MarkDirty();
            }
            else
            {
                string result = await PromptForTextInputAsync("Numbered List", "Enter starting number (Default: 1):", "1");
                if (result != null)
                {
                    format.ListType = MarkerType.Arabic;
                    if (int.TryParse(result, out int startNum) && startNum >= 0)
                    {
                        format.ListStart = startNum;
                    }
                    else
                    {
                        format.ListStart = 1;
                    }
                    NoteRichEditBox.Document.Selection.ParagraphFormat = format;
                    MarkDirty();
                }
            }
        }

        /// <summary>
        /// Prepends a checklist checkbox marker (☐) at the editor cursor location.
        /// </summary>
        private void ChecklistButton_Click(object sender, RoutedEventArgs e)
        {
            if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
            {
                ChangeActiveBlockType("todo");
                return;
            }
            var selection = NoteRichEditBox.Document.Selection;
            int start = selection.StartPosition;
            
            selection.GetText(TextGetOptions.None, out string selectedText);
            if (string.IsNullOrEmpty(selectedText))
            {
                selection.SetText(TextSetOptions.None, "☐ ");
                selection.SetRange(start + 2, start + 2);
            }
            else
            {
                selection.SetText(TextSetOptions.None, $"☐ {selectedText}");
            }
            MarkDirty();
        }

        /// <summary>
        /// Handler to apply foreground text colors onto selected text.
        /// </summary>
        private void TextColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item)
            {
                var hexColor = item.Tag?.ToString();
                if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
                {
                    string colorValue = hexColor == "Default" ? "" : hexColor;
                    if (string.IsNullOrEmpty(colorValue))
                        FormatActiveTextBoxSelection("<span>", "</span>");
                    else
                        FormatActiveTextBoxSelection($"<span style=\"color: {colorValue}\">", "</span>");
                    return;
                }
                var format = NoteRichEditBox.Document.Selection.CharacterFormat;
                
                if (hexColor == "Default")
                {
                    // Reset to default theme color by looking up resource or fallback
                    if (Application.Current.Resources.TryGetValue("TextFillColorPrimary", out object val) && val is Windows.UI.Color c)
                        format.ForegroundColor = c;
                    else
                        format.ForegroundColor = Microsoft.UI.Colors.Black;
                }
                else
                {
                    try
                    {
                        var color = ParseHexColor(hexColor);
                        format.ForegroundColor = color;
                    }
                    catch
                    {
                        // Fallback
                    }
                }
                
                NoteRichEditBox.Document.Selection.CharacterFormat = format;
                MarkDirty();
            }
        }

        // ── Find & Replace Bar Logic ──────────────────────────────────────────

        /// <summary>
        /// Closes the Find & Replace floating utility bar.
        /// </summary>
        private void CloseFindBar()
        {
            if (FindReplaceBar != null) FindReplaceBar.Visibility = Visibility.Collapsed;
            _findMatches.Clear();
            _findMatchIndex = -1;
            if (MatchCountText != null) MatchCountText.Text = "";
        }

        /// <summary>
        /// Closes the Find & Replace bar click handler.
        /// </summary>
        private void CloseFindBarBtn_Click(object sender, RoutedEventArgs e) => CloseFindBar();

        /// <summary>
        /// Event listener searching matches incrementally while query text changes.
        /// </summary>
        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FindInNote();
        }

        /// <summary>
        /// Key handler supporting instant next-match highlighting on Enter key press.
        /// </summary>
        private void FindTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                FindNextMatch();
                e.Handled = true;
            }
        }

        private void PrevMatchBtn_Click(object sender, RoutedEventArgs e) => FindPrevMatch();
        private void NextMatchBtn_Click(object sender, RoutedEventArgs e) => FindNextMatch();

        /// <summary>
        /// Scans the document contents to extract matches of the specified query string.
        /// </summary>
        private void FindInNote()
        {
            _findMatches.Clear();
            _findMatchIndex = -1;

            string term = FindTextBox?.Text;
            if (string.IsNullOrEmpty(term) || SelectedNote == null)
            {
                if (MatchCountText != null) MatchCountText.Text = "";
                return;
            }

            try
            {
                NoteRichEditBox.Document.GetText(TextGetOptions.None, out string fullText);
                int searchFrom = 0;
                while (searchFrom < fullText.Length)
                {
                    int idx = fullText.IndexOf(term, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    var range = NoteRichEditBox.Document.GetRange(idx, idx + term.Length);
                    _findMatches.Add(range);
                    searchFrom = idx + 1;
                }

                if (MatchCountText != null)
                    MatchCountText.Text = _findMatches.Count == 0 ? "No results" : $"1 of {_findMatches.Count}";

                if (_findMatches.Count > 0)
                {
                    _findMatchIndex = 0;
                    HighlightCurrentMatch();
                }
            }
            catch { }
        }

        /// <summary>
        /// Cycles focus forward to highlight the next search match.
        /// </summary>
        private void FindNextMatch()
        {
            if (_findMatches.Count == 0) { FindInNote(); return; }
            _findMatchIndex = (_findMatchIndex + 1) % _findMatches.Count;
            HighlightCurrentMatch();
        }

        /// <summary>
        /// Cycles focus backward to highlight the previous search match.
        /// </summary>
        private void FindPrevMatch()
        {
            if (_findMatches.Count == 0) { FindInNote(); return; }
            _findMatchIndex = (_findMatchIndex - 1 + _findMatches.Count) % _findMatches.Count;
            HighlightCurrentMatch();
        }

        /// <summary>
        /// Sets selection boundaries onto the active search match text range.
        /// </summary>
        private void HighlightCurrentMatch()
        {
            if (_findMatchIndex < 0 || _findMatchIndex >= _findMatches.Count) return;
            var range = _findMatches[_findMatchIndex];
            NoteRichEditBox.Document.Selection.SetRange(range.StartPosition, range.EndPosition);
            if (MatchCountText != null)
                MatchCountText.Text = $"{_findMatchIndex + 1} of {_findMatches.Count}";
        }

        /// <summary>
        /// Replaces the currently highlighted search match with the replacement string.
        /// </summary>
        private void ReplaceOneBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_findMatches.Count == 0 || _findMatchIndex < 0) return;
            string replacement = ReplaceTextBox?.Text ?? "";
            var range = _findMatches[_findMatchIndex];
            range.Text = replacement;
            MarkDirty();
            FindInNote();
        }

        /// <summary>
        /// Replaces all occurrences matching search terms with replacement content.
        /// </summary>
        private void ReplaceAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_findMatches.Count == 0) return;
            string term = FindTextBox?.Text;
            string replacement = ReplaceTextBox?.Text ?? "";
            if (string.IsNullOrEmpty(term)) return;

            NoteRichEditBox.Document.GetText(TextGetOptions.None, out string fullText);
            string newText = System.Text.RegularExpressions.Regex.Replace(
                fullText, System.Text.RegularExpressions.Regex.Escape(term),
                replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            NoteRichEditBox.Document.SetText(TextSetOptions.None, newText);
            MarkDirty();
            FindInNote();
            ShowStatusMessage($"Replaced all occurrences of \"{term}\"");
        }

        // ── Word Count & Reading Time Computations ─────────────────────────────

        /// <summary>
        /// Computes current characters count, word count, and estimated reading time and displays them on the editor status bar.
        /// </summary>
        private void UpdateWordCount()
        {
            if (SelectedNote == null)
            {
                if (WordCountTextBlock != null)
                {
                    WordCountTextBlock.Text = "Word Count: 0 | Characters: 0 | Reading Time: 0 min";
                }
                return;
            }

            try
            {
                string text = "";
                if (NativeBlockEditorScroll.Visibility == Visibility.Visible)
                {
                    text = _currentMarkdownContent ?? "";
                }
                else
                {
                    NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out text);
                }
                
                if (text == null) text = "";

                if (text.EndsWith("\r"))
                {
                    text = text.Substring(0, text.Length - 1);
                }

                int charCount = text.Length;
                var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int wordCount = words.Length;

                int readingTime = (int)Math.Max(1, Math.Round((double)wordCount / 200.0));
                if (wordCount == 0) readingTime = 0;

                if (WordCountTextBlock != null)
                {
                    WordCountTextBlock.Text = $"Word Count: {wordCount} | Characters: {charCount} | Reading Time: {readingTime} min";
                }
            }
            catch
            {
                if (WordCountTextBlock != null)
                {
                    WordCountTextBlock.Text = "Word Count: 0 | Characters: 0 | Reading Time: 0 min";
                }
            }
        }
    }
}
