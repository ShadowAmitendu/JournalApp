// Controls/MarkdownEditorBox.cs
// Obsidian-style inline markdown editor built on RichEditBox (no WebView2).
// Uses ONLY Microsoft.UI.Text — avoids all Windows.UI.Text ambiguity.

using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.UI;

namespace JournalApp
{
    public class MarkdownEditorBox : RichEditBox
    {
        // ── Theme colours ──────────────────────────────────────────────────────
        private static readonly Color HeadingColor    = Color.FromArgb(255,  90, 140, 255);
        private static readonly Color InlineCodeColor = Color.FromArgb(255, 255, 121, 198);
        private static readonly Color BulletColor     = Color.FromArgb(255, 150, 150, 170);
        private static readonly Color DefaultColor    = Color.FromArgb(255, 220, 220, 220);
        private static readonly Color MarkerColor     = Color.FromArgb(255, 100, 100, 120);

        // ── Font sizes ─────────────────────────────────────────────────────────
        private const float BaseFontSize = 15f;
        private const float H1Size       = 30f;
        private const float H2Size       = 24f;
        private const float H3Size       = 20f;
        private const float CodeSize     = 13f;

        // ── Internal state ─────────────────────────────────────────────────────
        private DispatcherTimer _debounce;
        private bool   _isFormatting  = false;
        private int    _lastCaretLine = -1;
        private string _lastRawText   = string.Empty;

        // ── RawMarkdown dependency property ────────────────────────────────────
        public static readonly DependencyProperty RawMarkdownProperty =
            DependencyProperty.Register(nameof(RawMarkdown), typeof(string),
                typeof(MarkdownEditorBox), new PropertyMetadata(string.Empty));

        public string RawMarkdown
        {
            get => (string)GetValue(RawMarkdownProperty);
            set => SetValue(RawMarkdownProperty, value);
        }

        // ── Constructor ────────────────────────────────────────────────────────
        public MarkdownEditorBox()
        {
            AcceptsReturn       = true;
            IsSpellCheckEnabled = true;
            TextWrapping        = TextWrapping.Wrap;
            FontFamily          = new FontFamily("Segoe UI Variable Text");
            FontSize            = BaseFontSize;
            Foreground          = new SolidColorBrush(DefaultColor);
            BorderThickness     = new Thickness(0);
            Padding             = new Thickness(24, 16, 24, 200);
            Background          = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFormatting(); };

            TextChanged      += OnTextChanged;
            SelectionChanged += OnSelectionChanged;
        }

        // ── Helpers: GetText / SetText wrappers ────────────────────────────────
        private void GetDocumentText(out string text)
            => Document.GetText(TextGetOptions.None, out text);

        private void SetDocumentText(string text)
            => Document.SetText(TextSetOptions.None, text);

