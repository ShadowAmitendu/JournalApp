// Controls/MarkdownToken.cs  —  Token model + regex-based markdown parser
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JournalApp
{
    public enum TokenType
    {
        Heading1, Heading2, Heading3,
        Bold, Italic, BoldItalic,
        InlineCode, CodeBlock,
        BulletList, NumberedList,
        Plain
    }

    public class MarkdownToken
    {
        public TokenType Type { get; set; }
        public int LineStart { get; set; }
        public int LineEnd { get; set; }
        public int ContentStart { get; set; }
        public int ContentEnd { get; set; }
        public int MarkerPrefixLength { get; set; }
        public int MarkerSuffixLength { get; set; }
        public string RawText { get; set; }
    }

    public static class MarkdownParser
    {
        private static readonly Regex H1      = new(@"^(# )(.+)$",             RegexOptions.Compiled);
        private static readonly Regex H2      = new(@"^(## )(.+)$",            RegexOptions.Compiled);
        private static readonly Regex H3      = new(@"^(### )(.+)$",           RegexOptions.Compiled);
        private static readonly Regex Bullet  = new(@"^(\s*[-*+] )(.+)$",     RegexOptions.Compiled);
        private static readonly Regex Numbered= new(@"^(\s*\d+\. )(.+)$",     RegexOptions.Compiled);
        private static readonly Regex CodeFence = new(@"^(```)(.*)$",          RegexOptions.Compiled);

        private static readonly Regex BoldItalicInline = new(@"(\*\*\*|___)(.+?)(\*\*\*|___)", RegexOptions.Compiled);
        private static readonly Regex BoldInline       = new(@"(\*\*|__)(.+?)(\*\*|__)",       RegexOptions.Compiled);
        private static readonly Regex ItalicInline     = new(@"(\*|_)(.+?)(\*|_)",             RegexOptions.Compiled);
        private static readonly Regex InlineCodeRx     = new(@"(`)([^`]+)(`)",                 RegexOptions.Compiled);

        public static List<MarkdownToken> ParseLines(string fullText)
        {
            var tokens = new List<MarkdownToken>();
            if (string.IsNullOrEmpty(fullText)) return tokens;

            bool inCodeBlock = false;
            int  codeBlockStart = -1;

            var lines = new List<(int start, int end, string text)>();
            int i = 0;
            while (i <= fullText.Length)
            {
                int nl = fullText.IndexOf('\r', i);
                if (nl < 0) nl = fullText.Length;
                string lineText = fullText.Substring(i, nl - i);
                lines.Add((i, nl, lineText));
                i = nl + 1;
                if (i < fullText.Length && fullText[i - 1] == '\r' && fullText[i] == '\n') i++;
                if (nl == fullText.Length) break;
            }

            foreach (var (start, end, text) in lines)
            {
                var cfm = CodeFence.Match(text);
                if (cfm.Success)
                {
                    if (!inCodeBlock) { inCodeBlock = true; codeBlockStart = start; }
                    else
                    {
                        tokens.Add(new MarkdownToken
                        {
                            Type = TokenType.CodeBlock,
                            LineStart = codeBlockStart, LineEnd = end,
                            ContentStart = codeBlockStart + 3, ContentEnd = end - 3,
                            MarkerPrefixLength = 3, MarkerSuffixLength = 3,
                            RawText = fullText.Substring(codeBlockStart, end - codeBlockStart)
                        });
                        inCodeBlock = false; codeBlockStart = -1;
                    }
                    continue;
                }

                if (inCodeBlock) continue;

                Match m;
                if ((m = H3.Match(text)).Success)     { tokens.Add(MakeLineToken(TokenType.Heading3,     start, end, m.Groups[1].Length, 0, text)); continue; }
                if ((m = H2.Match(text)).Success)     { tokens.Add(MakeLineToken(TokenType.Heading2,     start, end, m.Groups[1].Length, 0, text)); continue; }
                if ((m = H1.Match(text)).Success)     { tokens.Add(MakeLineToken(TokenType.Heading1,     start, end, m.Groups[1].Length, 0, text)); continue; }
                if ((m = Bullet.Match(text)).Success)  { tokens.Add(MakeLineToken(TokenType.BulletList,   start, end, m.Groups[1].Length, 0, text)); continue; }
                if ((m = Numbered.Match(text)).Success){ tokens.Add(MakeLineToken(TokenType.NumberedList, start, end, m.Groups[1].Length, 0, text)); continue; }

                ParseInlineLine(fullText, start, end, text, tokens);
            }

            return tokens;
        }

        private static MarkdownToken MakeLineToken(TokenType type, int lineStart, int lineEnd,
            int prefixLen, int suffixLen, string raw) => new MarkdownToken
        {
            Type = type, LineStart = lineStart, LineEnd = lineEnd,
            ContentStart = lineStart + prefixLen, ContentEnd = lineEnd - suffixLen,
            MarkerPrefixLength = prefixLen, MarkerSuffixLength = suffixLen, RawText = raw
        };

        private static void ParseInlineLine(string fullText, int lineStart, int lineEnd,
            string lineText, List<MarkdownToken> tokens)
        {
            foreach (Match m in BoldItalicInline.Matches(lineText))
                tokens.Add(MakeInlineToken(TokenType.BoldItalic, lineStart, m, 3, 3));

            foreach (Match m in BoldInline.Matches(lineText))
                if (!OverlapsExisting(tokens, lineStart + m.Index, lineStart + m.Index + m.Length))
                    tokens.Add(MakeInlineToken(TokenType.Bold, lineStart, m, 2, 2));

            foreach (Match m in ItalicInline.Matches(lineText))
                if (!OverlapsExisting(tokens, lineStart + m.Index, lineStart + m.Index + m.Length))
                    tokens.Add(MakeInlineToken(TokenType.Italic, lineStart, m, 1, 1));

            foreach (Match m in InlineCodeRx.Matches(lineText))
                if (!OverlapsExisting(tokens, lineStart + m.Index, lineStart + m.Index + m.Length))
                    tokens.Add(MakeInlineToken(TokenType.InlineCode, lineStart, m, 1, 1));
        }

        private static MarkdownToken MakeInlineToken(TokenType type, int lineStart, Match m,
            int prefixLen, int suffixLen)
        {
            int absStart = lineStart + m.Index;
            int absEnd   = absStart + m.Length;
            return new MarkdownToken
            {
                Type = type, LineStart = absStart, LineEnd = absEnd,
                ContentStart = absStart + prefixLen, ContentEnd = absEnd - suffixLen,
                MarkerPrefixLength = prefixLen, MarkerSuffixLength = suffixLen, RawText = m.Value
            };
        }

        private static bool OverlapsExisting(List<MarkdownToken> tokens, int start, int end)
        {
            foreach (var t in tokens)
                if (t.LineStart < end && t.LineEnd > start) return true;
            return false;
        }
    }
}
