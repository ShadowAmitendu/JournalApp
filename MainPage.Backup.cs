using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace JournalApp
{
    public sealed partial class MainPage
    {
        private readonly List<Backup.IBackupProvider> _backupProviders = new List<Backup.IBackupProvider>
        {
            new Backup.LocalFolderBackupProvider(),
            new Backup.GoogleDriveBackupProvider(),
            new Backup.OneDriveBackupProvider(),
            new Backup.DropboxBackupProvider()
        };

        private void LoadSavedBackupSettings()
        {
            try
            {
                // Local Backup
                string localPath = GetSetting("LocalBackupPath");
                if (!string.IsNullOrEmpty(localPath))
                {
                    LocalBackupSelectedPathText.Text = localPath;
                }
                UpdateLocalBackupUI();

                // Google Drive
                string gdToken = GetSetting("GoogleDriveToken");
                if (!string.IsNullOrEmpty(gdToken))
                {
                    GoogleDriveTokenPasswordBox.Password = gdToken;
                }
                UpdateGoogleDriveUI();

                // OneDrive
                string odToken = GetSetting("OneDriveToken");
                if (!string.IsNullOrEmpty(odToken))
                {
                    OneDriveTokenPasswordBox.Password = odToken;
                }
                UpdateOneDriveUI();

                // Dropbox
                string dbToken = GetSetting("DropboxToken");
                if (!string.IsNullOrEmpty(dbToken))
                {
                    DropboxTokenPasswordBox.Password = dbToken;
                }
                UpdateDropboxUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load backup settings: {ex.Message}");
            }
        }

        // =========================================================
        // LOCAL BACKUP HANDLERS
        // =========================================================
        private async void LocalBackupChooseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                var folderPicker = new FolderPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                StorageFolder folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    LocalBackupSelectedPathText.Text = folder.Path;
                    UpdateSaveSettingsButtonState();
                }
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Error", $"Failed to select folder: {ex.Message}");
            }
        }

        private async void LocalBackupSyncButton_Click(object sender, RoutedEventArgs e)
        {
            string path = LocalBackupSelectedPathText.Text;
            if (string.IsNullOrEmpty(path) || path == "No folder selected")
            {
                await ShowAlertAsync("Error", "Please select a backup folder first.");
                return;
            }

            var provider = new Backup.LocalFolderBackupProvider();
            var credentials = new Dictionary<string, string> { { "Path", path } };
            await provider.ConnectAsync(credentials);
            SaveSetting("LocalBackupPath", path);

            LocalBackupSyncButton.IsEnabled = false;
            LocalBackupDisconnectButton.Visibility = Visibility.Collapsed;
            LocalBackupStatusPanel.Visibility = Visibility.Visible;
            LocalBackupSyncProgressBar.Visibility = Visibility.Visible;
            LocalBackupSyncProgressBar.IsIndeterminate = true;
            LocalBackupStatusTitle.Text = "Status: Syncing...";
            LocalBackupStatusDetails.Text = "Copying files to local/network drive...";

            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetBackupButtonLoadingState(true);
            }

            try
            {
                var files = GetFilesToBackup();
                var progress = new Progress<double>(val =>
                {
                    LocalBackupSyncProgressBar.IsIndeterminate = false;
                    LocalBackupSyncProgressBar.Value = val;
                    if (MainWindow.Instance != null)
                    {
                        MainWindow.Instance.SetBackupProgress(val, 100);
                    }
                });

                await provider.SyncUpAsync(files, progress);

                DateTime now = DateTime.Now;
                SaveSetting("LocalLastSynced", now.ToString("o"));
                LocalBackupStatusTitle.Text = "Status: Connected & Synced";
                LocalBackupStatusDetails.Text = $"Last synced: {now.ToString("g")}";
                
                await ShowAlertAsync("Local Backup Complete", $"Successfully backed up {files.Count} files to {path}!");
            }
            catch (Exception ex)
            {
                LocalBackupStatusTitle.Text = "Status: Backup Failed";
                LocalBackupStatusDetails.Text = ex.Message;
                await ShowAlertAsync("Backup Failed", ex.Message);
            }
            finally
            {
                LocalBackupSyncButton.IsEnabled = true;
                LocalBackupSyncProgressBar.Visibility = Visibility.Collapsed;
                UpdateLocalBackupUI();
                UpdateSaveSettingsButtonState();
                UpdateTitleBarBackupButtonState();
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.SetBackupButtonLoadingState(false);
                }
            }
        }

        private async void LocalBackupPullButton_Click(object sender, RoutedEventArgs e)
        {
            var provider = new Backup.LocalFolderBackupProvider();
            if (!provider.IsConnected) return;

            LocalBackupStatusTitle.Text = "Status: Pulling...";
            LocalBackupSyncProgressBar.Visibility = Visibility.Visible;
            LocalBackupSyncProgressBar.IsIndeterminate = true;

            try
            {
                var progress = new Progress<double>(val =>
                {
                    LocalBackupSyncProgressBar.IsIndeterminate = false;
                    LocalBackupSyncProgressBar.Value = val;
                });

                await provider.PullDownAsync(progress);
                await ShowAlertAsync("Restore Complete", "Successfully restored journal entries from your local/network backup!");
                RefreshNotesList();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Restore Failed", ex.Message);
            }
            finally
            {
                LocalBackupSyncProgressBar.Visibility = Visibility.Collapsed;
                UpdateLocalBackupUI();
            }
        }

        private void LocalBackupDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSetting("LocalBackupPath");
            RemoveSetting("LocalLastSynced");
            LocalBackupSelectedPathText.Text = "No folder selected";
            UpdateLocalBackupUI();
            UpdateSaveSettingsButtonState();
            UpdateTitleBarBackupButtonState();
        }

        private void UpdateLocalBackupUI()
        {
            string path = GetSetting("LocalBackupPath");
            bool connected = !string.IsNullOrEmpty(path) && Directory.Exists(path);
            
            if (LocalBackupPullButton != null) LocalBackupPullButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            if (LocalBackupDisconnectButton != null) LocalBackupDisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            if (LocalBackupStatusPanel != null) LocalBackupStatusPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            
            if (connected && LocalBackupStatusTitle != null && LocalBackupStatusDetails != null)
            {
                LocalBackupStatusTitle.Text = "Status: Connected";
                string lastSync = GetSetting("LocalLastSynced");
                LocalBackupStatusDetails.Text = !string.IsNullOrEmpty(lastSync) && DateTime.TryParse(lastSync, out var dt) 
                    ? $"Last synced: {dt.ToString("g")}" 
                    : "Last synced: Never";
            }
        }

        // =========================================================
        // CLOUD PLATFORM BACKUP HANDLERS
        // =========================================================
        private void GoogleDriveTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => UpdateSaveSettingsButtonState();
        private void OneDriveTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => UpdateSaveSettingsButtonState();
        private void DropboxTokenPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => UpdateSaveSettingsButtonState();

        private async void GoogleDriveSyncButton_Click(object sender, RoutedEventArgs e)
        {
            string token = GoogleDriveTokenPasswordBox.Password?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                await ShowAlertAsync("Token Required", "Please enter a valid Google Drive Access Token.");
                return;
            }

            var provider = new Backup.GoogleDriveBackupProvider();
            bool connected = await provider.ConnectAsync(new Dictionary<string, string> { { "Token", token } });
            if (!connected)
            {
                await ShowAlertAsync("Connection Failed", "Invalid Access Token or network error.");
                return;
            }

            await ExecuteCloudSync(provider, GoogleDriveSyncButton, GoogleDriveSyncProgressBar, GoogleDriveStatusTitle, GoogleDriveStatusDetails, "GoogleDriveLastSynced");
        }

        private async void GoogleDrivePullButton_Click(object sender, RoutedEventArgs e) => await ExecuteCloudPull(new Backup.GoogleDriveBackupProvider(), GoogleDriveSyncProgressBar);
        private void GoogleDriveDisconnectButton_Click(object sender, RoutedEventArgs e) => ExecuteCloudDisconnect("GoogleDriveToken", "GoogleDriveLastSynced", GoogleDriveTokenPasswordBox, UpdateGoogleDriveUI);
        
        private void UpdateGoogleDriveUI() => UpdateCloudUI("GoogleDriveToken", "GoogleDriveLastSynced", GoogleDrivePullButton, GoogleDriveDisconnectButton, GoogleDriveStatusPanel, GoogleDriveStatusTitle, GoogleDriveStatusDetails);

        private async void OneDriveSyncButton_Click(object sender, RoutedEventArgs e)
        {
            string token = OneDriveTokenPasswordBox.Password?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                await ShowAlertAsync("Token Required", "Please enter a valid OneDrive Access Token.");
                return;
            }

            var provider = new Backup.OneDriveBackupProvider();
            bool connected = await provider.ConnectAsync(new Dictionary<string, string> { { "Token", token } });
            if (!connected)
            {
                await ShowAlertAsync("Connection Failed", "Invalid Access Token or network error.");
                return;
            }

            await ExecuteCloudSync(provider, OneDriveSyncButton, OneDriveSyncProgressBar, OneDriveStatusTitle, OneDriveStatusDetails, "OneDriveLastSynced");
        }

        private async void OneDrivePullButton_Click(object sender, RoutedEventArgs e) => await ExecuteCloudPull(new Backup.OneDriveBackupProvider(), OneDriveSyncProgressBar);
        private void OneDriveDisconnectButton_Click(object sender, RoutedEventArgs e) => ExecuteCloudDisconnect("OneDriveToken", "OneDriveLastSynced", OneDriveTokenPasswordBox, UpdateOneDriveUI);
        private void UpdateOneDriveUI() => UpdateCloudUI("OneDriveToken", "OneDriveLastSynced", OneDrivePullButton, OneDriveDisconnectButton, OneDriveStatusPanel, OneDriveStatusTitle, OneDriveStatusDetails);

        private async void DropboxSyncButton_Click(object sender, RoutedEventArgs e)
        {
            string token = DropboxTokenPasswordBox.Password?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                await ShowAlertAsync("Token Required", "Please enter a valid Dropbox Access Token.");
                return;
            }

            var provider = new Backup.DropboxBackupProvider();
            bool connected = await provider.ConnectAsync(new Dictionary<string, string> { { "Token", token } });
            if (!connected)
            {
                await ShowAlertAsync("Connection Failed", "Invalid Access Token or network error.");
                return;
            }

            await ExecuteCloudSync(provider, DropboxSyncButton, DropboxSyncProgressBar, DropboxStatusTitle, DropboxStatusDetails, "DropboxLastSynced");
        }

        private async void DropboxPullButton_Click(object sender, RoutedEventArgs e) => await ExecuteCloudPull(new Backup.DropboxBackupProvider(), DropboxSyncProgressBar);
        private void DropboxDisconnectButton_Click(object sender, RoutedEventArgs e) => ExecuteCloudDisconnect("DropboxToken", "DropboxLastSynced", DropboxTokenPasswordBox, UpdateDropboxUI);
        private void UpdateDropboxUI() => UpdateCloudUI("DropboxToken", "DropboxLastSynced", DropboxPullButton, DropboxDisconnectButton, DropboxStatusPanel, DropboxStatusTitle, DropboxStatusDetails);

        // =========================================================
        // PRIVATE CLOUD UTILITY HELPERS
        // =========================================================
        private async Task ExecuteCloudSync(Backup.IBackupProvider provider, Button syncButton, ProgressBar progressBar, TextBlock titleBlock, TextBlock detailsBlock, string syncTimeKey)
        {
            syncButton.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            titleBlock.Text = "Status: Syncing...";
            detailsBlock.Text = "Uploading files to cloud drive...";

            if (MainWindow.Instance != null)
            {
                MainWindow.Instance.SetBackupButtonLoadingState(true);
            }

            try
            {
                var files = GetFilesToBackup();
                var progress = new Progress<double>(val =>
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = val;
                    if (MainWindow.Instance != null)
                    {
                        MainWindow.Instance.SetBackupProgress(val, 100);
                    }
                });

                await provider.SyncUpAsync(files, progress);

                DateTime now = DateTime.Now;
                SaveSetting(syncTimeKey, now.ToString("o"));
                titleBlock.Text = "Status: Connected & Synced";
                detailsBlock.Text = $"Last synced: {now.ToString("g")}";

                await ShowAlertAsync("Backup Complete", $"Successfully backed up {files.Count} files to {provider.DisplayName}!");
            }
            catch (Exception ex)
            {
                titleBlock.Text = "Status: Sync Failed";
                detailsBlock.Text = ex.Message;
                await ShowAlertAsync("Sync Failed", ex.Message);
            }
            finally
            {
                syncButton.IsEnabled = true;
                progressBar.Visibility = Visibility.Collapsed;
                LoadSavedBackupSettings();
                UpdateSaveSettingsButtonState();
                UpdateTitleBarBackupButtonState();
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.SetBackupButtonLoadingState(false);
                }
            }
        }

        private async Task ExecuteCloudPull(Backup.IBackupProvider provider, ProgressBar progressBar)
        {
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            try
            {
                var progress = new Progress<double>(val =>
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = val;
                });

                await provider.PullDownAsync(progress);
                await ShowAlertAsync("Restore Complete", $"Successfully restored journal entries from {provider.DisplayName}!");
                RefreshNotesList();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Restore Failed", ex.Message);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                LoadSavedBackupSettings();
            }
        }

        private void ExecuteCloudDisconnect(string tokenKey, string syncTimeKey, PasswordBox passwordBox, Action updateUiAction)
        {
            RemoveSetting(tokenKey);
            RemoveSetting(syncTimeKey);
            if (passwordBox != null) passwordBox.Password = "";
            updateUiAction();
            UpdateSaveSettingsButtonState();
            UpdateTitleBarBackupButtonState();
        }

        private void UpdateCloudUI(string tokenKey, string syncTimeKey, Button pullButton, Button disconnectButton, StackPanel statusPanel, TextBlock titleBlock, TextBlock detailsBlock)
        {
            bool connected = !string.IsNullOrEmpty(GetSetting(tokenKey));
            
            if (pullButton != null) pullButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            if (disconnectButton != null) disconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            if (statusPanel != null) statusPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

            if (connected && titleBlock != null && detailsBlock != null)
            {
                titleBlock.Text = "Status: Connected";
                string lastSync = GetSetting(syncTimeKey);
                detailsBlock.Text = !string.IsNullOrEmpty(lastSync) && DateTime.TryParse(lastSync, out var dt) 
                    ? $"Last synced: {dt.ToString("g")}" 
                    : "Last synced: Never";
            }
        }

        private List<(string LocalPath, string RemotePath, string ContentType)> GetFilesToBackup()
        {
            var filesToSync = new List<(string LocalPath, string RemotePath, string ContentType)>();
            
            string notesMetaPath = Path.Combine(JournalManager.Instance.DataDir, "notes.json");
            if (File.Exists(notesMetaPath))
            {
                filesToSync.Add((notesMetaPath, "notes.json", "application/json"));
            }

            string categoriesMetaPath = Path.Combine(JournalManager.Instance.DataDir, "categories.json");
            if (File.Exists(categoriesMetaPath))
            {
                filesToSync.Add((categoriesMetaPath, "categories.json", "application/json"));
            }

            foreach (var rtfFile in Directory.GetFiles(JournalManager.Instance.NotesDir, "*.rtf"))
            {
                filesToSync.Add((rtfFile, $"Notes/{Path.GetFileName(rtfFile)}", "application/rtf"));
            }

            return filesToSync;
        }

        private void GoogleDriveSignInButton_Click(object sender, RoutedEventArgs e)
        {
            string googleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=1067280352495-2g76aeb1e8csh6bptbe04r0oeq6o4309.apps.googleusercontent.com&redirect_uri=http://localhost&response_type=token&scope=https://www.googleapis.com/auth/drive.file";
            _ = StartOAuthSignInAsync("Google Drive", googleAuthUrl, "http://localhost", GoogleDriveTokenPasswordBox);
        }

        private void OneDriveSignInButton_Click(object sender, RoutedEventArgs e)
        {
            string microsoftAuthUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=8923a101-70bf-4d10-8b43-26100c430932&redirect_uri=https://login.live.com/oauth20_desktop.srf&response_type=token&scope=files.readwrite";
            _ = StartOAuthSignInAsync("OneDrive", microsoftAuthUrl, "https://login.live.com/oauth20_desktop.srf", OneDriveTokenPasswordBox);
        }

        private void DropboxSignInButton_Click(object sender, RoutedEventArgs e)
        {
            string dropboxAuthUrl = "https://www.dropbox.com/oauth2/authorize?client_id=ab239c01923cd4e&redirect_uri=http://localhost&response_type=token";
            _ = StartOAuthSignInAsync("Dropbox", dropboxAuthUrl, "http://localhost", DropboxTokenPasswordBox);
        }

        private async Task StartOAuthSignInAsync(string serviceName, string authUrl, string redirectUriPrefix, PasswordBox targetPasswordBox)
        {
            try
            {
                var webView = new Microsoft.UI.Xaml.Controls.WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    MinWidth = 500,
                    MinHeight = 600
                };

                var browserDialog = new ContentDialog
                {
                    Title = $"Sign in to {serviceName}",
                    Content = webView,
                    CloseButtonText = "Cancel",
                    XamlRoot = this.XamlRoot
                };

                webView.NavigationStarting += (sender, args) =>
                {
                    string url = args.Uri.ToString();
                    if (url.StartsWith(redirectUriPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string token = null;
                        var uri = new Uri(url);
                        
                        string hash = uri.Fragment;
                        if (!string.IsNullOrEmpty(hash) && hash.Contains("access_token="))
                        {
                            var parts = hash.TrimStart('#').Split('&');
                            var tokenPart = parts.FirstOrDefault(p => p.StartsWith("access_token="));
                            if (tokenPart != null)
                            {
                                token = tokenPart.Split('=')[1];
                            }
                        }
                        else
                        {
                            var queryParts = uri.Query.TrimStart('?').Split('&');
                            var tokenPart = queryParts.FirstOrDefault(p => p.StartsWith("access_token="));
                            if (tokenPart != null)
                            {
                                token = tokenPart.Split('=')[1];
                            }
                        }

                        if (!string.IsNullOrEmpty(token))
                        {
                            targetPasswordBox.Password = token;
                            browserDialog.Hide();
                            _ = ShowAlertAsync("Sign In Successful", $"You have successfully signed in to {serviceName}!");
                        }
                    }
                };

                await webView.EnsureCoreWebView2Async();
                webView.Source = new Uri(authUrl);

                await browserDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowAlertAsync("Sign In Failed", $"An error occurred during authentication:\n{ex.Message}");
            }
        }
    }
}
