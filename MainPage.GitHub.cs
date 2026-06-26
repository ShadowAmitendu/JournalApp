// This file is a partial class extracted from MainPage.xaml.cs to reduce God Class size.
// Contains: GitHub sync, update check, PAT management, commit history.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Security.Credentials;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        private async Task DownloadAndInstallUpdate(string downloadUrl, string assetName)
        {
            if (UpdateStatusTextBlock != null)
                UpdateStatusTextBlock.Text = "Downloading update...";

            var mainWindow = MainWindow.Instance;
            if (mainWindow != null)
            {
                mainWindow.ShowTitleBarDownloadState(true);
            }

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
                            
                            if (totalBytes > 0)
                            {
                                int pct = (int)((double)totalRead / totalBytes * 100);
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (UpdateStatusTextBlock != null)
                                    {
                                        UpdateStatusTextBlock.Text = $"Downloading: {pct}%";
                                    }
                                    if (mainWindow != null)
                                    {
                                        mainWindow.UpdateTitleBarDownloadProgress(pct);
                                    }
                                });
                            }
                        }
                    }
                }

                if (UpdateStatusTextBlock != null)
                    UpdateStatusTextBlock.Text = "Trusting update package...";

                InstallSigningCertificate();

                if (UpdateStatusTextBlock != null)
                    UpdateStatusTextBlock.Text = "Launching installer...";

                if (mainWindow != null)
                {
                    mainWindow.UpdateTitleBarDownloadText("Installing...");
                }

                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempPath);
                await Windows.System.Launcher.LaunchFileAsync(file);

                await Task.Delay(1000);
                Microsoft.UI.Xaml.Application.Current.Exit();
            }
            catch (Exception ex)
            {
                if (UpdateStatusTextBlock != null)
                    UpdateStatusTextBlock.Text = "Check failed";
                if (mainWindow != null)
                {
                    mainWindow.ShowTitleBarDownloadState(false);
                    mainWindow.UpdateTitleBarDownloadText("Update Failed");
                }
                await ShowAlertAsync("Update Failed", $"Could not download or launch update: {ex.Message}");
            }
        }

        public async Task StartUpdateDownloadFromTitleBar(string downloadUrl, string assetName)
        {
            await DownloadAndInstallUpdate(downloadUrl, assetName);
        }

        private void InstallSigningCertificate()
        {
            try
            {
                string certPath = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "JournalAppDevKey.cer");
                if (!File.Exists(certPath))
                {
                    System.Diagnostics.Debug.WriteLine("[InstallSigningCertificate] Developer certificate not found at " + certPath);
                    return;
                }

                var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath);

                // Add the certificate to the CurrentUser's TrustedPeople store to trust the MSIX package installer
                using (var store = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.TrustedPeople, 
                    System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser))
                {
                    store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                    bool alreadyExists = false;
                    foreach (var existingCert in store.Certificates)
                    {
                        if (existingCert.Thumbprint == cert.Thumbprint)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    
                    if (!alreadyExists)
                    {
                        store.Add(cert);
                        System.Diagnostics.Debug.WriteLine("[InstallSigningCertificate] Successfully installed certificate to CurrentUser\\TrustedPeople.");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InstallSigningCertificate] Failed to import certificate: {ex.Message}");
            }
        }

        private async void TriggerUpdateCheckStartup()
        {
            try
            {
                // Install/trust developer signing certificate pre-emptively
                InstallSigningCertificate();

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ShadowAmitendu/JournalApp/releases/latest");
                request.Headers.UserAgent.TryParseAdd("JournalApp");
                
                string savedToken = GetSecureToken();
                if (string.IsNullOrEmpty(savedToken)) savedToken = GetSetting("GitHubToken"); // legacy plaintext fallback
                if (!string.IsNullOrEmpty(savedToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", savedToken);
                }

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latestTag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "1.0.0" : "1.0.0";
                
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

                if (hasNewUpdate && MainWindow.Instance != null)
                {
                    MainWindow.Instance.ShowUpdateAvailable(downloadUrl, assetName, latestTag);
                }
            }
            catch (Exception ex)
            {
                // Silent: update check runs on startup; failures should not interrupt the user
                System.Diagnostics.Debug.WriteLine($"[TriggerUpdateCheckStartup] {ex.Message}");
            }
        }

        private async void LoadGitHubCommitsAndHistory()
        {
            if (GitHubGrid == null || GitHubCommitsListView == null || GitHubHistoryProgressBar == null || GitHubHistoryErrorText == null) return;

            GitHubHistoryProgressBar.Visibility = Visibility.Visible;
            GitHubHistoryErrorText.Visibility = Visibility.Collapsed;
            GitHubCommitsListView.ItemsSource = null;
            CommitChartContainer.Children.Clear();

            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken"); // legacy plaintext fallback
            string repoName = GetSetting("GitHubRepo");

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repoName))
            {
                GitHubHistoryProgressBar.Visibility = Visibility.Collapsed;
                GitHubHistoryErrorText.Visibility = Visibility.Visible;
                GitHubHistoryErrorText.Text = "Please set up your GitHub Personal Access Token (PAT) and Repository name in the Settings tab first to view sync history.";
                return;
            }

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var userResponse = await _httpClient.GetAsync("https://api.github.com/user");
                if (!userResponse.IsSuccessStatusCode)
                {
                    throw new Exception("Failed to authenticate with GitHub. Check your token in Settings.");
                }

                using var userDoc = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
                string username = userDoc.RootElement.GetProperty("login").GetString();

                var commitsResponse = await _httpClient.GetAsync($"https://api.github.com/repos/{username}/{repoName}/commits");
                if (!commitsResponse.IsSuccessStatusCode)
                {
                    string errContent = await commitsResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to fetch repository commits: {commitsResponse.ReasonPhrase}. Details: {errContent}");
                }

                string commitsJson = await commitsResponse.Content.ReadAsStringAsync();
                using var commitsDoc = JsonDocument.Parse(commitsJson);
                
                var commitList = new List<GitHubCommitViewModel>();
                var commitDates = new Dictionary<string, int>();

                for (int i = 6; i >= 0; i--)
                {
                    string dateKey = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd");
                    commitDates[dateKey] = 0;
                }

                foreach (var commitObj in commitsDoc.RootElement.EnumerateArray())
                {
                    string sha = commitObj.GetProperty("sha").GetString() ?? "";
                    string shortSha = sha.Length > 7 ? sha.Substring(0, 7) : sha;

                    var commitInfo = commitObj.GetProperty("commit");
                    string message = commitInfo.GetProperty("message").GetString() ?? "";
                    
                    var authorInfo = commitInfo.GetProperty("author");
                    string authorName = authorInfo.GetProperty("name").GetString() ?? "";
                    string dateStr = authorInfo.GetProperty("date").GetString() ?? "";

                    DateTime commitDate = DateTime.TryParse(dateStr, out var d) ? d.ToLocalTime() : DateTime.Now;
                    
                    commitList.Add(new GitHubCommitViewModel
                    {
                        Message = message,
                        Author = authorName,
                        DateFormatted = commitDate.ToString("g"),
                        ShortSha = shortSha
                    });

                    string commitDateKey = commitDate.ToString("yyyy-MM-dd");
                    if (commitDates.ContainsKey(commitDateKey))
                    {
                        commitDates[commitDateKey]++;
                    }
                }

                RenderCommitActivityChart(commitDates);
                GitHubCommitsListView.ItemsSource = commitList;
                GitHubHistoryProgressBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                GitHubHistoryProgressBar.Visibility = Visibility.Collapsed;
                GitHubHistoryErrorText.Visibility = Visibility.Visible;
                GitHubHistoryErrorText.Text = $"Error: {ex.Message}";
            }
        }

        private void RenderCommitActivityChart(Dictionary<string, int> commitDates)
        {
            if (CommitChartContainer == null) return;
            CommitChartContainer.Children.Clear();

            int maxCommits = commitDates.Values.Max();
            if (maxCommits == 0) maxCommits = 1;

            var accentBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            var textBrushSecondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

            foreach (var kvp in commitDates)
            {
                string dateStr = kvp.Key;
                int count = kvp.Value;
                
                DateTime dt = DateTime.Parse(dateStr);
                string label = dt.ToString("dd MMM");

                var barStack = new StackPanel
                {
                    Width = 60,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                var countText = new TextBlock
                {
                    Text = $"{count}",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = textBrushSecondary
                };

                double maxHeight = 80;
                double calculatedHeight = ((double)count / maxCommits) * maxHeight;
                if (calculatedHeight < 3) calculatedHeight = 3;

                var bar = new Border
                {
                    Height = calculatedHeight,
                    Width = 20,
                    Background = accentBrush,
                    CornerRadius = new CornerRadius(4, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = textBrushSecondary
                };

                barStack.Children.Add(countText);
                barStack.Children.Add(bar);
                barStack.Children.Add(labelText);

                CommitChartContainer.Children.Add(barStack);
            }
        }

        private void RefreshGitHubHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadGitHubCommitsAndHistory();
        }

        public class GitHubCommitViewModel
        {
            public string Message { get; set; }
            public string Author { get; set; }
            public string DateFormatted { get; set; }
            public string ShortSha { get; set; }
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
                
                string savedToken = GetSecureToken();
                if (string.IsNullOrEmpty(savedToken)) savedToken = GetSetting("GitHubToken"); // legacy plaintext fallback
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

                // Window backdrop
                if (BackdropComboBox?.SelectedItem is ComboBoxItem backdropItem)
                    SaveSetting("AppBackdrop", backdropItem.Tag?.ToString());


                // Editor font
                if (EditorFontComboBox?.SelectedItem is ComboBoxItem editorFontItem)
                    SaveSetting("EditorFontFamily", editorFontItem.Tag?.ToString());

                // Editor font size
                if (EditorFontSizeSlider != null)
                    SaveSetting("EditorFontSize", EditorFontSizeSlider.Value.ToString());

                // Auto-save
                if (AutoSaveSlider != null)
                    SaveSetting("AutoSaveIntervalSeconds", AutoSaveSlider.Value.ToString());

                // Unsplash key
                if (UnsplashTokenPasswordBox != null)
                    SaveSetting("UnsplashAccessKey", UnsplashTokenPasswordBox.Password?.Trim());

                // GitHub
                if (GitHubTokenPasswordBox != null)
                    SaveSecureToken(GitHubTokenPasswordBox.Password?.Trim());
                if (GitHubRepoTextBox != null)
                    SaveSetting("GitHubRepo", GitHubRepoTextBox.Text?.Trim());

                // Master Password & Locked Categories
                string inputPassword = MasterPasswordBox != null ? MasterPasswordBox.Password : "";
                SaveSetting("MasterPassword", inputPassword);
                _masterPassword = inputPassword;

                string lockedCatsStr = string.Join(",", _lockedCategories);
                SaveSetting("LockedCategories", lockedCatsStr);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveGitHubSettings] {ex.Message}");
            }

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

            // Check Backdrop
            string savedBackdrop = GetSetting("AppBackdrop", "MicaAlt");
            string currentBackdrop = "MicaAlt";
            if (BackdropComboBox?.SelectedItem is ComboBoxItem backdropItem)
            {
                currentBackdrop = backdropItem.Tag?.ToString() ?? "MicaAlt";
            }
            if (currentBackdrop != savedBackdrop) isDirty = true;

            // Check Editor Font
            string savedFont = GetSetting("EditorFontFamily", "Segoe UI");
            string currentFont = "Segoe UI";
            if (EditorFontComboBox?.SelectedItem is ComboBoxItem fontItem)
            {
                currentFont = fontItem.Tag?.ToString() ?? "Segoe UI";
            }
            if (currentFont != savedFont) isDirty = true;

            // Check Editor Font Size
            string savedFontSizeStr = GetSetting("EditorFontSize", "14.0");
            double savedFontSize = 14.0;
            double.TryParse(savedFontSizeStr, out savedFontSize);
            double currentFontSize = EditorFontSizeSlider != null ? EditorFontSizeSlider.Value : 14.0;
            if (Math.Abs(currentFontSize - savedFontSize) > 0.01) isDirty = true;

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

            // Check Master Password
            string savedPassword = GetSetting("MasterPassword", "");
            string currentPassword = MasterPasswordBox != null ? MasterPasswordBox.Password : "";
            if (currentPassword != savedPassword) isDirty = true;

            // Check Locked Categories
            string savedLockedCats = GetSetting("LockedCategories", "");
            string currentLockedCats = string.Join(",", _lockedCategories);
            if (currentLockedCats != savedLockedCats) isDirty = true;

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
                string savedToken = GetSecureToken();
                if (string.IsNullOrEmpty(savedToken)) savedToken = GetSetting("GitHubToken"); // legacy plaintext fallback
                if (!string.IsNullOrEmpty(savedToken))
                {
                    GitHubTokenPasswordBox.Password = savedToken;
                    if (GitHubPullButton != null) GitHubPullButton.Visibility = Visibility.Visible;
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

                // Load Master Password & Locked Categories
                _masterPassword = GetSetting("MasterPassword", "");
                if (MasterPasswordBox != null)
                {
                    MasterPasswordBox.Password = _masterPassword;
                }
                string lockedCatsStr = GetSetting("LockedCategories", "");
                _lockedCategories.Clear();
                if (!string.IsNullOrEmpty(lockedCatsStr))
                {
                    var split = lockedCatsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    _lockedCategories.AddRange(split);
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
                if (GitHubPullButton != null) GitHubPullButton.Visibility = Visibility.Collapsed;
                if (GitHubDisconnectButton != null) GitHubDisconnectButton.Visibility = Visibility.Collapsed;
                
                // 3. Clear credentials from vault and settings
                RemoveSecureToken();
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
                catch (Exception rtfEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[RestoreDefaults] Could not delete RTF files: {rtfEx.Message}");
                }

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
                catch (Exception rtfEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[RestoreDefaults] Could not write welcome RTF: {rtfEx.Message}");
                }

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
            if (GitHubPullButton != null) GitHubPullButton.IsEnabled = false;
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
                if (GitHubPullButton != null) GitHubPullButton.Visibility = Visibility.Visible;
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
                if (GitHubPullButton != null) GitHubPullButton.IsEnabled = true;
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
                RemoveSecureToken();
                RemoveSetting("GitHubRepo");
                RemoveSetting("GitHubLastSynced");

                GitHubTokenPasswordBox.Password = "";
                GitHubRepoTextBox.Text = "My-JournalApp-Backup";
                GitHubStatusPanel.Visibility = Visibility.Collapsed;
                if (GitHubPullButton != null) GitHubPullButton.Visibility = Visibility.Collapsed;
                GitHubDisconnectButton.Visibility = Visibility.Collapsed;

                await ShowAlertAsync("Disconnected", "GitHub credentials have been removed from this device.");
            }
        }

        private async void GitHubPullButton_Click(object sender, RoutedEventArgs e)
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

            // Disable buttons during pull
            GitHubSyncButton.IsEnabled = false;
            if (GitHubPullButton != null) GitHubPullButton.IsEnabled = false;
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
                GitHubStatusDetails.Text = $"Accessing repository '{repoName}'...";

                // 2. Check if repository exists
                var repoResponse = await _httpClient.GetAsync($"https://api.github.com/repos/{username}/{repoName}");
                if (repoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new Exception($"The repository '{repoName}' does not exist on your GitHub account. Sync/backup first to create it.");
                }
                else if (!repoResponse.IsSuccessStatusCode)
                {
                    string errorResponse = await repoResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to access repository: {repoResponse.ReasonPhrase}. Details: {errorResponse}");
                }

                // 3. Download remote notes.json
                GitHubStatusDetails.Text = "Downloading remote notes metadata...";
                byte[] remoteNotesBytes = await DownloadRawFileFromGitHub(username, repoName, "notes.json");
                if (remoteNotesBytes == null)
                {
                    throw new Exception("No 'notes.json' found in the remote repository. Nothing to pull.");
                }

                string remoteNotesJson = System.Text.Encoding.UTF8.GetString(remoteNotesBytes);
                var remoteNotesList = System.Text.Json.JsonSerializer.Deserialize<List<JournalNote>>(remoteNotesJson);
                if (remoteNotesList == null)
                {
                    throw new Exception("Failed to parse remote notes metadata.");
                }

                // 4. Download remote categories.json (optional/fallback)
                GitHubStatusDetails.Text = "Downloading remote categories metadata...";
                List<JournalCategory> remoteCategoriesList = null;
                byte[] remoteCategoriesBytes = await DownloadRawFileFromGitHub(username, repoName, "categories.json");
                if (remoteCategoriesBytes != null)
                {
                    string remoteCategoriesJson = System.Text.Encoding.UTF8.GetString(remoteCategoriesBytes);
                    remoteCategoriesList = System.Text.Json.JsonSerializer.Deserialize<List<JournalCategory>>(remoteCategoriesJson);
                }

                // Prepare tracking variables for stats/info
                int updatedNotesCount = 0;
                int addedNotesCount = 0;
                int skippedNotesCount = 0;

                GitHubSyncProgressBar.IsIndeterminate = false;
                GitHubSyncProgressBar.Maximum = remoteNotesList.Count;
                GitHubSyncProgressBar.Value = 0;

                // 5. Merge notes and download corresponding RTF files
                for (int i = 0; i < remoteNotesList.Count; i++)
                {
                    var remoteNote = remoteNotesList[i];
                    GitHubStatusDetails.Text = $"Processing remote note {i + 1} of {remoteNotesList.Count}: {remoteNote.Title}...";
                    
                    var localNote = JournalManager.Instance.Notes.FirstOrDefault(n => n.Id == remoteNote.Id);
                    bool shouldDownloadRtf = false;

                    if (localNote == null)
                    {
                        // Note is brand new to local
                        shouldDownloadRtf = true;
                        JournalManager.Instance.Notes.Add(remoteNote);
                        addedNotesCount++;
                    }
                    else if (remoteNote.DateModified > localNote.DateModified)
                    {
                        // Remote note is newer, overwrite local metadata
                        shouldDownloadRtf = true;
                        
                        // Overwrite properties
                        localNote.Title = remoteNote.Title;
                        localNote.Category = remoteNote.Category;
                        localNote.DateCreated = remoteNote.DateCreated;
                        localNote.DateModified = remoteNote.DateModified;
                        localNote.HeroImagePath = remoteNote.HeroImagePath;
                        localNote.CoverOffsetY = remoteNote.CoverOffsetY;
                        localNote.CoverOffsetX = remoteNote.CoverOffsetX;
                        localNote.CoverBrightness = remoteNote.CoverBrightness;
                        localNote.CoverBlur = remoteNote.CoverBlur;
                        localNote.CoverAttributionText = remoteNote.CoverAttributionText;
                        localNote.CoverAttributionUrl = remoteNote.CoverAttributionUrl;
                        localNote.RtfFileName = remoteNote.RtfFileName;
                        localNote.IsFavorite = remoteNote.IsFavorite;
                        localNote.IsDeleted = remoteNote.IsDeleted;
                        localNote.IsPinned = remoteNote.IsPinned;
                        localNote.Mood = remoteNote.Mood;
                        localNote.Tags = remoteNote.Tags;
                        localNote.HasTime = remoteNote.HasTime;
                        localNote.EditorWidth = remoteNote.EditorWidth;
                        
                        // Moments
                        localNote.LocationTag = remoteNote.LocationTag;
                        localNote.WeatherTag = remoteNote.WeatherTag;
                        localNote.AttachedPhotoPaths = remoteNote.AttachedPhotoPaths;
                        
                        updatedNotesCount++;
                    }
                    else
                    {
                        // Local note is newer or equal, skip
                        skippedNotesCount++;
                    }

                    if (shouldDownloadRtf)
                    {
                        // Download the RTF file
                        string rtfPath = $"Notes/{remoteNote.RtfFileName}";
                        byte[] rtfBytes = await DownloadRawFileFromGitHub(username, repoName, rtfPath);
                        if (rtfBytes != null)
                        {
                            string localRtfPath = JournalManager.Instance.GetAbsoluteRtfPath(remoteNote.RtfFileName);
                            if (localRtfPath != null)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(localRtfPath));
                                await Task.Run(() => File.WriteAllBytes(localRtfPath, rtfBytes));
                            }
                        }
                    }

                    GitHubSyncProgressBar.Value = i + 1;
                }

                // 6. Merge categories
                if (remoteCategoriesList != null)
                {
                    foreach (var remoteCat in remoteCategoriesList)
                    {
                        var localCat = JournalManager.Instance.Categories.FirstOrDefault(c => c.Name.Equals(remoteCat.Name, StringComparison.OrdinalIgnoreCase));
                        if (localCat == null)
                        {
                            JournalManager.Instance.Categories.Add(remoteCat);
                        }
                        else
                        {
                            // Optionally update icon and color if missing or default
                            localCat.Icon = remoteCat.Icon;
                            localCat.Color = remoteCat.Color;
                        }
                    }
                    JournalManager.Instance.SaveCategories();
                }

                // 7. Save merged notes metadata locally
                JournalManager.Instance.SaveNotesMetadata();

                // Save credentials and last sync time on success
                SaveSecureToken(token);
                SaveSetting("GitHubRepo", repoName);
                
                string syncTime = DateTime.Now.ToString("g");
                SaveSetting("GitHubLastSynced", syncTime);

                GitHubStatusTitle.Text = "Status: Connected & Merged";
                GitHubStatusDetails.Text = $"Last pulled/synced: {syncTime}";
                if (GitHubPullButton != null) GitHubPullButton.Visibility = Visibility.Visible;
                GitHubDisconnectButton.Visibility = Visibility.Visible;

                // Refresh UI
                RefreshNotesList();

                await ShowAlertAsync("Restore & Merge Complete", 
                    $"Synchronization pull complete!\n\n" +
                    $"• Added: {addedNotesCount} new entries\n" +
                    $"• Updated: {updatedNotesCount} newer entries\n" +
                    $"• Skipped: {skippedNotesCount} local-only or up-to-date entries");
            }
            catch (Exception ex)
            {
                GitHubStatusTitle.Text = "Status: Connection Error";
                GitHubStatusDetails.Text = ex.Message;
                await ShowAlertAsync("Pull & Merge Failed", $"An error occurred while pulling your backup:\n{ex.Message}");
            }
            finally
            {
                GitHubSyncButton.IsEnabled = true;
                if (GitHubPullButton != null) GitHubPullButton.IsEnabled = true;
                GitHubDisconnectButton.IsEnabled = true;
                GitHubSyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<byte[]> DownloadRawFileFromGitHub(string username, string repoName, string githubPath)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{username}/{repoName}/contents/{githubPath}");
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3.raw"));
            
            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to download '{githubPath}': {response.ReasonPhrase}. Details: {err}");
            }
            return await response.Content.ReadAsByteArrayAsync();
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
    }
}
