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
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
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


    public sealed partial class MainPage : Page
    {
        public static MainPage Instance { get; private set; }
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly HttpClient _unsplashHttpClient = new HttpClient();

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
                catch {}
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
                catch {}
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
                catch {}
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
                        if (StatusMessageTextBlock != null)
                        {
                            StatusMessageTextBlock.Text = "All changes saved locally";
                        }

                        _isDirty = false;
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            _isDirty = false;
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
        private bool _isDirty = false;
        private bool _isPageInitialized = false;
        private object _previousSelectedItem;
        private bool _isUpdatingEffectsUI = false;

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
                catch {}
                throw;
            }

            _isPageInitialized = true;
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.0) };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

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
                LoadCategoriesList();
                RefreshNotesList();
                LoadSavedGitHubSettings();
                LoadSavedFonts();
                
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
            else
            {
                query = query.Where(n => !n.IsDeleted);
                if (_selectedCategory != "All Entries")
                {
                    query = query.Where(n => n.Category == _selectedCategory);
                }
            }

            // Apply search query filter
            string search = MainWindow.Instance?.SearchText?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(n => n.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                         n.Snippet.Contains(search, StringComparison.OrdinalIgnoreCase));
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
                if (catInfo != null)
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
        }

        private void LoadNoteContent()
        {
            if (SelectedNote == null) return;

            try
            {
                string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(SelectedNote.RtfFileName);
                if (File.Exists(rtfPath))
                {
                    using (var stream = File.OpenRead(rtfPath))
                    {
                        var winrtStream = stream.AsRandomAccessStream();
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

                        NoteRichEditBox.Document.Selection.SetRange(start, start + length);
                    }
                }
                else
                {
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load RTF content: {ex.Message}");
                NoteRichEditBox.Document.SetText(TextSetOptions.None, "");
            }
        }

        private void SaveCurrentNoteContent()
        {
            if (SelectedNote == null) return;

            try
            {
                // Save rich text file
                string rtfPath = JournalManager.Instance.GetAbsoluteRtfPath(SelectedNote.RtfFileName);
                using (var stream = File.Create(rtfPath))
                {
                    var winrtStream = stream.AsRandomAccessStream();
                    NoteRichEditBox.Document.SaveToStream(TextGetOptions.FormatRtf, winrtStream);
                }

                // Update title & snippet & modified date
                string plainText;
                NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out plainText);
                
                SelectedNote.Snippet = string.IsNullOrWhiteSpace(plainText) ? "No additional text" :
                    (plainText.Length > 80 ? plainText.Substring(0, 80).Replace("\r", " ").Replace("\n", " ").Trim() : plainText.Replace("\r", " ").Replace("\n", " ").Trim());

                SelectedNote.Title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "Untitled Note" : TitleTextBox.Text.Trim();
                SelectedNote.DateModified = DateTime.Now;

                JournalManager.Instance.SaveNotesMetadata();
                
                // Refresh list visually (but don't reset selection to avoid focus jumping)
                var currentSelection = SelectedNote;
                RefreshNotesList();
                SelectedNote = currentSelection;
                
                if (StatusMessageTextBlock != null)
                {
                    StatusMessageTextBlock.Text = $"Saved at {DateTime.Now.ToString("h:mm:ss tt")}";
                }
                _isDirty = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving note: {ex.Message}");
            }
        }

        private void MarkDirty()
        {
            if (!_isDataLoaded) return;
            _isDirty = true;
            if (StatusMessageTextBlock != null)
            {
                StatusMessageTextBlock.Text = "Saving...";
            }
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
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
        private void CategoriesNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
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
                // Show Main Editor Layout
                if (MainEditorGrid != null) MainEditorGrid.Visibility = Visibility.Visible;
                if (SettingsGrid != null) SettingsGrid.Visibility = Visibility.Collapsed;

                _selectedCategory = category.Name;
                if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = category.Name;
                RefreshNotesList();
            }
            else if (navItem != null)
            {
                if (navItem == TrashNavItem)
                {
                    if (MainEditorGrid != null) MainEditorGrid.Visibility = Visibility.Visible;
                    if (SettingsGrid != null) SettingsGrid.Visibility = Visibility.Collapsed;

                    _selectedCategory = "Trash";
                    if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = "Trash";
                    RefreshNotesList();
                }
                else if (navItem == FavoritesNavItem)
                {
                    if (MainEditorGrid != null) MainEditorGrid.Visibility = Visibility.Visible;
                    if (SettingsGrid != null) SettingsGrid.Visibility = Visibility.Collapsed;

                    _selectedCategory = "Favorites";
                    if (SelectedCategoryTitle != null) SelectedCategoryTitle.Text = "Favorites";
                    RefreshNotesList();
                }
                else
                {
                    // Hide Main Editor Layout
                    if (MainEditorGrid != null) MainEditorGrid.Visibility = Visibility.Collapsed;

                    if (navItem == SettingsNavItem && SettingsGrid != null)
                    {
                        SettingsGrid.Visibility = Visibility.Visible;
                        TriggerUpdateCheck();
                    }
                }
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
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                if (SelectedNote != null)
                {
                    SelectedNote = null;
                    if (NotesListView != null)
                    {
                        NotesListView.SelectedItem = null;
                    }
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

        private void ShowDeleteConfirmationFlyout(FrameworkElement? senderElement, JournalNote note, bool permanentlyDelete)
        {
            if (note == null || senderElement == null) return;

            var flyout = new Flyout();
            
            var stackPanel = new StackPanel { Spacing = 12, MaxWidth = 280 };
            
            var textBlock = new TextBlock
            {
                Text = permanentlyDelete 
                    ? $"Are you sure you want to permanently delete \"{note.Title}\"? This action cannot be undone."
                    : $"Are you sure you want to move \"{note.Title}\" to the Trash?",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap
            };
            
            var buttonsPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8 
            };

            var cancelButton = new Button
            {
                Content = "Cancel"
            };
            cancelButton.Click += (s, args) => flyout.Hide();

            var confirmButton = new Button
            {
                Content = permanentlyDelete ? "Delete Permanently" : "Move to Trash"
            };

            if (permanentlyDelete)
            {
                confirmButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            else
            {
                confirmButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            }

            confirmButton.Click += (s, args) =>
            {
                flyout.Hide();
                
                if (SelectedNote == note)
                {
                    SelectedNote = null; // Unselect first
                }
                
                if (permanentlyDelete)
                {
                    JournalManager.Instance.PermanentlyDeleteNote(note);
                    ShowStatusMessage("Journal entry permanently deleted");
                }
                else
                {
                    JournalManager.Instance.SoftDeleteNote(note);
                    ShowStatusMessage("Journal entry moved to Trash");
                }
                RefreshNotesList();
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(confirmButton);

            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(buttonsPanel);
            
            flyout.Content = stackPanel;

            // Determine the best anchor element
            FrameworkElement anchor = senderElement;
            if (senderElement is MenuFlyoutItem)
            {
                anchor = NotesListView.ContainerFromItem(note) as FrameworkElement ?? NotesListView;
            }

            flyout.ShowAt(anchor);
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

        // Formatting Command Buttons
        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            format.Bold = (format.Bold == FormatEffect.On) ? FormatEffect.Off : FormatEffect.On;
            NoteRichEditBox.Document.Selection.CharacterFormat = format;
            MarkDirty();
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            format.Italic = (format.Italic == FormatEffect.On) ? FormatEffect.Off : FormatEffect.On;
            NoteRichEditBox.Document.Selection.CharacterFormat = format;
            MarkDirty();
        }

        private void UnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.CharacterFormat;
            format.Underline = (format.Underline == UnderlineType.Single) ? UnderlineType.None : UnderlineType.Single;
            NoteRichEditBox.Document.Selection.CharacterFormat = format;
            MarkDirty();
        }

        private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Left;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        private void AlignCenterButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Center;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        private void AlignRightButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Right;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        private void AlignJustifyButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.Alignment = ParagraphAlignment.Justify;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
        }

        private void BulletListButton_Click(object sender, RoutedEventArgs e)
        {
            var format = NoteRichEditBox.Document.Selection.ParagraphFormat;
            format.ListType = (format.ListType == MarkerType.Bullet) ? MarkerType.None : MarkerType.Bullet;
            NoteRichEditBox.Document.Selection.ParagraphFormat = format;
            MarkDirty();
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

        // Hero Image Management
        private void UpdateHeroImageUI()
        {
            if (SelectedNote == null)
            {
                HeroImageContainer.Visibility = Visibility.Collapsed;
                HeroImage.Source = null;
                if (AddCoverButton != null) AddCoverButton.Visibility = Visibility.Collapsed;
                if (CoverAttributionBorder != null) CoverAttributionBorder.Visibility = Visibility.Collapsed;
                FadeOutTintOverlays();
            }
            else if (SelectedNote.HeroImagePath == "None")
            {
                HeroImageContainer.Visibility = Visibility.Collapsed;
                HeroImage.Source = null;
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
                    }
                    catch
                    {
                        HeroImageContainer.Visibility = Visibility.Collapsed;
                        HeroImage.Source = null;
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
                    }
                    catch
                    {
                        // Fallback to Picsum
                        HeroImageContainer.Visibility = Visibility.Visible;
                        string fallbackUrl = $"https://picsum.photos/seed/{SelectedNote.Id}/1200/600";
                        imagePath = ApplyBlurToImageUrl(fallbackUrl, SelectedNote.CoverBlur);
                        HeroImage.Source = new BitmapImage(new Uri(imagePath));
                    }
                }

                // Apply cover effects (Offset, Brightness, and Blur Sliders) to UI elements
                _isUpdatingEffectsUI = true;
                try
                {
                    if (HeroImageTranslate != null) HeroImageTranslate.Y = SelectedNote.CoverOffsetY;
                    if (CoverBrightnessOverlay != null) CoverBrightnessOverlay.Opacity = (100 - SelectedNote.CoverBrightness) / 100.0;
                    
                    if (CoverOffsetYSlider != null) CoverOffsetYSlider.Value = SelectedNote.CoverOffsetY;
                    if (CoverBrightnessSlider != null) CoverBrightnessSlider.Value = SelectedNote.CoverBrightness;
                    if (CoverBlurSlider != null) CoverBlurSlider.Value = SelectedNote.CoverBlur;
                    
                    if (OffsetYValueText != null) OffsetYValueText.Text = $"{(int)SelectedNote.CoverOffsetY}px";
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
        private void CoverOffsetYSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingEffectsUI || SelectedNote == null) return;
            SelectedNote.CoverOffsetY = e.NewValue;
            if (HeroImageTranslate != null) HeroImageTranslate.Y = e.NewValue;
            if (OffsetYValueText != null) OffsetYValueText.Text = $"{(int)e.NewValue}px";
            MarkDirty();
        }

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
            if (CoverOffsetYSlider != null) CoverOffsetYSlider.Value = 0;
            if (CoverBrightnessSlider != null) CoverBrightnessSlider.Value = 100;
            if (CoverBlurSlider != null) CoverBlurSlider.Value = 0;
            
            // Apply immediately to the note
            SelectedNote.CoverOffsetY = 0;
            SelectedNote.CoverBrightness = 100;
            SelectedNote.CoverBlur = 0;
            
            // Update UI elements directly to ensure instant visual update
            if (HeroImageTranslate != null) HeroImageTranslate.Y = 0;
            if (OffsetYValueText != null) OffsetYValueText.Text = "0px";
            if (CoverBrightnessOverlay != null) CoverBrightnessOverlay.Opacity = 0;
            if (BrightnessValueText != null) BrightnessValueText.Text = "100%";
            if (BlurValueText != null) BlurValueText.Text = "0%";
            
            UpdateHeroImageUI();
            MarkDirty();
            
            ShowStatusMessage("Cover adjustments reset to default");
        }

        // Photographer attribution is now handled natively via Hyperlink NavigateUri in XAML.

        // Unsplash credentials settings handlers
        private void SaveUnsplashKeyButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSetting("UnsplashAccessKey", UnsplashTokenPasswordBox.Password?.Trim());
        }

        private void UnsplashTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // Toggle button label: "Get Key" when empty, "Save Key" when a key is present
            if (UnsplashKeyButton != null)
            {
                bool hasKey = !string.IsNullOrWhiteSpace(UnsplashTokenPasswordBox.Password);
                UnsplashKeyButton.Content = hasKey ? "Save Key" : "Get Key";
            }
            UpdateSaveSettingsButtonState();
        }

        private async void GetUnsplashKeyButton_Click(object sender, RoutedEventArgs e)
        {
            // If a key is already entered, "Save Key" — just confirm it's saved (autosave already did it)
            if (!string.IsNullOrWhiteSpace(UnsplashTokenPasswordBox?.Password))
            {
                if (UnsplashKeyButton != null)
                {
                    UnsplashKeyButton.Content = "Saved ✓";
                    UnsplashKeyButton.IsEnabled = false;
                    await Task.Delay(1500);
                    UnsplashKeyButton.Content = "Save Key";
                    UnsplashKeyButton.IsEnabled = true;
                }
                return;
            }
            // No key yet — open Unsplash developer page to get one
            try
            {
                var uri = new Uri("https://unsplash.com/developers");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            catch {}
        }

        // Unsplash search popups and API querying handlers
        private async void SearchUnsplashHeroImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;
            
            if (UnsplashSearchDialog != null)
            {
                UnsplashSearchTextBox.Text = "";
                UnsplashResultsGridView.ItemsSource = null;
                UnsplashProgressRing.IsActive = false;
                UnsplashProgressRing.Visibility = Visibility.Collapsed;
                if (UnsplashErrorTextBlock != null)
                {
                    UnsplashErrorTextBlock.Text = "";
                    UnsplashErrorTextBlock.Visibility = Visibility.Collapsed;
                }
                
                UnsplashSearchDialog.XamlRoot = this.XamlRoot;
                await UnsplashSearchDialog.ShowAsync();
            }
        }

        private void UnsplashSearchButton_Click(object sender, RoutedEventArgs e)
        {
            PerformUnsplashSearch();
        }

        private void UnsplashSearchTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                PerformUnsplashSearch();
                e.Handled = true;
            }
        }

        private async void PerformUnsplashSearch()
        {
            string query = UnsplashSearchTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(query)) return;

            UnsplashProgressRing.Visibility = Visibility.Visible;
            UnsplashProgressRing.IsActive = true;
            UnsplashResultsGridView.ItemsSource = null;
            if (UnsplashErrorTextBlock != null)
            {
                UnsplashErrorTextBlock.Text = "";
                UnsplashErrorTextBlock.Visibility = Visibility.Collapsed;
            }

            try
            {
                string apiKey = UnsplashTokenPasswordBox?.Password?.Trim();
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = GetSetting("UnsplashAccessKey");
                }
                
                // Fallback to a demo developer key
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = "DFCwWNM7UGoROD84mWytKM5lZdNbAhulz6_lYPJui7g"; 
                }

                // Official Search Endpoint: GET /search/photos
                string url = $"https://api.unsplash.com/search/photos?query={Uri.EscapeDataString(query)}&per_page=30";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                // Authentication via header: Authorization: Client-ID YOUR_ACCESS_KEY
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Client-ID", apiKey);
                // Versioning via header: Accept-Version: v1
                request.Headers.Add("Accept-Version", "v1");
                request.Headers.UserAgent.TryParseAdd("JournalApp");
                
                var response = await _unsplashHttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("results", out var resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<UnsplashResult>();
                            foreach (var item in resultsArray.EnumerateArray())
                            {
                                var id = item.GetProperty("id").GetString();
                                var urls = item.GetProperty("urls");
                                var thumbUrl = urls.GetProperty("small").GetString();
                                var fullUrl = urls.GetProperty("regular").GetString();
                                
                                var user = item.GetProperty("user");
                                var photographer = user.GetProperty("name").GetString();
                                var photographerUsername = user.GetProperty("username").GetString();
                                var photographerUrl = $"https://unsplash.com/@{photographerUsername}?utm_source=JournalApp&utm_medium=referral";
                                
                                // Retrieve the download tracking endpoint as mandated by Unsplash API guidelines
                                string downloadTrackUrl = null;
                                if (item.TryGetProperty("links", out var linksElement))
                                {
                                    if (linksElement.TryGetProperty("download_location", out var dlElement))
                                    {
                                        downloadTrackUrl = dlElement.GetString();
                                    }
                                    else if (linksElement.TryGetProperty("download", out var dElement))
                                    {
                                        downloadTrackUrl = dElement.GetString();
                                    }
                                }
                                if (string.IsNullOrEmpty(downloadTrackUrl))
                                {
                                    downloadTrackUrl = $"https://api.unsplash.com/photos/{id}/download";
                                }

                                list.Add(new UnsplashResult
                                {
                                    Id = id,
                                    ThumbUrl = thumbUrl,
                                    FullUrl = fullUrl,
                                    Photographer = photographer,
                                    PhotographerUrl = photographerUrl,
                                    DownloadTrackUrl = downloadTrackUrl
                                });
                            }
                            UnsplashResultsGridView.ItemsSource = list;
                        }
                    }
                }
                else
                {
                    if (UnsplashErrorTextBlock != null)
                    {
                        UnsplashErrorTextBlock.Text = "Could not complete search. Please verify your Unsplash Access Key in Settings.";
                        UnsplashErrorTextBlock.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                if (UnsplashErrorTextBlock != null)
                {
                    UnsplashErrorTextBlock.Text = $"Search failed: {ex.Message}";
                    UnsplashErrorTextBlock.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                UnsplashProgressRing.IsActive = false;
                UnsplashProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        // Asynchronously reports a photo download event to Unsplash to comply with API guidelines
        private async Task TrackUnsplashDownloadAsync(string photoId, string trackUrl, string apiKey)
        {
            if (string.IsNullOrEmpty(trackUrl)) return;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, trackUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Client-ID", apiKey);
                request.Headers.Add("Accept-Version", "v1");
                request.Headers.UserAgent.TryParseAdd("JournalApp");

                var response = await _unsplashHttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Unsplash download tracking failed with status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error tracking Unsplash download: {ex.Message}");
            }
        }

        private async void UnsplashResultsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is UnsplashResult result && SelectedNote != null)
            {
                if (UnsplashSearchDialog != null)
                {
                    UnsplashSearchDialog.Hide();
                }

                ShowLoadingRing(true);
                ShowStatusMessage("Downloading Unsplash banner...");

                // Retrieve api key for download tracking
                string apiKey = UnsplashTokenPasswordBox?.Password?.Trim();
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = GetSetting("UnsplashAccessKey");
                }
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = "L-m_xJkH8Z5_2Wv-34e8H9hD3y8U-48hU438e83u"; 
                }

                // Trigger download event in the background as required by Unsplash API guidelines
                _ = TrackUnsplashDownloadAsync(result.Id, result.DownloadTrackUrl, apiKey);

                try
                {
                    string localName = await DownloadImageToLocalMediaAsync(result.FullUrl);
                    if (!string.IsNullOrEmpty(localName))
                    {
                        SelectedNote.HeroImagePath = localName;
                        SelectedNote.CoverAttributionText = result.Photographer;
                        SelectedNote.CoverAttributionUrl = result.PhotographerUrl;
                        
                        SelectedNote.CoverOffsetY = 0;
                        SelectedNote.CoverBrightness = 100;
                        SelectedNote.CoverBlur = 0;

                        JournalManager.Instance.SaveNotesMetadata();
                        UpdateHeroImageUI();
                        RefreshNotesList();
                        ShowStatusMessage("Unsplash banner applied successfully");
                    }
                    else
                    {
                        await ShowAlertAsync("Error", "Could not download the selected cover banner.");
                    }
                }
                catch (Exception ex)
                {
                    await ShowAlertAsync("Error", $"Failed to download banner: {ex.Message}");
                }
                finally
                {
                    ShowLoadingRing(false);
                }
            }
        }

        private async void SetLocalHeroImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            var picker = CreatePicker();
            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string localName = JournalManager.Instance.CopyImageToLocalMedia(file.Path);
                if (!string.IsNullOrEmpty(localName))
                {
                    SelectedNote.HeroImagePath = localName;
                    JournalManager.Instance.SaveNotesMetadata();
                    UpdateHeroImageUI();
                    RefreshNotesList();
                }
            }
        }

        private async void SetUrlHeroImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote == null) return;

            string url = await PromptForTextInputAsync("Set Hero Image", "Enter the cover image URL:", "https://images.unsplash.com/photo-1507842217343-583bb7270b66");
            if (string.IsNullOrEmpty(url)) return;

            ShowLoadingRing(true);
            string localName = await DownloadImageToLocalMediaAsync(url);
            ShowLoadingRing(false);

            if (!string.IsNullOrEmpty(localName))
            {
                SelectedNote.HeroImagePath = localName;
                JournalManager.Instance.SaveNotesMetadata();
                UpdateHeroImageUI();
                RefreshNotesList();
            }
            else
            {
                await ShowAlertAsync("Error", "Could not download image banner. Please verify the URL.");
            }
        }

        private void ResetHeroImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote != null)
            {
                SelectedNote.HeroImagePath = null; // Reverts to Picsum fallback
                JournalManager.Instance.SaveNotesMetadata();
                UpdateHeroImageUI();
                RefreshNotesList();
                ShowStatusMessage("Cover banner reset to default");
            }
        }

        private void RemoveHeroImage_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedNote != null)
            {
                SelectedNote.HeroImagePath = "None"; // Hides the banner
                JournalManager.Instance.SaveNotesMetadata();
                UpdateHeroImageUI();
                RefreshNotesList();
                ShowStatusMessage("Cover banner hidden");
            }
        }

        private void UpdateEditorHeaderUI()
        {
            if (SelectedNote == null) return;

            // Set Date Text
            DateDayTextBlock.Text = SelectedNote.DateCreated.ToString("dd");
            DateMonthYearTextBlock.Text = SelectedNote.DateCreated.ToString("MMMM yyyy");
            if (SelectedNote.HasTime)
            {
                DateTimeTextBlock.Text = SelectedNote.DateCreated.ToString("ddd, h:mm tt");
            }
            else
            {
                DateTimeTextBlock.Text = SelectedNote.DateCreated.ToString("dddd");
            }

            // Update editor toolbar actions based on Trash state
            if (SelectedNote.IsDeleted)
            {
                FavoriteToggle.Visibility = Visibility.Collapsed;
                MoveCategoryButton.Visibility = Visibility.Collapsed;
                DeleteNoteButton.Visibility = Visibility.Collapsed;
                RestoreNoteButton.Visibility = Visibility.Visible;
                PermanentlyDeleteNoteButton.Visibility = Visibility.Visible;
            }
            else
            {
                FavoriteToggle.Visibility = Visibility.Visible;
                MoveCategoryButton.Visibility = Visibility.Visible;
                DeleteNoteButton.Visibility = Visibility.Visible;
                RestoreNoteButton.Visibility = Visibility.Collapsed;
                PermanentlyDeleteNoteButton.Visibility = Visibility.Collapsed;
            }

            // Look up Category Details
            var category = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name == SelectedNote.Category);
            if (category != null)
            {
                CategoryBadgeIcon.Glyph = category.Icon;
                CategoryBadgeIcon.Foreground = GetBrushFromHex(category.Color);
                CategoryBadgeText.Text = category.Name;
            }
            else
            {
                CategoryBadgeIcon.Glyph = "\uE8B7"; // Default folder icon
                CategoryBadgeIcon.Foreground = GetBrushFromHex("#8A8886"); // Default gray color
                CategoryBadgeText.Text = SelectedNote.Category;
            }
        }

        private Microsoft.UI.Xaml.Media.Brush GetBrushFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            try
            {
                if (hex.StartsWith("#"))
                    hex = hex.Substring(1);

                byte a = 255;
                byte r = 0, g = 0, b = 0;

                if (hex.Length == 6)
                {
                    r = Convert.ToByte(hex.Substring(0, 2), 16);
                    g = Convert.ToByte(hex.Substring(2, 2), 16);
                    b = Convert.ToByte(hex.Substring(4, 2), 16);
                }
                else if (hex.Length == 8)
                {
                    a = Convert.ToByte(hex.Substring(0, 2), 16);
                    r = Convert.ToByte(hex.Substring(2, 2), 16);
                    g = Convert.ToByte(hex.Substring(4, 2), 16);
                    b = Convert.ToByte(hex.Substring(6, 2), 16);
                }
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
            }
            catch
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
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
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
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
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
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

        private void UpdateWordCount()
        {
            if (SelectedNote == null)
            {
                if (WordCountTextBlock != null)
                {
                    WordCountTextBlock.Text = "Word Count: 0 | Characters: 0";
                }
                return;
            }

            try
            {
                string text;
                NoteRichEditBox.Document.GetText(TextGetOptions.UseLf, out text);
                if (text == null) text = "";

                if (text.EndsWith("\r"))
                {
                    text = text.Substring(0, text.Length - 1);
                }

                int charCount = text.Length;
                var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int wordCount = words.Length;

                if (WordCountTextBlock != null)
                {
                    WordCountTextBlock.Text = $"Word Count: {wordCount} | Characters: {charCount}";
                }
            }
            catch
            {
                if (WordCountTextBlock != null)
                {
                    WordCountTextBlock.Text = "Word Count: 0 | Characters: 0";
                }
            }
        }

        private void PinListButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is JournalNote note)
            {
                note.IsPinned = !note.IsPinned;
                JournalManager.Instance.SaveNotesMetadata();
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
                var window = MainWindow.Instance;
                if (window != null && window.Content is FrameworkElement rootElement)
                {
                    if (themeTag == "Light")
                        rootElement.RequestedTheme = ElementTheme.Light;
                    else if (themeTag == "Dark")
                        rootElement.RequestedTheme = ElementTheme.Dark;
                    else
                        rootElement.RequestedTheme = ElementTheme.Default;
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
                border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
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
                border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
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
                    
                    ShowStatusMessage($"Editor default font changed to {fontName}");
                }
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

                // 2. Load Editor Font
                string editorFont = GetSetting("EditorFontFamily");
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

        private async Task DownloadAndInstallUpdate(string downloadUrl, string assetName)
        {
            if (UpdateStatusTextBlock != null)
                UpdateStatusTextBlock.Text = "Downloading update...";

            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), assetName);
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
                
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                using (var downloadResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    downloadResponse.EnsureSuccessStatusCode();
                    var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1L;
                    
                    using (var contentStream = await downloadResponse.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            
                            if (totalBytes > 0 && UpdateStatusTextBlock != null)
                            {
                                int pct = (int)((double)totalRead / totalBytes * 100);
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    UpdateStatusTextBlock.Text = $"Downloading: {pct}%";
                                });
                            }
                        }
                    }
                }

                if (UpdateStatusTextBlock != null)
                    UpdateStatusTextBlock.Text = "Launching installer...";

                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempPath);
                await Windows.System.Launcher.LaunchFileAsync(file);

                await Task.Delay(1000);
                Microsoft.UI.Xaml.Application.Current.Exit();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Update Failed", $"Could not download or launch update: {ex.Message}");
            }
        }

        // Trigger update checking
        private async void TriggerUpdateCheck()
        {
            if (UpdateStatusTextBlock != null)
                UpdateStatusTextBlock.Text = "Checking for updates...";
            if (LastCheckedTextBlock != null)
                LastCheckedTextBlock.Text = "Connecting to GitHub...";

            // Show spinner in button
            if (CheckUpdatesButton != null)
            {
                CheckUpdatesButton.IsEnabled = false;
                CheckUpdatesButton.Content = new Microsoft.UI.Xaml.Controls.StackPanel
                {
                    Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Microsoft.UI.Xaml.Controls.ProgressRing { IsActive = true, Width = 16, Height = 16, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center },
                        new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Checking...", VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center }
                    }
                };
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ShadowAmitendu/JournalApp/releases/latest");
                request.Headers.UserAgent.TryParseAdd("JournalApp");
                
                string savedToken = GetSetting("GitHubToken");
                if (!string.IsNullOrEmpty(savedToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", savedToken);
                }

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        if (UpdateStatusTextBlock != null)
                            UpdateStatusTextBlock.Text = "No releases found";
                        if (LastCheckedTextBlock != null)
                            LastCheckedTextBlock.Text = "Create a release on GitHub first to enable updates.";
                        return;
                    }
                    throw new Exception($"GitHub API returned: {response.ReasonPhrase} ({response.StatusCode})");
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latestTag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "1.0.0" : "1.0.0";
                string changelog = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                string releaseHtmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";

                // Find MSIX/Appx asset
                string downloadUrl = "";
                string assetName = "";
                if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsEl.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || 
                            name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            assetName = name;
                            break;
                        }
                    }
                }

                var currentVersion = GetAppVersion();
                var cleanLatest = NormalizeVersionString(latestTag);
                var cleanCurrent = NormalizeVersionString(currentVersion);

                bool hasNewUpdate = false;
                if (Version.TryParse(cleanLatest, out Version? remote) && Version.TryParse(cleanCurrent, out Version? local))
                {
                    if (remote > local)
                    {
                        hasNewUpdate = true;
                    }
                }

                if (hasNewUpdate)
                {
                    if (UpdateStatusTextBlock != null)
                        UpdateStatusTextBlock.Text = $"Update available: {latestTag}";
                    if (LastCheckedTextBlock != null)
                        LastCheckedTextBlock.Text = $"Current version: {currentVersion}";

                    var dialog = new ContentDialog
                    {
                        Title = "Update Available",
                        Content = new ScrollViewer
                        {
                            MaxHeight = 300,
                            Content = new TextBlock
                            {
                                Text = $"A new version ({latestTag}) is available. Would you like to update now?\n\nRelease Notes:\n{changelog}",
                                TextWrapping = TextWrapping.Wrap
                            }
                        },
                        PrimaryButtonText = !string.IsNullOrEmpty(downloadUrl) ? "Install Update" : "View Release",
                        CloseButtonText = "Later",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        if (string.IsNullOrEmpty(downloadUrl))
                        {
                            if (!string.IsNullOrEmpty(releaseHtmlUrl))
                            {
                                await Windows.System.Launcher.LaunchUriAsync(new Uri(releaseHtmlUrl));
                            }
                        }
                        else
                        {
                            await DownloadAndInstallUpdate(downloadUrl, assetName);
                        }
                    }
                }
                else
                {
                    if (UpdateStatusTextBlock != null)
                        UpdateStatusTextBlock.Text = "You're up to date!";
                    if (LastCheckedTextBlock != null)
                        LastCheckedTextBlock.Text = $"Last checked: {DateTime.Now:h:mm:ss tt} (Version: {currentVersion})";
                }
            }
            catch (Exception ex)
            {
                if (UpdateStatusTextBlock != null)
                    UpdateStatusTextBlock.Text = "Check failed";
                if (LastCheckedTextBlock != null)
                    LastCheckedTextBlock.Text = $"Error: {ex.Message}";
            }
            finally
            {
                if (CheckUpdatesButton != null)
                {
                    CheckUpdatesButton.Content = new Microsoft.UI.Xaml.Controls.StackPanel
                    {
                        Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Check Now", VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center }
                        }
                    };
                    CheckUpdatesButton.IsEnabled = true;
                }
            }
        }

        // Check for updates click handler
        private void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerUpdateCheck();
        }

        // Save all settings with visual confirmation
        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SaveSettingsButton == null) return;

            // Show saving state
            SaveSettingsButton.IsEnabled = false;
            if (SaveSettingsIcon != null) SaveSettingsIcon.Glyph = "\uE895"; // sync icon
            if (SaveSettingsText != null) SaveSettingsText.Text = "Saving...";

            await Task.Delay(600);

            // Persist all settings explicitly
            try
            {
                // Theme
                if (ThemeComboBox?.SelectedItem is ComboBoxItem themeItem)
                    SaveSetting("AppTheme", themeItem.Tag?.ToString());


                // Editor font
                if (EditorFontComboBox?.SelectedItem is ComboBoxItem editorFontItem)
                    SaveSetting("EditorFontFamily", editorFontItem.Tag?.ToString());

                // Auto-save
                if (AutoSaveSlider != null)
                    SaveSetting("AutoSaveIntervalSeconds", AutoSaveSlider.Value.ToString());

                // Unsplash key
                if (UnsplashTokenPasswordBox != null)
                    SaveSetting("UnsplashAccessKey", UnsplashTokenPasswordBox.Password?.Trim());

                // GitHub
                if (GitHubTokenPasswordBox != null)
                    SaveSetting("GitHubToken", GitHubTokenPasswordBox.Password?.Trim());
                if (GitHubRepoTextBox != null)
                    SaveSetting("GitHubRepo", GitHubRepoTextBox.Text?.Trim());
            }
            catch { }

            // Show success
            if (SaveSettingsIcon != null) SaveSettingsIcon.Glyph = "\uE73E"; // checkmark
            if (SaveSettingsText != null) SaveSettingsText.Text = "Saved!";

            await Task.Delay(1500);

            // Restore
            if (SaveSettingsIcon != null) SaveSettingsIcon.Glyph = "\uE74E"; // save icon
            if (SaveSettingsText != null) SaveSettingsText.Text = "Save Settings";
            UpdateSaveSettingsButtonState();
        }

        private void UpdateSaveSettingsButtonState()
        {
            if (SaveSettingsButton == null) return;

            bool isDirty = false;

            // Check Theme
            string savedTheme = GetSetting("AppTheme", "Default");
            string currentTheme = "Default";
            if (ThemeComboBox?.SelectedItem is ComboBoxItem themeItem)
            {
                currentTheme = themeItem.Tag?.ToString() ?? "Default";
            }
            if (currentTheme != savedTheme) isDirty = true;

            // Check Editor Font
            string savedFont = GetSetting("EditorFontFamily", "Segoe UI");
            string currentFont = "Segoe UI";
            if (EditorFontComboBox?.SelectedItem is ComboBoxItem fontItem)
            {
                currentFont = fontItem.Tag?.ToString() ?? "Segoe UI";
            }
            if (currentFont != savedFont) isDirty = true;

            // Check Auto-Save Interval
            string savedIntervalStr = GetSetting("AutoSaveIntervalSeconds", "5.0");
            double savedInterval = 5.0;
            double.TryParse(savedIntervalStr, out savedInterval);
            double currentInterval = AutoSaveSlider != null ? AutoSaveSlider.Value : 5.0;
            if (Math.Abs(currentInterval - savedInterval) > 0.01) isDirty = true;

            // Check Unsplash API Key
            string savedUnsplashKey = GetSetting("UnsplashAccessKey", "");
            string currentUnsplashKey = UnsplashTokenPasswordBox != null ? UnsplashTokenPasswordBox.Password?.Trim() : "";
            if (currentUnsplashKey != savedUnsplashKey) isDirty = true;

            // Check GitHub Token
            string savedGitHubToken = GetSetting("GitHubToken", "");
            string currentGitHubToken = GitHubTokenPasswordBox != null ? GitHubTokenPasswordBox.Password?.Trim() : "";
            if (currentGitHubToken != savedGitHubToken) isDirty = true;

            // Check GitHub Repo
            string savedGitHubRepo = GetSetting("GitHubRepo", "My-JournalApp-Backup");
            string currentGitHubRepo = GitHubRepoTextBox != null ? GitHubRepoTextBox.Text?.Trim() : "My-JournalApp-Backup";
            if (currentGitHubRepo != savedGitHubRepo) isDirty = true;

            SaveSettingsButton.IsEnabled = isDirty;
        }

        private void GitHubTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateSaveSettingsButtonState();
        }

        private void GitHubRepoTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSaveSettingsButtonState();
        }

        private void LoadSavedGitHubSettings()
        {
            try
            {
                string savedToken = GetSetting("GitHubToken");
                if (!string.IsNullOrEmpty(savedToken))
                {
                    GitHubTokenPasswordBox.Password = savedToken;
                    GitHubDisconnectButton.Visibility = Visibility.Visible;
                    GitHubStatusPanel.Visibility = Visibility.Visible;
                    GitHubStatusTitle.Text = "Status: Connected";
                    
                    string lastSyncStr = GetSetting("GitHubLastSynced");
                    if (!string.IsNullOrEmpty(lastSyncStr))
                    {
                        GitHubStatusDetails.Text = $"Last synced: {lastSyncStr}";
                    }
                    else
                    {
                        GitHubStatusDetails.Text = "Last synced: Never";
                    }
                }
                string savedRepo = GetSetting("GitHubRepo");
                if (!string.IsNullOrEmpty(savedRepo))
                {
                    GitHubRepoTextBox.Text = savedRepo;
                }
                string savedUnsplashKey = GetSetting("UnsplashAccessKey");
                if (!string.IsNullOrEmpty(savedUnsplashKey))
                {
                    UnsplashTokenPasswordBox.Password = savedUnsplashKey;
                    // Reflect that a key is already saved
                    if (UnsplashKeyButton != null)
                        UnsplashKeyButton.Content = "Save Key";
                }

                // Load and apply Theme on startup
                string savedTheme = GetSetting("AppTheme");
                if (!string.IsNullOrEmpty(savedTheme))
                {
                    var window = MainWindow.Instance;
                    if (window != null && window.Content is FrameworkElement rootElement)
                    {
                        if (savedTheme == "Light")
                            rootElement.RequestedTheme = ElementTheme.Light;
                        else if (savedTheme == "Dark")
                            rootElement.RequestedTheme = ElementTheme.Dark;
                        else
                            rootElement.RequestedTheme = ElementTheme.Default;
                    }

                    if (ThemeComboBox != null)
                    {
                        foreach (object itemObj in ThemeComboBox.Items)
                        {
                            if (itemObj is ComboBoxItem item && item.Tag?.ToString() == savedTheme)
                            {
                                ThemeComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }
                }

                // Load and apply Auto-Save Interval on startup
                string savedAutoSave = GetSetting("AutoSaveIntervalSeconds");
                if (!string.IsNullOrEmpty(savedAutoSave) && double.TryParse(savedAutoSave, out double interval))
                {
                    if (AutoSaveSlider != null)
                    {
                        AutoSaveSlider.Value = interval;
                    }
                    if (_autoSaveTimer != null)
                    {
                        _autoSaveTimer.Interval = TimeSpan.FromSeconds(interval);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        private async void RestoreToDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Restore to Default",
                Content = "Are you sure you want to restore all settings, categories, and entries to their default values? This will reset the app theme, auto-save settings, and clear custom data.",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                SelectedNote = null;
                
                // 1. Reset Settings UI
                if (ThemeComboBox != null) ThemeComboBox.SelectedIndex = 0; // Default
                if (AutoSaveSlider != null) AutoSaveSlider.Value = 5.0; // 5.0s
                if (UnsplashTokenPasswordBox != null) UnsplashTokenPasswordBox.Password = "";
                
                // 2. Reset GitHub sync UI and fields
                if (GitHubTokenPasswordBox != null) GitHubTokenPasswordBox.Password = "";
                if (GitHubRepoTextBox != null) GitHubRepoTextBox.Text = "My-JournalApp-Backup";
                if (GitHubStatusPanel != null) GitHubStatusPanel.Visibility = Visibility.Collapsed;
                if (GitHubDisconnectButton != null) GitHubDisconnectButton.Visibility = Visibility.Collapsed;
                
                // 3. Clear settings in ApplicationData
                RemoveSetting("GitHubToken");
                RemoveSetting("GitHubRepo");
                RemoveSetting("GitHubLastSynced");
                RemoveSetting("UnsplashAccessKey");

                // 4. Clear Notes and create a Welcome Note
                JournalManager.Instance.Notes.Clear();
                
                // Keep only default categories
                JournalManager.Instance.Categories.Clear();
                JournalManager.Instance.AddCategory("All Entries", "\uE80F", "#0078D4");
                JournalManager.Instance.AddCategory("Personal", "\uE77B", "#E3008C");
                JournalManager.Instance.AddCategory("Work", "\uE821", "#107C41");
                JournalManager.Instance.AddCategory("Ideas", "\uEA80", "#FFB900");

                // Clear RTF files on disk
                try
                {
                    foreach (var file in Directory.GetFiles(JournalManager.Instance.NotesDir, "*.rtf"))
                    {
                        File.Delete(file);
                    }
                }
                catch {}

                // Create a welcome note
                var welcomeNote = JournalManager.Instance.CreateNote("Personal");
                welcomeNote.Title = "Welcome to JournalApp!";
                welcomeNote.Snippet = "This is your new premium journaling workspace. Start writing here!";
                welcomeNote.HeroImagePath = "ms-appx:///Assets/Square150x150Logo.scale-200.png";
                JournalManager.Instance.SaveNotesMetadata();

                // Write default content into the welcome note RTF file
                try
                {
                    string welcomeRtfPath = JournalManager.Instance.GetAbsoluteRtfPath(welcomeNote.RtfFileName);
                    string defaultRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}} \viewkind4\uc1\pard\lang1033\f0\fs20 This is a premium, distraction-free digital journal designed for Windows 11.\par\par Key features include:\par\bullet  \b Real-time Auto-saving\b0\par\bullet  \b Custom Categories\b0 with icons and colors\par\bullet  \b Cloud Sync\b0 via GitHub Private Repositories\par\bullet  \b Trash Bin\b0 to restore deleted entries\par\bullet  \b Mica & Acrylic visual design\b0\par\par Happy writing!\par}";
                    File.WriteAllText(welcomeRtfPath, defaultRtf);
                }
                catch {}

                JournalManager.Instance.SaveCategories();
                
                LoadCategoriesList();
                RefreshNotesList();
                UpdateSaveSettingsButtonState();
                await ShowAlertAsync("Defaults Restored", "Application settings and default data have been successfully restored.");
            }
        }

        private async void GitHubSyncButton_Click(object sender, RoutedEventArgs e)
        {
            string token = GitHubTokenPasswordBox.Password?.Trim();
            string repoName = GitHubRepoTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(token))
            {
                await ShowAlertAsync("Authentication Required", "Please enter a valid GitHub Personal Access Token (PAT) first.");
                return;
            }
            if (string.IsNullOrEmpty(repoName))
            {
                await ShowAlertAsync("Repository Required", "Please enter a repository name for your backup.");
                return;
            }

            // Slugify repository name to match GitHub rules
            repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"\s+", "-");
            repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"[^a-zA-Z0-9\-_\.]", "").ToLowerInvariant();

            if (string.IsNullOrEmpty(repoName))
            {
                await ShowAlertAsync("Invalid Repository Name", "The repository name must contain valid alphanumeric characters, hyphens, underscores, or periods.");
                return;
            }

            // Update textbox to show cleaned name
            GitHubRepoTextBox.Text = repoName;

            // Disable buttons during sync
            GitHubSyncButton.IsEnabled = false;
            GitHubDisconnectButton.IsEnabled = false;
            GitHubStatusPanel.Visibility = Visibility.Visible;
            GitHubSyncProgressBar.Visibility = Visibility.Visible;
            GitHubSyncProgressBar.IsIndeterminate = true;
            GitHubStatusTitle.Text = "Connecting...";
            GitHubStatusDetails.Text = "Establishing connection with GitHub API...";

            try
            {
                // Set up HTTP client headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // 1. Get username
                var userResponse = await _httpClient.GetAsync("https://api.github.com/user");
                if (!userResponse.IsSuccessStatusCode)
                {
                    throw new Exception("Authentication failed. Make sure your token is valid and has 'repo' permissions.");
                }

                using var userDoc = System.Text.Json.JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
                string username = userDoc.RootElement.GetProperty("login").GetString();

                GitHubStatusTitle.Text = $"Authenticated as {username}";
                GitHubStatusDetails.Text = $"Checking if repository '{repoName}' exists...";

                // 2. Check if repository exists
                var repoResponse = await _httpClient.GetAsync($"https://api.github.com/repos/{username}/{repoName}");
                if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    GitHubStatusDetails.Text = $"Creating private repository '{repoName}' on your GitHub...";
                    
                    var repoData = new { name = repoName, description = "Secure private backup for my JournalApp entries", @private = true };
                    string repoJson = System.Text.Json.JsonSerializer.Serialize(repoData);
                    var createContent = new StringContent(repoJson, System.Text.Encoding.UTF8, "application/json");
                    
                    var createResponse = await _httpClient.PostAsync("https://api.github.com/user/repos", createContent);
                    if (!createResponse.IsSuccessStatusCode)
                    {
                        string errorResponse = await createResponse.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to create repository: {createResponse.ReasonPhrase}. Details: {errorResponse}");
                    }
                }
                else if (!repoResponse.IsSuccessStatusCode)
                {
                    string errorResponse = await repoResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to access repository: {repoResponse.ReasonPhrase}. Details: {errorResponse}");
                }

                // 3. Gather files to sync
                var filesToSync = new List<(string LocalPath, string GitHubPath, string Message)>();
                
                // Add metadata
                string notesMetaPath = Path.Combine(JournalManager.Instance.DataDir, "notes.json");
                if (File.Exists(notesMetaPath))
                {
                    filesToSync.Add((notesMetaPath, "notes.json", "Update notes metadata"));
                }

                string categoriesMetaPath = Path.Combine(JournalManager.Instance.DataDir, "categories.json");
                if (File.Exists(categoriesMetaPath))
                {
                    filesToSync.Add((categoriesMetaPath, "categories.json", "Update categories metadata"));
                }

                // Add all RTF note files
                foreach (var rtfFile in Directory.GetFiles(JournalManager.Instance.NotesDir, "*.rtf"))
                {
                    filesToSync.Add((rtfFile, $"Notes/{Path.GetFileName(rtfFile)}", $"Backup note {Path.GetFileNameWithoutExtension(rtfFile)}"));
                }

                // Update Progress Bar settings
                GitHubSyncProgressBar.IsIndeterminate = false;
                GitHubSyncProgressBar.Maximum = filesToSync.Count;
                GitHubSyncProgressBar.Value = 0;

                // 4. Sync files sequentially
                for (int i = 0; i < filesToSync.Count; i++)
                {
                    var file = filesToSync[i];
                    GitHubStatusDetails.Text = $"Syncing file {i + 1} of {filesToSync.Count}: {Path.GetFileName(file.GitHubPath)}...";
                    
                    await SyncFileToGitHub(username, repoName, file.LocalPath, file.GitHubPath, file.Message);
                    
                    GitHubSyncProgressBar.Value = i + 1;
                }

                // Save credentials and last sync time on success
                SaveSetting("GitHubToken", token);
                SaveSetting("GitHubRepo", repoName);
                
                string syncTime = DateTime.Now.ToString("g");
                SaveSetting("GitHubLastSynced", syncTime);

                GitHubStatusTitle.Text = "Status: Connected & Synced";
                GitHubStatusDetails.Text = $"Last synced: {syncTime}";
                GitHubDisconnectButton.Visibility = Visibility.Visible;

                await ShowAlertAsync("Synchronization Complete", $"Successfully backed up {filesToSync.Count} files to your private GitHub repository '{repoName}'!");
            }
            catch (Exception ex)
            {
                GitHubStatusTitle.Text = "Status: Connection Error";
                GitHubStatusDetails.Text = ex.Message;
                await ShowAlertAsync("Sync Failed", $"An error occurred during synchronization:\n{ex.Message}");
            }
            finally
            {
                GitHubSyncButton.IsEnabled = true;
                GitHubDisconnectButton.IsEnabled = true;
                GitHubSyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task SyncFileToGitHub(string username, string repoName, string localPath, string githubPath, string commitMessage)
        {
            if (!File.Exists(localPath)) return;

            string base64Content = Convert.ToBase64String(File.ReadAllBytes(localPath));
            
            // Check if file exists on GitHub to obtain its SHA (required for updating files in Git)
            string sha = null;
            var fileResponse = await _httpClient.GetAsync($"https://api.github.com/repos/{username}/{repoName}/contents/{githubPath}");
            if (fileResponse.IsSuccessStatusCode)
            {
                using var fileDoc = System.Text.Json.JsonDocument.Parse(await fileResponse.Content.ReadAsStringAsync());
                if (fileDoc.RootElement.TryGetProperty("sha", out var shaProp))
                {
                    sha = shaProp.GetString();
                }
            }

            // Create put request body
            var putData = new 
            {
                message = commitMessage,
                content = base64Content,
                sha = sha
            };
            string putJson = System.Text.Json.JsonSerializer.Serialize(putData);
            var putContent = new StringContent(putJson, System.Text.Encoding.UTF8, "application/json");

            var putResponse = await _httpClient.PutAsync($"https://api.github.com/repos/{username}/{repoName}/contents/{githubPath}", putContent);
            if (!putResponse.IsSuccessStatusCode)
            {
                string errorResponse = await putResponse.Content.ReadAsStringAsync();
                throw new Exception($"Failed to upload {githubPath}: {putResponse.ReasonPhrase}. Details: {errorResponse}");
            }
        }

        private async void GitHubDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Disconnect GitHub",
                Content = "Are you sure you want to disconnect and clear your GitHub credentials from this device?",
                PrimaryButtonText = "Disconnect",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                RemoveSetting("GitHubToken");
                RemoveSetting("GitHubRepo");
                RemoveSetting("GitHubLastSynced");

                GitHubTokenPasswordBox.Password = "";
                GitHubRepoTextBox.Text = "My-JournalApp-Backup";
                GitHubStatusPanel.Visibility = Visibility.Collapsed;
                GitHubDisconnectButton.Visibility = Visibility.Collapsed;

                await ShowAlertAsync("Disconnected", "GitHub credentials have been removed from this device.");
            }
        }

        private bool _isResizing = false;
        private double _originalWidth = 300;
        private double _pointerStartX = 0;

        private void ColumnSplitter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
            if (ColumnSplitter != null)
            {
                ColumnSplitter.Background = GetBrushFromHex("#400078D4"); // Accent color with opacity
            }
        }

        private void ColumnSplitter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isResizing)
            {
                this.ProtectedCursor = null;
                if (ColumnSplitter != null)
                {
                    ColumnSplitter.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
        }

        private void ColumnSplitter_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                _isResizing = true;
                element.CapturePointer(e.Pointer);
                
                var pt = e.GetCurrentPoint(this);
                _pointerStartX = pt.Position.X;
                _originalWidth = NotesListColumn.Width.Value;
                
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
                if (ColumnSplitter != null)
                {
                    ColumnSplitter.Background = GetBrushFromHex("#600078D4"); // Darker accent highlight during drag
                }
                e.Handled = true;
            }
        }

        private void ColumnSplitter_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isResizing && sender is UIElement element)
            {
                var pt = e.GetCurrentPoint(this);
                double deltaX = pt.Position.X - _pointerStartX;
                double newWidth = _originalWidth + deltaX;

                // Clamp within MinWidth and MaxWidth
                if (newWidth < 200) newWidth = 200;
                if (newWidth > 500) newWidth = 500;

                NotesListColumn.Width = new GridLength(newWidth);
                e.Handled = true;
            }
        }

        private void ColumnSplitter_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isResizing && sender is UIElement element)
            {
                _isResizing = false;
                element.ReleasePointerCapture(e.Pointer);
                this.ProtectedCursor = null;
                if (ColumnSplitter != null)
                {
                    ColumnSplitter.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
                e.Handled = true;
            }
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

            string[] rawLines = plainText.Split(new[] { "\n", "\r" }, StringSplitOptions.None);
            string bodyHtml = "";
            bool inPre = false;

            foreach (var line in rawLines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("```"))
                {
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
                }
                else
                {
                    if (trimmed.StartsWith("# "))
                    {
                        bodyHtml += $"<h1>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(2))}</h1>";
                    }
                    else if (trimmed.StartsWith("## "))
                    {
                        bodyHtml += $"<h2>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(3))}</h2>";
                    }
                    else if (trimmed.StartsWith("### "))
                    {
                        bodyHtml += $"<h3>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(4))}</h3>";
                    }
                    else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                    {
                        bodyHtml += $"<ul><li>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(2))}</li></ul>";
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
                        bodyHtml += $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>";
                    }
                }
            }

            if (inPre)
            {
                bodyHtml += "</pre>";
            }

            string html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 800px;
            margin: 40px auto;
            padding: 0 20px;
        }}
        h1 {{
            font-size: 2.2em;
            font-weight: 700;
            margin-bottom: 5px;
            color: #111;
        }}
        .meta-info {{
            font-size: 0.9em;
            color: #666;
            margin-bottom: 20px;
            border-bottom: 1px solid #eee;
            padding-bottom: 15px;
        }}
        .category-badge {{
            display: inline-block;
            background-color: #f0f0f0;
            padding: 2px 8px;
            border-radius: 12px;
            font-size: 0.85em;
            font-weight: 500;
            margin-left: 10px;
        }}
        p {{
            margin-bottom: 1.2em;
            font-size: 1.1em;
        }}
        pre {{
            background-color: #f8f8f8;
            padding: 15px;
            border-radius: 6px;
            overflow-x: auto;
            font-family: Consolas, Monaco, monospace;
            border: 1px solid #e1e1e1;
        }}
        code {{
            font-family: Consolas, Monaco, monospace;
            background-color: #f1f1f1;
            padding: 2px 4px;
            border-radius: 4px;
        }}
        blockquote {{
            border-left: 4px solid #ccc;
            margin: 0;
            padding-left: 15px;
            color: #666;
            font-style: italic;
        }}
        ul {{
            margin-bottom: 1.2em;
            padding-left: 20px;
        }}
        hr {{
            border: 0;
            border-top: 1px solid #eee;
            margin: 20px 0;
            padding: 0;
        }}
        @media print {{
            body {{
                margin: 20mm;
                max-width: 100%;
            }}
            button {{
                display: none;
            }}
        }}
    </style>
</head>
<body>
    <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
    <div class=""meta-info"">
        <span>{System.Net.WebUtility.HtmlEncode(dateString)}</span>
        <span class=""category-badge"">{System.Net.WebUtility.HtmlEncode(category)}</span>
    </div>
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
        }
    }
}
