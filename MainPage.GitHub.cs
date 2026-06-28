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
            GitHubCommitsListView.Visibility = Visibility.Collapsed;
            
            if (CommitChartSkeleton != null) CommitChartSkeleton.Visibility = Visibility.Visible;
            if (CommitChartContainer != null) CommitChartContainer.Visibility = Visibility.Collapsed;
            CommitChartContainer.Children.Clear();

            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken"); // legacy plaintext fallback
            string repoName = GetSetting("GitHubRepo");

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repoName))
            {
                GitHubHistoryProgressBar.Visibility = Visibility.Collapsed;
                if (CommitChartSkeleton != null) CommitChartSkeleton.Visibility = Visibility.Collapsed;
                if (CommitChartContainer != null) CommitChartContainer.Visibility = Visibility.Collapsed;
                if (GitHubCommitsListView != null) GitHubCommitsListView.Visibility = Visibility.Collapsed;
                GitHubHistoryErrorText.Visibility = Visibility.Visible;
                GitHubHistoryErrorText.Text = "Please set up your GitHub Personal Access Token (PAT) and Repository name in the Settings tab first to view sync history.";
                return;
            }

            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                string username = GetSetting("GitHubUsername");
                if (string.IsNullOrEmpty(username))
                {
                    var userResponse = await _httpClient.GetAsync("https://api.github.com/user");
                    if (!userResponse.IsSuccessStatusCode)
                    {
                        throw new Exception("Failed to authenticate with GitHub. Check your token in Settings.");
                    }

                    using var userDoc = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
                    username = userDoc.RootElement.GetProperty("login").GetString() ?? "";
                    if (!string.IsNullOrEmpty(username))
                    {
                        SaveSetting("GitHubUsername", username);
                    }
                }

                string cachedETag = GetSetting("GitHubCommitsCache_ETag");
                string cachedRepo = GetSetting("GitHubCommitsCache_Repo");
                string cachedJson = null;

                try
                {
                    string cachePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "github_commits_cache.json");
                    if (File.Exists(cachePath))
                    {
                        cachedJson = File.ReadAllText(cachePath);
                    }
                }
                catch { }

                if (!string.IsNullOrEmpty(cachedETag) && !string.IsNullOrEmpty(cachedJson) && cachedRepo == repoName)
                {
                    if (System.Net.Http.Headers.EntityTagHeaderValue.TryParse(cachedETag, out var etagHeader))
                    {
                        _httpClient.DefaultRequestHeaders.IfNoneMatch.Add(etagHeader);
                    }
                }

                var commitsResponse = await _httpClient.GetAsync($"https://api.github.com/repos/{username}/{repoName}/commits");
                
                string commitsJson;
                if (commitsResponse.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    commitsJson = cachedJson;
                }
                else
                {
                     if (!commitsResponse.IsSuccessStatusCode)
                     {
                         if (commitsResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                         {
                             throw new FileNotFoundException($"Repository '{repoName}' was not found. Please click 'Sync Now' in Settings to create and backup your journal to this repository first.");
                         }
                         string errContent = await commitsResponse.Content.ReadAsStringAsync();
                         throw new Exception($"Failed to fetch repository commits: {commitsResponse.ReasonPhrase}. Details: {errContent}");
                     }

                    commitsJson = await commitsResponse.Content.ReadAsStringAsync();

                    if (commitsResponse.Headers.ETag != null)
                    {
                        SaveSetting("GitHubCommitsCache_ETag", commitsResponse.Headers.ETag.ToString());
                        SaveSetting("GitHubCommitsCache_Repo", repoName);

                        try
                        {
                            string cachePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "github_commits_cache.json");
                            File.WriteAllText(cachePath, commitsJson);
                        }
                        catch { }
                    }
                }

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
                if (CommitChartSkeleton != null) CommitChartSkeleton.Visibility = Visibility.Collapsed;
                if (CommitChartContainer != null) CommitChartContainer.Visibility = Visibility.Visible;
                if (GitHubCommitsListView != null) GitHubCommitsListView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                GitHubHistoryProgressBar.Visibility = Visibility.Collapsed;
                if (CommitChartSkeleton != null) CommitChartSkeleton.Visibility = Visibility.Collapsed;
                if (CommitChartContainer != null) CommitChartContainer.Visibility = Visibility.Collapsed;
                if (GitHubCommitsListView != null) GitHubCommitsListView.Visibility = Visibility.Collapsed;
                GitHubHistoryErrorText.Visibility = Visibility.Visible;
                if (ex is FileNotFoundException || ex.Message.Contains("was not found"))
                {
                    GitHubHistoryErrorText.Text = ex.Message;
                }
                else
                {
                    GitHubHistoryErrorText.Text = $"Error: {ex.Message}";
                }
            }
        }

        private void RenderCommitActivityChart(Dictionary<string, int> commitDates)
        {
            if (CommitChartContainer == null) return;
            CommitChartContainer.Children.Clear();

            int maxCommits = commitDates.Values.Max();
            if (maxCommits == 0) maxCommits = 1;

            var accentBrush = GetThemeBrush("AccentFillColorDefaultBrush", "#0078D4");
            var textBrushSecondary = GetThemeBrush("TextFillColorSecondaryBrush", "#8A8886");

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

                    // Build a rich "What's New" content panel
                    var contentPanel = new StackPanel { Spacing = 0 };

                    // Version badge row
                    var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
                    var fromBadge = new Border
                    {
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 3, 8, 3),
                        Child = new TextBlock { Text = $"v{currentVersion}", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    };
                    var arrow = new FontIcon
                    {
                        Glyph = "\uE72A",
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var toBadge = new Border
                    {
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 0, 120, 212)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 3, 8, 3),
                        Child = new TextBlock { Text = latestTag, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlHighlightAltBaseHighBrush"] }
                    };
                    badgeRow.Children.Add(fromBadge);
                    badgeRow.Children.Add(arrow);
                    badgeRow.Children.Add(toBadge);
                    contentPanel.Children.Add(badgeRow);

                    // Release notes section
                    if (!string.IsNullOrWhiteSpace(changelog))
                    {
                        var notesHeader = new TextBlock
                        {
                            Text = "What's New",
                            FontSize = 13,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Margin = new Thickness(0, 0, 0, 8),
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        };
                        contentPanel.Children.Add(notesHeader);

                        var notesList = new StackPanel { Spacing = 6 };
                        // Parse markdown-style bullet points from release body
                        var lines = changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var rawLine in lines)
                        {
                            var line = rawLine.Trim();
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            bool isBullet = line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• ");
                            string text = isBullet ? line.Substring(2).Trim() : line.TrimStart('#').Trim();
                            bool isHeader = rawLine.TrimStart().StartsWith("#");

                            if (isHeader)
                            {
                                notesList.Children.Add(new TextBlock
                                {
                                    Text = text,
                                    FontSize = 13,
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    Margin = new Thickness(0, 8, 0, 2),
                                    TextWrapping = TextWrapping.Wrap
                                });
                            }
                            else
                            {
                                var bulletRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                                if (isBullet)
                                {
                                    bulletRow.Children.Add(new FontIcon
                                    {
                                        Glyph = "\uF127",
                                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                                        FontSize = 10,
                                        VerticalAlignment = VerticalAlignment.Top,
                                        Margin = new Thickness(0, 3, 0, 0),
                                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlHighlightAltBaseHighBrush"]
                                    });
                                }
                                bulletRow.Children.Add(new TextBlock
                                {
                                    Text = text,
                                    FontSize = 13,
                                    TextWrapping = TextWrapping.Wrap,
                                    MaxWidth = 380
                                });
                                notesList.Children.Add(bulletRow);
                            }
                        }
                        contentPanel.Children.Add(notesList);
                    }
                    else
                    {
                        contentPanel.Children.Add(new TextBlock
                        {
                            Text = "A new version is available. Click Install Update to get the latest improvements.",
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        });
                    }

                    var dialog = new ContentDialog
                    {
                        Title = "Update Available 🎉",
                        Content = new ScrollViewer
                        {
                            MaxHeight = 380,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Content = contentPanel,
                            Padding = new Thickness(0, 4, 8, 0)
                        },
                        PrimaryButtonText = !string.IsNullOrEmpty(downloadUrl) ? "Install Update" : "View on GitHub",
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
                                await OpenUriWithBrowserSelectionAsync(new Uri(releaseHtmlUrl));
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

                // Auto-backup toggle
                if (AutoBackupToggle != null)
                    SaveSetting("AutoBackupOnSave", AutoBackupToggle.IsOn ? "True" : "False");

                // Windows Hello on startup
                if (WindowsHelloToggle != null)
                    SaveSetting("UseWindowsHello", WindowsHelloToggle.IsOn ? "True" : "False");

                // Lock on Minimize
                if (LockOnMinimizeToggle != null)
                    SaveSetting("LockOnMinimize", LockOnMinimizeToggle.IsOn ? "True" : "False");

                // Spell Check
                if (SpellCheckToggle != null)
                    SaveSetting("SpellCheck", SpellCheckToggle.IsOn ? "True" : "False");

                // Show Word Count
                if (ShowWordCountToggle != null)
                    SaveSetting("ShowWordCount", ShowWordCountToggle.IsOn ? "True" : "False");

                // Show Note Snippets
                if (ShowSnippetsToggle != null)
                    SaveSetting("ShowSnippets", ShowSnippetsToggle.IsOn ? "True" : "False");

                // Default Sort Order
                if (DefaultSortComboBox?.SelectedItem is ComboBoxItem sortItem)
                    SaveSetting("DefaultSortOrder", sortItem.Tag?.ToString() ?? "DateCreatedDesc");

                // Confirm Before Delete
                if (ConfirmDeleteToggle != null)
                    SaveSetting("ConfirmBeforeDelete", ConfirmDeleteToggle.IsOn ? "True" : "False");

                // Unsplash key
                if (UnsplashTokenPasswordBox != null)
                    SaveSetting("UnsplashAccessKey", UnsplashTokenPasswordBox.Password?.Trim());

                // GitHub
                if (GitHubTokenPasswordBox != null)
                    SaveSecureToken(GitHubTokenPasswordBox.Password?.Trim());
                if (GitHubRepoTextBox != null)
                    SaveSetting("GitHubRepo", GitHubRepoTextBox.Text?.Trim());

                // Master Password & Locked Categories — store securely in Credential Manager
                string inputPassword = MasterPasswordBox != null ? MasterPasswordBox.Password : "";
                SaveSecureMasterPassword(inputPassword);
                _masterPassword = inputPassword;

                string lockedCatsStr = string.Join(",", _lockedCategories);
                SaveSetting("LockedCategories", lockedCatsStr);

                // Local Backup Path
                if (LocalBackupSelectedPathText != null)
                {
                    string pathStr = LocalBackupSelectedPathText.Text;
                    if (pathStr == "No folder selected") pathStr = "";
                    SaveSetting("LocalBackupPath", pathStr);
                }

                // Google Drive Token
                if (GoogleDriveTokenPasswordBox != null)
                    SaveSetting("GoogleDriveToken", GoogleDriveTokenPasswordBox.Password?.Trim());

                // OneDrive Token
                if (OneDriveTokenPasswordBox != null)
                    SaveSetting("OneDriveToken", OneDriveTokenPasswordBox.Password?.Trim());

                // Dropbox Token
                if (DropboxTokenPasswordBox != null)
                    SaveSetting("DropboxToken", DropboxTokenPasswordBox.Password?.Trim());

                // Ollama API URL
                if (OllamaUrlTextBox != null)
                {
                    string url = OllamaUrlTextBox.Text?.Trim() ?? "http://localhost:11434";
                    SaveSetting("OllamaUrl", url);
                    OllamaService.Instance.BaseUrl = url;
                }

                // Blog Settings
                if (BlogRepoTextBox != null)
                {
                    string blogRepo = BlogRepoTextBox.Text?.Trim() ?? "my-journal-blog";
                    SaveSetting("BlogRepo", blogRepo);
                    UpdateBlogLiveUrlLink(blogRepo);
                }
                if (BlogTitleTextBox != null)
                    SaveSetting("BlogTitle", BlogTitleTextBox.Text?.Trim() ?? "My Journal Blog");
                if (BlogDescTextBox != null)
                    SaveSetting("BlogDesc", BlogDescTextBox.Text?.Trim() ?? "A collection of my thoughts and memories.");
                if (BlogCustomCssTextBox != null)
                    SaveSetting("BlogCustomCss", BlogCustomCssTextBox.Text);

                // Refresh UI configurations
                LoadSavedBackupSettings();
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
            UpdateTitleBarBackupButtonState();
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
            string savedGitHubToken = GetSecureToken();
            if (string.IsNullOrEmpty(savedGitHubToken)) savedGitHubToken = GetSetting("GitHubToken", "");
            string currentGitHubToken = GitHubTokenPasswordBox != null ? GitHubTokenPasswordBox.Password?.Trim() : "";
            if (currentGitHubToken != savedGitHubToken) isDirty = true;

            // Check GitHub Repo
            string savedGitHubRepo = GetSetting("GitHubRepo", "My-JournalApp-Backup");
            string currentGitHubRepo = GitHubRepoTextBox != null ? GitHubRepoTextBox.Text?.Trim() : "My-JournalApp-Backup";
            if (currentGitHubRepo != savedGitHubRepo) isDirty = true;

            // Check Local Backup Path
            string savedLocalPath = GetSetting("LocalBackupPath", "");
            string currentLocalPath = LocalBackupSelectedPathText != null ? LocalBackupSelectedPathText.Text : "";
            if (currentLocalPath == "No folder selected") currentLocalPath = "";
            if (currentLocalPath != savedLocalPath) isDirty = true;

            // Check Google Drive Token
            string savedGDToken = GetSetting("GoogleDriveToken", "");
            string currentGDToken = GoogleDriveTokenPasswordBox != null ? GoogleDriveTokenPasswordBox.Password?.Trim() : "";
            if (currentGDToken != savedGDToken) isDirty = true;

            // Check OneDrive Token
            string savedODToken = GetSetting("OneDriveToken", "");
            string currentODToken = OneDriveTokenPasswordBox != null ? OneDriveTokenPasswordBox.Password?.Trim() : "";
            if (currentODToken != savedODToken) isDirty = true;

            // Check Dropbox Token
            string savedDBToken = GetSetting("DropboxToken", "");
            string currentDBToken = DropboxTokenPasswordBox != null ? DropboxTokenPasswordBox.Password?.Trim() : "";
            if (currentDBToken != savedDBToken) isDirty = true;

            // Check Master Password
            string savedPassword = GetSecureMasterPassword();
            string currentPassword = MasterPasswordBox != null ? MasterPasswordBox.Password : "";
            if (currentPassword != savedPassword) isDirty = true;

            // Check Locked Categories
            string savedLockedCats = GetSetting("LockedCategories", "");
            string currentLockedCats = string.Join(",", _lockedCategories);
            if (currentLockedCats != savedLockedCats) isDirty = true;

            // Check Windows Hello
            string savedHello = GetSetting("UseWindowsHello", "True");
            bool currentHello = WindowsHelloToggle?.IsOn ?? true;
            if (currentHello.ToString() != string.Equals(savedHello, "True", StringComparison.OrdinalIgnoreCase).ToString()) isDirty = true;

            // Check Lock on Minimize
            string savedLockMin = GetSetting("LockOnMinimize", "False");
            bool currentLockMin = LockOnMinimizeToggle?.IsOn ?? false;
            if (currentLockMin.ToString() != string.Equals(savedLockMin, "True", StringComparison.OrdinalIgnoreCase).ToString()) isDirty = true;

            // Check Spell Check
            string savedSpell = GetSetting("SpellCheck", "True");
            bool currentSpell = SpellCheckToggle?.IsOn ?? true;
            if (currentSpell.ToString() != string.Equals(savedSpell, "True", StringComparison.OrdinalIgnoreCase).ToString()) isDirty = true;

            // Check Show Word Count
            string savedWC = GetSetting("ShowWordCount", "True");
            bool currentWC = ShowWordCountToggle?.IsOn ?? true;
            if (currentWC.ToString() != string.Equals(savedWC, "True", StringComparison.OrdinalIgnoreCase).ToString()) isDirty = true;

            // Check Show Snippets
            string savedSnippets = GetSetting("ShowSnippets", "True");
            bool currentSnippets = ShowSnippetsToggle?.IsOn ?? true;
            if (currentSnippets.ToString() != string.Equals(savedSnippets, "True", StringComparison.OrdinalIgnoreCase).ToString()) isDirty = true;

            // Check Default Sort Order
            string savedSort = GetSetting("DefaultSortOrder", "DateCreatedDesc");
            string currentSort = (DefaultSortComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "DateCreatedDesc";
            if (currentSort != savedSort) isDirty = true;

            // Check Confirm Before Delete
            string savedConfirm = GetSetting("ConfirmBeforeDelete", "False");
            bool currentConfirm = ConfirmDeleteToggle?.IsOn ?? false;
            if (currentConfirm.ToString() != string.Equals(savedConfirm, "True", StringComparison.OrdinalIgnoreCase).ToString()) isDirty = true;

            // Check Ollama URL
            string savedOllamaUrl = GetSetting("OllamaUrl", "http://localhost:11434");
            string currentOllamaUrl = OllamaUrlTextBox != null ? OllamaUrlTextBox.Text?.Trim() : "http://localhost:11434";
            if (currentOllamaUrl != savedOllamaUrl) isDirty = true;

            // Check Blog settings
            string savedBlogRepo = GetSetting("BlogRepo", "my-journal-blog");
            string currentBlogRepo = BlogRepoTextBox != null ? BlogRepoTextBox.Text?.Trim() : "my-journal-blog";
            if (currentBlogRepo != savedBlogRepo) isDirty = true;

            string savedBlogTitle = GetSetting("BlogTitle", "My Journal Blog");
            string currentBlogTitle = BlogTitleTextBox != null ? BlogTitleTextBox.Text?.Trim() : "My Journal Blog";
            if (currentBlogTitle != savedBlogTitle) isDirty = true;

            string savedBlogDesc = GetSetting("BlogDesc", "A collection of my thoughts and memories.");
            string currentBlogDesc = BlogDescTextBox != null ? BlogDescTextBox.Text?.Trim() : "A collection of my thoughts and memories.";
            if (currentBlogDesc != savedBlogDesc) isDirty = true;

            string savedBlogCss = GetSetting("BlogCustomCss", "");
            string currentBlogCss = BlogCustomCssTextBox != null ? BlogCustomCssTextBox.Text : "";
            if (currentBlogCss != savedBlogCss) isDirty = true;

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
                        if (DateTime.TryParse(lastSyncStr, out var parsedTime))
                        {
                            GitHubStatusDetails.Text = $"Last synced: {parsedTime.ToString("g")}";
                        }
                        else
                        {
                            GitHubStatusDetails.Text = $"Last synced: {lastSyncStr}";
                        }
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
                    ApplyCustomThemeBrushes(savedTheme);

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

                // Load Auto-Backup Toggle state on startup
                string savedAutoBackup = GetSetting("AutoBackupOnSave", "False");
                if (AutoBackupToggle != null)
                {
                    AutoBackupToggle.IsOn = string.Equals(savedAutoBackup, "True", StringComparison.OrdinalIgnoreCase);
                }

                // Load Windows Hello toggle
                string savedHello = GetSetting("UseWindowsHello", "True");
                _useWindowsHello = string.Equals(savedHello, "True", StringComparison.OrdinalIgnoreCase);
                if (WindowsHelloToggle != null)
                    WindowsHelloToggle.IsOn = _useWindowsHello;

                // Load Lock on Minimize toggle
                string savedLockMin = GetSetting("LockOnMinimize", "False");
                _lockOnMinimize = string.Equals(savedLockMin, "True", StringComparison.OrdinalIgnoreCase);
                if (LockOnMinimizeToggle != null)
                    LockOnMinimizeToggle.IsOn = _lockOnMinimize;

                // Load Spell Check toggle
                string savedSpell = GetSetting("SpellCheck", "True");
                bool spellOn = string.Equals(savedSpell, "True", StringComparison.OrdinalIgnoreCase);
                if (SpellCheckToggle != null)
                    SpellCheckToggle.IsOn = spellOn;
                if (NoteRichEditBox != null)
                    NoteRichEditBox.IsSpellCheckEnabled = spellOn;

                // Load Show Word Count toggle
                string savedWC = GetSetting("ShowWordCount", "True");
                _showWordCount = string.Equals(savedWC, "True", StringComparison.OrdinalIgnoreCase);
                if (ShowWordCountToggle != null)
                    ShowWordCountToggle.IsOn = _showWordCount;
                if (WordCountTextBlock != null)
                    WordCountTextBlock.Visibility = _showWordCount ? Visibility.Visible : Visibility.Collapsed;

                // Load Show Snippets toggle
                string savedSnippets = GetSetting("ShowSnippets", "True");
                _showSnippets = string.Equals(savedSnippets, "True", StringComparison.OrdinalIgnoreCase);
                if (ShowSnippetsToggle != null)
                    ShowSnippetsToggle.IsOn = _showSnippets;

                // Load Default Sort Order combobox
                string savedSort = GetSetting("DefaultSortOrder", "DateCreatedDesc");
                _currentSortOption = savedSort;
                if (DefaultSortComboBox != null)
                {
                    foreach (object obj in DefaultSortComboBox.Items)
                    {
                        if (obj is ComboBoxItem item && item.Tag?.ToString() == savedSort)
                        {
                            DefaultSortComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Load Confirm Before Delete toggle
                string savedConfirm = GetSetting("ConfirmBeforeDelete", "False");
                _confirmBeforeDelete = string.Equals(savedConfirm, "True", StringComparison.OrdinalIgnoreCase);
                if (ConfirmDeleteToggle != null)
                    ConfirmDeleteToggle.IsOn = _confirmBeforeDelete;

                // Load Master Password & Locked Categories — from secure Credential Manager
                _masterPassword = GetSecureMasterPassword();
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

                // Load Ollama URL setting
                string savedOllamaUrl = GetSetting("OllamaUrl", "http://localhost:11434");
                if (OllamaUrlTextBox != null)
                {
                    OllamaUrlTextBox.Text = savedOllamaUrl;
                }
                OllamaService.Instance.BaseUrl = savedOllamaUrl;

                // Load Blog configurations
                string savedBlogRepo = GetSetting("BlogRepo", "my-journal-blog");
                if (BlogRepoTextBox != null) BlogRepoTextBox.Text = savedBlogRepo;

                string savedBlogTitle = GetSetting("BlogTitle", "My Journal Blog");
                if (BlogTitleTextBox != null) BlogTitleTextBox.Text = savedBlogTitle;

                string savedBlogDesc = GetSetting("BlogDesc", "A collection of my thoughts and memories.");
                if (BlogDescTextBox != null) BlogDescTextBox.Text = savedBlogDesc;

                string savedBlogCss = GetSetting("BlogCustomCss", "");
                if (BlogCustomCssTextBox != null) BlogCustomCssTextBox.Text = savedBlogCss;

                UpdateBlogLiveUrlLink(savedBlogRepo);

                LoadSavedBackupSettings();
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

                // Create a spelling test note with prompt
                var spellNote = JournalManager.Instance.CreateNote("Personal");
                spellNote.Title = "Writing Prompt: Something New";
                spellNote.Snippet = "Prompt: Write about a time you tried something new and what you learned from it.";
                
                try
                {
                    string spellRtfPath = JournalManager.Instance.GetAbsoluteRtfPath(spellNote.RtfFileName);
                    string spellRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}} \viewkind4\uc1\pard\lang1033\f0\fs20 \b Prompt:\b0  Write about a time you tried something new and what you learned from it.\par\par Last week, I tryed (misspelled) to cook a new dish. I learnned (misspelled) that patience is key when trying new recipes!\par}";
                    File.WriteAllText(spellRtfPath, spellRtf);
                }
                catch (Exception rtfEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[RestoreDefaults] Could not write spell note RTF: {rtfEx.Message}");
                }

                JournalManager.Instance.SaveCategories();
                
                LoadCategoriesList();
                RefreshNotesList();
                UpdateSaveSettingsButtonState();
                UpdateTitleBarBackupButtonState();
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
            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetBackupButtonLoadingState(true);
            }
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
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.SetBackupProgress(0, filesToSync.Count);
                }

                // 4. Sync files sequentially
                for (int i = 0; i < filesToSync.Count; i++)
                {
                    var file = filesToSync[i];
                    GitHubStatusDetails.Text = $"Syncing file {i + 1} of {filesToSync.Count}: {Path.GetFileName(file.GitHubPath)}...";
                    
                    await SyncFileToGitHub(username, repoName, file.LocalPath, file.GitHubPath, file.Message);
                    
                    GitHubSyncProgressBar.Value = i + 1;
                    if (MainWindow.Instance != null)
                    {
                        MainWindow.Instance.SetBackupProgress(i + 1, filesToSync.Count);
                    }
                }

                // Save credentials and last sync time on success
                SaveSetting("GitHubToken", token);
                SaveSetting("GitHubRepo", repoName);
                
                DateTime now = DateTime.Now;
                SaveSetting("GitHubLastSynced", now.ToString("o"));

                GitHubStatusTitle.Text = "Status: Connected & Synced";
                GitHubStatusDetails.Text = $"Last synced: {now.ToString("g")}";
                if (GitHubPullButton != null) GitHubPullButton.Visibility = Visibility.Visible;
                GitHubDisconnectButton.Visibility = Visibility.Visible;

                string successMsg = $"Successfully backed up {filesToSync.Count} files to your private GitHub repository '{repoName}'!";
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.ShowBackupCompleteNotification(successMsg);
                }
                else
                {
                    await ShowAlertAsync("Synchronization Complete", successMsg);
                }
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
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.SetBackupButtonLoadingState(false);
                }
                UpdateTitleBarBackupButtonState();
            }
        }

        private async Task SyncFileToGitHub(string username, string repoName, string localPath, string githubPath, string commitMessage)
        {
            if (!File.Exists(localPath)) return;

            string base64Content = Convert.ToBase64String(File.ReadAllBytes(localPath));

            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");

            Action<HttpRequestMessage> applyHeaders = (req) =>
            {
                req.Headers.UserAgent.Clear();
                req.Headers.UserAgent.TryParseAdd("JournalApp");
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
            };
            
            // Helper function to fetch the latest SHA without using any cached response
            Func<Task<string>> fetchShaFunc = async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{username}/{repoName}/contents/{githubPath}");
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                applyHeaders(request);
                
                var fileResponse = await _httpClient.SendAsync(request);
                if (fileResponse.IsSuccessStatusCode)
                {
                    using var fileDoc = System.Text.Json.JsonDocument.Parse(await fileResponse.Content.ReadAsStringAsync());
                    if (fileDoc.RootElement.TryGetProperty("sha", out var shaProp))
                    {
                        return shaProp.GetString();
                    }
                }
                return null;
            };

            string sha = await fetchShaFunc();

            // Helper function to execute the PUT request
            Func<string, Task<HttpResponseMessage>> executePutFunc = async (currentSha) =>
            {
                var putData = new 
                {
                    message = commitMessage,
                    content = base64Content,
                    sha = currentSha
                };
                string putJson = System.Text.Json.JsonSerializer.Serialize(putData);
                
                var request = new HttpRequestMessage(HttpMethod.Put, $"https://api.github.com/repos/{username}/{repoName}/contents/{githubPath}");
                applyHeaders(request);
                request.Content = new StringContent(putJson, System.Text.Encoding.UTF8, "application/json");
                
                return await _httpClient.SendAsync(request);
            };

            var putResponse = await executePutFunc(sha);
            
            // Auto-retry once if we get a Conflict (409), which happens if the remote ref updated in the background
            if (putResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncFileToGitHub] Conflict 409 on {githubPath}. Fetching fresh SHA and retrying...");
                sha = await fetchShaFunc();
                putResponse = await executePutFunc(sha);
            }

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
                UpdateTitleBarBackupButtonState();
                 if (CategoriesNavView != null && CategoriesNavView.SelectedItem == (object)GitHubNavItem)
                 {
                     CategoriesNavView.SelectedItem = SettingsNavItem;
                 }
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
                
                DateTime now = DateTime.Now;
                SaveSetting("GitHubLastSynced", now.ToString("o"));

                GitHubStatusTitle.Text = "Status: Connected & Merged";
                GitHubStatusDetails.Text = $"Last pulled/synced: {now.ToString("g")}";
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

        private bool IsBackupNeededForProvider(string lastSyncKey)
        {
            string lastSyncStr = GetSetting(lastSyncKey);
            if (string.IsNullOrEmpty(lastSyncStr)) return true; // Never synced, need backup
            if (!DateTime.TryParse(lastSyncStr, out var lastSyncTime)) return true;

            string notesMetaPath = Path.Combine(JournalManager.Instance.DataDir, "notes.json");
            if (File.Exists(notesMetaPath) && File.GetLastWriteTime(notesMetaPath) > lastSyncTime.AddSeconds(1)) return true;

            string categoriesMetaPath = Path.Combine(JournalManager.Instance.DataDir, "categories.json");
            if (File.Exists(categoriesMetaPath) && File.GetLastWriteTime(categoriesMetaPath) > lastSyncTime.AddSeconds(1)) return true;

            if (Directory.Exists(JournalManager.Instance.NotesDir))
            {
                foreach (var file in Directory.GetFiles(JournalManager.Instance.NotesDir, "*.rtf"))
                {
                    if (File.GetLastWriteTime(file) > lastSyncTime.AddSeconds(1)) return true;
                }
            }

            return false;
        }

        public bool IsBackupNeeded()
        {
            // GitHub
            string ghToken = GetSecureToken();
            if (string.IsNullOrEmpty(ghToken)) ghToken = GetSetting("GitHubToken");
            string ghRepo = GetSetting("GitHubRepo");
            if (!string.IsNullOrEmpty(ghToken) && !string.IsNullOrEmpty(ghRepo))
            {
                if (IsBackupNeededForProvider("GitHubLastSynced")) return true;
            }

            // Local
            if (!string.IsNullOrEmpty(GetSetting("LocalBackupPath")))
            {
                if (IsBackupNeededForProvider("LocalLastSynced")) return true;
            }

            // Google Drive
            if (!string.IsNullOrEmpty(GetSetting("GoogleDriveToken")))
            {
                if (IsBackupNeededForProvider("GoogleDriveLastSynced")) return true;
            }

            // OneDrive
            if (!string.IsNullOrEmpty(GetSetting("OneDriveToken")))
            {
                if (IsBackupNeededForProvider("OneDriveLastSynced")) return true;
            }

            // Dropbox
            if (!string.IsNullOrEmpty(GetSetting("DropboxToken")))
            {
                if (IsBackupNeededForProvider("DropboxLastSynced")) return true;
            }

            return false;
        }

        private async Task<bool> IsRemoteAheadAsync()
        {
            try
            {
                string token = GetSecureToken();
                if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
                string repoName = GetSetting("GitHubRepo");
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repoName)) return false;

                repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"\s+", "-");
                repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"[^a-zA-Z0-9\-_\.]", "").ToLowerInvariant();

                string username = await GetGitHubUsername(token);
                if (string.IsNullOrEmpty(username)) return false;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                
                var response = await client.GetAsync($"https://api.github.com/repos/{username}/{repoName}/commits?per_page=1");
                if (response.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var firstCommit = doc.RootElement[0];
                        string dateStr = firstCommit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();
                        if (DateTime.TryParse(dateStr, out var remoteCommitTime))
                        {
                            string lastSyncStr = GetSetting("GitHubLastSynced");
                            if (!string.IsNullOrEmpty(lastSyncStr) && DateTime.TryParse(lastSyncStr, out var localSyncTime))
                            {
                                return remoteCommitTime > localSyncTime.AddSeconds(2);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IsRemoteAhead error: {ex.Message}");
            }
            return false;
        }

        public async void UpdateTitleBarBackupButtonState()
        {
            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
            string repoName = GetSetting("GitHubRepo");

            bool isConfigured = (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repoName)) ||
                                !string.IsNullOrEmpty(GetSetting("LocalBackupPath")) ||
                                !string.IsNullOrEmpty(GetSetting("GoogleDriveToken")) ||
                                !string.IsNullOrEmpty(GetSetting("OneDriveToken")) ||
                                !string.IsNullOrEmpty(GetSetting("DropboxToken"));

            if (GitHubNavItem != null)
            {
                GitHubNavItem.Visibility = (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repoName)) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (MainWindow.Instance == null) return;
            bool isBackupNeeded = IsBackupNeeded();
            
            bool remoteAhead = false;
            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(repoName))
            {
                remoteAhead = await IsRemoteAheadAsync();
            }

            MainWindow.Instance.UpdateBackupButtonState(isBackupNeeded, isConfigured);

            if (remoteAhead)
            {
                MainWindow.Instance.SetPullChangesState();
            }
        }

        public async void TriggerBackupFromTitleBar()
        {
            if (MainWindow.Instance != null && MainWindow.Instance.IsPullChangesState)
            {
                GitHubPullButton_Click(null, null);
                return;
            }

            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetBackupButtonLoadingState(true);
            }

            try
            {
                var files = GetFilesToBackup();

                // GitHub
                string ghToken = GetSecureToken();
                if (string.IsNullOrEmpty(ghToken)) ghToken = GetSetting("GitHubToken");
                string ghRepo = GetSetting("GitHubRepo");
                if (!string.IsNullOrEmpty(ghToken) && !string.IsNullOrEmpty(ghRepo))
                {
                    await SyncGitHubBackupSilent();
                }

                // Local Backup
                string localPath = GetSetting("LocalBackupPath");
                if (!string.IsNullOrEmpty(localPath) && Directory.Exists(localPath))
                {
                    var provider = new Backup.LocalFolderBackupProvider();
                    await provider.SyncUpAsync(files, null);
                    SaveSetting("LocalLastSynced", DateTime.Now.ToString("o"));
                }

                // Google Drive
                if (!string.IsNullOrEmpty(GetSetting("GoogleDriveToken")))
                {
                    var provider = new Backup.GoogleDriveBackupProvider();
                    await provider.SyncUpAsync(files, null);
                    SaveSetting("GoogleDriveLastSynced", DateTime.Now.ToString("o"));
                }

                // OneDrive
                if (!string.IsNullOrEmpty(GetSetting("OneDriveToken")))
                {
                    var provider = new Backup.OneDriveBackupProvider();
                    await provider.SyncUpAsync(files, null);
                    SaveSetting("OneDriveLastSynced", DateTime.Now.ToString("o"));
                }

                // Dropbox
                if (!string.IsNullOrEmpty(GetSetting("DropboxToken")))
                {
                    var provider = new Backup.DropboxBackupProvider();
                    await provider.SyncUpAsync(files, null);
                    SaveSetting("DropboxLastSynced", DateTime.Now.ToString("o"));
                }

                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.ShowBackupCompleteNotification("Successfully backed up all changes to your configured backup platforms.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Titlebar backup error: {ex.Message}");
                await ShowAlertAsync("Backup Failed", $"An error occurred during background sync:\n{ex.Message}");
            }
            finally
            {
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.SetBackupButtonLoadingState(false);
                }
                LoadSavedBackupSettings();
                UpdateSaveSettingsButtonState();
                UpdateTitleBarBackupButtonState();
            }
        }

        private async Task SyncGitHubBackupSilent()
        {
            string token = GetSecureToken();
            if (string.IsNullOrEmpty(token)) token = GetSetting("GitHubToken");
            string repoName = GetSetting("GitHubRepo");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repoName)) return;

            // Configure request headers on shared client
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"\s+", "-");
            repoName = System.Text.RegularExpressions.Regex.Replace(repoName, @"[^a-zA-Z0-9\-_\.]", "").ToLowerInvariant();

            string username = await GetGitHubUsername(token);
            if (string.IsNullOrEmpty(username)) return;

            await EnsureGitHubRepositoryExists(username, repoName, token);

            var files = GetFilesToBackup();
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                await SyncFileToGitHub(username, repoName, file.LocalPath, file.RemotePath, $"Backup note {Path.GetFileNameWithoutExtension(file.LocalPath)}");
            }
            SaveSetting("GitHubLastSynced", DateTime.Now.ToString("o"));
        }

        private async Task<string> GetGitHubUsername(string token)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await client.GetAsync("https://api.github.com/user");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("login").GetString();
        }

        private async Task EnsureGitHubRepositoryExists(string username, string repoName, string token)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("JournalApp");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var response = await client.GetAsync($"https://api.github.com/repos/{username}/{repoName}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var repoData = new { name = repoName, description = "Secure private backup for my JournalApp entries", @private = true };
                string repoJson = System.Text.Json.JsonSerializer.Serialize(repoData);
                var content = new StringContent(repoJson, System.Text.Encoding.UTF8, "application/json");
                var createResponse = await client.PostAsync("https://api.github.com/user/repos", content);
                createResponse.EnsureSuccessStatusCode();
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }
        }
    }
}
