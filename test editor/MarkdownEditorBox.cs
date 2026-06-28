using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.UI;
using Windows.UI.Text;

namespace MarkdownEditor.Controls
{
    /// <summary>
    /// A RichEditBox that renders Markdown inline — headings grow, bold/italic render,
    /// syntax markers hide — and reveals raw markers on the active line (Obsidian-style).
    /// </summary>
    public class MarkdownEditorBox : RichEditBox
    {
        // ── Theme colours (change these to match your app accent) ──────────────
        private static readonly Color HeadingColor    = Color.FromArgb(255, 90,  140, 255);
        private static readonly Color CodeBgColor     = Color.FromArgb(255, 40,  42,  54);
        private static readonly Color CodeFgColor     = Color.FromArgb(255, 248, 248, 242);
        private static readonly Color InlineCodeColor = Color.FromArgb(255, 255, 121, 198);
        private static readonly Color BulletColor     = Color.FromArgb(255, 150, 150, 170);
        private static readonly Color DefaultColor    = Color.FromArgb(255, 220, 220, 220);
        private static readonly Color MarkerColor     = Color.FromArgb(255, 100, 100, 120);

        // ── Font sizes ──────────────────────────────────────────────────────────
        private const float BaseFontSize = 15f;
        private const float H1Size  = 30f;
        private const float H2Size  = 24f;
        private const float H3Size  = 20f;
        private const float CodeSize = 13f;

        // ── Internal state ──────────────────────────────────────────────────────
        private DispatcherTimer _debounce;
        private bool _isFormatting = false;
        private int  _lastCaretLine = -1;
        private string _lastRawText = string.Empty;

        // Dependency property so XAML can bind RawMarkdown
        public static readonly DependencyProperty RawMarkdownProperty =
            DependencyProperty.Register(nameof(RawMarkdown), typeof(string),
                typeof(MarkdownEditorBox), new PropertyMetadata(string.Empty));

        public string RawMarkdown
        {
            get => (string)GetValue(RawMarkdownProperty);
            set => SetValue(RawMarkdownProperty, value);
        }

        public MarkdownEditorBox()
        {
            AcceptsReturn = true;
            IsSpellCheckEnabled = true;
            TextWrapping = TextWrapping.Wrap;
            FontFamily = new FontFamily("Segoe UI Variable Text");
            FontSize = BaseFontSize;
            Foreground = new SolidColorBrush(DefaultColor);
            Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 36));
            BorderThickness = new Thickness(0);
            Padding = new Thickness(48, 48, 48, 48);

