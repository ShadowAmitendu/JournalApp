using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Security.Credentials;
using Span = Microsoft.UI.Xaml.Documents.Span;

namespace JournalApp
{
    public class IconOption
    {
        public string Name { get; set; }
        public string Glyph { get; set; }
    }

    public class ColorOption
    {
        public string Name { get; set; }
        public string Hex { get; set; }
    }

    public class NotesGroup : List<JournalNote>
    {
        public string Key { get; set; }
        public string CategoryColor { get; set; }
        public string CategoryIcon { get; set; }
        public bool IsHeaderVisible { get; set; }

        public NotesGroup(IEnumerable<JournalNote> items) : base(items) { }
    }

    /// <summary>Wrapper used by the Photo Gallery grid to support phantom placeholder items
    /// that pad the last row so the 3-column layout never looks broken.</summary>
    public class GalleryItem
    {
        public JournalNote Note { get; set; }
        public bool IsPlaceholder { get; set; }
        /// <summary>Explicit override for gallery image — falls back to AvatarImagePath (picsum auto-image).</summary>
        public string GalleryImagePath { get; set; }
        // Convenience accessors used by x:Bind in the DataTemplate
        public string HeroImagePath => GalleryImagePath ?? Note?.AvatarImagePath ?? "";
        public string Title => Note?.Title ?? "";
        public string DateModifiedFormatted => Note?.DateModifiedFormatted ?? "";
    }


    public sealed partial class MainPage : Page
    {
        public static MainPage Instance { get; private set; }

        // HttpClients with 15-second timeout to prevent hanging on dead requests
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        private static readonly HttpClient _unsplashHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // ── Secure credential storage (Windows Credential Manager) ────────
        private const string _vaultResource = "JournalApp_GitHub";
        private const string _vaultUsername = "GitHubPAT";

