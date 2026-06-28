using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace MarkdownEditor
{
    public sealed partial class MarkdownEditorPage : Page
    {
        private string _currentFilePath = null;

        public MarkdownEditorPage()
        {
            InitializeComponent();

            // Load a demo document so the editor isn't blank
            Editor.LoadMarkdown(DemoMarkdown);

            // Keyboard shortcuts
            KeyDown += OnKeyDown;
        }

        // ── Keyboard shortcuts ──────────────────────────────────────────────────

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            bool ctrl = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (!ctrl) return;

            switch (e.Key)
            {
                case VirtualKey.B: InsertOrWrap("**", "**"); e.Handled = true; break;
                case VirtualKey.I: InsertOrWrap("*", "*");   e.Handled = true; break;
                case VirtualKey.N: NewButton_Click(null, null); e.Handled = true; break;
                case VirtualKey.O: OpenButton_Click(null, null); e.Handled = true; break;
                case VirtualKey.S: SaveButton_Click(null, null); e.Handled = true; break;
            }
        }

        // ── Toolbar handlers ────────────────────────────────────────────────────

        private void BoldButton_Click(object sender, RoutedEventArgs e)
            => InsertOrWrap("**", "**");

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
            => InsertOrWrap("*", "*");

        private void CodeButton_Click(object sender, RoutedEventArgs e)
            => InsertOrWrap("`", "`");

        private void H1Button_Click(object sender, RoutedEventArgs e)
            => PrependLineMarker("# ");

        private void H2Button_Click(object sender, RoutedEventArgs e)
            => PrependLineMarker("## ");

        private void H3Button_Click(object sender, RoutedEventArgs e)
            => PrependLineMarker("### ");

        private void BulletButton_Click(object sender, RoutedEventArgs e)
            => PrependLineMarker("- ");

        private void NumberButton_Click(object sender, RoutedEventArgs e)
            => PrependLineMarker("1. ");

        // ── File operations ─────────────────────────────────────────────────────

        private async void NewButton_Click(object sender, RoutedEventArgs e)
        {
            // Prompt save if dirty — simplified; you can add a ContentDialog here
            Editor.LoadMarkdown(string.Empty);
            _currentFilePath = null;
            FileNameText.Text = "Untitled.md";
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".txt");

            // WinUI 3 requires window handle initialisation
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            string content = await FileIO.ReadTextAsync(file);
            _currentFilePath = file.Path;
            FileNameText.Text = file.Name;
            Editor.LoadMarkdown(content);
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFilePath is null)
            {
                var picker = new FileSavePicker();
                picker.DefaultFileExtension = ".md";
                picker.FileTypeChoices.Add("Markdown", new[] { ".md" });
                picker.SuggestedFileName = "Untitled";

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file is null) return;
                _currentFilePath = file.Path;
                FileNameText.Text = file.Name;
            }

            await File.WriteAllTextAsync(_currentFilePath, Editor.RawMarkdown);
        }

        // ── Insertion helpers ───────────────────────────────────────────────────

        /// <summary>Wraps selected text with prefix/suffix, or inserts placeholder.</summary>
        private void InsertOrWrap(string prefix, string suffix)
        {
            var sel = Editor.Document.Selection;
            string selected = sel.Text;

            if (string.IsNullOrEmpty(selected))
            {
                // Insert markers and place caret between them
                sel.Text = prefix + suffix;
                sel.StartPosition = sel.EndPosition - suffix.Length;
                sel.EndPosition = sel.StartPosition;
            }
            else
            {
                sel.Text = prefix + selected + suffix;
            }
            Editor.Focus(FocusState.Programmatic);
        }

        /// <summary>Adds a block marker at the start of the current line.</summary>
        private void PrependLineMarker(string marker)
        {
            var sel = Editor.Document.Selection;
            int caretPos = sel.StartPosition;

            Editor.Document.GetText(Windows.UI.Text.TextGetOptions.None, out string fullText);

            // Find start of current line
            int lineStart = caretPos;
            while (lineStart > 0 && fullText[lineStart - 1] != '\r' && fullText[lineStart - 1] != '\n')
                lineStart--;

            // Check if marker already present — toggle it off
            string lineText = "";
            int lineEnd = lineStart;
            while (lineEnd < fullText.Length && fullText[lineEnd] != '\r' && fullText[lineEnd] != '\n')
                lineEnd++;
            lineText = fullText.Substring(lineStart, lineEnd - lineStart);

            var range = Editor.Document.GetRange(lineStart, lineStart + lineText.Length);

            if (lineText.StartsWith(marker))
                range.Text = lineText.Substring(marker.Length);
            else
                range.Text = marker + lineText;

            Editor.Focus(FocusState.Programmatic);
        }

        // ── Demo content ────────────────────────────────────────────────────────

        private const string DemoMarkdown = @"# Welcome to Markdown Editor

## Getting Started

This editor renders Markdown **inline** — just like Obsidian.
Move your cursor onto any line to reveal its raw syntax.

### Features

- **Bold** and *italic* text render live
- `Inline code` is highlighted in pink
- Headings shrink their `#` markers when unfocused
- Bullet and numbered lists are supported

### Code Block

```
public static void Main()
{
    Console.WriteLine(""Hello, Markdown!"");
}
```

Start typing below to try it out.
";
    }
}
