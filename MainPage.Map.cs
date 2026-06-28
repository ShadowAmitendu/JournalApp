// MainPage.Map.cs
// Geotagging Map bridge logic between C# host and Leaflet.js MapPage.html.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        private bool _mapInitialized = false;

        private async Task InitMapAsync()
        {
            if (_mapInitialized) return;

            try
            {
                await JournalMapWebView.EnsureCoreWebView2Async();
                JournalMapWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                JournalMapWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                JournalMapWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                JournalMapWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

                // Subscribe to map clicks
                JournalMapWebView.CoreWebView2.WebMessageReceived += JournalMapWebView_WebMessageReceived;

                // Load Leaflet page
                string mapPath = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "MapPage.html");
                JournalMapWebView.CoreWebView2.Navigate("file:///" + mapPath.Replace("\\", "/"));

                JournalMapWebView.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _mapInitialized = true;
                    _ = LoadMapDataAsync();
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Map] Init map failed: {ex.Message}");
            }
        }

        private async Task LoadMapDataAsync()
        {
            await InitMapAsync();
            if (!_mapInitialized) return;

            // Format theme style
            string savedTheme = GetSetting("AppTheme", "Default");
            if (savedTheme == "Default")
            {
                savedTheme = this.ActualTheme == ElementTheme.Dark ? "dark" : "light";
            }

            // Sync theme
            await JournalMapWebView.CoreWebView2.ExecuteScriptAsync($"window.setTheme('{savedTheme}')");

            // Gather notes that have geotag coordinates
            var geotaggedNotes = JournalManager.Instance.Notes
                .Where(n => !n.IsDeleted && (n.Latitude != 0.0 || n.Longitude != 0.0))
                .Select(n => new {
                    Id = n.Id,
                    Title = n.Title,
                    Snippet = n.Snippet,
                    DateFormatted = n.DateCreated.ToString("MMMM d, yyyy"),
                    Latitude = n.Latitude,
                    Longitude = n.Longitude
                })
                .ToList();

            var notesJson = JsonSerializer.Serialize(geotaggedNotes);
            await JournalMapWebView.CoreWebView2.ExecuteScriptAsync($"window.setMapData({JsonSerializer.Serialize(notesJson)})");
        }

        private void JournalMapWebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "openNote")
                {
                    string noteId = root.TryGetProperty("noteId", out var idProp) ? idProp.GetString() : null;
                    if (!string.IsNullOrEmpty(noteId))
                      {
                        var note = JournalManager.Instance.Notes.FirstOrDefault(n => n.Id == noteId);
                        if (note != null)
                        {
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                // Switch view to Main Editor
                                CategoriesNavView.SelectedItem = null; // deselect MapNavItem in footer
                                ShowGrid(MainEditorGrid);
                                
                                // Load the selected note
                                SelectedNote = note;
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Map] Message parse error: {ex.Message}");
            }
        }

        private void RefreshMapBtn_Click(object sender, RoutedEventArgs e)
        {
            _mapInitialized = false;
            _ = InitMapAsync();
        }
    }
}
