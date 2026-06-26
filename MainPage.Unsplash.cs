// This file is a partial class extracted from MainPage.xaml.cs to reduce God Class size.
// Contains: Unsplash image search, download, and credential handlers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace JournalApp
{
    public sealed partial class MainPage
    {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaunchUnsplashUri] Failed: {ex.Message}");
            }
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

    }
}