        private static string GetSecureToken()
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(_vaultResource, _vaultUsername);
                cred.RetrievePassword();
                return cred.Password;
            }
            catch { return string.Empty; }
        }

        private static void SaveSecureToken(string token)
        {
            try
            {
                var vault = new PasswordVault();
                // Remove old entry first to avoid duplicates
                try { vault.Remove(vault.Retrieve(_vaultResource, _vaultUsername)); } catch { }
                if (!string.IsNullOrWhiteSpace(token))
                    vault.Add(new PasswordCredential(_vaultResource, _vaultUsername, token));

                RemoveSetting("GitHubUsername");
                RemoveSetting("GitHubCommitsCache_ETag");
                RemoveSetting("GitHubCommitsCache_Repo");
                try
                {
                    string cachePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "github_commits_cache.json");
                    if (File.Exists(cachePath)) File.Delete(cachePath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveSecureToken] Failed: {ex.Message}");
                // Fallback to LocalSettings if Credential Manager is unavailable
                SaveSetting("GitHubToken", token);
                RemoveSetting("GitHubUsername");
                RemoveSetting("GitHubCommitsCache_ETag");
                RemoveSetting("GitHubCommitsCache_Repo");
                try
                {
                    string cachePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "github_commits_cache.json");
                    if (File.Exists(cachePath)) File.Delete(cachePath);
                }
                catch { }
            }
        }

        private static void RemoveSecureToken()
        {
            try
            {
                var vault = new PasswordVault();
                try { vault.Remove(vault.Retrieve(_vaultResource, _vaultUsername)); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RemoveSecureToken] Failed: {ex.Message}");
            }
            RemoveSetting("GitHubToken");
            RemoveSetting("GitHubUsername");
            RemoveSetting("GitHubCommitsCache_ETag");
            RemoveSetting("GitHubCommitsCache_Repo");
            try
            {
                string cachePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "github_commits_cache.json");
                if (File.Exists(cachePath)) File.Delete(cachePath);
            }
            catch { }
        }

        // ── Secure master password storage (Windows Credential Manager) ──────
        private const string _vaultMasterPwResource = "JournalApp_MasterPassword";
        private const string _vaultMasterPwUsername = "MasterPW";

        private static string GetSecureMasterPassword()
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(_vaultMasterPwResource, _vaultMasterPwUsername);
                cred.RetrievePassword();
                return cred.Password;
            }
            catch
            {
                // Migrate from old plaintext LocalSettings if present
                string legacy = GetSetting("MasterPassword", "");
                if (!string.IsNullOrEmpty(legacy))
                {
                    SaveSecureMasterPassword(legacy);
                    RemoveSetting("MasterPassword");
                    return legacy;
                }
                return string.Empty;
            }
        }

        private static void SaveSecureMasterPassword(string password)
        {
            try
            {
                var vault = new PasswordVault();
                // Remove old entry first
                try { vault.Remove(vault.Retrieve(_vaultMasterPwResource, _vaultMasterPwUsername)); } catch { }
                if (!string.IsNullOrWhiteSpace(password))
                    vault.Add(new PasswordCredential(_vaultMasterPwResource, _vaultMasterPwUsername, password));

                // Clean up legacy plaintext entry if it exists
                RemoveSetting("MasterPassword");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveSecureMasterPassword] Failed: {ex.Message}");
            }
        }

        private static string GetSetting(string key, string defaultValue = "")
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (localSettings.Values.TryGetValue(key, out object val) && val is string str)
                {
                    return str;
                }
            }
            catch (InvalidOperationException)
            {
                try
                {
                    string path = Path.Combine(JournalManager.Instance.DataDir, "appsettings.json");
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null && dict.TryGetValue(key, out string val))
                        {
                            return val;
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[GetSetting] File fallback failed for key '{key}': {fallbackEx.Message}");
                }
            }
            return defaultValue;
        }

        private static void SaveSetting(string key, string value)
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values[key] = value;
            }
            catch (InvalidOperationException)
            {
                try
                {
                    string path = Path.Combine(JournalManager.Instance.DataDir, "appsettings.json");
                    Dictionary<string, string> dict = null;
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    }
                    dict ??= new Dictionary<string, string>();
                    dict[key] = value;
                    File.WriteAllText(path, JsonSerializer.Serialize(dict));
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveSetting] File fallback failed for key '{key}': {fallbackEx.Message}");
                }
            }
        }

        private static void RemoveSetting(string key)
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values.Remove(key);
            }
            catch (InvalidOperationException)
            {
                try
                {
                    string path = Path.Combine(JournalManager.Instance.DataDir, "appsettings.json");
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null && dict.Remove(key))
                        {
                            File.WriteAllText(path, JsonSerializer.Serialize(dict));
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[RemoveSetting] File fallback failed for key '{key}': {fallbackEx.Message}");
                }
            }
        }


        private JournalNote _selectedNote;
        private bool _isSelectingNote = false;

        public JournalNote SelectedNote
        {
            get => _selectedNote;
            set
            {
                if (_selectedNote != value)
                {
                    // Save the current note content first
                    if (_selectedNote != null && _isDirty)
                    {
                        SaveCurrentNoteContent();
                    }

                    _selectedNote = value;

                    // Synchronize list selection to match the active note
                    if (NotesListView != null && NotesListView.SelectedItem != _selectedNote)
                    {
                        _isSelectingNote = true;
                        try
                        {
                            NotesListView.SelectedItem = _selectedNote;
                        }
                        finally
                        {
                            _isSelectingNote = false;
                        }
                    }

                    _isLoadingNote = true;
                    _isDataLoaded = false; // Disable auto-save events while loading note details
                    _autoSaveTimer.Stop();

                    if (_selectedNote != null)
                    {
                        // Set up UI for active note
                        EditorStackPanel.Visibility = Visibility.Visible;
                        NoNotePlaceholder.Visibility = Visibility.Collapsed;
                        if (EditorToolbar != null)
                        {
                            EditorToolbar.Visibility = Visibility.Visible;
                        }
                        if (EditorStatusBar != null)
                        {
                            EditorStatusBar.Visibility = Visibility.Visible;
                        }

                        TitleTextBox.Text = _selectedNote.Title;
                        FavoriteToggle.IsChecked = _selectedNote.IsFavorite;
                        PinToggle.IsChecked = _selectedNote.IsPinned;

                        // Load Hero Image
                        UpdateHeroImageUI();

                        // Load Date and Category details in the redesigned header
                        UpdateEditorHeaderUI();

                        // Initialize Date/Time Editor Flyout controls
                        if (EditDateCalendarView != null)
                        {
                            EditDateCalendarView.SelectedDates.Clear();
                            EditDateCalendarView.SelectedDates.Add(_selectedNote.DateCreated.Date);
                        }
                        if (IncludeTimeToggle != null)
                        {
                            IncludeTimeToggle.IsOn = _selectedNote.HasTime;
                        }
                        if (EditTimePicker != null)
                        {
                            EditTimePicker.Time = _selectedNote.DateCreated.TimeOfDay;
                            EditTimePicker.Visibility = _selectedNote.HasTime ? Visibility.Visible : Visibility.Collapsed;
                        }

                        // Reset Markdown Preview Toggle and visibility
                        if (MarkdownPreviewToggle != null)
                        {
                            MarkdownPreviewToggle.IsChecked = false;
                        }
                        if (NoteRichEditBox != null)
                        {
                            NoteRichEditBox.Visibility = Visibility.Visible;
                        }
                        if (MarkdownPreviewTextBlock != null)
                        {
                            MarkdownPreviewTextBlock.Visibility = Visibility.Collapsed;
                        }

                        // Apply note's saved editor width
                        ApplyEditorWidth(_selectedNote.EditorWidth);

                        // Load RTF file
                        LoadNoteContent();
                        UpdateWordCount();
                        UpdateMomentsUI();
                        if (StatusMessageTextBlock != null)
                        {
                            StatusMessageTextBlock.Text = "All changes saved locally";
                        }

                        _isDirty = false;
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            _isDirty = false;
                            _isLoadingNote = false;
                            _isDataLoaded = true;
                        });
                    }
                    else
                    {
                        EditorStackPanel.Visibility = Visibility.Collapsed;
                        NoNotePlaceholder.Visibility = Visibility.Visible;
                        if (EditorToolbar != null)
                        {
                            EditorToolbar.Visibility = Visibility.Collapsed;
                        }
                        if (EditorStatusBar != null)
                        {
                            EditorStatusBar.Visibility = Visibility.Collapsed;
                        }
                    }

                }
            }
        }

        private string _selectedCategory = "All Entries";
        private string _currentSortOption = "DateCreatedDesc";
        private List<JournalNote> _allNotes;
        private DispatcherTimer _autoSaveTimer;
        private bool _isDataLoaded = false;
        private bool _isLoadingNote = false;
        private bool _isDirty = false;
        private bool _isPageInitialized = false;
        private object _previousSelectedItem;
        private bool _isUpdatingEffectsUI = false;
        private bool _isFilteringByTag = false;
        private bool _isNavigating = false;

        // Undo trash
        private JournalNote _lastSoftDeletedNote;
        private JournalNote _lastSoftDeletedPreviousSelection;
        private DispatcherTimer _undoToastTimer;



        // Full-text search cache (key = note Id, value = stripped plain text)
        private readonly Dictionary<string, string> _rtfTextCache = new();

        // Security & Locking
        private readonly List<string> _unlockedCategories = new();
        private readonly List<string> _lockedCategories = new();
        private string _masterPassword = "";
        private bool _disableSavingCurrentNote = false;
        private static readonly System.Threading.SemaphoreSlim _dialogSemaphore = new(1, 1);

        // User-configurable settings state
        private bool _useWindowsHello = true;
        private bool _lockOnMinimize = false;
        private bool _confirmBeforeDelete = false;
        private bool _showWordCount = true;
        private bool _showSnippets = true;


        public MainPage()
        {
            Instance = this;
            try
            {
                this.InitializeComponent();
            }
            catch (Exception ex)
            {
                try
                {
                    string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JournalApp", "crash.txt");
                    System.IO.File.WriteAllText(path, ex.ToString());
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[InitializeComponent] Failed: {innerEx.Message}");
                }
                throw;
            }

            _isPageInitialized = true;
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.0) };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
            _recordingTimer.Tick += RecordingTimer_Tick;
            _audioPlayer.MediaEnded += AudioPlayer_MediaEnded;

            _undoToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.0) };
            _undoToastTimer.Tick += (s, e) =>
            {
                _undoToastTimer.Stop();
                if (UndoTrashInfoBar != null) UndoTrashInfoBar.IsOpen = false;
                _lastSoftDeletedNote = null;
                _lastSoftDeletedPreviousSelection = null;
            };

            Loaded += MainPage_Loaded;

            // Initialize custom category icon and color options
            var iconOptions = new List<IconOption>
            {
                new IconOption { Name = "Folder", Glyph = "\uE8B7" },
                new IconOption { Name = "Journal", Glyph = "\uE8F1" },
                new IconOption { Name = "Heart", Glyph = "\uE006" },
                new IconOption { Name = "Star", Glyph = "\uE734" },
                new IconOption { Name = "Home", Glyph = "\uE80F" },
                new IconOption { Name = "Briefcase", Glyph = "\uE821" },
                new IconOption { Name = "Lightbulb", Glyph = "\uEA80" },
                new IconOption { Name = "Tag", Glyph = "\uE8EC" },
                new IconOption { Name = "Pen", Glyph = "\uEDC6" },
                new IconOption { Name = "Cloud", Glyph = "\uE753" },
                new IconOption { Name = "Map", Glyph = "\uE707" },
                new IconOption { Name = "Music", Glyph = "\uE189" }
            };

            var colorOptions = new List<ColorOption>
            {
                new ColorOption { Name = "Blue", Hex = "#0078D4" },
                new ColorOption { Name = "Pink", Hex = "#E3008C" },
                new ColorOption { Name = "Green", Hex = "#107C41" },
                new ColorOption { Name = "Yellow", Hex = "#FFB900" },
                new ColorOption { Name = "Purple", Hex = "#5C2D91" },
                new ColorOption { Name = "Teal", Hex = "#008272" },
                new ColorOption { Name = "Orange", Hex = "#D83B01" },
                new ColorOption { Name = "Red", Hex = "#A80000" },
                new ColorOption { Name = "Gray", Hex = "#8A8886" }
            };

            IconSelectionGridView.ItemsSource = iconOptions;
            ColorSelectionGridView.ItemsSource = colorOptions;

            IconSelectionGridView.SelectedIndex = 0;
            ColorSelectionGridView.SelectedIndex = 8;

            EditIconSelectionGridView.ItemsSource = iconOptions;
            EditColorSelectionGridView.ItemsSource = colorOptions;
        }

        public void ToggleCategoriesPane()
        {
            CategoriesNavView.IsPaneOpen = !CategoriesNavView.IsPaneOpen;
        }

        public void ShowStatusMessage(string message)
        {
            if (StatusMessageTextBlock != null)
            {
                StatusMessageTextBlock.Text = message;
            }
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AboutVersionTextBlock != null)
                {
                    AboutVersionTextBlock.Text = GetAppVersion();
                }
                LoadSavedGitHubSettings();
                LoadCategoriesList();
                RefreshNotesList();
                CheckAppLockOnStartup();
                LoadSavedFonts();
                _ = PopulateMicrophoneDevicesAsync();
                
                // Auto-select "All Entries"
                var allEntriesCategory = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name == "All Entries");
                if (allEntriesCategory != null)
                {
                    var allEntriesItem = CategoriesNavView.MenuItems
                        .OfType<NavigationViewItem>()
                        .FirstOrDefault(item => (item.Tag as JournalCategory)?.Name == "All Entries");
                    if (allEntriesItem != null)
                    {
                        CategoriesNavView.SelectedItem = allEntriesItem;
                        _previousSelectedItem = allEntriesItem;
                    }
                }
                UpdateSaveSettingsButtonState();
                TriggerUpdateCheckStartup();
                UpdateStreakUI();

                // Save on window close / deactivate (WinUI 3 safe pattern)
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.Closed += OnWindowClosed;
                    MainWindow.Instance.VisibilityChanged += OnWindowVisibilityChanged;
                    UpdateTitleBarBackupButtonState();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JournalApp", "crash.txt");
                    System.IO.File.WriteAllText(path, "Loaded Crash:\n" + ex.ToString());
                }
                catch {}
                throw;
            }
        }

        private void CheckAppLockOnStartup()
        {
            if (!string.IsNullOrEmpty(_masterPassword))
            {
                if (AppLockOverlay != null)
                {
                    AppLockOverlay.Visibility = Visibility.Visible;
                }

                if (AppLockPasswordBox != null)
                {
                    AppLockPasswordBox.Password = "";
                }

                if (AppLockStatusText != null)
                {
                    AppLockStatusText.Visibility = Visibility.Collapsed;
                }

                // Check Hello capability and auto-authenticate only if the setting is enabled
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    bool helloAvailable = false;
                    if (_useWindowsHello)
                    {
                        try
                        {
                            var availability = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
                            helloAvailable = availability == Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available;
                        }
                        catch { }
                    }

                    if (WindowsHelloButton != null)
                    {
                        WindowsHelloButton.Visibility = helloAvailable ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (helloAvailable)
                    {
                        await AutoAuthenticateHelloAsync();
                    }
                });
            }
        }

        private async void WindowsHelloButton_Click(object sender, RoutedEventArgs e)
        {
            await AutoAuthenticateHelloAsync();
        }

        private async Task AutoAuthenticateHelloAsync()
        {
            if (AppLockStatusText != null)
            {
                AppLockStatusText.Visibility = Visibility.Collapsed;
            }

            bool verified = await VerifyWithWindowsHelloAsync();
            if (verified)
            {
                UnlockJournal();
            }
            else
            {
                if (AppLockStatusText != null)
                {
                    AppLockStatusText.Text = "Windows Hello authentication cancelled or failed.";
                    AppLockStatusText.Visibility = Visibility.Visible;
                }
            }
        }

        private async Task<bool> VerifyWithWindowsHelloAsync()
        {
            try
            {
                var availability = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
                if (availability == Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available)
                {
                    var result = await Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync("Authenticate to unlock your Journal.");
                    return result == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsHello] Verification error: {ex.Message}");
            }
            return false;
        }

        private void UnlockWithPassword_Click(object sender, RoutedEventArgs e)
        {
            VerifyAndUnlockWithPassword();
        }

        private void AppLockPasswordBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                VerifyAndUnlockWithPassword();
                e.Handled = true;
            }
        }

        private void VerifyAndUnlockWithPassword()
        {
            if (AppLockPasswordBox == null) return;
            string entered = AppLockPasswordBox.Password;
            if (entered == _masterPassword)
            {
                UnlockJournal();
            }
            else
            {
                if (AppLockStatusText != null)
                {
                    AppLockStatusText.Text = "Incorrect password. Please try again.";
                    AppLockStatusText.Visibility = Visibility.Visible;
                }
                AppLockPasswordBox.SelectAll();
            }
        }

        private void UnlockJournal()
        {
            if (AppLockOverlay != null)
            {
                AppLockOverlay.Visibility = Visibility.Collapsed;
            }
            if (AppLockPasswordBox != null)
            {
                AppLockPasswordBox.Password = "";
            }
            if (AppLockStatusText != null)
            {
                AppLockStatusText.Visibility = Visibility.Collapsed;
            }
        }

        private void OnWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs e)
        {
            // Final flush before the process exits
            try
            {
                if (_isDirty && SelectedNote != null)
                    SaveCurrentNoteContent();
            }
            catch { }

            // Dispose the audio player to release native handles
            try
            {
                _audioPlayer?.Dispose();
                _audioPlayer = null;
            }
            catch { }
        }

        private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs e)
        {
            // Save when the window is hidden (minimized / occluded)
            if (!e.Visible && _isDirty && SelectedNote != null)
            {
                try { SaveCurrentNoteContent(); } catch { }
            }

            // Re-lock when minimized if the setting is on
            if (!e.Visible && _lockOnMinimize && !string.IsNullOrEmpty(_masterPassword))
            {
                if (AppLockOverlay != null)
                    AppLockOverlay.Visibility = Visibility.Visible;
                if (AppLockPasswordBox != null)
                    AppLockPasswordBox.Password = "";
                if (AppLockStatusText != null)
                    AppLockStatusText.Visibility = Visibility.Collapsed;

                // Re-trigger Hello on restore if enabled
                if (_useWindowsHello)
                {
                    this.DispatcherQueue.TryEnqueue(async () =>
                    {
                        bool helloAvailable = false;
                        try
                        {
                            var availability = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
                            helloAvailable = availability == Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available;
                        }
                        catch { }
                        if (WindowsHelloButton != null)
                            WindowsHelloButton.Visibility = helloAvailable ? Visibility.Visible : Visibility.Collapsed;
                    });
                }
            }
        }

        private Microsoft.UI.Xaml.Media.Brush GetThemeBrush(string key, string fallbackHex = "#808080")
        {
            try
            {
                if (Application.Current.Resources.TryGetValue(key, out object val) && val is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    return brush;
                }
                
                foreach (var dict in Application.Current.Resources.MergedDictionaries)
                {
                    if (dict.TryGetValue(key, out object val2) && val2 is Microsoft.UI.Xaml.Media.Brush brush2)
                    {
                        return brush2;
                    }
                    if (dict.ThemeDictionaries != null)
                    {
                        foreach (var themeKey in dict.ThemeDictionaries.Keys)
                        {
                            if (dict.ThemeDictionaries[themeKey] is ResourceDictionary themeDict)
                            {
                                if (themeDict.TryGetValue(key, out object val3) && val3 is Microsoft.UI.Xaml.Media.Brush brush3)
                                {
                                    return brush3;
                                }
                            }
                        }
                    }
                }
                
                if (this.Resources.TryGetValue(key, out object pageVal) && pageVal is Microsoft.UI.Xaml.Media.Brush pageBrush)
                {
                    return pageBrush;
                }
            }
            catch { }
            return GetBrushFromHex(fallbackHex);
        }

        private void LoadCategoriesList()
        {
            CategoriesNavView.MenuItems.Clear();
            var categories = JournalManager.Instance.Categories;
            foreach (var category in categories)
            {
                var fontIcon = new FontIcon 
                { 
                    Glyph = category.Icon, 
                    FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] 
                };
                if (!string.IsNullOrEmpty(category.Color))
                {
                    fontIcon.Foreground = GetBrushFromHex(category.Color);
                }

                var navItem = new NavigationViewItem
                {
                    Content = category.Name,
                    Tag = category,
                    Icon = fontIcon,
                    CanDrag = true,
                    AllowDrop = true
                };

                navItem.DragStarting += Category_DragStarting;
                navItem.DragOver += Category_DragOver;
                navItem.Drop += Category_Drop;
                navItem.ContextFlyout = CreateCategoryMenuFlyout();

                CategoriesNavView.MenuItems.Add(navItem);
            }

            // Append separator and "Add Category" item programmatically
            CategoriesNavView.MenuItems.Add(new NavigationViewItemSeparator());

            var addCategoryItem = new NavigationViewItem
            {
                Content = "Add Category",
                Icon = new FontIcon { Glyph = "\uE710", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] },
                Tag = "AddCategory"
            };
            CategoriesNavView.MenuItems.Add(addCategoryItem);

            // Gather all tags from notes
            var uniqueTags = new HashSet<string>();
            if (JournalManager.Instance.Notes != null)
            {
                foreach (var note in JournalManager.Instance.Notes)
                {
                    if (note.IsDeleted) continue;
                    if (note.Tags != null)
                    {
                        foreach (var tag in note.Tags)
                        {
                            uniqueTags.Add(tag.ToLowerInvariant());
                        }
                    }
                }
            }

            if (uniqueTags.Count > 0)
            {
                CategoriesNavView.MenuItems.Add(new NavigationViewItemSeparator());
                
                var tagsHeader = new NavigationViewItemHeader
                {
                    Content = "Tags"
                };
                CategoriesNavView.MenuItems.Add(tagsHeader);

                foreach (var tag in uniqueTags)
                {
                    int count = JournalManager.Instance.Notes.Count(n => !n.IsDeleted && n.Tags != null && n.Tags.Contains(tag));
                    var navItem = new NavigationViewItem
                    {
                        Content = $"#{tag} ({count})",
                        Tag = $"Tag:{tag}",
                        Icon = new FontIcon { Glyph = "\uE1CB", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] }
                    };
                    CategoriesNavView.MenuItems.Add(navItem);
                }
            }
        }

        private MenuFlyout CreateCategoryMenuFlyout()
        {
            var menu = new MenuFlyout();
            menu.Opening += CategoryMenuFlyout_Opening;

            var renameItem = new MenuFlyoutItem { Text = "Rename Category" };
            renameItem.Icon = new FontIcon { Glyph = "\uE8AC", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] };
            renameItem.Click += RenameCategory_Click;
            menu.Items.Add(renameItem);

            var changeItem = new MenuFlyoutItem { Text = "Change Icon & Color" };
            changeItem.Icon = new FontIcon { Glyph = "\uE7B3", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] };
            changeItem.Click += ChangeCategoryIconColor_Click;
            menu.Items.Add(changeItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var moveUpItem = new MenuFlyoutItem { Text = "Move Up" };
            moveUpItem.Icon = new FontIcon { Glyph = "\uE110", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] };
            moveUpItem.Click += MoveCategoryUp_Click;
            menu.Items.Add(moveUpItem);

            var moveDownItem = new MenuFlyoutItem { Text = "Move Down" };
            moveDownItem.Icon = new FontIcon { Glyph = "\uE1FD", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"] };
            moveDownItem.Click += MoveCategoryDown_Click;
            menu.Items.Add(moveDownItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete Category", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) };
            deleteItem.Icon = new FontIcon { Glyph = "\uE74D", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"], Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) };
            deleteItem.Click += DeleteCategory_Click;
            menu.Items.Add(deleteItem);

            return menu;
        }

        private void RefreshNotesList()
        {
            _allNotes = JournalManager.Instance.Notes;

            // Apply category filter
            var query = _allNotes.AsEnumerable();
            if (_selectedCategory == "Trash")
            {
                query = query.Where(n => n.IsDeleted);
            }
            else if (_selectedCategory == "Favorites")
            {
                query = query.Where(n => !n.IsDeleted && n.IsFavorite);
            }
            else if (_selectedCategory != null && _selectedCategory.StartsWith("Tag:"))
            {
                string tag = _selectedCategory.Substring(4).ToLowerInvariant();
                query = query.Where(n => !n.IsDeleted && n.Tags != null && n.Tags.Contains(tag));
            }
            else
            {
                query = query.Where(n => !n.IsDeleted);
                if (_selectedCategory != "All Entries")
                {
                    query = query.Where(n => n.Category == _selectedCategory);
                }
            }

            // Apply search query filter (full-text: title + snippet + RTF body)
            string search = MainWindow.Instance?.SearchText?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(n => NoteMatchesSearch(n, search));
            }

            var categoriesList = JournalManager.Instance.Categories;

            // Apply sorting: group by category first if in "All Entries", otherwise just flat sorting
            if (_selectedCategory == "All Entries")
            {
                // Create a category index map to preserve the custom categories ordering from the sidebar
                var categoryIndexMap = categoriesList
                    .Select((c, idx) => new { c.Name, idx })
                    .ToDictionary(x => x.Name, x => x.idx, StringComparer.OrdinalIgnoreCase);

                query = query.OrderBy(n => 
                {
                    if (categoryIndexMap.TryGetValue(n.Category, out int index))
                        return index;
                    return int.MaxValue;
                });

                var orderedQuery = ((IOrderedEnumerable<JournalNote>)query).ThenByDescending(n => n.IsPinned);
                
                switch (_currentSortOption)
                {
                    case "DateCreatedAsc":
                        query = orderedQuery.ThenBy(n => n.DateCreated);
                        break;
                    case "DateModifiedDesc":
                        query = orderedQuery.ThenByDescending(n => n.DateModified);
                        break;
                    case "TitleAsc":
                        query = orderedQuery.ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "TitleDesc":
                        query = orderedQuery.ThenByDescending(n => n.Title, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "DateCreatedDesc":
                    default:
                        query = orderedQuery.ThenByDescending(n => n.DateCreated);
                        break;
                }
            }
            else
            {
                // Normal flat sorting when viewing a single category, trash, or favorites
                var orderedQuery = query.OrderByDescending(n => n.IsPinned);
                
                switch (_currentSortOption)
                {
                    case "DateCreatedAsc":
                        query = orderedQuery.ThenBy(n => n.DateCreated);
                        break;
                    case "DateModifiedDesc":
                        query = orderedQuery.ThenByDescending(n => n.DateModified);
                        break;
                    case "TitleAsc":
                        query = orderedQuery.ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "TitleDesc":
                        query = orderedQuery.ThenByDescending(n => n.Title, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "DateCreatedDesc":
                    default:
                        query = orderedQuery.ThenByDescending(n => n.DateCreated);
                        break;
                }
            }

            var list = query.ToList();

            // Reset all transient header and divider flags
            foreach (var note in _allNotes)
            {
                note.IsCategoryHeaderVisible = false;
                note.IsBottomDividerVisible = true;
            }

            // Dynamically evaluate bottom borders for note items
            for (int i = 0; i < list.Count; i++)
            {
                var note = list[i];
                // Hide the bottom divider line if this is the last note in the list,
                // or if the next note belongs to a different category
                if (i == list.Count - 1 || note.Category != list[i + 1].Category)
                {
                    note.IsBottomDividerVisible = false;
                }
            }

            // Group the list for CollectionViewSource
            var grouped = new List<NotesGroup>();
            if (_selectedCategory == "All Entries")
            {
                var categoryIndexMap = categoriesList
                    .Select((c, idx) => new { c.Name, idx })
                    .ToDictionary(x => x.Name, x => x.idx, StringComparer.OrdinalIgnoreCase);

                var groups = list.GroupBy(n => n.Category)
                    .OrderBy(g =>
                    {
                        if (categoryIndexMap.TryGetValue(g.Key, out int index))
                            return index;
                        return int.MaxValue;
                    }).ToList();

                bool showHeaders = groups.Count > 1;

                foreach (var g in groups)
                {
                    var catInfo = categoriesList.FirstOrDefault(c => c.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
                    grouped.Add(new NotesGroup(g)
                    {
                        Key = g.Key,
                        CategoryColor = catInfo?.Color ?? "#808080",
                        CategoryIcon = catInfo?.Icon ?? "\uE889",
                        IsHeaderVisible = showHeaders
                    });
                }
            }
            else
            {
                // Single category, Trash, or Favorites - group into a single group for consistency
                string groupKey = _selectedCategory;
                var catInfo = categoriesList.FirstOrDefault(c => c.Name.Equals(groupKey, StringComparison.OrdinalIgnoreCase));
                
                string color = "#808080";
                string icon = "\uE889";
                if (groupKey != null && groupKey.StartsWith("Tag:"))
                {
                    groupKey = $"#{groupKey.Substring(4)}";
                    color = "#FF8C00"; // Dark Orange for tags
                    icon = "\uE1CB";   // Tag icon
                }
                else if (catInfo != null)
                {
                    color = catInfo.Color ?? "#808080";
                    icon = catInfo.Icon ?? "\uE889";
                }
                else if (groupKey == "Trash")
                {
                    color = "#FF0000";
                    icon = "\uE74D";
                }
                else if (groupKey == "Favorites")
                {
                    color = "#FFD700";
                    icon = "\uE734";
                }

                grouped.Add(new NotesGroup(list)
                {
                    Key = groupKey,
                    CategoryColor = color,
                    CategoryIcon = icon,
                    IsHeaderVisible = false // Hide group headers when viewing a single category/Trash/Favorites
                });
            }

            if (NotesCVS != null)
            {
                NotesCVS.Source = grouped;
            }
            
            if (NewNoteButton != null)
            {
                NewNoteButton.Visibility = (_selectedCategory == "Trash") ? Visibility.Collapsed : Visibility.Visible;
            }
            if (PlaceholderNewNoteButton != null)
            {
                PlaceholderNewNoteButton.Visibility = (_selectedCategory == "Trash") ? Visibility.Collapsed : Visibility.Visible;
            }
            
            // Update Trash badge and Empty Trash button
            int trashCount = _allNotes.Count(n => n.IsDeleted);
            if (TrashInfoBadge != null)
            {
                TrashInfoBadge.Value = trashCount;
                TrashInfoBadge.Visibility = trashCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (EmptyTrashButton != null)
            {
                EmptyTrashButton.Visibility = (_selectedCategory == "Trash") ? Visibility.Visible : Visibility.Collapsed;
                EmptyTrashButton.IsEnabled = trashCount > 0;
            }

            // Update status bar note count
            string categoryName = _selectedCategory == "All Entries" ? "All Categories" : _selectedCategory;
            if (EntryCountTextBlock != null)
            {
                EntryCountTextBlock.Text = $"{list.Count} {(list.Count == 1 ? "entry" : "entries")} in {categoryName}";
            }
            
            // Select current note in list if it's still present in the list
            if (SelectedNote != null && list.Contains(SelectedNote))
            {
                NotesListView.SelectedItem = SelectedNote;
            }

            UpdateStreakUI();
        }

        private async void LoadNoteContent()
        {
            if (SelectedNote == null) return;

            if (TitleTextBox != null) TitleTextBox.IsEnabled = true;
            UpdateLockNoteButtonState();

            // ── Route to block editor for markdown notes ──────────────────────
            if (SelectedNote.ContentFormat == "markdown")
            {
                if (NoteRichEditBox != null) NoteRichEditBox.IsEnabled = false;
                await LoadMarkdownNoteAsync(SelectedNote);
                return;
            }

            // ── Legacy RTF path ───────────────────────────────────────────────
            // Ensure RTF editor is visible and WebView2 is hidden
            if (NoteRichEditBox != null)
            {
                NoteRichEditBox.Visibility = Visibility.Visible;
                NoteRichEditBox.IsEnabled = true;
            }
            if (NoteEditorWebView != null) NoteEditorWebView.Visibility = Visibility.Collapsed;

            if (SelectedNote.IsLocked)
            {
                bool verified = false;
                try
                {
                    var verResult = await Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync($"Unlock journal entry: {SelectedNote.Title}");
                    if (verResult == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified)
                    {
                        verified = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UserConsentVerifier error: {ex.Message}");
                    if (!string.IsNullOrEmpty(_masterPassword))
                    {
                        string pwdInput = await PromptForPasswordInputAsync("Locked Entry", "Enter master password to unlock:");
                        if (pwdInput == _masterPassword)
                        {
                            verified = true;
                        }
                    }
                    else
                    {
                        verified = true;
                    }
                }

                if (!verified)
                {
                    if (NoteRichEditBox != null)
                    {
                        NoteRichEditBox.Document.SetText(TextSetOptions.None, "🔒 [Locked Entry - Biometric Verification Failed]");
                        NoteRichEditBox.IsEnabled = false;
                    }
                    if (TitleTextBox != null)
                    {
                        TitleTextBox.IsEnabled = false;
                    }
                    _disableSavingCurrentNote = true;
                    return;
                }
            }

            try
            {
                string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(SelectedNote.RtfFileName);
                if (File.Exists(rtfPath))
                {
                    byte[] fileBytes = File.ReadAllBytes(rtfPath);
                    // Default to NOT encrypted. Only mark encrypted when file is long enough
                    // to check the header AND it does NOT start with "{\rtf"
                    bool isEncrypted = false;
                    if (fileBytes.Length >= 5)
                    {
                        // Check if it starts with "{\rtf" (ASCII: 123, 92, 114, 116, 102)
                        if (!(fileBytes[0] == 123 && fileBytes[1] == 92 && fileBytes[2] == 114 && fileBytes[3] == 116 && fileBytes[4] == 102))
                        {
                            isEncrypted = true;
                        }
                    }

                    byte[] loadedBytes = fileBytes;
                    bool decryptionFailedOrSkipped = false;
                    if (isEncrypted)
                    {
                        if (_lockedCategories.Contains(SelectedNote.Category) && !string.IsNullOrEmpty(_masterPassword))
                        {
                            try
                            {
                                loadedBytes = EncryptionHelper.Decrypt(fileBytes, _masterPassword);
                            }
                            catch (Exception decryptEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[LoadNoteContent] Decryption failed: {decryptEx.Message}");
                                NoteRichEditBox.Document.SetText(TextSetOptions.None, "🔒 [Encrypted Note - Decryption Failed]");
                                decryptionFailedOrSkipped = true;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[LoadNoteContent] Encrypted note skipped (not in locked category or empty password)");
                            NoteRichEditBox.Document.SetText(TextSetOptions.None, "🔒 [Encrypted Note - Locked]");
                            decryptionFailedOrSkipped = true;
                        }
                    }

                    if (decryptionFailedOrSkipped)
                    {
                        _disableSavingCurrentNote = true;
                        return;
                    }

                    _disableSavingCurrentNote = false;

                    using (var ms = new MemoryStream(loadedBytes))
                    {
                        var winrtStream = ms.AsRandomAccessStream();
                        NoteRichEditBox.Document.LoadFromStream(TextSetOptions.FormatRtf, winrtStream);
                    }

                    // Prevent hardcoded black text in dark theme or white text in light theme
                    NoteRichEditBox.Document.GetText(TextGetOptions.None, out string rtfText);
                    if (!string.IsNullOrEmpty(rtfText))
                    {
                        var start = NoteRichEditBox.Document.Selection.StartPosition;
                        var length = NoteRichEditBox.Document.Selection.Length;

                        NoteRichEditBox.Document.Selection.SetRange(0, rtfText.Length);
                        var format = NoteRichEditBox.Document.Selection.CharacterFormat;

                        if (this.ActualTheme == ElementTheme.Dark)
                        {
                            if (format.ForegroundColor.R == 0 && format.ForegroundColor.G == 0 && format.ForegroundColor.B == 0)
                            {
                                format.ForegroundColor = Microsoft.UI.Colors.White;
                                NoteRichEditBox.Document.Selection.CharacterFormat = format;
                            }
                        }
                        else if (this.ActualTheme == ElementTheme.Light)
                        {
                            if (format.ForegroundColor.R == 255 && format.ForegroundColor.G == 255 && format.ForegroundColor.B == 255)
                            {
                                format.ForegroundColor = Microsoft.UI.Colors.Black;
                                NoteRichEditBox.Document.Selection.CharacterFormat = format;
                            }
                        }

                        // Apply Paragraph Spacing (SpaceAfter)
                        var paraFormat = NoteRichEditBox.Document.Selection.ParagraphFormat;
                        paraFormat.SpaceAfter = 10f;
                        NoteRichEditBox.Document.Selection.ParagraphFormat = paraFormat;

                        NoteRichEditBox.Document.Selection.SetRange(start, start + length);
                    }
                }
                else
                {
                    _disableSavingCurrentNote = false;
                    NoteRichEditBox.Document.SetText(TextSetOptions.None, "");
                }

                // Apply correct default text color for new typing/empty states based on active theme
                var defaultFormat = NoteRichEditBox.Document.GetDefaultCharacterFormat();
                if (this.ActualTheme == ElementTheme.Dark)
                {
                    defaultFormat.ForegroundColor = Microsoft.UI.Colors.White;
                }
                else
                {
                    defaultFormat.ForegroundColor = Microsoft.UI.Colors.Black;
                }
                NoteRichEditBox.Document.SetDefaultCharacterFormat(defaultFormat);

                var defaultPara = NoteRichEditBox.Document.GetDefaultParagraphFormat();
                defaultPara.SpaceAfter = 10f;
                NoteRichEditBox.Document.SetDefaultParagraphFormat(defaultPara);

                // Load Blog toggle state
                if (PublishToBlogToggle != null)
                {
                    _isLoadingNote = true;
                    PublishToBlogToggle.IsChecked = SelectedNote.IsBlogPublished;
                    _isLoadingNote = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load RTF content: {ex.Message}");
                NoteRichEditBox.Document.SetText(TextSetOptions.None, "");
            }
        }

        private void SaveCurrentNoteContent()
        {
            if (SelectedNote == null || _disableSavingCurrentNote) return;

            // Block editor (markdown) notes are saved directly via WebView2 message callback
            if (SelectedNote.ContentFormat == "markdown")
            {
                SaveMarkdownNoteContent();
                return;
            }

            try
            {
                // Retrieve plain text first for tags and snippet
                string plainText;
                NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out plainText);

                // Save rich text file
                string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(SelectedNote.RtfFileName);
                byte[] rtfBytes;
                using (var ms = new MemoryStream())
                {
                    var winrtStream = ms.AsRandomAccessStream();
                    NoteRichEditBox.Document.SaveToStream(TextGetOptions.FormatRtf, winrtStream);
                    rtfBytes = ms.ToArray();
                }

                if (_lockedCategories.Contains(SelectedNote.Category) && !string.IsNullOrEmpty(_masterPassword))
                {
                    rtfBytes = EncryptionHelper.Encrypt(rtfBytes, _masterPassword);
                }

                File.WriteAllBytes(rtfPath, rtfBytes);

                // Extract inline hashtags from the note body
                var inlineTags = new List<string>();
                if (!string.IsNullOrEmpty(plainText))
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(plainText, @"\B#([a-zA-Z0-9_]+)");
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        string tag = match.Groups[1].Value.ToLowerInvariant();
                        if (!inlineTags.Contains(tag))
                        {
                            inlineTags.Add(tag);
                        }
                    }
                }
                // Merge inline hashtags with manually-added tags (preserve both)
                var existingTags = SelectedNote.Tags ?? new List<string>();
                foreach (var inlineTag in inlineTags)
                {
                    if (!existingTags.Contains(inlineTag))
                    {
                        existingTags.Add(inlineTag);
                    }
                }
                SelectedNote.Tags = existingTags;

                // Invalidate full-text cache so the next search re-reads from file
                _rtfTextCache.Remove(SelectedNote.Id);

                SelectedNote.Snippet = string.IsNullOrWhiteSpace(plainText) ? "No additional text" :
                    (plainText.Length > 80 ? plainText.Substring(0, 80).Replace("\r", " ").Replace("\n", " ").Trim() : plainText.Replace("\r", " ").Replace("\n", " ").Trim());

                SelectedNote.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "Untitled Note" : TitleTextBox.Text.Trim();
                SelectedNote.DateModified = DateTime.Now;

                JournalManager.Instance.SaveNotesMetadata();
                
                // Refresh list visually (but don't reset selection to avoid focus jumping)
                var currentSelection = SelectedNote;
                LoadCategoriesList(); // Re-populate tags list in case tags changed
                RefreshNotesList();
                SelectedNote = currentSelection;
                
                if (StatusMessageTextBlock != null)
                {
                    StatusMessageTextBlock.Text = $"Saved at {DateTime.Now.ToString("h:mm:ss tt")}";
                }
                _isDirty = false;
                UpdateTitleBarBackupButtonState();

                if (AutoBackupToggle != null && AutoBackupToggle.IsOn)
                {
                    string token = GetSetting("GitHubToken", "");
                    string repo = GetSetting("GitHubRepo", "");
                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repo))
                    {
                        TriggerBackupFromTitleBar();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving note: {ex.Message}");
            }
        }

        private void MarkDirty()
        {
            if (!_isDataLoaded || _isLoadingNote) return;
            _isDirty = true;
            if (StatusMessageTextBlock != null)
            {
                StatusMessageTextBlock.Text = "Saving...";
            }
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
            UpdateTitleBarBackupButtonState();
        }

        private void AutoSaveTimer_Tick(object sender, object e)
        {
            _autoSaveTimer.Stop();
            if (_isDirty && SelectedNote != null)
            {
                SaveCurrentNoteContent();
            }
        }

        // Selection Handlers
        private async void CategoriesNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isNavigating) return;
            _isNavigating = true;
            try
            {
                JournalCategory category = null;
                NavigationViewItem navItem = null;

                if (args.SelectedItem is NavigationViewItem item)
                {
                    navItem = item;
                    if (item.Tag is string tag && tag == "AddCategory")
                    {
                        // Re-select the previous item so "Add Category" button doesn't remain visually selected
                        if (_previousSelectedItem != null)
                        {
                            CategoriesNavView.SelectedItem = _previousSelectedItem;
                        }
                        ShowNewCategoryDialog();
                        return;
                    }
                    else
                    {
                        _previousSelectedItem = item;
                        category = item.Tag as JournalCategory;
                    }
                }

                if (category != null)
                {
                    // Accessing a locked category requires password unlock
                    if (_lockedCategories.Contains(category.Name) && !_unlockedCategories.Contains(category.Name))
                    {
                        if (string.IsNullOrEmpty(_masterPassword))
                        {
                            var noPassDialog = new ContentDialog
                            {
                                Title = "Security Warning",
                                Content = "This category is marked as locked, but no Master Password is set in Settings. Please set a Master Password first.",
                                CloseButtonText = "OK",
                                XamlRoot = this.XamlRoot
                            };
                            await noPassDialog.ShowAsync();
                            if (_previousSelectedItem != null)
                            {
                                CategoriesNavView.SelectedItem = _previousSelectedItem;
                            }
                            return;
                        }

                        var passwordBox = new PasswordBox
                        {
                            Header = "Enter Master Password",
                            PlaceholderText = "Password",
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        var challengeDialog = new ContentDialog
                        {
                            Title = $"Unlock Category: {category.Name}",
                            Content = passwordBox,
                            PrimaryButtonText = "Unlock",
                            CloseButtonText = "Cancel",
                            DefaultButton = ContentDialogButton.Primary,
                            XamlRoot = this.XamlRoot
                        };
                        challengeDialog.Opened += (s, e) => passwordBox.Focus(FocusState.Programmatic);

                        var result = await challengeDialog.ShowAsync();
                        if (result == ContentDialogResult.Primary && passwordBox.Password == _masterPassword)
                        {
                            _unlockedCategories.Add(category.Name);
                        }
                        else
                        {
                            if (result == ContentDialogResult.Primary)
                            {
                                var errorDialog = new ContentDialog
                                {
                                    Title = "Access Denied",
                                    Content = "Incorrect master password. Access denied.",
                                    CloseButtonText = "OK",
                                    XamlRoot = this.XamlRoot
                                };
                                await errorDialog.ShowAsync();
                            }
                            if (_previousSelectedItem != null)
                            {
                                CategoriesNavView.SelectedItem = _previousSelectedItem;
                            }
                            return;
                        }
                    }

                    ShowGrid(MainEditorGrid);
                    _selectedCategory = category.Name;
                    if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = category.Name;
                    RefreshNotesList();
                }
                else if (navItem != null)
                {
                    if (navItem == TrashNavItem)
                    {
                        ShowGrid(MainEditorGrid);
                        _selectedCategory = "Trash";
                        if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = "Trash";
                        RefreshNotesList();
                    }
                    else if (navItem == FavoritesNavItem)
                    {
                        ShowGrid(MainEditorGrid);
                        _selectedCategory = "Favorites";
                        if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = "Favorites";
                        RefreshNotesList();
                    }
                    else if (navItem.Tag is string tagStr && tagStr.StartsWith("Tag:"))
                    {
                        ShowGrid(MainEditorGrid);
                        string tagName = tagStr.Substring(4);
                        _selectedCategory = tagStr;
                        if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = $"#{tagName}";
                        RefreshNotesList();
                    }
                    else if (navItem == SettingsNavItem)
                    {
                        ShowGrid(SettingsGrid);
                        PopulateLockedCategoriesSettings();
                        TriggerUpdateCheck();
                    }
                    else if (navItem == GitHubNavItem)
                    {
                        ShowGrid(GitHubGrid);
                        LoadGitHubCommitsAndHistory();
                    }
                    else if (navItem == StatsNavItem)
                    {
                        ShowGrid(StatsGrid);
                        PopulateMoodStats();
                        PopulateContributionGraph();
                        PopulateTagCloud();
                        _ = LoadMoodChartAsync();
                    }
                    else if (navItem == GalleryNavItem)
                    {
                        ShowGrid(GalleryGrid);
                        PopulateGallery();
                    }
                    else if (navItem == BlogPageNavItem)
                    {
                        ShowGrid(BlogPageGrid);
                        PopulateBlogPage();
                    }
                    else if (navItem == MapNavItem)
                    {
                        ShowGrid(MapGrid);
                        _ = LoadMapDataAsync();
                    }
                }
            }
            finally
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    _isNavigating = false;
                });
            }
        }

        private void NotesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSelectingNote) return;
            if (NotesListView.SelectedItem is JournalNote note)
            {
                _isSelectingNote = true;
                try
                {
                    SelectedNote = note;
                }
                finally
                {
                    _isSelectingNote = false;
                }
            }
        }

        private void Page_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrl = ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (isCtrl && e.Key == Windows.System.VirtualKey.F)
            {
                if (SelectedNote != null && FindReplaceBar != null)
                {
                    FindReplaceBar.Visibility = Visibility.Visible;
                    FindTextBox?.Focus(FocusState.Programmatic);
                    e.Handled = true;
                }
                return;
            }

            if (isCtrl && e.Key == Windows.System.VirtualKey.S)
            {
                if (SelectedNote != null && !_disableSavingCurrentNote)
                {
                    _autoSaveTimer.Stop();
                    SaveCurrentNoteContent();
                    _isDirty = false;
                    if (StatusMessageTextBlock != null)
                        StatusMessageTextBlock.Text = "Saved";
                    e.Handled = true;
                }
                return;
            }


            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                // Close Find bar first if open
                if (FindReplaceBar != null && FindReplaceBar.Visibility == Visibility.Visible)
                {
                    CloseFindBar();
                    e.Handled = true;
                    return;
                }
                if (SelectedNote != null)
                {
                    SelectedNote = null;
                    if (NotesListView != null) NotesListView.SelectedItem = null;
                    e.Handled = true;
                }
            }
        }

        // Search Handlers
        public void OnTitleSearchTextChanged()
        {
            RefreshNotesList();
        }



        // Action Handlers
        private void NewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            var note = JournalManager.Instance.CreateNote(_selectedCategory == "Favorites" ? "Personal" : _selectedCategory);
            if (_selectedCategory == "Favorites")
            {
                note.IsFavorite = true;
                JournalManager.Instance.SaveNotesMetadata();
            }
            RefreshNotesList();
            NotesListView.SelectedItem = note;
        }

        private async void ShowDeleteConfirmationFlyout(FrameworkElement? senderElement, JournalNote note, bool permanentlyDelete)
        {
            if (note == null || senderElement == null) return;

            // Soft delete: instant action with 5-second undo toast — no blocking dialog (unless confirm setting is on)
            if (!permanentlyDelete)
            {
                if (_confirmBeforeDelete)
                {
                    var confirmDialog = new ContentDialog
                    {
                        Title = "Move to Trash?",
                        Content = $"\"{note.Title}\" will be moved to Trash.",
                        PrimaryButtonText = "Move to Trash",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot
                    };
                    var result = await confirmDialog.ShowAsync();
                    if (result != ContentDialogResult.Primary) return;
                }

                _lastSoftDeletedNote = note;
                _lastSoftDeletedPreviousSelection = SelectedNote == note ? null : SelectedNote;

                if (SelectedNote == note)
                    SelectedNote = null;

                JournalManager.Instance.SoftDeleteNote(note);
                _rtfTextCache.Remove(note.Id); // invalidate cache entry
                RefreshNotesList();

                if (UndoTrashInfoBar != null)
                {
                    UndoTrashInfoBar.Message = $"\"{note.Title}\" moved to Trash";
                    UndoTrashInfoBar.IsOpen = true;
                }
                _undoToastTimer.Stop();
                _undoToastTimer.Start();
                return;
            }

            // Permanent delete: keep confirmation flyout
            var flyout = new Flyout();

            var stackPanel = new StackPanel { Spacing = 12, MaxWidth = 280 };

            var textBlock = new TextBlock
            {
                Text = $"Are you sure you want to permanently delete \"{note.Title}\"? This action cannot be undone.",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap
            };

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            var cancelButton = new Button { Content = "Cancel" };
            cancelButton.Click += (s, args) => flyout.Hide();

            var confirmButton = new Button
            {
                Content = "Delete Permanently",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red)
            };

            confirmButton.Click += (s, args) =>
            {
                flyout.Hide();
                if (SelectedNote == note) SelectedNote = null;
                _rtfTextCache.Remove(note.Id);
                JournalManager.Instance.PermanentlyDeleteNote(note);
                ShowStatusMessage("Journal entry permanently deleted");
                RefreshNotesList();
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(confirmButton);
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(buttonsPanel);
            flyout.Content = stackPanel;

            FrameworkElement anchor = senderElement;
            if (senderElement is MenuFlyoutItem)
                anchor = NotesListView.ContainerFromItem(note) as FrameworkElement ?? NotesListView;

            flyout.ShowAt(anchor);
        }

        private void UndoTrashButton_Click(object sender, RoutedEventArgs e)
        {
            _undoToastTimer.Stop();
            if (UndoTrashInfoBar != null) UndoTrashInfoBar.IsOpen = false;

            if (_lastSoftDeletedNote != null)
            {
                JournalManager.Instance.RestoreNote(_lastSoftDeletedNote);
                RefreshNotesList();
                // Restore the selection that was active before deletion
                if (_lastSoftDeletedPreviousSelection != null)
                    NotesListView.SelectedItem = _lastSoftDeletedPreviousSelection;
                else
                    NotesListView.SelectedItem = _lastSoftDeletedNote;
                ShowStatusMessage($"Restored \"{_lastSoftDeletedNote.Title}\"");
                _lastSoftDeletedNote = null;
                _lastSoftDeletedPreviousSelection = null;
            }
        }

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            ShowDeleteConfirmationFlyout(sender as FrameworkElement, SelectedNote, SelectedNote.IsDeleted);
        }

        private async void EmptyTrashButton_Click(object sender, RoutedEventArgs e)
        {
            int deletedCount = JournalManager.Instance.Notes.Count(n => n.IsDeleted);
            if (deletedCount == 0) return;

            var dialog = new ContentDialog
            {
                Title = "Empty Trash",
                Content = $"Are you sure you want to permanently delete all {deletedCount} items in the Trash? This action cannot be undone.",
                PrimaryButtonText = "Empty Trash",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // Unselect the active note if it is deleted (since it's about to be permanently deleted)
                if (SelectedNote != null && SelectedNote.IsDeleted)
                {
                    SelectedNote = null;
                }

                JournalManager.Instance.EmptyTrash();
                RefreshNotesList();
                ShowStatusMessage("Trash emptied successfully");
            }
        }

        private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkDirty();
        }

        private void TitleTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (NoteRichEditBox != null)
                {
                    NoteRichEditBox.Focus(FocusState.Programmatic);
                    // Place cursor at the end of the content (or beginning if empty)
                    int length = NoteRichEditBox.Document.Selection.Length;
                    NoteRichEditBox.Document.Selection.SetRange(length, length);
                }
                e.Handled = true;
            }
        }

        private void NoteRichEditBox_TextChanged(object sender, RoutedEventArgs e)
        {
            UpdateWordCount();
            MarkDirty();

            // If live markdown preview is active, render it in real-time as the user types
            if (MarkdownPreviewToggle != null && MarkdownPreviewToggle.IsChecked == true && MarkdownPreviewTextBlock != null)
            {
                string plainText;
                NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out plainText);
                RenderMarkdownToRichTextBlock(plainText, MarkdownPreviewTextBlock);
            }
        }

        private static readonly string[] DailyWritingPrompts = new[]
        {
            "What is something that made you smile today?",
            "Describe a challenge you faced recently and how you handled it.",
            "What are three things you are most grateful for right now?",
            "Write about a person who has had a positive impact on your life.",
            "What is a goal you want to achieve this week, and how will you reach it?",
            "Describe a place where you feel completely peaceful and happy.",
            "What is a lesson you learned recently, and why was it important?",
            "If you could travel anywhere right now, where would you go and why?",
            "Write about a memory from your childhood that still brings you joy.",
            "What is a quote or saying that inspires you, and what does it mean to you?",
            "Describe your perfect day from start to finish.",
            "What are some habits you want to cultivate or let go of?",
            "Write about a book, movie, or song that recently moved you, and why.",
            "How do you feel today physically, mentally, and emotionally?",
            "What is something you are looking forward to in the near future?",
            "Write about a time you tried something new and what you learned from it."
        };

        private static readonly Random _promptRandom = new Random();

        private void InspirationFlyout_Opening(object sender, object e)
        {
            ShowRandomPrompt();
        }

        private void NewPrompt_Click(object sender, RoutedEventArgs e)
        {
            ShowRandomPrompt();
        }

        private void ShowRandomPrompt()
        {
            if (PromptDisplayTextBlock != null)
            {
                int index = _promptRandom.Next(DailyWritingPrompts.Length);
                PromptDisplayTextBlock.Text = DailyWritingPrompts[index];
            }
        }

        private void InsertPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null || PromptDisplayTextBlock == null) return;
            
            string prompt = PromptDisplayTextBlock.Text;
            if (string.IsNullOrEmpty(prompt)) return;

            string prefix = "\r\n";
            NoteRichEditBox.Document.GetText(TextGetOptions.None, out string allText);
            if (string.IsNullOrEmpty(allText) || allText.Equals("\r") || NoteRichEditBox.Document.Selection.StartPosition == 0)
            {
                prefix = "";
            }

            string textToInsert = $"{prefix}> Prompt: {prompt}\r\n\r\n";
            NoteRichEditBox.Document.Selection.SetText(TextSetOptions.None, textToInsert);
            
            int endPos = NoteRichEditBox.Document.Selection.EndPosition;
            NoteRichEditBox.Document.Selection.SetRange(endPos, endPos);
            NoteRichEditBox.Focus(FocusState.Programmatic);

            if (InspirationFlyout != null)
            {
                InspirationFlyout.Hide();
            }
        }



        // Favorites
        private void FavoriteToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (SelectedNote != null)
            {
                SelectedNote.IsFavorite = true;
                JournalManager.Instance.SaveNotesMetadata();
                RefreshNotesList();
            }
        }

        private void FavoriteToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (SelectedNote != null)
            {
                SelectedNote.IsFavorite = false;
                JournalManager.Instance.SaveNotesMetadata();
                RefreshNotesList();
            }
        }

        private void PinToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (SelectedNote != null)
            {
                SelectedNote.IsPinned = true;
                JournalManager.Instance.SaveNotesMetadata();
                RefreshNotesList();
            }
        }

        private void PinToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (SelectedNote != null)
            {
                SelectedNote.IsPinned = false;
                JournalManager.Instance.SaveNotesMetadata();
                RefreshNotesList();
            }
        }

        private void MoodItem_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null || sender is not MenuFlyoutItem item) return;
            string mood = item.Tag?.ToString() ?? "None";
            SelectedNote.Mood = mood;
            JournalManager.Instance.SaveNotesMetadata();
            UpdateEditorHeaderUI();
            RefreshNotesList();
        }

        // Categories creation
        private async void ShowNewCategoryDialog()
        {
            NewCategoryTextBox.Text = "";
            IconSelectionGridView.SelectedIndex = 0;
            ColorSelectionGridView.SelectedIndex = 8;

            NewCategoryDialog.XamlRoot = this.XamlRoot;
            var result = await NewCategoryDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string name = NewCategoryTextBox.Text?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    string icon = "\uE8B7"; // Default folder
                    if (IconSelectionGridView.SelectedItem is IconOption selectedIcon)
                    {
                        icon = selectedIcon.Glyph;
                    }

                    string color = "#8A8886"; // Default gray
                    if (ColorSelectionGridView.SelectedItem is ColorOption selectedColor)
                    {
                        color = selectedColor.Hex;
                    }

                    if (JournalManager.Instance.Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        await ShowAlertAsync("Error", "A category with this name already exists.");
                        return;
                    }

                    JournalManager.Instance.AddCategory(name, icon, color);
                    LoadCategoriesList();
                    
                    // Select the new category
                    var category = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name == name);
                    if (category != null)
                    {
                        var targetItem = CategoriesNavView.MenuItems
                            .OfType<NavigationViewItem>()
                            .FirstOrDefault(item => item.Tag == category);
                        if (targetItem != null)
                        {
                            CategoriesNavView.SelectedItem = targetItem;
                        }
                    }
                }
            }
        }

        private void MoveCategoryMenu_Opening(object sender, object e)
        {
            if (sender is MenuFlyout menu)
            {
                menu.Items.Clear();
                if (SelectedNote == null) return;

                foreach (var category in JournalManager.Instance.Categories)
                {
                    if (category.Name == "All Entries") continue;

                    var fontIcon = new FontIcon { Glyph = category.Icon };
                    if (!string.IsNullOrEmpty(category.Color))
                    {
                        var converter = new HexToBrushConverter();
                        var brush = converter.Convert(category.Color, typeof(Microsoft.UI.Xaml.Media.Brush), null, null) as Microsoft.UI.Xaml.Media.Brush;
                        if (brush != null)
                        {
                            fontIcon.Foreground = brush;
                        }
                    }

                    var item = new MenuFlyoutItem
                    {
                        Text = category.Name,
                        Icon = fontIcon
                    };
                    
                    string categoryName = category.Name;
                    item.Click += (s, args) =>
                    {
                        SelectedNote.Category = categoryName;
                        JournalManager.Instance.SaveNotesMetadata();
                        RefreshNotesList();
                        UpdateEditorHeaderUI();
                    };

                    menu.Items.Add(item);
                }
            }
        }

        // Context menu right-click event handlers for categories
        private void CategoryMenuFlyout_Opening(object sender, object e)
        {
            if (sender is MenuFlyout menuFlyout)
            {
                // Defer execution so that menuFlyout.Target is fully resolved
                menuFlyout.DispatcherQueue.TryEnqueue(() =>
                {
                    var target = menuFlyout.Target as FrameworkElement;
                    var category = target?.Tag as JournalCategory;
                    if (category == null) return;

                    bool isAllEntries = (category.Name == "All Entries");

                    var list = JournalManager.Instance.Categories;
                    int index = list.IndexOf(category);

                    foreach (var item in menuFlyout.Items)
                    {
                        if (item is FrameworkElement fe)
                        {
                            fe.DataContext = category;
                        }

                        if (item is MenuFlyoutItem flyoutItem)
                        {
                            if (flyoutItem.Text == "Rename Category" || 
                                flyoutItem.Text == "Change Icon & Color" ||
                                flyoutItem.Text == "Delete Category")
                            {
                                flyoutItem.IsEnabled = !isAllEntries;
                            }
                            else if (flyoutItem.Text == "Move Up")
                            {
                                flyoutItem.IsEnabled = !isAllEntries && index > 1;
                            }
                            else if (flyoutItem.Text == "Move Down")
                            {
                                flyoutItem.IsEnabled = !isAllEntries && index > 0 && index < list.Count - 1;
                            }
                        }
                    }
                });
            }
        }

        private async void RenameCategory_Click(object sender, RoutedEventArgs e)
        {
            var category = (sender as FrameworkElement)?.DataContext as JournalCategory;
            if (category == null || category.Name == "All Entries") return;

            string oldName = category.Name;
            string newName = await PromptForTextInputAsync("Rename Category", "Enter new name for the category:", oldName);
            if (!string.IsNullOrEmpty(newName) && newName != oldName)
            {
                if (JournalManager.Instance.Categories.Any(c => c.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                {
                    await ShowAlertAsync("Error", "A category with this name already exists.");
                    return;
                }

                category.Name = newName;
                JournalManager.Instance.SaveCategories();

                if (_selectedCategory == oldName)
                {
                    _selectedCategory = newName;
                    SelectedCategoryTitle.Text = newName;
                }

                foreach (var note in JournalManager.Instance.Notes)
                {
                    if (note.Category == oldName)
                    {
                        note.Category = newName;
                    }
                }
                JournalManager.Instance.SaveNotesMetadata();

                LoadCategoriesList();
                RefreshNotesList();
            }
        }

        private async void ChangeCategoryIconColor_Click(object sender, RoutedEventArgs e)
        {
            var category = (sender as FrameworkElement)?.DataContext as JournalCategory;
            if (category == null || category.Name == "All Entries") return;

            var iconOptions = EditIconSelectionGridView.ItemsSource as List<IconOption>;
            if (iconOptions != null)
            {
                var iconItem = iconOptions.FirstOrDefault(i => i.Glyph == category.Icon);
                EditIconSelectionGridView.SelectedItem = iconItem;
            }

            var colorOptions = EditColorSelectionGridView.ItemsSource as List<ColorOption>;
            if (colorOptions != null)
            {
                var colorItem = colorOptions.FirstOrDefault(c => c.Hex == category.Color);
                EditColorSelectionGridView.SelectedItem = colorItem;
            }

            EditCategoryDialog.XamlRoot = this.XamlRoot;
            var result = await EditCategoryDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (EditIconSelectionGridView.SelectedItem is IconOption selectedIcon)
                {
                    category.Icon = selectedIcon.Glyph;
                }

                if (EditColorSelectionGridView.SelectedItem is ColorOption selectedColor)
                {
                    category.Color = selectedColor.Hex;
                }

                JournalManager.Instance.SaveCategories();
                LoadCategoriesList();
                RefreshNotesList();
            }
        }

        private void MoveCategoryUp_Click(object sender, RoutedEventArgs e)
        {
            var category = (sender as FrameworkElement)?.DataContext as JournalCategory;
            if (category == null || category.Name == "All Entries") return;

            var list = JournalManager.Instance.Categories;
            int index = list.IndexOf(category);
            if (index > 1)
            {
                list.RemoveAt(index);
                list.Insert(index - 1, category);
                JournalManager.Instance.SaveCategories();
                LoadCategoriesList();
                var targetItem = CategoriesNavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(item => item.Tag == category);
                if (targetItem != null)
                {
                    CategoriesNavView.SelectedItem = targetItem;
                }
            }
        }

        private void MoveCategoryDown_Click(object sender, RoutedEventArgs e)
        {
            var category = (sender as FrameworkElement)?.DataContext as JournalCategory;
            if (category == null || category.Name == "All Entries") return;

            var list = JournalManager.Instance.Categories;
            int index = list.IndexOf(category);
            if (index > 0 && index < list.Count - 1)
            {
                list.RemoveAt(index);
                list.Insert(index + 1, category);
                JournalManager.Instance.SaveCategories();
                LoadCategoriesList();
                var targetItem = CategoriesNavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(item => item.Tag == category);
                if (targetItem != null)
                {
                    CategoriesNavView.SelectedItem = targetItem;
                }
            }
        }

        private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            var category = (sender as FrameworkElement)?.DataContext as JournalCategory;
            if (category == null || category.Name == "All Entries") return;

            var dialog = new ContentDialog
            {
                Title = "Delete Category",
                Content = $"Are you sure you want to delete the category \"{category.Name}\"? Notes inside will be moved to Personal.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                JournalManager.Instance.Categories.Remove(category);
                JournalManager.Instance.SaveCategories();

                foreach (var note in JournalManager.Instance.Notes)
                {
                    if (note.Category == category.Name)
                    {
                        note.Category = "Personal";
                    }
                }
                JournalManager.Instance.SaveNotesMetadata();

                if (_selectedCategory == category.Name)
                {
                    _selectedCategory = "All Entries";
                    SelectedCategoryTitle.Text = "All Entries";
                    var allEntriesCategory = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name == "All Entries");
                    if (allEntriesCategory != null)
                    {
                        var targetItem = CategoriesNavView.MenuItems
                            .OfType<NavigationViewItem>()
                            .FirstOrDefault(item => item.Tag == allEntriesCategory);
                        if (targetItem != null)
                        {
                            CategoriesNavView.SelectedItem = targetItem;
                        }
                    }
                }

                LoadCategoriesList();
                RefreshNotesList();
                UpdateEditorHeaderUI();
            }
        }

        // Drag and drop category reordering
        private void Category_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            if (sender is FrameworkElement fe && fe.Tag is JournalCategory category)
            {
                if (category.Name == "All Entries")
                {
                    args.Cancel = true;
                    return;
                }
                args.Data.Properties["Category"] = category;
                args.Data.RequestedOperation = DataPackageOperation.Move;
            }
        }

        private void Category_DragOver(object sender, DragEventArgs args)
        {
            if (sender is FrameworkElement fe && fe.Tag is JournalCategory targetCategory)
            {
                if (targetCategory.Name == "All Entries")
                {
                    args.AcceptedOperation = DataPackageOperation.None;
                    return;
                }

                if (args.DataView.Properties.ContainsKey("Category"))
                {
                    var sourceCategory = args.DataView.Properties["Category"] as JournalCategory;
                    if (sourceCategory != null && sourceCategory != targetCategory)
                    {
                        args.AcceptedOperation = DataPackageOperation.Move;
                        return;
                    }
                }
            }
            args.AcceptedOperation = DataPackageOperation.None;
        }

        private void Category_Drop(object sender, DragEventArgs args)
        {
            if (sender is FrameworkElement fe && fe.Tag is JournalCategory targetCategory)
            {
                if (targetCategory.Name == "All Entries") return;

                if (args.DataView.Properties.ContainsKey("Category"))
                {
                    var sourceCategory = args.DataView.Properties["Category"] as JournalCategory;
                    if (sourceCategory != null && sourceCategory != targetCategory)
                    {
                        var list = JournalManager.Instance.Categories;
                        int sourceIndex = list.IndexOf(sourceCategory);
                        int targetIndex = list.IndexOf(targetCategory);

                        if (sourceIndex >= 0 && targetIndex >= 0)
                        {
                            list.RemoveAt(sourceIndex);
                            list.Insert(targetIndex, sourceCategory);

                            JournalManager.Instance.SaveCategories();
                            LoadCategoriesList();
                            CategoriesNavView.SelectedItem = sourceCategory;
                        }
                    }
                }
            }
        }

        // Image Selection Helpers (Hwnd wrapper)
        private FileOpenPicker CreatePicker()
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".gif");
            return picker;
        }

        // Inline Image Insertion
        private async void InsertLocalImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            var picker = CreatePicker();
            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await InsertLocalImageIntoDocAsync(file.Path);
            }
        }

        private async void InsertUrlImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            string url = await PromptForTextInputAsync("Insert Web Image", "Enter the URL of the image to insert:", "https://example.com/image.jpg");
            if (string.IsNullOrEmpty(url)) return;

            ShowLoadingRing(true);
            string localName = await DownloadImageToLocalMediaAsync(url);
            ShowLoadingRing(false);

            if (!string.IsNullOrEmpty(localName))
            {
                string absolutePath = JournalManager.Instance.GetAbsoluteMediaPath(localName);
                await InsertLocalImageIntoDocAsync(absolutePath);
            }
            else
            {
                await ShowAlertAsync("Error", "Could not download the specified image. Please check the URL and your connection.");
            }
        }

        private async Task InsertLocalImageIntoDocAsync(string absolutePath)
        {
            try
            {
                // Prompt for custom dimensions before inserting
                var dimensions = await PromptForImageSizeAsync();
                if (dimensions == null) return; // User cancelled

                // Copy to local media so that notes are fully self-contained and portable
                string localName = JournalManager.Instance.CopyImageToLocalMedia(absolutePath);
                string savedPath = JournalManager.Instance.GetAbsoluteMediaPath(localName);
                
                var storageFile = await StorageFile.GetFileFromPathAsync(savedPath);
                using (var stream = await storageFile.OpenAsync(FileAccessMode.Read))
                {
                    // Use custom width and height
                    NoteRichEditBox.Document.Selection.InsertImage(dimensions.Value.width, dimensions.Value.height, 0, VerticalCharacterAlignment.Baseline, "Image", stream);
                    MarkDirty();
                }
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Error", $"Could not insert image: {ex.Message}");
            }
        }

        private async void NoteRichEditBox_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (NoteRichEditBox == null) return;
            NoteRichEditBox.Document.Selection.GetText(Microsoft.UI.Text.TextGetOptions.None, out string selectedText);
            if (selectedText == "\xFFFC")
            {
                e.Handled = true;
                await ResizeSelectedImageAsync();
            }
        }

        private async Task ResizeSelectedImageAsync()
        {
            if (NoteRichEditBox == null) return;
            try
            {
                NoteRichEditBox.Document.Selection.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out string rtf);
                
                int currentWidth = 300;
                int currentHeight = 300;
                
                var currentDims = GetImageDimensionsFromRtf(rtf);
                if (currentDims != null)
                {
                    currentWidth = currentDims.Value.width;
                    currentHeight = currentDims.Value.height;
                }
                
                var newDims = await PromptForImageSizeWithDefaultsAsync(currentWidth, currentHeight);
                if (newDims == null) return;
                
                string newRtf = SetImageDimensionsInRtf(rtf, newDims.Value.width, newDims.Value.height);
                NoteRichEditBox.Document.Selection.SetText(Microsoft.UI.Text.TextSetOptions.FormatRtf, newRtf);
                MarkDirty();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Error", $"Could not resize image: {ex.Message}");
            }
        }

        private async Task<(int width, int height)?> PromptForImageSizeWithDefaultsAsync(int defaultWidth, int defaultHeight)
        {
            var widthBox = new TextBox
            {
                Header = "Width (pixels)",
                Text = defaultWidth.ToString(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var heightBox = new TextBox
            {
                Header = "Height (pixels)",
                Text = defaultHeight.ToString(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var panel = new StackPanel { Spacing = 12, Width = 260 };
            panel.Children.Add(new TextBlock { 
                Text = "Specify the display dimensions for the selected image.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886")
            });
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            Grid.SetColumn(widthBox, 0);
            Grid.SetColumn(heightBox, 2);
            grid.Children.Add(widthBox);
            grid.Children.Add(heightBox);
            
            panel.Children.Add(grid);

            var dialog = new ContentDialog
            {
                Title = "Resize Image",
                Content = panel,
                PrimaryButtonText = "Resize",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (!int.TryParse(widthBox.Text?.Trim(), out int w) || w <= 0) w = defaultWidth;
                if (!int.TryParse(heightBox.Text?.Trim(), out int h) || h <= 0) h = defaultHeight;
                return (w, h);
            }
            return null;
        }

        private (int width, int height)? GetImageDimensionsFromRtf(string rtf)
        {
            var wMatch = System.Text.RegularExpressions.Regex.Match(rtf, @"\\picwgoal(?<val>\d+)");
            var hMatch = System.Text.RegularExpressions.Regex.Match(rtf, @"\\pichgoal(?<val>\d+)");
            if (wMatch.Success && hMatch.Success)
            {
                if (int.TryParse(wMatch.Groups["val"].Value, out int wTwips) &&
                    int.TryParse(hMatch.Groups["val"].Value, out int hTwips))
                {
                    return (wTwips / 15, hTwips / 15);
                }
            }
            return null;
        }

        private string SetImageDimensionsInRtf(string rtf, int newWidth, int newHeight)
        {
            int newWTwips = newWidth * 15;
            int newHTwips = newHeight * 15;

            if (rtf.Contains("\\picwgoal"))
            {
                rtf = System.Text.RegularExpressions.Regex.Replace(rtf, @"\\picwgoal\d+", $"\\picwgoal{newWTwips}");
            }
            else
            {
                rtf = rtf.Replace("\\pict", $"\\pict\\picwgoal{newWTwips}");
            }

            if (rtf.Contains("\\pichgoal"))
            {
                rtf = System.Text.RegularExpressions.Regex.Replace(rtf, @"\\pichgoal\d+", $"\\pichgoal{newHTwips}");
            }
            else
            {
                rtf = rtf.Replace("\\pict", $"\\pict\\pichgoal{newHTwips}");
            }

            rtf = System.Text.RegularExpressions.Regex.Replace(rtf, @"\\picw\d+", $"\\picw{newWTwips}");
            rtf = System.Text.RegularExpressions.Regex.Replace(rtf, @"\\pich\d+", $"\\pich{newHTwips}");

            return rtf;
        }

        // Hero Image Management
        private void UpdateHeroImageUI()
        {
            if (SelectedNote == null)
            {
                HeroImageContainer.Visibility = Visibility.Collapsed;
                HeroImage.Source = null;
                HeroImage.Width = double.NaN;
                HeroImage.Height = double.NaN;
                if (HeroImageTransform != null) { HeroImageTransform.TranslateX = 0; HeroImageTransform.TranslateY = 0; }
                if (AddCoverButton != null) AddCoverButton.Visibility = Visibility.Collapsed;
                if (CoverAttributionBorder != null) CoverAttributionBorder.Visibility = Visibility.Collapsed;
                FadeOutTintOverlays();
            }
            else if (SelectedNote.HeroImagePath == "None")
            {
                HeroImageContainer.Visibility = Visibility.Collapsed;
                HeroImage.Source = null;
                HeroImage.Width = double.NaN;
                HeroImage.Height = double.NaN;
                if (HeroImageTransform != null) { HeroImageTransform.TranslateX = 0; HeroImageTransform.TranslateY = 0; }
                if (AddCoverButton != null) AddCoverButton.Visibility = Visibility.Visible;
                if (CoverAttributionBorder != null) CoverAttributionBorder.Visibility = Visibility.Collapsed;
                FadeOutTintOverlays();
            }
            else
            {
                if (AddCoverButton != null) AddCoverButton.Visibility = Visibility.Collapsed;
                string imagePath = null;
                if (string.IsNullOrEmpty(SelectedNote.HeroImagePath))
                {
                    // Show default Picsum fallback
                    HeroImageContainer.Visibility = Visibility.Visible;
                    imagePath = $"https://picsum.photos/seed/{SelectedNote.Id}/1200/600";
                    
                    // Apply blur to the web fallback
                    imagePath = ApplyBlurToImageUrl(imagePath, SelectedNote.CoverBlur);
                    
                    try
                    {
                        HeroImage.Source = new BitmapImage(new Uri(imagePath));
                        UpdateHeroImageSizeAndConstraints();
                    }
                    catch
                    {
                        HeroImageContainer.Visibility = Visibility.Collapsed;
                        HeroImage.Source = null;
                        HeroImage.Width = double.NaN;
                        HeroImage.Height = double.NaN;
                        if (HeroImageTransform != null) { HeroImageTransform.TranslateX = 0; HeroImageTransform.TranslateY = 0; }
                        imagePath = null;
                    }
                }
                else
                {
                    // Show custom image
                    try
                    {
                        HeroImageContainer.Visibility = Visibility.Visible;
                        string absPath = JournalManager.Instance.GetAbsoluteMediaPath(SelectedNote.HeroImagePath);
                        
                        // If it's a web URL (not local), apply the blur parameter
                        if (SelectedNote.HeroImagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                            SelectedNote.HeroImagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            imagePath = ApplyBlurToImageUrl(absPath, SelectedNote.CoverBlur);
                        }
                        else
                        {
                            imagePath = absPath;
                        }
                        
                        HeroImage.Source = new BitmapImage(new Uri(imagePath));
                        UpdateHeroImageSizeAndConstraints();
                    }
                    catch
                    {
                        // Fallback to Picsum
                        HeroImageContainer.Visibility = Visibility.Visible;
                        string fallbackUrl = $"https://picsum.photos/seed/{SelectedNote.Id}/1200/600";
                        imagePath = ApplyBlurToImageUrl(fallbackUrl, SelectedNote.CoverBlur);
                        HeroImage.Source = new BitmapImage(new Uri(imagePath));
                        UpdateHeroImageSizeAndConstraints();
                    }
                }

                // Apply cover effects (Offset, Brightness, and Blur Sliders) to UI elements
                _isUpdatingEffectsUI = true;
                try
                {
                    if (HeroImageTransform != null)
                    {
                        HeroImageTransform.TranslateY = SelectedNote.CoverOffsetY;
                        HeroImageTransform.TranslateX = SelectedNote.CoverOffsetX;
                    }
                    if (CoverBrightnessOverlay != null) CoverBrightnessOverlay.Opacity = (100 - SelectedNote.CoverBrightness) / 100.0;
                    
                    if (CoverBrightnessSlider != null) CoverBrightnessSlider.Value = SelectedNote.CoverBrightness;
                    if (CoverBlurSlider != null) CoverBlurSlider.Value = SelectedNote.CoverBlur;
                    
                    if (BrightnessValueText != null) BrightnessValueText.Text = $"{(int)SelectedNote.CoverBrightness}%";
                    if (BlurValueText != null) BlurValueText.Text = $"{(int)SelectedNote.CoverBlur}%";
                }
                finally
                {
                    _isUpdatingEffectsUI = false;
                }

                // Show/hide photographer attribution badge
                if (CoverAttributionBorder != null && PhotographerNameRun != null && PhotographerHyperlink != null)
                {
                    if (!string.IsNullOrEmpty(SelectedNote.CoverAttributionText))
                    {
                        PhotographerNameRun.Text = SelectedNote.CoverAttributionText;
                        if (!string.IsNullOrEmpty(SelectedNote.CoverAttributionUrl))
                        {
                            try
                            {
                                PhotographerHyperlink.NavigateUri = new Uri(SelectedNote.CoverAttributionUrl);
                            }
                            catch
                            {
                                PhotographerHyperlink.NavigateUri = null;
                            }
                        }
                        else
                        {
                            PhotographerHyperlink.NavigateUri = null;
                        }
                        CoverAttributionBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        CoverAttributionBorder.Visibility = Visibility.Collapsed;
                    }
                }

                // Dynamically extract dominant color and apply the background gradient tint
                if (!string.IsNullOrEmpty(imagePath))
                {
                    var currentNoteId = SelectedNote.Id;
                    Task.Run(async () =>
                    {
                        Windows.UI.Color tintColor = await ExtractDominantColorAsync(imagePath);
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (SelectedNote != null && SelectedNote.Id == currentNoteId)
                            {
                                FadeInTintOverlays(tintColor);
                            }
                        });
                    });
                }
                else
                {
                    FadeOutTintOverlays();
                }
            }
        }

        private void HeroImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            UpdateHeroImageSizeAndConstraints();
        }

        private void UpdateHeroImageSizeAndConstraints()
        {
            if (HeroImage == null || HeroImageContainer == null || HeroImage.Source == null) return;

            HeroImage.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill;
            HeroImage.Width = double.NaN;
            HeroImage.Height = double.NaN;
            HeroImage.HorizontalAlignment = HorizontalAlignment.Center;
            HeroImage.VerticalAlignment = VerticalAlignment.Center;

            if (HeroImageTransform != null)
            {
                HeroImageTransform.TranslateX = 0;
                HeroImageTransform.TranslateY = 0;
            }
        }


        private string ApplyBlurToImageUrl(string url, double blurValue)
        {
            if (blurValue <= 0) return url;
            
            if (url.Contains("picsum.photos", StringComparison.OrdinalIgnoreCase))
            {
                int picsumBlur = (int)Math.Clamp(blurValue / 10.0, 1, 10);
                string separator = url.Contains("?") ? "&" : "?";
                return $"{url}{separator}blur={picsumBlur}";
            }
            else if (url.Contains("unsplash.com", StringComparison.OrdinalIgnoreCase) || 
                     url.Contains("images.unsplash.com", StringComparison.OrdinalIgnoreCase))
            {
                int unsplashBlur = (int)(blurValue * 1.5);
                if (unsplashBlur < 1) unsplashBlur = 1;
                string separator = url.Contains("?") ? "&" : "?";
                return $"{url}{separator}blur={unsplashBlur}";
            }
            return url;
        }

        // Dynamic Tint Overlay animations and helper methods
        private void FadeInTintOverlays(Windows.UI.Color color)
        {
            if (NotesListTintStop1 != null) NotesListTintStop1.Color = Windows.UI.Color.FromArgb(35, color.R, color.G, color.B);
            if (EditorTintStop1 != null) EditorTintStop1.Color = Windows.UI.Color.FromArgb(35, color.R, color.G, color.B);
            
            StartOpacityAnimation(NotesListTintOverlay, 1.0, 500);
            StartOpacityAnimation(EditorTintOverlay, 1.0, 500);
        }
        
        private void FadeOutTintOverlays()
        {
            StartOpacityAnimation(NotesListTintOverlay, 0.0, 300);
            StartOpacityAnimation(EditorTintOverlay, 0.0, 300);
        }
        
        private void StartOpacityAnimation(UIElement element, double targetOpacity, double durationMs)
        {
            if (element == null) return;
            
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            
            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            storyboard.Children.Add(animation);
            
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, element);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
            
            storyboard.Begin();
        }

        private async Task<Windows.UI.Color> ExtractDominantColorAsync(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath))
                {
                    return Microsoft.UI.Colors.Transparent;
                }

                if (imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return await GetImageColorFromUrlOrFallbackAsync(imagePath);
                }
                
                if (!System.IO.File.Exists(imagePath))
                {
                    return Microsoft.UI.Colors.Transparent;
                }

                StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath);
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    return await ExtractAverageColorFromStreamAsync(stream);
                }
            }
            catch
            {
                return GetFallbackColorForPath(imagePath);
            }
        }

        private async Task<Windows.UI.Color> ExtractAverageColorFromStreamAsync(Windows.Storage.Streams.IRandomAccessStream stream)
        {
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var transform = new Windows.Graphics.Imaging.BitmapTransform
            {
                ScaledWidth = 16,
                ScaledHeight = 16,
                InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Linear
            };
            
            var pixelData = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                transform,
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb
            );
            
            byte[] pixels = pixelData.DetachPixelData();
            long totalR = 0, totalG = 0, totalB = 0;
            int count = pixels.Length / 4;
            
            for (int i = 0; i < pixels.Length; i += 4)
            {
                totalR += pixels[i];
                totalG += pixels[i + 1];
                totalB += pixels[i + 2];
            }
            
            if (count == 0) return Microsoft.UI.Colors.Transparent;
            
            byte r = (byte)(totalR / count);
            byte g = (byte)(totalG / count);
            byte b = (byte)(totalB / count);
            
            return Windows.UI.Color.FromArgb(255, r, g, b);
        }

        private async Task<Windows.UI.Color> GetImageColorFromUrlOrFallbackAsync(string url)
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(1500))
                {
                    var response = await _httpClient.GetAsync(url, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                        using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                        {
                            using (var writer = new Windows.Storage.Streams.DataWriter(ms.GetOutputStreamAt(0)))
                            {
                                writer.WriteBytes(bytes);
                                await writer.StoreAsync();
                            }
                            return await ExtractAverageColorFromStreamAsync(ms);
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return GetFallbackColorForPath(url);
        }

        private Windows.UI.Color GetFallbackColorForPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return Microsoft.UI.Colors.Transparent;
            
            int hash = path.GetHashCode();
            double hue = Math.Abs(hash % 360);
            double saturation = 0.6;
            double lightness = 0.5;
            
            return ColorFromHsl(hue, saturation, lightness);
        }

        private Windows.UI.Color ColorFromHsl(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
                g = HueToRgb(p, q, h / 360.0);
                b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
            }
            return Windows.UI.Color.FromArgb(255, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        // Cover adjustment slider event handlers

        private void CoverBrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingEffectsUI || SelectedNote == null) return;
            SelectedNote.CoverBrightness = e.NewValue;
            if (CoverBrightnessOverlay != null) CoverBrightnessOverlay.Opacity = (100.0 - e.NewValue) / 100.0;
            if (BrightnessValueText != null) BrightnessValueText.Text = $"{(int)e.NewValue}%";
            MarkDirty();
        }

        private void CoverBlurSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingEffectsUI || SelectedNote == null) return;
            SelectedNote.CoverBlur = e.NewValue;
            if (BlurValueText != null) BlurValueText.Text = $"{(int)e.NewValue}%";
            MarkDirty();
            
            // Re-apply image source with new blur parameters dynamically
            UpdateHeroImageUI();
        }

        private void ResetEffectsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            
            // Set sliders back to default
            if (CoverBrightnessSlider != null) CoverBrightnessSlider.Value = 100;
            if (CoverBlurSlider != null) CoverBlurSlider.Value = 0;
            
            // Apply immediately to the note
            SelectedNote.CoverOffsetY = 0;
            SelectedNote.CoverOffsetX = 0;
            SelectedNote.CoverBrightness = 100;
            SelectedNote.CoverBlur = 0;
            
            // Update UI elements directly to ensure instant visual update
            if (HeroImageTransform != null)
            {
                HeroImageTransform.TranslateY = 0;
                HeroImageTransform.TranslateX = 0;
            }
            if (CoverBrightnessOverlay != null) CoverBrightnessOverlay.Opacity = 0;
            if (BrightnessValueText != null) BrightnessValueText.Text = "100%";
            if (BlurValueText != null) BlurValueText.Text = "0%";
            
            UpdateHeroImageUI();
            MarkDirty();
            
            ShowStatusMessage("Cover adjustments reset to default");
        }

        private void AdjustHeroButton_Click(object sender, RoutedEventArgs e)
        {
            // Handled automatically by XAML Flyout, but method required for compilation
        }

        // Legacy HeroDragGrid handlers — inline drag replaced by Photos-like modal
        private void HeroDragGrid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { }
        private void HeroDragGrid_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { }
        private void HeroDragGrid_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { }




        // Photographer attribution is now handled natively via Hyperlink NavigateUri in XAML.

        // ── Moments Section ──────────────────────────────────────────────────────────

        /// <summary>Update the Moments chips and photo strip to reflect the currently selected note.</summary>
        private void UpdateMomentsUI()
        {
            if (SelectedNote == null) return;

            // Location chip
            if (LocationChipText != null)
            {
                bool hasLoc = !string.IsNullOrEmpty(SelectedNote.LocationTag);
                LocationChipText.Text = hasLoc ? SelectedNote.LocationTag : "Add Location";
                LocationChipText.Foreground = hasLoc
                    ? GetThemeBrush("TextFillColorPrimaryBrush", "#000000")
                    : GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886");
            }

            // Weather chip
            if (WeatherChipText != null)
            {
                var weatherEmoji = SelectedNote.WeatherTag switch
                {
                    "Sunny"        => "☀️ Sunny",
                    "PartlyCloudy" => "⛅ Partly Cloudy",
                    "Cloudy"       => "☁️ Cloudy",
                    "Rainy"        => "🌧️ Rainy",
                    "Stormy"       => "⚡ Stormy",
                    "Snowy"        => "❄️ Snowy",
                    "Foggy"        => "🌫️ Foggy",
                    _              => "Add Weather"
                };
                WeatherChipText.Text = weatherEmoji;
                bool hasWeather = !string.IsNullOrEmpty(SelectedNote.WeatherTag);
                WeatherChipText.Foreground = hasWeather
                    ? GetThemeBrush("TextFillColorPrimaryBrush", "#000000")
                    : GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886");
            }

            // Photo strip — rebuild (keep "Add Photo" button, prepend thumbnails)
            if (MomentPhotosPanel != null)
            {
                // Remove all children except the "Add Photo" button (always last)
                while (MomentPhotosPanel.Children.Count > 1)
                    MomentPhotosPanel.Children.RemoveAt(0);

                if (SelectedNote.AttachedPhotoPaths != null)
                {
                    int insertIndex = 0;
                    foreach (var photoPath in SelectedNote.AttachedPhotoPaths)
                    {
                        var absPath = JournalManager.Instance.GetAbsoluteMediaPath(photoPath);
                        var thumb = new Grid
                        {
                            Width = 100, Height = 100,
                            CornerRadius = new CornerRadius(10),
                            Margin = new Thickness(0, 0, 0, 0)
                        };
                        var img = new Image
                        {
                            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(absPath)),
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center
                        };
                        var overlay = new Border
                        {
                            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                            CornerRadius = new CornerRadius(10),
                            Tag = photoPath
                        };
                        overlay.Tapped += RemoveMomentPhoto_Tapped;
                        thumb.Children.Add(img);
                        thumb.Children.Add(overlay);
                        MomentPhotosPanel.Children.Insert(insertIndex++, thumb);
                    }
                }
            }
            UpdateAttachedAudioUI();
            UpdateAttachedTagsUI();
        }

        private async void LocationChip_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            string current = SelectedNote.LocationTag ?? "";
            string result = await PromptForTextInputAsync("Location", "Enter a place or address:", current.Length > 0 ? current : "e.g. New York, NY");
            if (result != null)
            {
                await SearchLocationAndWeatherAsync(result);
            }
        }

        private async Task SearchLocationAndWeatherAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            
            try
            {
                if (AutoDetectMomentsProgress != null)
                {
                    AutoDetectMomentsProgress.Visibility = Visibility.Visible;
                    AutoDetectMomentsProgress.IsActive = true;
                }
                if (AutoDetectMomentsIcon != null)
                {
                    AutoDetectMomentsIcon.Visibility = Visibility.Collapsed;
                }
                
                string searchUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";
                string searchJson = await _httpClient.GetStringAsync(searchUrl);
                using var searchDoc = JsonDocument.Parse(searchJson);
                var root = searchDoc.RootElement;
                
                if (root.TryGetProperty("results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Array && resultsEl.GetArrayLength() > 0)
                {
                    var firstResult = resultsEl[0];
                    string name = firstResult.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                    string admin1 = firstResult.TryGetProperty("admin1", out var admin1Prop) ? admin1Prop.GetString() : "";
                    string country = firstResult.TryGetProperty("country", out var countryProp) ? countryProp.GetString() : "";
                    double lat = firstResult.TryGetProperty("latitude", out var latProp) ? latProp.GetDouble() : 0.0;
                    double lon = firstResult.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() : 0.0;

                    string formattedLocation = name;
                    if (!string.IsNullOrEmpty(admin1))
                        formattedLocation += $", {admin1}";
                    if (!string.IsNullOrEmpty(country))
                        formattedLocation += $", {country}";

                    SelectedNote.LocationTag = formattedLocation;
                    SelectedNote.Latitude = lat;
                    SelectedNote.Longitude = lon;

                    string weatherTag = "Sunny";
                    if (lat != 0.0 || lon != 0.0)
                    {
                        string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";
                        string weatherJson = await _httpClient.GetStringAsync(weatherUrl);
                        using var weatherDoc = JsonDocument.Parse(weatherJson);
                        var weatherRoot = weatherDoc.RootElement;
                        if (weatherRoot.TryGetProperty("current_weather", out var cwProp))
                        {
                            int weatherCode = cwProp.TryGetProperty("weathercode", out var wcProp) ? wcProp.GetInt32() : 0;
                            weatherTag = MapWmoCodeToWeatherTag(weatherCode);
                        }
                    }
                    SelectedNote.WeatherTag = weatherTag;
                }
                else
                {
                    SelectedNote.LocationTag = query;
                }
                
                UpdateMomentsUI();
                MarkDirty();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to search location/weather: {ex.Message}");
                SelectedNote.LocationTag = query;
                UpdateMomentsUI();
                MarkDirty();
            }
            finally
            {
                if (AutoDetectMomentsProgress != null)
                {
                    AutoDetectMomentsProgress.Visibility = Visibility.Collapsed;
                    AutoDetectMomentsProgress.IsActive = false;
                }
                if (AutoDetectMomentsIcon != null)
                {
                    AutoDetectMomentsIcon.Visibility = Visibility.Visible;
                }
            }
        }

        private void WeatherOption_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            if (sender is MenuFlyoutItem item)
            {
                SelectedNote.WeatherTag = item.Tag?.ToString() ?? "";
                UpdateMomentsUI();
                MarkDirty();
            }
        }

        private async void AutoDetectMoments_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            if (AutoDetectMomentsProgress != null)
            {
                AutoDetectMomentsProgress.Visibility = Visibility.Visible;
                AutoDetectMomentsProgress.IsActive = true;
            }
            if (AutoDetectMomentsIcon != null)
            {
                AutoDetectMomentsIcon.Visibility = Visibility.Collapsed;
            }
            if (AutoDetectMomentsText != null)
            {
                AutoDetectMomentsText.Text = "Detecting...";
            }
            if (AutoDetectMomentsButton != null)
            {
                AutoDetectMomentsButton.IsEnabled = false;
            }

            try
            {
                string ipGeoUrl = "https://ipapi.co/json/";
                string geoJson = await _httpClient.GetStringAsync(ipGeoUrl);
                using var geoDoc = JsonDocument.Parse(geoJson);
                var root = geoDoc.RootElement;
                
                string city = root.TryGetProperty("city", out var cityProp) ? cityProp.GetString() : "";
                string region = root.TryGetProperty("region_code", out var regionProp) ? regionProp.GetString() : "";
                string country = root.TryGetProperty("country_code", out var countryProp) ? countryProp.GetString() : "";
                
                double lat = root.TryGetProperty("latitude", out var latProp) ? latProp.GetDouble() : 0.0;
                double lon = root.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() : 0.0;

                string locationText = "";
                if (!string.IsNullOrEmpty(city))
                {
                    locationText = string.IsNullOrEmpty(region) ? $"{city}, {country}" : $"{city}, {region}";
                }

                string weatherTag = "Sunny";
                if (lat != 0.0 || lon != 0.0)
                {
                    string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true";
                    string weatherJson = await _httpClient.GetStringAsync(weatherUrl);
                    using var weatherDoc = JsonDocument.Parse(weatherJson);
                    var weatherRoot = weatherDoc.RootElement;
                    if (weatherRoot.TryGetProperty("current_weather", out var cwProp))
                    {
                        int weatherCode = cwProp.TryGetProperty("weathercode", out var wcProp) ? wcProp.GetInt32() : 0;
                        weatherTag = MapWmoCodeToWeatherTag(weatherCode);
                    }
                }

                if (!string.IsNullOrEmpty(locationText))
                {
                    SelectedNote.LocationTag = locationText;
                    SelectedNote.Latitude = lat;
                    SelectedNote.Longitude = lon;
                }
                SelectedNote.WeatherTag = weatherTag;
                
                UpdateMomentsUI();
                MarkDirty();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to auto-detect location/weather: {ex.Message}");
                await ShowAlertAsync("Auto-Detect Failed", "Could not automatically determine location or weather. Please check your internet connection.");
            }
            finally
            {
                if (AutoDetectMomentsProgress != null)
                {
                    AutoDetectMomentsProgress.Visibility = Visibility.Collapsed;
                    AutoDetectMomentsProgress.IsActive = false;
                }
                if (AutoDetectMomentsIcon != null)
                {
                    AutoDetectMomentsIcon.Visibility = Visibility.Visible;
                }
                if (AutoDetectMomentsText != null)
                {
                    AutoDetectMomentsText.Text = "Auto-detect";
                }
                if (AutoDetectMomentsButton != null)
                {
                    AutoDetectMomentsButton.IsEnabled = true;
                }
            }
        }

        private string MapWmoCodeToWeatherTag(int wmoCode)
        {
            return wmoCode switch
            {
                0 => "Sunny",
                1 or 2 => "PartlyCloudy",
                3 => "Cloudy",
                45 or 48 => "Foggy",
                51 or 53 or 55 => "Rainy",
                61 or 63 or 65 or 80 or 81 or 82 => "Rainy",
                71 or 73 or 75 or 77 or 85 or 86 => "Snowy",
                95 or 96 or 99 => "Stormy",
                _ => "Sunny"
            };
        }



        private void UpdateAttachedTagsUI()
        {
            if (AttachedTagsPanel == null) return;
            AttachedTagsPanel.Children.Clear();

            if (SelectedNote == null || SelectedNote.Tags == null) return;

            var textBrushSecondary = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886");
            var borderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", "#E5E5E5");
            var bgBrush = GetThemeBrush("ControlAltFillColorSecondaryBrush", "#F9F9F9");

            foreach (var tag in SelectedNote.Tags)
            {
                var tagBorder = new Border
                {
                    Background = bgBrush,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 6, 4),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var contentPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6
                };

                var tagText = new TextBlock
                {
                    Text = $"#{tag}",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = textBrushSecondary
                };

                var deleteBtn = new Button
                {
                    Content = "\uE894",
                    FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    FontSize = 8,
                    Width = 16,
                    Height = 16,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = tag
                };
                deleteBtn.Click += DeleteTagBtn_Click;

                contentPanel.Children.Add(tagText);
                contentPanel.Children.Add(deleteBtn);
                tagBorder.Child = contentPanel;

                AttachedTagsPanel.Children.Add(tagBorder);
            }
        }

        private void DeleteTagBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && SelectedNote != null)
            {
                if (SelectedNote.Tags != null && SelectedNote.Tags.Contains(tag))
                {
                    SelectedNote.Tags.Remove(tag);
                    JournalManager.Instance.SaveNotesMetadata();
                    UpdateAttachedTagsUI();
                    LoadCategoriesList();
                    RefreshNotesList();
                    MarkDirty();
                }
            }
        }

        private void AddTagBtn_Click(object sender, RoutedEventArgs e)
        {
            AddCurrentTag();
        }

        private void NewTagTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                AddCurrentTag();
                e.Handled = true;
            }
        }

        private void AddCurrentTag()
        {
            if (NewTagTextBox == null || SelectedNote == null) return;
            string rawTag = NewTagTextBox.Text.Trim();
            if (string.IsNullOrEmpty(rawTag)) return;

            if (rawTag.StartsWith("#"))
            {
                rawTag = rawTag.Substring(1);
            }
            rawTag = rawTag.ToLowerInvariant().Trim();

            if (!string.IsNullOrEmpty(rawTag))
            {
                if (SelectedNote.Tags == null)
                {
                    SelectedNote.Tags = new List<string>();
                }

                if (!SelectedNote.Tags.Contains(rawTag))
                {
                    SelectedNote.Tags.Add(rawTag);
                    JournalManager.Instance.SaveNotesMetadata();
                    UpdateAttachedTagsUI();
                    LoadCategoriesList();
                    RefreshNotesList();
                    MarkDirty();
                }
            }

            NewTagTextBox.Text = "";
        }



        private async void AddMomentPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            // Copy into the journal Media folder
            string destFileName = $"moment_{SelectedNote.Id}_{DateTime.Now:yyyyMMddHHmmss}{System.IO.Path.GetExtension(file.Name)}";
            string destPath = System.IO.Path.Combine(JournalManager.Instance.MediaDir, destFileName);
            System.IO.File.Copy(file.Path, destPath, overwrite: true);

            SelectedNote.AttachedPhotoPaths ??= new System.Collections.Generic.List<string>();
            SelectedNote.AttachedPhotoPaths.Add(destFileName);

            UpdateMomentsUI();
            MarkDirty();
            ShowStatusMessage("Photo added to Moments");
        }

        private void RemoveMomentPhoto_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (SelectedNote == null || sender is not Border b) return;
            string photoPath = b.Tag?.ToString();
            if (string.IsNullOrEmpty(photoPath)) return;
            SelectedNote.AttachedPhotoPaths?.Remove(photoPath);
            UpdateMomentsUI();
            MarkDirty();
        }

        // Helper Dialogs
        private async Task<string> PromptForTextInputAsync(string title, string instruction, string placeholder)
        {
            var textBox = new TextBox
            {
                PlaceholderText = placeholder,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = instruction });
            panel.Children.Add(textBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            return (result == ContentDialogResult.Primary) ? textBox.Text?.Trim() : null;
        }

        private async Task<(int width, int height)?> PromptForImageSizeAsync()
        {
            var widthBox = new TextBox
            {
                Header = "Width (pixels)",
                Text = "300",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var heightBox = new TextBox
            {
                Header = "Height (pixels)",
                Text = "300",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var panel = new StackPanel { Spacing = 12, Width = 260 };
            panel.Children.Add(new TextBlock { 
                Text = "Specify the display dimensions for the imported image.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886")
            });
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            Grid.SetColumn(widthBox, 0);
            Grid.SetColumn(heightBox, 2);
            grid.Children.Add(widthBox);
            grid.Children.Add(heightBox);
            
            panel.Children.Add(grid);

            var dialog = new ContentDialog
            {
                Title = "Resize Image",
                Content = panel,
                PrimaryButtonText = "Insert",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (!int.TryParse(widthBox.Text?.Trim(), out int w) || w <= 0) w = 300;
                if (!int.TryParse(heightBox.Text?.Trim(), out int h) || h <= 0) h = 300;
                return (w, h);
            }
            return null;
        }

        private async Task ShowAlertAsync(string title, string message)
        {
            await _dialogSemaphore.WaitAsync();
            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowAlertAsync error: {ex.Message}");
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        private void ShowLoadingRing(bool show)
        {
            // Simple indicator if needed
        }

        private async Task<string> DownloadImageToLocalMediaAsync(string url)
        {
            try
            {
                var uri = new Uri(url);
                var bytes = await _unsplashHttpClient.GetByteArrayAsync(uri);

                string ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(ext) || ext.Length > 5)
                {
                    ext = ".jpg";
                }

                string uniqueName = $"{Guid.NewGuid()}{ext}";
                string destPath = Path.Combine(JournalManager.Instance.MediaDir, uniqueName);
                await File.WriteAllBytesAsync(destPath, bytes);
                return uniqueName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to download image: {ex.Message}");
                return null;
            }
        }

        // ── Full-text search ────────────────────────────────────────────────
        private bool NoteMatchesSearch(JournalNote note, string search)
        {
            if (note.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
            if (note.Snippet.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;

            // Fall back to full RTF file content (cached)
            try
            {
                if (!_rtfTextCache.TryGetValue(note.Id, out string cachedText))
                {
                    string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(note.RtfFileName);
                    if (!string.IsNullOrEmpty(rtfPath) && File.Exists(rtfPath))
                    {
                        string raw = File.ReadAllText(rtfPath);
                        // Strip RTF control words with a lightweight regex
                        cachedText = System.Text.RegularExpressions.Regex.Replace(raw, @"\\[a-z]+\-?\d*\s?|[{}]", " ");
                        cachedText = System.Text.RegularExpressions.Regex.Replace(cachedText, @"\s+", " ").Trim();
                        _rtfTextCache[note.Id] = cachedText;
                    }
                    else
                    {
                        _rtfTextCache[note.Id] = string.Empty;
                        cachedText = string.Empty;
                    }
                }
                return !string.IsNullOrEmpty(cachedText) &&
                       cachedText.Contains(search, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }



        private void PinListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is JournalNote note)
            {
                note.IsPinned = !note.IsPinned;
                JournalManager.Instance.SaveNotesMetadata();
                if (SelectedNote == note)
                {
                    PinToggle.IsChecked = note.IsPinned;
                }
                RefreshNotesList();
            }
        }

        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string option)
            {
                _currentSortOption = option;
                RefreshNotesList();
            }
        }

        private void FavoriteListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is JournalNote note)
            {
                note.IsFavorite = !note.IsFavorite;
                JournalManager.Instance.SaveNotesMetadata();
                
                // If the toggled note is the currently selected note, update the editor's favorite toggle too
                if (SelectedNote == note)
                {
                    FavoriteToggle.IsChecked = note.IsFavorite;
                }
                
                RefreshNotesList();
            }
        }

        private void DeleteListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is JournalNote note)
            {
                ShowDeleteConfirmationFlyout(button, note, false);
            }
        }

        private void RestoreListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is JournalNote note)
            {
                JournalManager.Instance.RestoreNote(note);
                RefreshNotesList();
            }
        }

        private void PermanentlyDeleteListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is JournalNote note)
            {
                ShowDeleteConfirmationFlyout(button, note, true);
            }
        }

        // Theme selection handler
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            if (ThemeComboBox == null || App.Current == null) return;
            
            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string themeTag = selectedItem.Tag?.ToString();
                ApplyCustomThemeBrushes(themeTag);
                SyncBlockEditorTheme();
            }
            UpdateSaveSettingsButtonState();
        }

        private static Windows.UI.Color GetColorFromHex(string hex)
        {
            hex = hex.Replace("#", "");
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            else if (hex.Length == 8)
            {
                byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                return Windows.UI.Color.FromArgb(a, r, g, b);
            }
            return Microsoft.UI.Colors.Transparent;
        }

        private void ApplyCustomThemeBrushes(string themeName)
        {
            var window = MainWindow.Instance;
            if (window == null || !(window.Content is FrameworkElement rootElement)) return;

            string[] keys = new string[]
            {
                "SolidBackgroundFillColorBaseBrush",
                "SolidBackgroundFillColorBaseAltBrush",
                "CardBackgroundFillColorDefaultBrush",
                "CardBackgroundFillColorSecondaryBrush",
                "CardStrokeColorDefaultBrush",
                "TextFillColorPrimaryBrush",
                "TextFillColorSecondaryBrush"
            };

            foreach (var key in keys)
            {
                rootElement.Resources.Remove(key);
            }

            if (themeName == "Sepia")
            {
                rootElement.RequestedTheme = ElementTheme.Light;
                rootElement.Resources["SolidBackgroundFillColorBaseBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#F4ECD8"));
                rootElement.Resources["SolidBackgroundFillColorBaseAltBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#EFE6D1"));
                rootElement.Resources["CardBackgroundFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#EFE6D1"));
                rootElement.Resources["CardBackgroundFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#E9DFCA"));
                rootElement.Resources["CardStrokeColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#DED0B6"));
                rootElement.Resources["TextFillColorPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#5C4033"));
                rootElement.Resources["TextFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#7D6B58"));
            }
            else if (themeName == "Nord")
            {
                rootElement.RequestedTheme = ElementTheme.Dark;
                rootElement.Resources["SolidBackgroundFillColorBaseBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#2E3440"));
                rootElement.Resources["SolidBackgroundFillColorBaseAltBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#3B4252"));
                rootElement.Resources["CardBackgroundFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#3B4252"));
                rootElement.Resources["CardBackgroundFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#434C5E"));
                rootElement.Resources["CardStrokeColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#4C566A"));
                rootElement.Resources["TextFillColorPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#D8DEE9"));
                rootElement.Resources["TextFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#E5E9F0"));
            }
            else if (themeName == "Dracula")
            {
                rootElement.RequestedTheme = ElementTheme.Dark;
                rootElement.Resources["SolidBackgroundFillColorBaseBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#282A36"));
                rootElement.Resources["SolidBackgroundFillColorBaseAltBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#1F2029"));
                rootElement.Resources["CardBackgroundFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#44475A"));
                rootElement.Resources["CardBackgroundFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#6272A4"));
                rootElement.Resources["CardStrokeColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#BD93F9"));
                rootElement.Resources["TextFillColorPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#F8F8F2"));
                rootElement.Resources["TextFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#6272A4"));
            }
            else if (themeName == "Forest")
            {
                rootElement.RequestedTheme = ElementTheme.Dark;
                rootElement.Resources["SolidBackgroundFillColorBaseBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#1A2421"));
                rootElement.Resources["SolidBackgroundFillColorBaseAltBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#222E2A"));
                rootElement.Resources["CardBackgroundFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#2D3A34"));
                rootElement.Resources["CardBackgroundFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#222E2A"));
                rootElement.Resources["CardStrokeColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#3B4E45"));
                rootElement.Resources["TextFillColorPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#E8F0EC"));
                rootElement.Resources["TextFillColorSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetColorFromHex("#8FA399"));
            }
            else
            {
                if (themeName == "Light")
                    rootElement.RequestedTheme = ElementTheme.Light;
                else if (themeName == "Dark")
                    rootElement.RequestedTheme = ElementTheme.Dark;
                else
                    rootElement.RequestedTheme = ElementTheme.Default;
            }
        }

        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            if (BackdropComboBox == null) return;

            if (BackdropComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string backdropTag = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(backdropTag))
                {
                    if (MainWindow.Instance != null)
                    {
                        MainWindow.Instance.SetBackdrop(backdropTag);
                    }
                }
            }
            UpdateSaveSettingsButtonState();
        }

        private void AboutCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Expand/collapse is now handled natively by the Expander control
        }

        private void AboutCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = GetThemeBrush("CardBackgroundFillColorSecondaryBrush", "#EFEFEF");
                try
                {
                    typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?
                        .SetValue(border, Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand));
                }
                catch { }
            }
        }

        private void AboutCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = GetThemeBrush("CardBackgroundFillColorDefaultBrush", "#FFFFFF");
                try
                {
                    typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?
                        .SetValue(border, null);
                }
                catch { }
            }
        }



        private void EditorFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            if (EditorFontComboBox == null) return;

            if (EditorFontComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string fontName = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(fontName))
                {
                    ApplyEditorFont(fontName);
                    SyncBlockEditorFont();
                    
                    ShowStatusMessage($"Editor default font changed to {fontName}");
                }
            }
            UpdateSaveSettingsButtonState();
        }

        private void EditorFontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            if (NoteRichEditBox != null)
            {
                NoteRichEditBox.FontSize = e.NewValue;
            }
            UpdateSaveSettingsButtonState();
        }

        private void ApplyAppFont(string fontName)
        {
            var fontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontName);
            
            // Set on MainPage
            this.FontFamily = fontFamily;
            
            // Set on MainWindow root element and title bar controls to affect the whole application
            var window = MainWindow.Instance;
            if (window != null)
            {
                window.ApplyAppFont(fontName);
            }
        }

        private void ApplyEditorFont(string fontName)
        {
            var fontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontName);
            
            if (TitleTextBox != null) TitleTextBox.FontFamily = fontFamily;
            if (NoteRichEditBox != null) NoteRichEditBox.FontFamily = fontFamily;
            if (MarkdownPreviewTextBlock != null) MarkdownPreviewTextBlock.FontFamily = fontFamily;
        }

        private void LoadSavedFonts()
        {
            try
            {
                // Always apply Segoe UI Variable as the application-wide font
                ApplyAppFont("Segoe UI Variable");

                // Load and select saved window backdrop
                string savedBackdrop = GetSetting("AppBackdrop", "MicaAlt");
                if (BackdropComboBox != null)
                {
                    foreach (object itemObj in BackdropComboBox.Items)
                    {
                        if (itemObj is ComboBoxItem item && item.Tag?.ToString() == savedBackdrop)
                        {
                            BackdropComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // 2. Load Editor Font
                string editorFont = GetSetting("EditorFontFamily");

                // 3. Load Editor Font Size
                string savedFontSizeStr = GetSetting("EditorFontSize", "14.0");
                if (double.TryParse(savedFontSizeStr, out double fontSize))
                {
                    if (NoteRichEditBox != null) NoteRichEditBox.FontSize = fontSize;
                    if (EditorFontSizeSlider != null) EditorFontSizeSlider.Value = fontSize;
                }
                if (!string.IsNullOrEmpty(editorFont))
                {
                    ApplyEditorFont(editorFont);
                    
                    if (EditorFontComboBox != null)
                    {
                        foreach (object itemObj in EditorFontComboBox.Items)
                        {
                            if (itemObj is ComboBoxItem item && item.Tag?.ToString() == editorFont)
                            {
                                EditorFontComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load saved fonts: {ex.Message}");
            }
        }

        private void ToolbarFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isDataLoaded || NoteRichEditBox == null || ToolbarFontComboBox == null) return;
            if (ToolbarFontComboBox.SelectedItem is ComboBoxItem item)
            {
                string fontName = item.Tag?.ToString();
                if (!string.IsNullOrEmpty(fontName))
                {
                    var format = NoteRichEditBox.Document.Selection.CharacterFormat;
                    format.Name = fontName;
                    NoteRichEditBox.Document.Selection.CharacterFormat = format;
                    MarkDirty();
                }
            }
        }

        private void NoteRichEditBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!_isDataLoaded || ToolbarFontComboBox == null || NoteRichEditBox == null) return;
            
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            string fontName = format.Name;
            
            if (!string.IsNullOrEmpty(fontName))
            {
                // Temporarily disable event handling to avoid infinite loops
                ToolbarFontComboBox.SelectionChanged -= ToolbarFontComboBox_SelectionChanged;
                
                bool found = false;
                foreach (object itemObj in ToolbarFontComboBox.Items)
                {
                    if (itemObj is ComboBoxItem item && item.Tag?.ToString().Equals(fontName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        ToolbarFontComboBox.SelectedItem = item;
                        found = true;
                        break;
                    }
                }
                
                if (!found)
                {
                    ToolbarFontComboBox.SelectedIndex = -1;
                }
                
                ToolbarFontComboBox.SelectionChanged += ToolbarFontComboBox_SelectionChanged;
            }
        }

        // AutoSave delay slider handler
        private void AutoSaveSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Interval = TimeSpan.FromSeconds(e.NewValue);
            }
            UpdateSaveSettingsButtonState();
        }

        private void AutoBackupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            UpdateSaveSettingsButtonState();
        }

        private void WindowsHelloToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            _useWindowsHello = WindowsHelloToggle?.IsOn ?? true;
            UpdateSaveSettingsButtonState();
        }

        private void LockOnMinimizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            _lockOnMinimize = LockOnMinimizeToggle?.IsOn ?? false;
            UpdateSaveSettingsButtonState();
        }

        private void SpellCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            bool isOn = SpellCheckToggle?.IsOn ?? true;
            if (NoteRichEditBox != null)
                NoteRichEditBox.IsSpellCheckEnabled = isOn;
            UpdateSaveSettingsButtonState();
        }

        private void ShowWordCountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            _showWordCount = ShowWordCountToggle?.IsOn ?? true;
            if (WordCountTextBlock != null)
                WordCountTextBlock.Visibility = _showWordCount ? Visibility.Visible : Visibility.Collapsed;
            UpdateSaveSettingsButtonState();
        }

        private void ShowSnippetsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            _showSnippets = ShowSnippetsToggle?.IsOn ?? true;
            ApplySnippetVisibility();
            UpdateSaveSettingsButtonState();
        }

        private void ApplySnippetVisibility()
        {
            if (NotesListView == null) return;
            var vis = _showSnippets ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in NotesListView.Items)
            {
                var container = NotesListView.ContainerFromItem(item) as ListViewItem;
                if (container == null) continue;
                // Walk the visual tree to find TextBlocks tagged "SnippetRow"
                FindAndSetVisibility(container, "SnippetRow", vis);
            }
        }

        private static void FindAndSetVisibility(DependencyObject parent, string tag, Visibility vis)
        {
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Tag?.ToString() == tag)
                    fe.Visibility = vis;
                else
                    FindAndSetVisibility(child, tag, vis);
            }
        }

        private void NotesListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            var vis = _showSnippets ? Visibility.Visible : Visibility.Collapsed;
            FindAndSetVisibility(args.ItemContainer, "SnippetRow", vis);
        }


        private void DefaultSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageInitialized) return;
            if (DefaultSortComboBox?.SelectedItem is ComboBoxItem item)
            {
                _currentSortOption = item.Tag?.ToString() ?? "DateCreatedDesc";
                RefreshNotesList();
            }
            UpdateSaveSettingsButtonState();
        }

        private void ConfirmDeleteToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isPageInitialized) return;
            _confirmBeforeDelete = ConfirmDeleteToggle?.IsOn ?? false;
            UpdateSaveSettingsButtonState();
        }

        // Clear all data click handler
        private async void ClearAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear All Data",
                Content = "Are you sure you want to delete ALL journal entries and categories? This action is irreversible.",
                PrimaryButtonText = "Delete Everything",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                SelectedNote = null;
                JournalManager.Instance.Notes.Clear();
                
                // Keep only the default categories
                JournalManager.Instance.Categories.Clear();
                JournalManager.Instance.AddCategory("All Entries", "\uE80F", "#0078D4");
                JournalManager.Instance.AddCategory("Personal", "\uE77B", "#E3008C");
                JournalManager.Instance.AddCategory("Work", "\uE821", "#107C41");
                JournalManager.Instance.AddCategory("Ideas", "\uEA80", "#FFB900");
                
                JournalManager.Instance.SaveNotesMetadata();
                JournalManager.Instance.SaveCategories();
                
                // Clear RTF files on disk
                try
                {
                    foreach (var file in Directory.GetFiles(JournalManager.Instance.NotesDir, "*.rtf"))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to clear RTF files: {ex.Message}");
                }

                LoadCategoriesList();
                RefreshNotesList();
                await ShowAlertAsync("Data Cleared", "All journal entries and categories have been deleted.");
            }
        }

        // Export data click handler
        private async void ExportDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPicker = new FolderPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                StorageFolder folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    string exportPath = Path.Combine(folder.Path, $"JournalBackup_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(exportPath);

                    // Copy metadata files
                    File.Copy(Path.Combine(JournalManager.Instance.DataDir, "notes.json"), Path.Combine(exportPath, "notes.json"), true);
                    File.Copy(Path.Combine(JournalManager.Instance.DataDir, "categories.json"), Path.Combine(exportPath, "categories.json"), true);

                    // Copy notes and media directories
                    var targetNotesDir = Path.Combine(exportPath, "Notes");
                    Directory.CreateDirectory(targetNotesDir);
                    foreach (var file in Directory.GetFiles(JournalManager.Instance.NotesDir))
                    {
                        File.Copy(file, Path.Combine(targetNotesDir, Path.GetFileName(file)), true);
                    }

                    var targetMediaDir = Path.Combine(exportPath, "Media");
                    Directory.CreateDirectory(targetMediaDir);
                    foreach (var file in Directory.GetFiles(JournalManager.Instance.MediaDir))
                    {
                        File.Copy(file, Path.Combine(targetMediaDir, Path.GetFileName(file)), true);
                    }

                    await ShowAlertAsync("Export Succeeded", $"Your backup has been saved to:\n{exportPath}");
                }
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Export Failed", $"An error occurred during export: {ex.Message}");
            }
        }

        private string GetExtensionForTag(string tag)
        {
            return tag switch
            {
                "MD" => ".md",
                "HTML" => ".html",
                "RTF" => ".rtf",
                _ => ".md"
            };
        }

        private async void BatchExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var formatCombo = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 12, 0, 12)
                };
                formatCombo.Items.Add(new ComboBoxItem { Content = "Markdown (.md)", Tag = "MD" });
                formatCombo.Items.Add(new ComboBoxItem { Content = "HTML (.html)", Tag = "HTML" });
                formatCombo.Items.Add(new ComboBoxItem { Content = "Rich Text Format (.rtf)", Tag = "RTF" });
                formatCombo.SelectedIndex = 0;

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(new TextBlock { Text = "Choose the export format:" });
                panel.Children.Add(formatCombo);

                var dialog = new ContentDialog
                {
                    Title = "Batch Export Entries",
                    Content = panel,
                    PrimaryButtonText = "Choose Folder & Export",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                var res = await dialog.ShowAsync();
                if (res != ContentDialogResult.Primary) return;

                var selectedTag = (formatCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "MD";

                var folderPicker = new FolderPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                StorageFolder folder = await folderPicker.PickSingleFolderAsync();
                if (folder == null) return;

                string exportPath = Path.Combine(folder.Path, $"JournalExport_{selectedTag}_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(exportPath);

                var notes = JournalManager.Instance.Notes.Where(n => !n.IsDeleted).ToList();
                int count = 0;

                var tempRichEdit = new RichEditBox();
                ((Panel)this.Content).Children.Add(tempRichEdit);
                tempRichEdit.Visibility = Visibility.Collapsed;

                foreach (var note in notes)
                {
                    string safeTitle = string.Join("_", note.Title.Split(Path.GetInvalidFileNameChars()));
                    if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Untitled";
                    
                    string baseFileName = $"{note.DateCreated:yyyy-MM-dd}_{safeTitle}";
                    string finalFileName = baseFileName;
                    int offset = 1;
                    string ext = GetExtensionForTag(selectedTag);
                    while (File.Exists(Path.Combine(exportPath, finalFileName + ext)))
                    {
                        finalFileName = $"{baseFileName}_{offset++}";
                    }
                    
                    string destFilePath = Path.Combine(exportPath, finalFileName + ext);

                    string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(note.RtfFileName);
                    if (!File.Exists(rtfPath)) continue;

                    byte[] fileBytes = File.ReadAllBytes(rtfPath);
                    bool isEncrypted = true;
                    if (fileBytes.Length >= 5)
                    {
                        if (fileBytes[0] == 123 && fileBytes[1] == 92 && fileBytes[2] == 114 && fileBytes[3] == 116 && fileBytes[4] == 102)
                        {
                            isEncrypted = false;
                        }
                    }

                    byte[] loadedBytes = fileBytes;
                    if (isEncrypted && _lockedCategories.Contains(note.Category) && !string.IsNullOrEmpty(_masterPassword))
                    {
                        try
                        {
                            loadedBytes = EncryptionHelper.Decrypt(fileBytes, _masterPassword);
                        }
                        catch (Exception decryptEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[BatchExport] Decryption failed: {decryptEx.Message}");
                            continue;
                        }
                    }

                    if (selectedTag == "RTF")
                    {
                        File.WriteAllBytes(destFilePath, loadedBytes);
                        count++;
                    }
                    else
                    {
                        using (var ms = new MemoryStream(loadedBytes))
                        {
                            var winrtStream = ms.AsRandomAccessStream();
                            tempRichEdit.Document.LoadFromStream(TextSetOptions.FormatRtf, winrtStream);
                        }

                        tempRichEdit.Document.GetText(TextGetOptions.UseLf, out string plainText);
                        
                        if (selectedTag == "MD")
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine($"# {note.Title}");
                            sb.AppendLine($"*Date Created: {note.DateCreated:f}*");
                            if (!string.IsNullOrEmpty(note.LocationTag)) sb.AppendLine($"*Location: {note.LocationTag}*");
                            if (!string.IsNullOrEmpty(note.WeatherTag)) sb.AppendLine($"*Weather: {note.WeatherTag}*");
                            if (note.Tags != null && note.Tags.Count > 0)
                            {
                                sb.AppendLine($"*Tags: {string.Join(", ", note.Tags.Select(t => "#" + t))}*");
                            }
                            sb.AppendLine();
                            sb.AppendLine("---");
                            sb.AppendLine();
                            sb.AppendLine(plainText);

                            File.WriteAllText(destFilePath, sb.ToString(), System.Text.Encoding.UTF8);
                            count++;
                        }
                        else if (selectedTag == "HTML")
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine("<!DOCTYPE html>");
                            sb.AppendLine("<html>");
                            sb.AppendLine("<head>");
                            sb.AppendLine("    <meta charset=\"utf-8\">");
                            sb.AppendLine($"    <title>{System.Net.WebUtility.HtmlEncode(note.Title)}</title>");
                            sb.AppendLine("    <style>");
                            sb.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; max-width: 800px; margin: 40px auto; padding: 0 20px; color: #333; }");
                            sb.AppendLine("        h1 { border-bottom: 1px solid #eee; padding-bottom: 10px; }");
                            sb.AppendLine("        .metadata { color: #666; font-size: 0.9em; margin-bottom: 20px; line-height: 1.8; }");
                            sb.AppendLine("        .content { white-space: pre-wrap; font-size: 1.05em; }");
                            sb.AppendLine("        .tags { margin-top: 20px; }");
                            sb.AppendLine("        .tag { background: #eee; padding: 4px 8px; border-radius: 4px; font-size: 0.85em; margin-right: 6px; display: inline-block; text-decoration: none; color: #555; }");
                            sb.AppendLine("    </style>");
                            sb.AppendLine("</head>");
                            sb.AppendLine("<body>");
                            sb.AppendLine($"    <h1>{System.Net.WebUtility.HtmlEncode(note.Title)}</h1>");
                            sb.AppendLine("    <div class=\"metadata\">");
                            sb.AppendLine($"        <strong>Date Created:</strong> {note.DateCreated:f}<br>");
                            if (!string.IsNullOrEmpty(note.LocationTag)) sb.AppendLine($"        <strong>Location:</strong> {System.Net.WebUtility.HtmlEncode(note.LocationTag)}<br>");
                            if (!string.IsNullOrEmpty(note.WeatherTag)) sb.AppendLine($"        <strong>Weather:</strong> {System.Net.WebUtility.HtmlEncode(note.WeatherTag)}<br>");
                            sb.AppendLine("    </div>");
                            sb.AppendLine("    <hr>");
                            sb.AppendLine($"    <div class=\"content\">{System.Net.WebUtility.HtmlEncode(plainText)}</div>");
                            if (note.Tags != null && note.Tags.Count > 0)
                            {
                                sb.AppendLine("    <div class=\"tags\">");
                                foreach (var t in note.Tags)
                                {
                                    sb.AppendLine($"        <span class=\"tag\">#{System.Net.WebUtility.HtmlEncode(t)}</span>");
                                }
                                sb.AppendLine("    </div>");
                            }
                            sb.AppendLine("</body>");
                            sb.AppendLine("</html>");

                            File.WriteAllText(destFilePath, sb.ToString(), System.Text.Encoding.UTF8);
                            count++;
                        }
                    }
                }

                ((Panel)this.Content).Children.Remove(tempRichEdit);

                await ShowAlertAsync("Export Succeeded", $"Successfully exported {count} entries to:\n{exportPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to batch export entries: {ex.Message}");
                await ShowAlertAsync("Export Failed", $"An error occurred during batch export: {ex.Message}");
            }
        }

        private string GetAppVersion()
        {
            try
            {
                var version = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch (InvalidOperationException)
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}" : "1.0.0.0";
            }
        }

        private string NormalizeVersionString(string versionStr)
        {
            if (string.IsNullOrEmpty(versionStr)) return "0.0.0.0";
            if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                versionStr = versionStr.Substring(1);
            }
            int dashIndex = versionStr.IndexOf('-');
            if (dashIndex > 0)
            {
                versionStr = versionStr.Substring(0, dashIndex);
            }
            return versionStr.Trim();
        }

        // Date/Time Editor Event Handlers
        private void IncludeTimeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (EditTimePicker != null && IncludeTimeToggle != null)
            {
                EditTimePicker.Visibility = IncludeTimeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ApplyDateButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            DateTime selectedDate = DateTime.Today;
            if (EditDateCalendarView.SelectedDates.Count > 0)
            {
                selectedDate = EditDateCalendarView.SelectedDates[0].Date;
            }

            TimeSpan selectedTime = TimeSpan.Zero;
            bool includeTime = IncludeTimeToggle.IsOn;
            if (includeTime)
            {
                selectedTime = EditTimePicker.Time;
            }

            DateTime newDateTime = selectedDate.Date.Add(selectedTime);

            SelectedNote.DateCreated = newDateTime;
            SelectedNote.HasTime = includeTime;
            SelectedNote.DateModified = DateTime.Now;

            JournalManager.Instance.SaveNotesMetadata();

            UpdateEditorHeaderUI();

            var currentSelection = SelectedNote;
            RefreshNotesList();
            SelectedNote = currentSelection;

            DateEditFlyout.Hide();
            ShowStatusMessage("Date and time updated");
        }

        private void CancelDateButton_Click(object sender, RoutedEventArgs e)
        {
            DateEditFlyout.Hide();
        }

        private void RestoreNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            var noteToRestore = SelectedNote;
            SelectedNote = null; // Unselect first
            JournalManager.Instance.RestoreNote(noteToRestore);
            RefreshNotesList();
            ShowStatusMessage("Journal entry restored");
        }

        private void PermanentlyDeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            ShowDeleteConfirmationFlyout(sender as FrameworkElement, SelectedNote, true);
        }

        // Editor Layout Width Event Handlers
        private void EditorWidth_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null || sender is not MenuFlyoutItem item) return;
            string widthTag = item.Tag as string;
            if (string.IsNullOrEmpty(widthTag)) return;

            SelectedNote.EditorWidth = widthTag;
            JournalManager.Instance.SaveNotesMetadata();

            ApplyEditorWidth(widthTag);
            ShowStatusMessage($"Layout width set to {item.Text}");
        }

        private void ApplyEditorWidth(string widthTag)
        {
            if (EditorContentStackPanel == null) return;
            switch (widthTag)
            {
                case "Narrow":
                    EditorContentStackPanel.MaxWidth = 600;
                    break;
                case "Wide":
                    EditorContentStackPanel.MaxWidth = 1000;
                    break;
                case "Full":
                    EditorContentStackPanel.MaxWidth = double.PositiveInfinity;
                    break;
                case "Medium":
                default:
                    EditorContentStackPanel.MaxWidth = 800;
                    break;
            }
        }

        // Markdown Preview Event Handlers
        private void MarkdownPreviewToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (NoteRichEditBox == null || MarkdownPreviewTextBlock == null) return;

            // Retrieve plain text from RichEditBox
            string plainText;
            NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out plainText);

            // Render Markdown into RichTextBlock
            RenderMarkdownToRichTextBlock(plainText, MarkdownPreviewTextBlock);

            // Toggle visibility to show preview only (full-width)
            if (EditorColumn != null) EditorColumn.Width = new GridLength(0, GridUnitType.Pixel);
            if (PreviewColumn != null) PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            if (EditorPreviewDivider != null) EditorPreviewDivider.Visibility = Visibility.Collapsed;

            NoteRichEditBox.Visibility = Visibility.Collapsed;
            MarkdownPreviewTextBlock.Visibility = Visibility.Visible;
            ShowStatusMessage("Markdown Preview Mode");
        }

        private void MarkdownPreviewToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (NoteRichEditBox == null || MarkdownPreviewTextBlock == null) return;

            // Restore editor only (full-width)
            if (EditorColumn != null) EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            if (PreviewColumn != null) PreviewColumn.Width = new GridLength(0, GridUnitType.Pixel);
            if (EditorPreviewDivider != null) EditorPreviewDivider.Visibility = Visibility.Collapsed;

            NoteRichEditBox.Visibility = Visibility.Visible;
            MarkdownPreviewTextBlock.Visibility = Visibility.Collapsed;
            ShowStatusMessage("Edit Mode");
        }

        private void RenderMarkdownToRichTextBlock(string markdownText, RichTextBlock richTextBlock)
        {
            richTextBlock.Blocks.Clear();
            if (string.IsNullOrEmpty(markdownText)) return;

            string[] lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;
            Paragraph codeParagraph = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (!inCodeBlock && !line.TrimStart().StartsWith("```"))
                {
                    line = line.TrimStart(' ', '\t');
                }

                // Handle code blocks
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        inCodeBlock = false;
                        codeParagraph = null;
                    }
                    else
                    {
                        inCodeBlock = true;
                        codeParagraph = new Paragraph
                        {
                            Margin = new Thickness(0, 8, 0, 8),
                            TextIndent = 12
                        };
                        richTextBlock.Blocks.Add(codeParagraph);
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    if (codeParagraph != null)
                    {
                        if (codeParagraph.Inlines.Count > 0)
                        {
                            codeParagraph.Inlines.Add(new LineBreak());
                        }
                        codeParagraph.Inlines.Add(new Run
                        {
                            Text = line,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            Foreground = GetBrushFromHex("#98C379")
                        });
                    }
                    continue;
                }

                // Handle blockquotes
                if (line.StartsWith("> "))
                {
                    var quoteParagraph = new Paragraph
                    {
                        Margin = new Thickness(16, 8, 0, 8),
                        Foreground = GetBrushFromHex("#8A8886")
                    };
                    quoteParagraph.Inlines.Add(new Italic());
                    var italicSpan = (Italic)quoteParagraph.Inlines[0];
                    ParseInlineStyles(line.Substring(2), italicSpan.Inlines);
                    richTextBlock.Blocks.Add(quoteParagraph);
                    continue;
                }

                // Handle horizontal rule
                if (line.Trim() == "---" || line.Trim() == "***")
                {
                    var hrParagraph = new Paragraph { Margin = new Thickness(0, 12, 0, 12) };
                    hrParagraph.Inlines.Add(new Run
                    {
                        Text = "__________________________________________________",
                        Foreground = GetBrushFromHex("#8A8886")
                    });
                    richTextBlock.Blocks.Add(hrParagraph);
                    continue;
                }

                // Handle headings
                if (line.StartsWith("# "))
                {
                    var heading = new Paragraph
                    {
                        Margin = new Thickness(0, 16, 0, 8),
                        FontSize = 26,
                        FontWeight = FontWeights.Bold
                    };
                    ParseInlineStyles(line.Substring(2), heading.Inlines);
                    richTextBlock.Blocks.Add(heading);
                    continue;
                }
                else if (line.StartsWith("## "))
                {
                    var heading = new Paragraph
                    {
                        Margin = new Thickness(0, 14, 0, 6),
                        FontSize = 20,
                        FontWeight = FontWeights.Bold
                    };
                    ParseInlineStyles(line.Substring(3), heading.Inlines);
                    richTextBlock.Blocks.Add(heading);
                    continue;
                }
                else if (line.StartsWith("### "))
                {
                    var heading = new Paragraph
                    {
                        Margin = new Thickness(0, 12, 0, 4),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold
                    };
                    ParseInlineStyles(line.Substring(4), heading.Inlines);
                    richTextBlock.Blocks.Add(heading);
                    continue;
                }

                // Handle checklists
                bool isTodoUnchecked = line.StartsWith("- [ ] ") || line.StartsWith("☐ ");
                bool isTodoChecked = line.StartsWith("- [x] ") || line.StartsWith("- [X] ") || line.StartsWith("☒ ") || line.StartsWith("☑ ");
                
                if (isTodoUnchecked || isTodoChecked)
                {
                    var listParagraph = new Paragraph
                    {
                        Margin = new Thickness(20, 4, 0, 4)
                    };
                    
                    string checkGlyph = isTodoChecked ? "☑  " : "☐  ";
                    listParagraph.Inlines.Add(new Run { Text = checkGlyph, FontWeight = FontWeights.Bold, Foreground = GetBrushFromHex(isTodoChecked ? "#107C41" : "#8A8886") });
                    
                    int sliceIndex = 0;
                    if (line.StartsWith("- [ ] ") || line.StartsWith("- [x] ") || line.StartsWith("- [X] "))
                    {
                        sliceIndex = 6;
                    }
                    else if (line.StartsWith("☐ ") || line.StartsWith("☒ ") || line.StartsWith("☑ "))
                    {
                        sliceIndex = 2;
                    }
                    
                    var textSpan = new Span();
                    ParseInlineStyles(line.Substring(sliceIndex), textSpan.Inlines);
                    if (isTodoChecked)
                    {
                        foreach (var inline in textSpan.Inlines)
                        {
                            if (inline is Run run)
                            {
                                run.Foreground = GetBrushFromHex("#8A8886");
                            }
                        }
                    }
                    listParagraph.Inlines.Add(textSpan);
                    richTextBlock.Blocks.Add(listParagraph);
                    continue;
                }

                // Handle bullet lists
                bool isBulletList = line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ") || 
                                    line.StartsWith("• ") || line.StartsWith("▪ ") || line.StartsWith("◦ ");
                if (isBulletList)
                {
                    var listParagraph = new Paragraph
                    {
                        Margin = new Thickness(20, 4, 0, 4)
                    };
                    listParagraph.Inlines.Add(new Run { Text = "•  ", FontWeight = FontWeights.Bold });
                    ParseInlineStyles(line.Substring(2), listParagraph.Inlines);
                    richTextBlock.Blocks.Add(listParagraph);
                    continue;
                }

                // Handle numbered lists (e.g., "1. ", "12. ")
                bool isNumberedList = false;
                int dotIndex = -1;
                for (int j = 0; j < line.Length; j++)
                {
                    if (char.IsDigit(line[j])) continue;
                    if (line[j] == '.' && j > 0 && j + 1 < line.Length && line[j + 1] == ' ')
                    {
                        isNumberedList = true;
                        dotIndex = j;
                    }
                    break;
                }

                if (isNumberedList && dotIndex != -1)
                {
                    var listParagraph = new Paragraph
                    {
                        Margin = new Thickness(20, 4, 0, 4)
                    };
                    string prefix = line.Substring(0, dotIndex + 2); // e.g., "1. "
                    listParagraph.Inlines.Add(new Run { Text = prefix + " ", FontWeight = FontWeights.Bold });
                    ParseInlineStyles(line.Substring(dotIndex + 2), listParagraph.Inlines);
                    richTextBlock.Blocks.Add(listParagraph);
                    continue;
                }

                // Handle empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    var spacerParagraph = new Paragraph { Margin = new Thickness(0, 8, 0, 0) };
                    spacerParagraph.Inlines.Add(new Run { Text = " " });
                    richTextBlock.Blocks.Add(spacerParagraph);
                    continue;
                }

                // Standard Paragraph
                var paragraph = new Paragraph { Margin = new Thickness(0, 6, 0, 6) };
                ParseInlineStyles(line, paragraph.Inlines);
                richTextBlock.Blocks.Add(paragraph);
            }
        }

        private void ParseInlineStyles(string text, InlineCollection inlines)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            int index = 0;
            while (index < text.Length)
            {
                int boldIdx = text.IndexOf("**", index);
                int italicIdx = text.IndexOf("*", index);
                int codeIdx = text.IndexOf("`", index);
                int linkIdx = text.IndexOf("[", index);
                
                int minIdx = int.MaxValue;
                string nextTag = null;
                
                if (boldIdx != -1 && boldIdx < minIdx) { minIdx = boldIdx; nextTag = "**"; }
                if (italicIdx != -1 && italicIdx < minIdx && (boldIdx == -1 || italicIdx != boldIdx)) { minIdx = italicIdx; nextTag = "*"; }
                if (codeIdx != -1 && codeIdx < minIdx) { minIdx = codeIdx; nextTag = "`"; }
                if (linkIdx != -1 && linkIdx < minIdx) { minIdx = linkIdx; nextTag = "["; }
                
                if (nextTag == null)
                {
                    inlines.Add(new Run { Text = text.Substring(index) });
                    break;
                }
                
                if (minIdx > index)
                {
                    inlines.Add(new Run { Text = text.Substring(index, minIdx - index) });
                }
                
                index = minIdx;
                
                if (nextTag == "**")
                {
                    int closeIdx = text.IndexOf("**", index + 2);
                    if (closeIdx != -1)
                    {
                        var run = new Run { Text = text.Substring(index + 2, closeIdx - (index + 2)) };
                        var bold = new Bold();
                        bold.Inlines.Add(run);
                        inlines.Add(bold);
                        index = closeIdx + 2;
                    }
                    else
                    {
                        inlines.Add(new Run { Text = "**" });
                        index += 2;
                    }
                }
                else if (nextTag == "*")
                {
                    int closeIdx = text.IndexOf("*", index + 1);
                    if (closeIdx != -1)
                    {
                        var run = new Run { Text = text.Substring(index + 1, closeIdx - (index + 1)) };
                        var italic = new Italic();
                        italic.Inlines.Add(run);
                        inlines.Add(italic);
                        index = closeIdx + 1;
                    }
                    else
                    {
                        inlines.Add(new Run { Text = "*" });
                        index += 1;
                    }
                }
                else if (nextTag == "`")
                {
                    int closeIdx = text.IndexOf("`", index + 1);
                    if (closeIdx != -1)
                    {
                        var run = new Run { 
                            Text = text.Substring(index + 1, closeIdx - (index + 1)),
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            Foreground = GetBrushFromHex("#E06C75")
                        };
                        inlines.Add(run);
                        index = closeIdx + 1;
                    }
                    else
                    {
                        inlines.Add(new Run { Text = "`" });
                        index += 1;
                    }
                }
                else if (nextTag == "[")
                {
                    int closeTextIdx = text.IndexOf("]", index + 1);
                    if (closeTextIdx != -1)
                    {
                        int openUrlIdx = text.IndexOf("(", closeTextIdx);
                        if (openUrlIdx == closeTextIdx + 1)
                        {
                            int closeUrlIdx = text.IndexOf(")", openUrlIdx);
                            if (closeUrlIdx != -1)
                            {
                                string linkText = text.Substring(index + 1, closeTextIdx - (index + 1));
                                string url = text.Substring(openUrlIdx + 1, closeUrlIdx - (openUrlIdx + 1));
                                
                                try
                                {
                                    var hyperlink = new Hyperlink { NavigateUri = new Uri(url) };
                                    hyperlink.Inlines.Add(new Run { Text = linkText });
                                    inlines.Add(hyperlink);
                                }
                                catch
                                {
                                    inlines.Add(new Run { Text = $"[{linkText}]({url})" });
                                }
                                index = closeUrlIdx + 1;
                                continue;
                            }
                        }
                    }
                    inlines.Add(new Run { Text = "[" });
                    index += 1;
                }
            }
        }

        // Print Note Event Handler (HTML-Based automatic print preview launcher)
        private void PrintNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            LaunchPrintPreview(SelectedNote);
        }

        private void ContextMenuPrint_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            LaunchPrintPreview(SelectedNote);
        }

        private void LaunchPrintPreview(JournalNote note)
        {
            string html = GenerateNoteHtml(note, autoPrint: true);
            if (string.IsNullOrEmpty(html)) return;

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "JournalApp");
                Directory.CreateDirectory(tempDir);
                string tempPath = Path.Combine(tempDir, $"print_note_{note.Id}.html");
                File.WriteAllText(tempPath, html);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });
                
                ShowStatusMessage("Print/PDF preview launched");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to print: {ex.Message}");
                ShowStatusMessage("Failed to launch print preview");
            }
        }

        private async void ContextMenuExportHtml_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            string html = GenerateNoteHtml(SelectedNote, autoPrint: false);
            if (string.IsNullOrEmpty(html)) return;

            try
            {
                var savePicker = CreateSavePicker();
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("HTML Document", new List<string>() { ".html" });
                
                string safeTitle = string.IsNullOrWhiteSpace(SelectedNote.Title) ? "Untitled" : SelectedNote.Title;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    safeTitle = safeTitle.Replace(c, '_');
                }
                savePicker.SuggestedFileName = safeTitle;

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    await File.WriteAllTextAsync(file.Path, html);
                    ShowStatusMessage("Exported as HTML successfully");
                }
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Export Failed", $"Failed to save HTML file: {ex.Message}");
            }
        }

        private FileSavePicker CreateSavePicker()
        {
            var picker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            return picker;
        }

        private string GenerateNoteHtml(JournalNote note, bool autoPrint)
        {
            if (note == null || NoteRichEditBox == null) return string.Empty;

            string title = string.IsNullOrWhiteSpace(note.Title) ? "Untitled Note" : note.Title.Trim();
            string plainText;
            NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out plainText);

            string dateString = note.DateCreated.ToString("MMMM d, yyyy h:mm tt");
            string category = note.Category;

            // Resolve Cover Image URL (supporting ms-appx URLs directly for printing browser compatibility)
            string imageSrc = "";
            if (!string.IsNullOrEmpty(note.HeroImagePath))
            {
                try
                {
                    string path = note.HeroImagePath;
                    if (path.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
                    {
                        string subPath = path.Replace("ms-appx:///", "").Replace("ms-appx://", "").Replace('/', '\\');
                        string baseDir = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
                        string absolutePath = Path.Combine(baseDir, subPath);
                        imageSrc = new Uri(absolutePath).AbsoluteUri;
                    }
                    else
                    {
                        string absolutePath = JournalManager.Instance.GetAbsoluteMediaPath(path);
                        if (absolutePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                            absolutePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            imageSrc = absolutePath;
                        }
                        else
                        {
                            imageSrc = new Uri(absolutePath).AbsoluteUri;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GenerateNoteHtml] Image path conversion failed: {ex.Message}");
                }
            }

            // Resolve Category Details
            var catInfo = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
            string badgeColor = catInfo?.Color ?? "#808080";

            // Resolve Mood Badge
            string moodBadgeHtml = "";
            if (!string.IsNullOrEmpty(note.Mood) && note.Mood != "None")
            {
                moodBadgeHtml = $"<span class=\"mood-badge\">{System.Net.WebUtility.HtmlEncode(note.Mood)}</span>";
            }

            // Resolve Tag Badges
            string tagsHtml = "";
            if (note.Tags != null && note.Tags.Count > 0)
            {
                foreach (var tag in note.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tagsHtml += $"<span class=\"tag-badge\">#{System.Net.WebUtility.HtmlEncode(tag.TrimStart('#'))}</span>";
                    }
                }
            }

            string[] rawLines = plainText.Split(new[] { "\n", "\r" }, StringSplitOptions.None);
            string bodyHtml = "";
            bool inPre = false;
            bool inUl = false;
            bool inBlockquote = false;

            foreach (var line in rawLines)
            {
                string trimmed = line.Trim();
                
                // Handle code blocks
                if (trimmed.StartsWith("```"))
                {
                    if (inUl) { bodyHtml += "</ul>"; inUl = false; }
                    if (inBlockquote) { bodyHtml += "</blockquote>"; inBlockquote = false; }

                    if (inPre)
                    {
                        bodyHtml += "</pre>";
                        inPre = false;
                    }
                    else
                    {
                        bodyHtml += "<pre>";
                        inPre = true;
                    }
                    continue;
                }

                if (inPre)
                {
                    bodyHtml += System.Net.WebUtility.HtmlEncode(line) + "\n";
                    continue;
                }

                // Handle blockquotes
                if (trimmed.StartsWith(">"))
                {
                    if (inUl) { bodyHtml += "</ul>"; inUl = false; }

                    string quoteContent = trimmed.Substring(1).TrimStart();
                    string parsedContent = ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(quoteContent));

                    if (!inBlockquote)
                    {
                        bodyHtml += "<blockquote>";
                        inBlockquote = true;
                    }
                    bodyHtml += $"<p>{parsedContent}</p>";
                    continue;
                }
                else if (inBlockquote)
                {
                    bodyHtml += "</blockquote>";
                    inBlockquote = false;
                }

                // Handle lists
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    string itemContent = trimmed.Substring(2);
                    string parsedContent = ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(itemContent));

                    if (!inUl)
                    {
                        bodyHtml += "<ul>";
                        inUl = true;
                    }
                    bodyHtml += $"<li>{parsedContent}</li>";
                    continue;
                }
                else if (inUl)
                {
                    bodyHtml += "</ul>";
                    inUl = false;
                }

                // Handle headers, dividers, blank lines, and normal text paragraphs
                if (trimmed.StartsWith("# "))
                {
                    string headerVal = trimmed.Substring(2);
                    bodyHtml += $"<h1>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(headerVal))}</h1>";
                }
                else if (trimmed.StartsWith("## "))
                {
                    string headerVal = trimmed.Substring(3);
                    bodyHtml += $"<h2>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(headerVal))}</h2>";
                }
                else if (trimmed.StartsWith("### "))
                {
                    string headerVal = trimmed.Substring(4);
                    bodyHtml += $"<h3>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(headerVal))}</h3>";
                }
                else if (trimmed == "---")
                {
                    bodyHtml += "<hr>";
                }
                else if (string.IsNullOrWhiteSpace(trimmed))
                {
                    bodyHtml += "<br>";
                }
                else
                {
                    bodyHtml += $"<p>{ProcessInlineMarkdown(System.Net.WebUtility.HtmlEncode(line))}</p>";
                }
            }

            if (inPre) bodyHtml += "</pre>";
            if (inUl) bodyHtml += "</ul>";
            if (inBlockquote) bodyHtml += "</blockquote>";

            string html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
    <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
    <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
    <link href=""https://fonts.googleapis.com/css2?family=Playfair+Display:ital,wght@0,400..900;1,400..900&family=Plus+Jakarta+Sans:ital,wght@0,200..800;1,200..800&display=swap"" rel=""stylesheet"">
    <style>
        body {{
            font-family: 'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
            line-height: 1.8;
            color: #2d3748;
            max-width: 760px;
            margin: 50px auto;
            padding: 0 32px;
            background-color: #ffffff;
        }}
        
        .header {{
            margin-bottom: 40px;
        }}

        h1 {{
            font-family: 'Playfair Display', Georgia, serif;
            font-size: 2.8em;
            font-weight: 700;
            line-height: 1.25;
            margin: 0 0 16px 0;
            color: #1a202c;
            letter-spacing: -0.02em;
        }}

        .meta-info {{
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: 12px;
            font-size: 0.9em;
            color: #718096;
            margin-bottom: 24px;
            padding-bottom: 20px;
            border-bottom: 1px solid #e2e8f0;
        }}

        .meta-date {{
            font-weight: 500;
        }}

        .category-badge {{
            display: inline-block;
            background-color: {badgeColor}12;
            color: {badgeColor};
            border: 1px solid {badgeColor}25;
            padding: 4px 12px;
            border-radius: 20px;
            font-size: 0.8em;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.03em;
        }}

        .mood-badge {{
            display: inline-block;
            background-color: #fefaf0;
            color: #b7791f;
            border: 1px solid #fbecb2;
            padding: 4px 12px;
            border-radius: 20px;
            font-size: 0.8em;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.03em;
        }}

        .tag-badge {{
            display: inline-block;
            background-color: #f7fafc;
            color: #4a5568;
            border: 1px solid #e2e8f0;
            padding: 4px 12px;
            border-radius: 20px;
            font-size: 0.8em;
            font-weight: 500;
            letter-spacing: 0.03em;
        }}

        .cover-image {{
            width: 100%;
            height: 380px;
            object-fit: cover;
            border-radius: 12px;
            margin-bottom: 36px;
            box-shadow: 0 4px 20px rgba(0, 0, 0, 0.08);
        }}

        .content {{
            font-size: 1.125em;
            color: #2d3748;
        }}

        p {{
            margin: 0 0 1.6em 0;
        }}

        h2, h3, h4 {{
            font-family: 'Playfair Display', Georgia, serif;
            color: #1a202c;
            margin-top: 1.8em;
            margin-bottom: 0.6em;
            font-weight: 700;
        }}

        h2 {{
            font-size: 1.8em;
            border-bottom: 1px solid #edf2f7;
            padding-bottom: 8px;
        }}

        h3 {{
            font-size: 1.4em;
        }}

        ul, ol {{
            margin: 0 0 1.6em 0;
            padding-left: 24px;
        }}

        li {{
            margin-bottom: 0.6em;
        }}

        pre {{
            background-color: #f7fafc;
            padding: 20px;
            border-radius: 8px;
            overflow-x: auto;
            font-family: Consolas, Monaco, monospace;
            font-size: 0.9em;
            border: 1px solid #e2e8f0;
            line-height: 1.5;
            margin: 0 0 1.6em 0;
        }}

        code {{
            font-family: Consolas, Monaco, monospace;
            background-color: #f7fafc;
            padding: 2px 6px;
            border-radius: 4px;
            border: 1px solid #e2e8f0;
            font-size: 0.9em;
            color: #c53030;
        }}

        pre code {{
            background-color: transparent;
            padding: 0;
            border: none;
            font-size: inherit;
            color: inherit;
        }}

        blockquote {{
            border-left: 4px solid {badgeColor};
            background-color: {badgeColor}08;
            margin: 0 0 1.6em 0;
            padding: 16px 20px;
            color: #4a5568;
            font-style: italic;
            border-radius: 0 8px 8px 0;
        }}

        hr {{
            border: 0;
            height: 1px;
            background: #e2e8f0;
            margin: 40px 0;
            position: relative;
        }}

        hr::after {{
            content: ""✦"";
            position: absolute;
            left: 50%;
            top: 50%;
            transform: translate(-50%, -50%);
            background: #fff;
            padding: 0 10px;
            color: #cbd5e0;
            font-size: 0.9em;
        }}

        @media print {{
            body {{
                margin: 15mm 20mm;
                max-width: 100%;
                font-size: 11pt;
                color: #000;
            }}
            .cover-image {{
                max-height: 280px;
                box-shadow: none;
                page-break-inside: avoid;
            }}
            .meta-info {{
                border-bottom: 1px solid #000;
            }}
            blockquote {{
                background-color: #fff;
                border-left: 3pt solid #000;
                page-break-inside: avoid;
            }}
            pre {{
                background-color: #fff;
                border: 1pt solid #000;
                page-break-inside: avoid;
            }}
            a {{
                text-decoration: underline;
                color: #000;
            }}
        }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
        <div class=""meta-info"">
            <span class=""meta-date"">{System.Net.WebUtility.HtmlEncode(dateString)}</span>
            <span class=""category-badge"">{System.Net.WebUtility.HtmlEncode(category)}</span>
            {moodBadgeHtml}
            {tagsHtml}
        </div>
    </div>
    {(!string.IsNullOrEmpty(imageSrc) ? $"<img class=\"cover-image\" src=\"{imageSrc}\" />" : "")}
    <div class=""content"">
        {bodyHtml}
    </div>
";

            if (autoPrint)
            {
                html += @"    <script>
        window.addEventListener('load', () => {
            window.print();
        });
    </script>
";
            }

            html += @"</body>
</html>";

            return html;
        }

        private string ProcessInlineMarkdown(string encodedText)
        {
            if (string.IsNullOrEmpty(encodedText)) return string.Empty;

            // Bold: **text** or __text__
            encodedText = System.Text.RegularExpressions.Regex.Replace(encodedText, @"\*\*(.*?)\*\*", "<strong>$1</strong>");
            encodedText = System.Text.RegularExpressions.Regex.Replace(encodedText, @"__(.*?)__", "<strong>$1</strong>");

            // Italic: *text* or _text_
            encodedText = System.Text.RegularExpressions.Regex.Replace(encodedText, @"\*(.*?)\*", "<em>$1</em>");
            encodedText = System.Text.RegularExpressions.Regex.Replace(encodedText, @"_(.*?)_", "<em>$1</em>");

            // Inline code: `code`
            encodedText = System.Text.RegularExpressions.Regex.Replace(encodedText, @"`(.*?)`", "<code>$1</code>");

            return encodedText;
        }

        // Context menu right-click select and opening handlers
        private void NotesListItem_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is JournalNote note)
            {
                NotesListView.SelectedItem = note;
            }
        }

        private void NotesListItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                SetDeleteButtonsVisibility(grid, true);
            }
        }

        private void NotesListItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                SetDeleteButtonsVisibility(grid, false);
            }
        }

        private void SetDeleteButtonsVisibility(DependencyObject parent, bool visible)
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Button button)
                {
                    var tag = button.Tag as string;
                    if (tag == "DeleteButton" || tag == "ActionButton")
                    {
                        button.Opacity = visible ? 1.0 : 0.0;
                        button.IsHitTestVisible = visible;
                    }
                    else
                    {
                        SetDeleteButtonsVisibility(child, visible);
                    }
                }
                else
                {
                    SetDeleteButtonsVisibility(child, visible);
                }
            }
        }

        private void NotesListContextFlyout_Opening(object sender, object e)
        {
            if (sender is not MenuFlyout menu) return;
            var target = menu.Target as FrameworkElement;
            var note = target?.DataContext as JournalNote;
            if (note == null) return;

            SelectedNote = note; // Sync selection immediately

            // 1. Update Delete menu item text and colors based on trash state
            var deleteItem = ContextMenuDelete;
            if (deleteItem != null)
            {
                deleteItem.Visibility = Visibility.Visible;
                if (note.IsDeleted)
                {
                    deleteItem.Text = "Delete Permanently";
                    deleteItem.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    if (deleteItem.Icon is FontIcon fontIcon)
                    {
                        fontIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    }
                }
                else
                {
                    deleteItem.Text = "Move to Trash";
                    deleteItem.ClearValue(Control.ForegroundProperty);
                    if (deleteItem.Icon is FontIcon fontIcon)
                    {
                        fontIcon.ClearValue(IconElement.ForegroundProperty);
                    }
                }
            }

            // 2. Populate Move to Category sub-menu dynamically
            ContextMenuMoveToCategorySubItem.Items.Clear();
            ContextMenuMoveToCategorySubItem.IsEnabled = !note.IsDeleted;

            if (!note.IsDeleted)
            {
                foreach (var category in JournalManager.Instance.Categories)
                {
                    if (category.Name == "All Entries") continue;

                    var fontIcon = new FontIcon 
                    { 
                        Glyph = category.Icon,
                        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Resources["SymbolThemeFontFamily"]
                    };
                    if (!string.IsNullOrEmpty(category.Color))
                    {
                        var converter = new HexToBrushConverter();
                        var brush = converter.Convert(category.Color, typeof(Microsoft.UI.Xaml.Media.Brush), null, null) as Microsoft.UI.Xaml.Media.Brush;
                        if (brush != null)
                        {
                            fontIcon.Foreground = brush;
                        }
                    }

                    var item = new MenuFlyoutItem
                    {
                        Text = category.Name,
                        Icon = fontIcon
                    };

                    if (category.Name == note.Category)
                    {
                        item.IsEnabled = false;
                        item.Text += " (Current)";
                    }

                    string categoryName = category.Name;
                    item.Click += (s, args) =>
                    {
                        note.Category = categoryName;
                        JournalManager.Instance.SaveNotesMetadata();
                        RefreshNotesList();
                        UpdateEditorHeaderUI();
                    };

                    ContextMenuMoveToCategorySubItem.Items.Add(item);
                }
            }
        }

        private void HeroImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (HeroImageContainer == null) return;
            
            var clipGeometry = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
            };
            HeroImageContainer.Clip = clipGeometry;
            UpdateHeroImageSizeAndConstraints();
        }

        // ── Feature Additions Partials ──

        private void ShowGrid(Grid gridToShow)
        {
            var allGrids = new Grid[] { MainEditorGrid, SettingsGrid, GitHubGrid, StatsGrid, GalleryGrid, BlogPageGrid, MapGrid };
            foreach (var g in allGrids)
            {
                if (g == null) continue;
                if (g == gridToShow)
                {
                    g.Visibility = Visibility.Visible;
                    AnimateGridIn(g);
                }
                else
                {
                    g.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void AnimateGridIn(Grid target)
        {
            // Ensure a CompositeTransform is available for translate animation
            if (target.RenderTransform is not CompositeTransform)
                target.RenderTransform = new CompositeTransform();
            target.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            var ct = (CompositeTransform)target.RenderTransform;
            ct.TranslateY = 18;
            target.Opacity = 0;

            var sb = new Storyboard();

            // Fade in
            var fadeAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeAnim, target);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");
            sb.Children.Add(fadeAnim);

            // Slide up
            var slideAnim = new DoubleAnimation
            {
                From = 18,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideAnim, target);
            Storyboard.SetTargetProperty(slideAnim, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
            sb.Children.Add(slideAnim);

            sb.Begin();
        }



        private void PopulateGallery()
        {
            if (GalleryGridView == null) return;

            // Include ALL non-deleted notes — even those without an explicit cover use the auto-assigned picsum image
            var photoNotes = JournalManager.Instance.Notes
                .Where(n => !n.IsDeleted)
                .OrderByDescending(n => n.DateCreated)
                .ToList();

            // Empty state
            if (GalleryEmptyState != null)
                GalleryEmptyState.Visibility = photoNotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (GalleryGridView != null)
                GalleryGridView.Visibility = photoNotes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            // Build padded list: use AvatarImagePath so auto-images are shown too
            const int columns = 3;
            var items = photoNotes.Select(n => new GalleryItem
            {
                Note = n,
                IsPlaceholder = false,
                GalleryImagePath = n.AvatarImagePath  // explicit cover OR auto picsum
            }).ToList();

            int remainder = items.Count % columns;
            if (remainder != 0)
            {
                int padding = columns - remainder;
                for (int i = 0; i < padding; i++)
                    items.Add(new GalleryItem { IsPlaceholder = true });
            }

            GalleryGridView.ItemsSource = items;
        }

        private void GalleryGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GalleryItem item && item.Note != null && !item.IsPlaceholder)
            {
                var note = item.Note;
                var images = new List<string>();

                // 1. Add cover image if exists
                if (!string.IsNullOrEmpty(note.HeroImagePath) && note.HeroImagePath != "None")
                {
                    images.Add(JournalManager.Instance.GetAbsoluteMediaPath(note.HeroImagePath));
                }
                else if (string.IsNullOrEmpty(note.HeroImagePath))
                {
                    // Add default picsum fallback if they have no custom cover
                    images.Add($"https://picsum.photos/seed/{note.Id}/1200/600");
                }

                // 2. Add all attached photos
                if (note.AttachedPhotoPaths != null)
                {
                    foreach (var path in note.AttachedPhotoPaths)
                    {
                        images.Add(JournalManager.Instance.GetAbsoluteMediaPath(path));
                    }
                }

                if (images.Count > 0)
                {
                    if (GalleryViewerNoteTitle != null) GalleryViewerNoteTitle.Text = note.Title;
                    if (GalleryViewerNoteDate != null) GalleryViewerNoteDate.Text = note.DateCreated.ToString("MMMM d, yyyy");
                    
                    if (GalleryViewerFlipView != null) GalleryViewerFlipView.ItemsSource = images;
                    if (GalleryViewerGridView != null) GalleryViewerGridView.ItemsSource = images;

                    if (GalleryViewerFlipView != null) GalleryViewerFlipView.SelectedIndex = 0;
                    if (GalleryViewerGridView != null) GalleryViewerGridView.SelectedIndex = 0;

                    if (GalleryNoteViewerOverlay != null) GalleryNoteViewerOverlay.Visibility = Visibility.Visible;
                }
            }
        }

        private void CloseGalleryViewerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (GalleryNoteViewerOverlay != null) GalleryNoteViewerOverlay.Visibility = Visibility.Collapsed;
            if (GalleryViewerFlipView != null) GalleryViewerFlipView.ItemsSource = null;
            if (GalleryViewerGridView != null) GalleryViewerGridView.ItemsSource = null;
        }

        private bool _isSyncingGallerySelection = false;

        private void GalleryViewerGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingGallerySelection) return;
            _isSyncingGallerySelection = true;
            try
            {
                if (GalleryViewerGridView != null && GalleryViewerFlipView != null && GalleryViewerGridView.SelectedIndex >= 0)
                {
                    GalleryViewerFlipView.SelectedIndex = GalleryViewerGridView.SelectedIndex;
                }
            }
            finally
            {
                _isSyncingGallerySelection = false;
            }
        }

        private void GalleryViewerFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingGallerySelection) return;
            _isSyncingGallerySelection = true;
            try
            {
                if (GalleryViewerFlipView != null && GalleryViewerGridView != null && GalleryViewerFlipView.SelectedIndex >= 0)
                {
                    GalleryViewerGridView.SelectedIndex = GalleryViewerFlipView.SelectedIndex;
                }
            }
            finally
            {
                _isSyncingGallerySelection = false;
            }
        }

        private async void ContextMenuExportMarkdown_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            string plainText;
            NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out plainText);

            try
            {
                var savePicker = CreateSavePicker();
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Markdown Document", new List<string>() { ".md" });
                
                string safeTitle = string.IsNullOrWhiteSpace(SelectedNote.Title) ? "Untitled" : SelectedNote.Title;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    safeTitle = safeTitle.Replace(c, '_');
                }
                savePicker.SuggestedFileName = safeTitle;

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("---");
                    sb.AppendLine($"title: \"{SelectedNote.Title.Replace("\"", "\\\"")}\"");
                    sb.AppendLine($"date: {SelectedNote.DateCreated.ToString("yyyy-MM-dd HH:mm:ss")}");
                    sb.AppendLine($"category: \"{SelectedNote.Category.Replace("\"", "\\\"")}\"");
                    if (!string.IsNullOrEmpty(SelectedNote.Mood) && SelectedNote.Mood != "None")
                    {
                        sb.AppendLine($"mood: \"{SelectedNote.Mood.Replace("\"", "\\\"")}\"");
                    }
                    if (SelectedNote.Tags != null && SelectedNote.Tags.Count > 0)
                    {
                        sb.AppendLine("tags:");
                        foreach (var tag in SelectedNote.Tags)
                        {
                            sb.AppendLine($"  - {tag}");
                        }
                    }
                    sb.AppendLine("---");
                    sb.AppendLine();
                    sb.AppendLine(plainText);

                    await File.WriteAllTextAsync(file.Path, sb.ToString());
                    ShowStatusMessage("Exported as Markdown successfully");
                }
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Export Failed", $"Failed to save Markdown file: {ex.Message}");
            }
        }

        private async void ImportNoteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.ViewMode = PickerViewMode.List;
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".md");
                picker.FileTypeFilter.Add(".txt");
                picker.FileTypeFilter.Add(".rtf");

                StorageFile file = await picker.PickSingleFileAsync();
                if (file == null) return;

                string extension = Path.GetExtension(file.Path).ToLowerInvariant();
                string title = Path.GetFileNameWithoutExtension(file.Name);
                string category = (_selectedCategory == "All Entries" || _selectedCategory == "Favorites" || _selectedCategory == "Trash") 
                    ? "Personal" 
                    : _selectedCategory;
                if (_selectedCategory.StartsWith("Tag:")) category = "Personal";

                string plainText = "";
                string rtfContent = "";
                List<string> tags = new List<string>();
                string mood = "None";
                DateTime dateCreated = DateTime.Now;

                if (extension == ".rtf")
                {
                    rtfContent = await File.ReadAllTextAsync(file.Path);
                }
                else
                {
                    plainText = await File.ReadAllTextAsync(file.Path);

                    if (plainText.StartsWith("---"))
                    {
                        int nextDash = plainText.IndexOf("---", 3);
                        if (nextDash > 0)
                        {
                            string frontmatter = plainText.Substring(3, nextDash - 3);
                            plainText = plainText.Substring(nextDash + 3).TrimStart();

                            var lines = frontmatter.Split('\n');
                            string currentKey = "";
                            foreach (var line in lines)
                            {
                                string trimmed = line.Trim();
                                if (string.IsNullOrEmpty(trimmed)) continue;

                                if (trimmed.StartsWith("-") && currentKey == "tags")
                                {
                                    string tagVal = trimmed.Substring(1).Trim().Trim('"').ToLowerInvariant();
                                    if (!tags.Contains(tagVal)) tags.Add(tagVal);
                                    continue;
                                }

                                int colon = trimmed.IndexOf(':');
                                if (colon > 0)
                                {
                                    string key = trimmed.Substring(0, colon).Trim().ToLowerInvariant();
                                    string val = trimmed.Substring(colon + 1).Trim().Trim('"');
                                    currentKey = key;

                                    if (key == "title" && !string.IsNullOrEmpty(val))
                                    {
                                        title = val;
                                    }
                                    else if (key == "category" && !string.IsNullOrEmpty(val))
                                    {
                                        category = val;
                                    }
                                    else if (key == "mood" && !string.IsNullOrEmpty(val))
                                    {
                                        mood = val;
                                    }
                                    else if (key == "date")
                                    {
                                        if (DateTime.TryParse(val, out DateTime parsedDate))
                                        {
                                            dateCreated = parsedDate;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                var existingCategory = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
                if (existingCategory == null)
                {
                    JournalManager.Instance.AddCategory(category, "\uE889", "#808080");
                }

                var note = JournalManager.Instance.CreateNote(category);
                note.Title = title;
                note.DateCreated = dateCreated;
                note.DateModified = DateTime.Now;
                note.Mood = mood;

                if (!string.IsNullOrEmpty(plainText))
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(plainText, @"\B#([a-zA-Z0-9_]+)");
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        string tag = match.Groups[1].Value.ToLowerInvariant();
                        if (!tags.Contains(tag)) tags.Add(tag);
                    }
                }
                note.Tags = tags;

                string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(note.RtfFileName);
                if (extension == ".rtf")
                {
                    await File.WriteAllTextAsync(rtfPath, rtfContent);
                }
                else
                {
                    string escapedText = plainText.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
                    escapedText = escapedText.Replace("\r\n", "\\line\n").Replace("\n", "\\line\n");
                    string rtfEnvelope = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Segoe UI;}}\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs28 " + escapedText + "}";
                    
                    byte[] rtfBytes = System.Text.Encoding.ASCII.GetBytes(rtfEnvelope);
                    if (_lockedCategories.Contains(note.Category) && !string.IsNullOrEmpty(_masterPassword))
                    {
                        rtfBytes = EncryptionHelper.Encrypt(rtfBytes, _masterPassword);
                    }
                    await File.WriteAllBytesAsync(rtfPath, rtfBytes);
                }

                _rtfTextCache.Remove(note.Id);
                note.Snippet = string.IsNullOrWhiteSpace(plainText) ? "No additional text" :
                    (plainText.Length > 80 ? plainText.Substring(0, 80).Replace("\r", " ").Replace("\n", " ").Trim() : plainText.Replace("\r", " ").Replace("\n", " ").Trim());

                JournalManager.Instance.SaveNotesMetadata();
                
                LoadCategoriesList();
                RefreshNotesList();
                NotesListView.SelectedItem = note;
                SelectedNote = note;

                ShowStatusMessage("Note imported successfully");
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Import Failed", $"Failed to import note: {ex.Message}");
            }
        }

        private void PopulateLockedCategoriesSettings()
        {
            if (LockedCategoriesStackPanel == null) return;
            LockedCategoriesStackPanel.Children.Clear();

            var categories = JournalManager.Instance.Categories;
            foreach (var cat in categories)
            {
                if (cat.Name == "All Entries" || cat.Name == "Favorites" || cat.Name == "Trash")
                    continue;

                var cb = new CheckBox
                {
                    Content = cat.Name,
                    IsChecked = _lockedCategories.Contains(cat.Name),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                cb.Checked += (s, e) =>
                {
                    if (!_lockedCategories.Contains(cat.Name))
                    {
                        _lockedCategories.Add(cat.Name);
                        UpdateSaveSettingsButtonState();
                    }
                };
                cb.Unchecked += (s, e) =>
                {
                    if (_lockedCategories.Contains(cat.Name))
                    {
                        _lockedCategories.Remove(cat.Name);
                        UpdateSaveSettingsButtonState();
                    }
                };

                LockedCategoriesStackPanel.Children.Add(cb);
            }
        }

        private void MasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateSaveSettingsButtonState();
        }

        public async Task OpenUriWithBrowserSelectionAsync(Uri uri)
        {
            if (uri == null) return;
            string url = uri.AbsoluteUri;

            var dialog = new ContentDialog
            {
                Title = "Open Link",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 12, Padding = new Thickness(0, 10, 0, 10) };
            stack.Children.Add(new TextBlock { Text = $"Choose how you want to open this link:\n{url}", FontSize = 12, TextWrapping = TextWrapping.Wrap });

            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add(new ComboBoxItem { Content = "Default System Browser", Tag = "Default" });
            combo.Items.Add(new ComboBoxItem { Content = "Google Chrome", Tag = "Chrome" });
            combo.Items.Add(new ComboBoxItem { Content = "Microsoft Edge", Tag = "Edge" });
            combo.Items.Add(new ComboBoxItem { Content = "Mozilla Firefox", Tag = "Firefox" });
            combo.Items.Add(new ComboBoxItem { Content = "Opera", Tag = "Opera" });
            combo.Items.Add(new ComboBoxItem { Content = "Built-in Browser", Tag = "BuiltIn" });
            combo.SelectedIndex = 0;
            stack.Children.Add(combo);

            dialog.Content = stack;
            dialog.PrimaryButtonText = "Open";
            
            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                var selected = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                if (selected == "BuiltIn")
                {
                    await OpenInBuiltInBrowserAsync(uri);
                }
                else if (selected == "Chrome")
                {
                    await LaunchBrowserProcessAsync("chrome.exe", url, uri);
                }
                else if (selected == "Edge")
                {
                    await LaunchBrowserProcessAsync("msedge.exe", url, uri);
                }
                else if (selected == "Firefox")
                {
                    await LaunchBrowserProcessAsync("firefox.exe", url, uri);
                }
                else if (selected == "Opera")
                {
                    await LaunchBrowserProcessAsync("opera.exe", url, uri);
                }
                else
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            }
        }

        private async Task LaunchBrowserProcessAsync(string exe, string url, Uri fallbackUri)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                await Windows.System.Launcher.LaunchUriAsync(fallbackUri);
            }
        }

        private async Task OpenInBuiltInBrowserAsync(Uri uri)
        {
            try
            {
                var webView = new Microsoft.UI.Xaml.Controls.WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    MinWidth = 800,
                    MinHeight = 500
                };

                var container = new Grid
                {
                    Width = 850,
                    Height = 550,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                container.Children.Add(webView);

                var browserDialog = new ContentDialog
                {
                    Title = "Built-in Browser",
                    Content = container,
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot
                };

                await webView.EnsureCoreWebView2Async();
                webView.Source = uri;

                await browserDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Error", $"Could not open built-in browser:\n{ex.Message}");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }

        private void UpdateLockNoteButtonState()
        {
            if (LockNoteButton == null || SelectedNote == null) return;
            if (SelectedNote.IsLocked)
            {
                LockNoteIcon.Glyph = "\uE72E";
                LockNoteText.Text = "Locked";
                ToolTipService.SetToolTip(LockNoteButton, "This note is secure. Click to unlock/remove protection.");
            }
            else
            {
                LockNoteIcon.Glyph = "\uE785";
                LockNoteText.Text = "Unlocked";
                ToolTipService.SetToolTip(LockNoteButton, "This note is unsecured. Click to secure with Windows Hello.");
            }
        }

        private async void LockNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            if (SelectedNote.IsLocked)
            {
                bool verified = false;
                try
                {
                    var verResult = await Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync("Remove lock from this entry");
                    if (verResult == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified)
                    {
                        verified = true;
                    }
                }
                catch
                {
                    if (!string.IsNullOrEmpty(_masterPassword))
                    {
                        string pwdInput = await PromptForPasswordInputAsync("Locked Entry", "Enter master password to unlock:");
                        if (pwdInput == _masterPassword)
                        {
                            verified = true;
                        }
                        else
                        {
                            await ShowAlertAsync("Incorrect Password", "Master password verification failed.");
                        }
                    }
                    else
                    {
                        verified = true;
                    }
                }

                if (!verified) return;

                SelectedNote.IsLocked = false;
                await ShowAlertAsync("Note Unlocked", "Protection has been successfully removed from this entry.");
            }
            else
            {
                SelectedNote.IsLocked = true;
                await ShowAlertAsync("Note Locked", "This entry is now secured. Windows Hello verification will be required to view it next time.");
            }

            UpdateLockNoteButtonState();
            JournalManager.Instance.SaveNotesMetadata();
        }

        private async Task<string> PromptForPasswordInputAsync(string title, string instruction)
        {
            await _dialogSemaphore.WaitAsync();
            try
            {
                var pwdBox = new PasswordBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                var stack = new StackPanel { Spacing = 8 };
                stack.Children.Add(new TextBlock { Text = instruction, FontSize = 13 });
                stack.Children.Add(pwdBox);

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = stack,
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                var res = await dialog.ShowAsync();
                return res == ContentDialogResult.Primary ? pwdBox.Password : null;
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }
    }
}
