// MainPage.Zen.cs
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Playback;
using Windows.Media.Core;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        private MediaPlayer _ambientPlayer;
        private bool _isZenModeActive = false;

        // ── Zen Mode Initialization ──────────────────────────────────────────
        private void InitZenMode()
        {
            if (NoteRichEditBox != null)
                NoteRichEditBox.PreviewKeyDown += NoteRichEditBox_PreviewKeyDown;
            if (NativeBlockEditorScroll != null)
                NativeBlockEditorScroll.PreviewKeyDown += NoteRichEditBox_PreviewKeyDown;

            // Trigger background download of typewriter click sounds
            _ = EnsureTypewriterSoundsDownloadedAsync();
        }

        // ── Keyboard / Typing Event Keypress Feedback ────────────────────────
        private void NoteRichEditBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_isZenModeActive && e.Key == Windows.System.VirtualKey.Escape)
            {
                if (ZenModeToggle != null)
                {
                    ZenModeToggle.IsOn = false;
                    e.Handled = true;
                    return;
                }
            }

            if (TypewriterSoundToggle != null && TypewriterSoundToggle.IsOn)
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    TypewriterPlayer.PlayReturn();
                }
                else if (e.Key != Windows.System.VirtualKey.Shift &&
                         e.Key != Windows.System.VirtualKey.Control &&
                         e.Key != Windows.System.VirtualKey.Left &&
                         e.Key != Windows.System.VirtualKey.Right &&
                         e.Key != Windows.System.VirtualKey.Up &&
                         e.Key != Windows.System.VirtualKey.Down &&
                         e.Key != Windows.System.VirtualKey.Tab &&
                         e.Key != Windows.System.VirtualKey.Escape)
                {
                    TypewriterPlayer.PlayClick();
                }
            }
        }

        // ── Settings Handlers ────────────────────────────────────────────────
        private void ZenModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (ZenModeToggle == null) return;
            _isZenModeActive = ZenModeToggle.IsOn;

            // 1. Toggle fullscreen state
            ToggleFullscreen(_isZenModeActive);

            // 2. Hide / restore window title bar
            if (MainWindow.Instance != null && MainWindow.Instance.AppTitleBarControl != null)
            {
                MainWindow.Instance.AppTitleBarControl.Visibility = _isZenModeActive ? Visibility.Collapsed : Visibility.Visible;
            }

            // 3. Hide / restore side panels, toolbars, status bar and headers
            if (_isZenModeActive)
            {
                // Collapse AI assistant panel if open
                if (_aiPanelOpen)
                {
                    _aiPanelOpen = false;
                    AnimateAIPanel(false);
                    if (AIAssistantButton != null)
                    {
                        AIAssistantButton.IsChecked = false;
                    }
                }

                // Hide Categories navigation pane completely (removing icons)
                if (CategoriesNavView != null)
                {
                    CategoriesNavView.IsPaneVisible = false;
                }

                // Hide Notes List Grid and collapse column width
                if (NotesListGrid != null) NotesListGrid.Visibility = Visibility.Collapsed;
                if (NotesListColumn != null) NotesListColumn.Width = new GridLength(0);
                if (ColumnSplitter != null) ColumnSplitter.Visibility = Visibility.Collapsed;

                // Hide distracting editor chrome (keeping the formatting toolbar visible)
                if (EditorToolbar != null) EditorToolbar.Visibility = Visibility.Visible;
                if (EditorStatusBar != null) EditorStatusBar.Visibility = Visibility.Collapsed;
                if (HeroImageContainer != null) HeroImageContainer.Visibility = Visibility.Collapsed;
                if (NoteHeaderGrid != null) NoteHeaderGrid.Visibility = Visibility.Collapsed;
                if (NoteHeaderDivider != null) NoteHeaderDivider.Visibility = Visibility.Collapsed;

                // Shift focus to editor
                if (SelectedNote != null && SelectedNote.ContentFormat == "markdown")
                    NativeBlockEditorScroll?.Focus(FocusState.Programmatic);
                else
                    NoteRichEditBox?.Focus(FocusState.Programmatic);

                ShowStatusMessage("Zen Focus Mode Activated");
            }
            else
            {
                // Restore Categories navigation pane completely
                if (CategoriesNavView != null)
                {
                    CategoriesNavView.IsPaneVisible = true;
                    CategoriesNavView.IsPaneToggleButtonVisible = true;
                }

                // Restore Notes List Column width and visibility
                if (NotesListGrid != null) NotesListGrid.Visibility = Visibility.Visible;
                if (NotesListColumn != null) NotesListColumn.Width = new GridLength(370);
                if (ColumnSplitter != null) ColumnSplitter.Visibility = Visibility.Visible;

                // Restore editor chrome
                if (EditorToolbar != null && SelectedNote != null) EditorToolbar.Visibility = Visibility.Visible;
                if (EditorStatusBar != null && SelectedNote != null) EditorStatusBar.Visibility = Visibility.Visible;
                if (NoteHeaderGrid != null) NoteHeaderGrid.Visibility = Visibility.Visible;
                if (NoteHeaderDivider != null) NoteHeaderDivider.Visibility = Visibility.Visible;

                // Restore Hero Image cover if note has one
                UpdateHeroImageUI();

                ShowStatusMessage("Zen Focus Mode Deactivated");
            }
        }

        private void TypewriterSoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (TypewriterSoundToggle == null) return;
            if (TypewriterSoundToggle.IsOn)
            {
                ShowStatusMessage("Typewriter audio feedback enabled");
            }
            else
            {
                ShowStatusMessage("Typewriter audio feedback disabled");
            }
        }

        private void AmbientSoundComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AmbientSoundComboBox == null) return;
            if (AmbientSoundComboBox.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "none";
                PlayAmbientSound(tag);
            }
        }

        private void AmbientVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_ambientPlayer == null) return;
            // Map 0-100 to 0.0-1.0 volume scale
            _ambientPlayer.Volume = e.NewValue / 100.0;
        }

        // ── Helper presentation functions ────────────────────────────────────
        private void ToggleFullscreen(bool enable)
        {
            var window = MainWindow.Instance;
            if (window == null) return;

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                if (enable)
                {
                    appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                }
                else
                {
                    appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ZenMode] Fullscreen toggle error: {ex.Message}");
            }
        }

        private void InitializeAmbientPlayer()
        {
            if (_ambientPlayer != null) return;
            _ambientPlayer = new MediaPlayer();
            _ambientPlayer.IsLoopingEnabled = true;
            if (AmbientVolumeSlider != null)
            {
                _ambientPlayer.Volume = AmbientVolumeSlider.Value / 100.0;
            }
            else
            {
                _ambientPlayer.Volume = 0.5;
            }
        }

        private void PlayAmbientSound(string type)
        {
            InitializeAmbientPlayer();

            string url = "";
            switch (type)
            {
                case "rain":
                    url = "https://palousemindfulness.com/disks/RAIN.mp3";
                    break;
                case "lofi":
                    url = "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-1.mp3";
                    break;
                case "waves":
                    url = "http://www.boweavilrecordings.com/mp3s/ocean.mp3";
                    break;
                default:
                    _ambientPlayer.Pause();
                    return;
            }

            try
            {
                _ambientPlayer.Source = MediaSource.CreateFromUri(new Uri(url));
                _ambientPlayer.Play();
                ShowStatusMessage($"Playing ambient sound: {type}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Ambient] Play failed: {ex.Message}");
            }
        }

        private async Task EnsureTypewriterSoundsDownloadedAsync()
        {
            string clickPath = Path.Combine(JournalManager.Instance.MediaDir, "typewriter_click.mp3");
            string returnPath = Path.Combine(JournalManager.Instance.MediaDir, "typewriter_return.mp3");

            bool needsClick = !File.Exists(clickPath) || new FileInfo(clickPath).Length < 1000;
            bool needsReturn = !File.Exists(returnPath) || new FileInfo(returnPath).Length < 1000;

            if (needsClick || needsReturn)
            {
                using var client = new System.Net.Http.HttpClient();
                try
                {
                    if (needsClick)
                    {
                        var data = await client.GetByteArrayAsync("https://bigsoundbank.com/UPLOAD/mp3/2842.mp3");
                        await File.WriteAllBytesAsync(clickPath, data);
                    }
                    if (needsReturn)
                    {
                        var data = await client.GetByteArrayAsync("https://bigsoundbank.com/UPLOAD/mp3/1317.mp3");
                        await File.WriteAllBytesAsync(returnPath, data);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Typewriter] Download failed: {ex.Message}");
                }
            }

            TypewriterPlayer.Initialize(clickPath, returnPath);
        }
    }

    public static class TypewriterPlayer
    {
        [System.Runtime.InteropServices.DllImport("winmm.dll")]
        private static extern int mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);

        private static string _clickPath;
        private static string _returnPath;
        private const int PoolSize = 8;
        private static int _poolIndex = 0;
        private static bool _isInitialized = false;

        public static void Initialize(string clickPath, string returnPath)
        {
            if (_isInitialized) return;
            _clickPath = clickPath;
            _returnPath = returnPath;

            try
            {
                for (int i = 0; i < PoolSize; i++)
                {
                    mciSendString($"open \"{_clickPath}\" type mpegvideo alias click{i}", null, 0, IntPtr.Zero);
                }
                mciSendString($"open \"{_returnPath}\" type mpegvideo alias returnSound", null, 0, IntPtr.Zero);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Typewriter] Init error: {ex.Message}");
            }
        }

        public static void PlayClick()
        {
            if (!_isInitialized) return;
            try
            {
                int index = _poolIndex;
                _poolIndex = (_poolIndex + 1) % PoolSize;
                
                mciSendString($"seek click{index} to start", null, 0, IntPtr.Zero);
                mciSendString($"play click{index}", null, 0, IntPtr.Zero);
            }
            catch {}
        }

        public static void PlayReturn()
        {
            if (!_isInitialized) return;
            try
            {
                mciSendString("seek returnSound to start", null, 0, IntPtr.Zero);
                mciSendString("play returnSound", null, 0, IntPtr.Zero);
            }
            catch {}
        }

        public static void Close()
        {
            if (!_isInitialized) return;
            try
            {
                for (int i = 0; i < PoolSize; i++)
                {
                    mciSendString($"close click{i}", null, 0, IntPtr.Zero);
                }
                mciSendString("close returnSound", null, 0, IntPtr.Zero);
                _isInitialized = false;
            }
            catch {}
        }
    }
}