            // Debounce timer — formats 250 ms after user stops typing
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFormatting(); };

            TextChanged += OnTextChanged;
            SelectionChanged += OnSelectionChanged;
        }

        // ── Event handlers ──────────────────────────────────────────────────────

        private void OnTextChanged(object sender, RoutedEventArgs e)
        {
            if (_isFormatting) return;
            _debounce.Stop();
            _debounce.Start();

            // Keep RawMarkdown in sync
            Document.GetText(TextGetOptions.None, out string raw);
            _lastRawText = raw;
            RawMarkdown = raw;
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_isFormatting) return;
            int caretLine = GetCaretLineIndex();
            if (caretLine != _lastCaretLine)
            {
                int prev = _lastCaretLine;
                _lastCaretLine = caretLine;
                // Re-format the previously active line (hide markers again)
                // and the new active line (reveal markers)
                RefreshLines(prev, caretLine);
            }
        }

        // ── Formatting engine ───────────────────────────────────────────────────

        private void ApplyFormatting()
        {
            Document.GetText(TextGetOptions.None, out string text);
            if (string.IsNullOrEmpty(text)) return;

            _isFormatting = true;
            try
            {
                var tokens = MarkdownParser.ParseLines(text);
                int activeLine = GetCaretLineIndex();

                // 1. Reset entire document to base style
                ResetAll(text.Length);

                // 2. Apply token formatting
                foreach (var token in tokens)
                {
                    bool isActiveLine = IsOnActiveLine(token, text, activeLine);
                    ApplyToken(token, isActiveLine, text);
                }
            }
            finally
            {
                _isFormatting = false;
            }
        }

        /// <summary>Re-formats only two lines cheaply on caret movement.</summary>
        private void RefreshLines(int prevLine, int newLine)
        {
            Document.GetText(TextGetOptions.None, out string text);
            if (string.IsNullOrEmpty(text)) return;

            _isFormatting = true;
            try
            {
                var tokens = MarkdownParser.ParseLines(text);
                var lineOffsets = GetLineOffsets(text);

                foreach (var token in tokens)
                {
                    int tokenLine = OffsetToLineIndex(token.LineStart, lineOffsets);
                    if (tokenLine == prevLine || tokenLine == newLine)
                    {
                        // Reset just this token's range
                        ResetRange(token.LineStart, token.LineEnd - token.LineStart);
                        bool isActive = (tokenLine == newLine);
                        ApplyToken(token, isActive, text);
                    }
                }
            }
            finally
            {
                _isFormatting = false;
            }
        }

        private void ApplyToken(MarkdownToken token, bool isActiveLine, string fullText)
        {
            switch (token.Type)
            {
                case TokenType.Heading1:
                    ApplyHeading(token, H1Size, isActiveLine, FontWeights.Bold);
                    break;
                case TokenType.Heading2:
                    ApplyHeading(token, H2Size, isActiveLine, FontWeights.Bold);
                    break;
                case TokenType.Heading3:
                    ApplyHeading(token, H3Size, isActiveLine, FontWeights.SemiBold);
                    break;
                case TokenType.Bold:
                    ApplyInlineStyle(token, isActiveLine, bold: true);
                    break;
                case TokenType.Italic:
                    ApplyInlineStyle(token, isActiveLine, italic: true);
                    break;
                case TokenType.BoldItalic:
                    ApplyInlineStyle(token, isActiveLine, bold: true, italic: true);
                    break;
                case TokenType.InlineCode:
                    ApplyInlineCode(token, isActiveLine);
                    break;
                case TokenType.CodeBlock:
                    ApplyCodeBlock(token, isActiveLine);
                    break;
                case TokenType.BulletList:
                case TokenType.NumberedList:
                    ApplyListItem(token, isActiveLine);
                    break;
            }
        }

        // ── Per-token formatters ────────────────────────────────────────────────

        private void ApplyHeading(MarkdownToken t, float size, bool active, FontWeight weight)
        {
            if (active)
            {
                // Show raw text, but still style it
                var fullRange = GetRange(t.LineStart, t.LineEnd - t.LineStart);
                fullRange.CharacterFormat.Size = size;
                fullRange.CharacterFormat.Weight = weight;
                fullRange.CharacterFormat.ForegroundColor = HeadingColor;

                // Dim the marker
                var marker = GetRange(t.LineStart, t.MarkerPrefixLength);
                marker.CharacterFormat.ForegroundColor = MarkerColor;
                marker.CharacterFormat.Size = size * 0.6f;
            }
            else
            {
                // Style content
                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                content.CharacterFormat.Size = size;
                content.CharacterFormat.Weight = weight;
                content.CharacterFormat.ForegroundColor = HeadingColor;

                // Hide marker: shrink to 0.1pt and colour matches background
                HideMarker(t.LineStart, t.MarkerPrefixLength);
            }
        }

        private void ApplyInlineStyle(MarkdownToken t, bool active,
            bool bold = false, bool italic = false)
        {
            if (active)
            {
                // Show full raw text, dim markers
                var markerPre = GetRange(t.LineStart, t.MarkerPrefixLength);
                markerPre.CharacterFormat.ForegroundColor = MarkerColor;

                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                if (bold)   content.CharacterFormat.Weight = FontWeights.Bold;
                if (italic) content.CharacterFormat.Italic = FormatEffect.On;
                content.CharacterFormat.ForegroundColor = DefaultColor;

                var markerSuf = GetRange(t.ContentEnd, t.MarkerSuffixLength);
                markerSuf.CharacterFormat.ForegroundColor = MarkerColor;
            }
            else
            {
                // Hide markers, style content
                HideMarker(t.LineStart, t.MarkerPrefixLength);
                HideMarker(t.ContentEnd, t.MarkerSuffixLength);

                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                if (bold)   content.CharacterFormat.Weight = FontWeights.Bold;
                if (italic) content.CharacterFormat.Italic = FormatEffect.On;
                content.CharacterFormat.ForegroundColor = DefaultColor;
            }
        }

        private void ApplyInlineCode(MarkdownToken t, bool active)
        {
            if (active)
            {
                var marker = GetRange(t.LineStart, t.MarkerPrefixLength);
                marker.CharacterFormat.ForegroundColor = MarkerColor;

                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                content.CharacterFormat.ForegroundColor = InlineCodeColor;
                content.CharacterFormat.Size = CodeSize;
                content.CharacterFormat.Name = "Cascadia Code";

                var markerEnd = GetRange(t.ContentEnd, t.MarkerSuffixLength);
                markerEnd.CharacterFormat.ForegroundColor = MarkerColor;
            }
            else
            {
                HideMarker(t.LineStart, t.MarkerPrefixLength);
                HideMarker(t.ContentEnd, t.MarkerSuffixLength);

                var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
                content.CharacterFormat.ForegroundColor = InlineCodeColor;
                content.CharacterFormat.Size = CodeSize;
                content.CharacterFormat.Name = "Cascadia Code";
            }
        }

        private void ApplyCodeBlock(MarkdownToken t, bool active)
        {
            var fullRange = GetRange(t.LineStart, t.LineEnd - t.LineStart);
            fullRange.CharacterFormat.ForegroundColor = CodeFgColor;
            fullRange.CharacterFormat.Size = CodeSize;
            fullRange.CharacterFormat.Name = "Cascadia Code";

            if (!active)
            {
                HideMarker(t.LineStart, 3);
                if (t.LineEnd - 3 > t.LineStart)
                    HideMarker(t.LineEnd - 3, 3);
            }
            else
            {
                var fence = GetRange(t.LineStart, 3);
                fence.CharacterFormat.ForegroundColor = MarkerColor;
            }
        }

        private void ApplyListItem(MarkdownToken t, bool active)
        {
            if (active)
            {
                var marker = GetRange(t.LineStart, t.MarkerPrefixLength);
                marker.CharacterFormat.ForegroundColor = BulletColor;
            }
            else
            {
                // Keep bullet marker visible but dim it (Obsidian keeps •)
                // Replace "-" marker display with bullet character via colour dim
                var marker = GetRange(t.LineStart, t.MarkerPrefixLength);
                marker.CharacterFormat.ForegroundColor = BulletColor;
                marker.CharacterFormat.Size = BaseFontSize * 0.85f;
            }

            var content = GetRange(t.ContentStart, t.ContentEnd - t.ContentStart);
            content.CharacterFormat.ForegroundColor = DefaultColor;
            content.CharacterFormat.Size = BaseFontSize;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>Makes a marker invisible by setting its size to 0.1pt and colour to bg.</summary>
        private void HideMarker(int start, int length)
        {
            if (length <= 0) return;
            var r = GetRange(start, length);
            r.CharacterFormat.Size = 0.1f;
            r.CharacterFormat.ForegroundColor = Color.FromArgb(0, 26, 26, 36); // transparent
        }

        private void ResetAll(int length)
        {
            var all = Document.GetRange(0, length);
            all.CharacterFormat.SetDefault();
            all.CharacterFormat.Size = BaseFontSize;
            all.CharacterFormat.ForegroundColor = DefaultColor;
            all.CharacterFormat.Name = "Segoe UI Variable Text";
            all.CharacterFormat.Weight = FontWeights.Normal;
            all.CharacterFormat.Italic = FormatEffect.Off;
        }

        private void ResetRange(int start, int length)
        {
            if (length <= 0) return;
            var r = GetRange(start, length);
            r.CharacterFormat.SetDefault();
            r.CharacterFormat.Size = BaseFontSize;
            r.CharacterFormat.ForegroundColor = DefaultColor;
            r.CharacterFormat.Name = "Segoe UI Variable Text";
            r.CharacterFormat.Weight = FontWeights.Normal;
            r.CharacterFormat.Italic = FormatEffect.Off;
        }

        private ITextRange GetRange(int start, int length)
            => Document.GetRange(start, start + length);

        private int GetCaretLineIndex()
        {
            Document.GetText(TextGetOptions.None, out string text);
            int caretPos = Document.Selection.StartPosition;
            return OffsetToLineIndex(caretPos, GetLineOffsets(text));
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
        {
            var offsets = GetLineOffsets(text);
            return OffsetToLineIndex(token.LineStart, offsets) == activeLine;
        }

        /// <summary>
        /// Called externally to load markdown content into the editor.
        /// </summary>
        public void LoadMarkdown(string markdown)
        {
            _isFormatting = true;
            Document.SetText(TextSetOptions.None, markdown);
            _isFormatting = false;
            _lastRawText = markdown;
            RawMarkdown = markdown;
            ApplyFormatting();
        }
    }
}
