# MarkdownEditorBox — WinUI 3 Inline Markdown Editor

An Obsidian-style markdown editor control for WinUI 3.  
Syntax markers **hide when unfocused** and **reveal on the active line**.

---

## Files

| File | Purpose |
|---|---|
| `Controls/MarkdownToken.cs` | Token model + `MarkdownParser` (regex-based line parser) |
| `Controls/MarkdownEditorBox.cs` | `RichEditBox` subclass — the core control |
| `MarkdownEditorPage.xaml` | Host page with toolbar |
| `MarkdownEditorPage.xaml.cs` | Toolbar actions, file I/O, keyboard shortcuts |

---

## Setup

### 1. NuGet — no extra packages needed
Everything uses built-in WinUI 3 / Windows SDK APIs only.

### 2. Add to your project
Copy the `Controls/` folder and both page files into your WinUI 3 project.
Make sure the namespace in each file matches yours (find-replace `MarkdownEditor`).

### 3. Register the page
In `App.xaml.cs` / your navigation setup, navigate to `MarkdownEditorPage`.

```csharp
// Example — in your MainWindow or shell frame:
ContentFrame.Navigate(typeof(MarkdownEditorPage));
```

### 4. Expose your MainWindow
`MarkdownEditorPage.xaml.cs` calls `App.MainWindow` for the file picker HWND.
Add this to `App.xaml.cs`:

```csharp
public static Window MainWindow { get; private set; }

protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    MainWindow = new MainWindow();
    MainWindow.Activate();
}
```

---

## What renders

| Markdown | Rendered |
|---|---|
| `# Heading 1` | Large blue heading, `# ` hides when unfocused |
| `## Heading 2` | Medium blue heading |
| `### Heading 3` | Small blue heading |
| `**bold**` | **Bold**, markers hide |
| `*italic*` | *Italic*, markers hide |
| `` `code` `` | Pink monospace, backticks hide |
| ` ```...``` ` | Code block, dark bg, monospace |
| `- item` | Bullet list (marker dims) |
| `1. item` | Numbered list |

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+B` | Wrap selection in `**bold**` |
| `Ctrl+I` | Wrap selection in `*italic*` |
| `Ctrl+N` | New document |
| `Ctrl+O` | Open `.md` file |
| `Ctrl+S` | Save file |

---

## Customising

All colours and font sizes are constants at the top of `MarkdownEditorBox.cs`:

```csharp
private static readonly Color HeadingColor    = Color.FromArgb(255, 90,  140, 255);
private static readonly Color InlineCodeColor = Color.FromArgb(255, 255, 121, 198);
private const float H1Size  = 30f;
private const float H2Size  = 24f;
private const float H3Size  = 20f;
private const float BaseFontSize = 15f;
```

The editor column is capped at **720–860 px wide** (Obsidian-style centred layout).
Change `MaxWidth="860"` in the XAML `Grid.ColumnDefinitions` to adjust.

---

## Known Limitations

- `RichEditBox` internally stores RTF, so the `GetText` round-trip on every
  keystroke (debounced 250 ms) is the main perf cost. On large files (>50 KB)
  consider increasing the debounce or doing incremental line-only re-formatting.
- `HideMarker` sets font size to `0.1f` (WinUI minimum) rather than truly
  zero — invisible in practice but the character still occupies a tiny sliver
  of space. This is a RichEditBox constraint.
- Nested markdown (bold inside a heading) requires two passes and is partially
  supported; complex nesting may not render perfectly.