        // ── Event handlers ─────────────────────────────────────────────────────
        private void OnTextChanged(object sender, RoutedEventArgs e)
        {
            if (_isFormatting) return;
            GetDocumentText(out string raw);
            if (raw == _lastRawText) return;

            _debounce.Stop();
            _debounce.Start();
            _lastRawText = raw;
            RawMarkdown  = raw;
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_isFormatting) return;
            int caretLine = GetCaretLineIndex();
            if (caretLine != _lastCaretLine)
            {
                int prev = _lastCaretLine;
                _lastCaretLine = caretLine;
                RefreshLines(prev, caretLine);
            }
        }

        // ── Formatting engine ──────────────────────────────────────────────────
        private void ApplyFormatting()
        {
            GetDocumentText(out string text);
            if (string.IsNullOrEmpty(text)) return;

            _isFormatting = true;
            try
            {
                int activeLine = GetCaretLineIndex();
                ResetAll(text.Length);
                var tokens = MarkdownParser.ParseLines(text);
                foreach (var token in tokens)
                    ApplyToken(token, IsOnActiveLine(token, text, activeLine));
            }
            finally { _isFormatting = false; }
        }

        private void RefreshLines(int prevLine, int newLine)
        {
            GetDocumentText(out string text);
            if (string.IsNullOrEmpty(text)) return;

            _isFormatting = true;
            try
            {
                var tokens      = MarkdownParser.ParseLines(text);
                var lineOffsets = GetLineOffsets(text);
                foreach (var token in tokens)
                {
                    int tokenLine = OffsetToLineIndex(token.LineStart, lineOffsets);
                    if (tokenLine == prevLine || tokenLine == newLine)
                    {
                        ResetRange(token.LineStart, token.LineEnd - token.LineStart);
                        ApplyToken(token, tokenLine == newLine);
                    }
                }
            }
            finally { _isFormatting = false; }
        }

        private void ApplyToken(MarkdownToken token, bool isActiveLine)
        {
            switch (token.Type)
            {
                case TokenType.Heading1:    ApplyHeading(token, H1Size, isActiveLine, 700); break;
                case TokenType.Heading2:    ApplyHeading(token, H2Size, isActiveLine, 700); break;
                case TokenType.Heading3:    ApplyHeading(token, H3Size, isActiveLine, 600); break;
                case TokenType.Bold:        ApplyInlineStyle(token, isActiveLine, bold: true);               break;
                case TokenType.Italic:      ApplyInlineStyle(token, isActiveLine, italic: true);             break;
                case TokenType.BoldItalic:  ApplyInlineStyle(token, isActiveLine, bold: true, italic: true); break;
                case TokenType.InlineCode:  ApplyInlineCode(token, isActiveLine);                            break;
                case TokenType.CodeBlock:   ApplyCodeBlock(token, isActiveLine);                             break;
                case TokenType.BulletList:
                case TokenType.NumberedList: ApplyListItem(token, isActiveLine);                             break;
            }
        }

        // ── Per-token formatters ───────────────────────────────────────────────
        private void ApplyHeading(MarkdownToken t, float size, bool active, int weight)
        {
            if (active)
            {
                var full = GetRange(t.LineStart, t.LineEnd - t.LineStart);
                full.CharacterFormat.Size            = size;
                full.CharacterFormat.Weight          = weight;
                full.CharacterFormat.ForegroundColor = HeadingColor;
                var marker = GetRange(t.LineStart, t.MarkerPrefixLength);
                marker.CharacterFormat.ForegroundColor = MarkerColor;
                marker.CharacterFormat.Size            = size * 0.6f;
            }
            else
            {
                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                content.CharacterFormat.Size            = size;
                content.CharacterFormat.Weight          = weight;
                content.CharacterFormat.ForegroundColor = HeadingColor;
                HideMarker(t.LineStart, t.MarkerPrefixLength);
            }
        }

        private void ApplyInlineStyle(MarkdownToken t, bool active, bool bold = false, bool italic = false)
        {
            if (active)
            {
                GetRange(t.LineStart, t.MarkerPrefixLength).CharacterFormat.ForegroundColor = MarkerColor;
                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                if (bold)   content.CharacterFormat.Weight = 700;
                if (italic) content.CharacterFormat.Italic = FormatEffect.On;
                content.CharacterFormat.ForegroundColor = DefaultColor;
                GetRange(t.ContentEnd, t.MarkerSuffixLength).CharacterFormat.ForegroundColor = MarkerColor;
            }
            else
            {
                HideMarker(t.LineStart, t.MarkerPrefixLength);
                HideMarker(t.ContentEnd, t.MarkerSuffixLength);
                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                if (bold)   content.CharacterFormat.Weight = 700;
                if (italic) content.CharacterFormat.Italic = FormatEffect.On;
                content.CharacterFormat.ForegroundColor = DefaultColor;
            }
        }

        private void ApplyInlineCode(MarkdownToken t, bool active)
        {
            if (active)
            {
                GetRange(t.LineStart, t.MarkerPrefixLength).CharacterFormat.ForegroundColor = MarkerColor;
                var c = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                c.CharacterFormat.ForegroundColor = InlineCodeColor;
                c.CharacterFormat.Size            = CodeSize;
                c.CharacterFormat.Name            = "Cascadia Code";
                GetRange(t.ContentEnd, t.MarkerSuffixLength).CharacterFormat.ForegroundColor = MarkerColor;
            }
            else
            {
                HideMarker(t.LineStart, t.MarkerPrefixLength);
                HideMarker(t.ContentEnd, t.MarkerSuffixLength);
                var c = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                c.CharacterFormat.ForegroundColor = InlineCodeColor;
                c.CharacterFormat.Size            = CodeSize;
                c.CharacterFormat.Name            = "Cascadia Code";
            }
        }

        private void ApplyCodeBlock(MarkdownToken t, bool active)
        {
            var full = GetRange(t.LineStart, t.LineEnd - t.LineStart);
            full.CharacterFormat.ForegroundColor = Color.FromArgb(255, 248, 248, 242);
            full.CharacterFormat.Size            = CodeSize;
            full.CharacterFormat.Name            = "Cascadia Code";

            if (!active)
            {
                HideMarker(t.LineStart, 3);
                if (t.LineEnd - 3 > t.LineStart) HideMarker(t.LineEnd - 3, 3);
            }
            else
            {
                GetRange(t.LineStart, 3).CharacterFormat.ForegroundColor = MarkerColor;
            }
        }

        private void ApplyListItem(MarkdownToken t, bool active)
        {
            var marker = GetRange(t.LineStart, t.MarkerPrefixLength);
            marker.CharacterFormat.ForegroundColor = BulletColor;
            if (!active) marker.CharacterFormat.Size = BaseFontSize * 0.85f;

            var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
            content.CharacterFormat.ForegroundColor = DefaultColor;
            content.CharacterFormat.Size            = BaseFontSize;
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private void HideMarker(int start, int length)
        {
            if (length <= 0) return;
            var r = GetRange(start, length);
            r.CharacterFormat.Size            = 0.1f;
            r.CharacterFormat.ForegroundColor = Color.FromArgb(0, 26, 26, 36);
        }

        private void ResetAll(int length)
        {
            var all = Document.GetRange(0, length);
            all.CharacterFormat.Size            = BaseFontSize;
            all.CharacterFormat.ForegroundColor = DefaultColor;
            all.CharacterFormat.Name            = "Segoe UI Variable Text";
            all.CharacterFormat.Weight          = 400; // Normal
            all.CharacterFormat.Italic          = FormatEffect.Off;
            all.CharacterFormat.Underline       = UnderlineType.None;
        }

        private void ResetRange(int start, int length)
        {
            if (length <= 0) return;
            var r = Document.GetRange(start, start + length);
            r.CharacterFormat.Size            = BaseFontSize;
            r.CharacterFormat.ForegroundColor = DefaultColor;
            r.CharacterFormat.Name            = "Segoe UI Variable Text";
            r.CharacterFormat.Weight          = 400; // Normal
            r.CharacterFormat.Italic          = FormatEffect.Off;
            r.CharacterFormat.Underline       = UnderlineType.None;
        }

        private Microsoft.UI.Text.ITextRange GetRange(int start, int length)
            => Document.GetRange(start, start + length);

        private int GetCaretLineIndex()
        {
            GetDocumentText(out string text);
            return OffsetToLineIndex(Document.Selection.StartPosition, GetLineOffsets(text));
        }

        private static List<int> GetLineOffsets(string text)
        {
            var offsets = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    int next = i + 1;
                    if (next < text.Length && text[next] == '\n') next++;
                    offsets.Add(next);
                    i = next - 1;
                }
            }
            return offsets;
        }

        private static int OffsetToLineIndex(int offset, List<int> lineOffsets)
        {
            for (int i = lineOffsets.Count - 1; i >= 0; i--)
                if (offset >= lineOffsets[i]) return i;
            return 0;
        }

        private bool IsOnActiveLine(MarkdownToken token, string text, int activeLine)
            => OffsetToLineIndex(token.LineStart, GetLineOffsets(text)) == activeLine;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Loads raw markdown and applies formatting. Call when a note is selected.</summary>
        public void LoadMarkdown(string markdown)
        {
            _isFormatting = true;
            SetDocumentText(markdown ?? string.Empty);
            _isFormatting = false;
            _lastRawText  = markdown ?? string.Empty;
            RawMarkdown   = markdown ?? string.Empty;
            ApplyFormatting();
        }

        /// <summary>Wraps the current selection with markdown prefix/suffix (e.g. ** / **).</summary>
        public void WrapSelection(string prefix, string suffix)
        {
            var sel      = Document.Selection;
            string selected = sel.Text;
            sel.Text = string.IsNullOrEmpty(selected) ? prefix + suffix : prefix + selected + suffix;
            if (string.IsNullOrEmpty(selected))
            {
                sel.StartPosition = sel.EndPosition - suffix.Length;
                sel.EndPosition   = sel.StartPosition;
            }
            Focus(FocusState.Programmatic);
        }

        /// <summary>Toggles a line-level prefix (e.g. "# ", "- ") on the current line.</summary>
        public void ToggleLinePrefix(string marker)
        {
            var sel      = Document.Selection;
            int caretPos = sel.StartPosition;
            GetDocumentText(out string fullText);

            int lineStart = caretPos;
            while (lineStart > 0 && fullText[lineStart - 1] != '\r' && fullText[lineStart - 1] != '\n')
                lineStart--;
            int lineEnd = lineStart;
            while (lineEnd < fullText.Length && fullText[lineEnd] != '\r' && fullText[lineEnd] != '\n')
                lineEnd++;

            string lineText = fullText.Substring(lineStart, lineEnd - lineStart);
            var range = Document.GetRange(lineStart, lineStart + lineText.Length);
            range.Text = lineText.StartsWith(marker) ? lineText.Substring(marker.Length) : marker + lineText;
            Focus(FocusState.Programmatic);
        }
    }
}
