using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace JournalApp.Backup
{
    // ==========================================
    // GOOGLE DRIVE BACKUP PROVIDER
    // ==========================================
    public class GoogleDriveBackupProvider : IBackupProvider
    {
        private readonly HttpClient _httpClient = new HttpClient();
        public string Name => "GoogleDrive";
        public string DisplayName => "Google Drive";

        public bool IsConnected => !string.IsNullOrEmpty(GetSetting("GoogleDriveToken"));

        public async Task<bool> ConnectAsync(Dictionary<string, string> config)
        {
            if (config != null && config.TryGetValue("Token", out string token))
            {
                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        // Validate token by fetching user info or drive root
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        var response = await _httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            SaveSetting("GoogleDriveToken", token);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Google Drive validation error: {ex.Message}");
                    }
                }
            }
            return false;
        }

        public Task DisconnectAsync()
        {
            RemoveSetting("GoogleDriveToken");
            return Task.CompletedTask;
        }

        public async Task SyncUpAsync(List<(string LocalPath, string RemotePath, string ContentType)> files, IProgress<double> progress)
        {
            string token = GetSetting("GoogleDriveToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Google Drive not connected.");

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (!File.Exists(file.LocalPath)) continue;

                byte[] fileBytes = await File.ReadAllBytesAsync(file.LocalPath);
                
                // For Google Drive simple metadata setup, we do a multipart upload
                var boundary = "foo_bar_boundary_" + Guid.NewGuid().ToString();
                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                var content = new MultipartContent("related", boundary);
                
                // Metadata Part
                var metadata = new { name = file.RemotePath, parents = new[] { "appDataFolder" } }; // Use appDataFolder sandbox or root
                // Note: To use appDataFolder, scope must include drive.appdata. We'll default to creating in Root/Apps/JournalApp if folder is found,
                // or just standard root folder. Let's do simple name first.
                var metaContent = new StringContent(JsonSerializer.Serialize(new { name = Path.GetFileName(file.RemotePath) }), Encoding.UTF8, "application/json");
                content.Add(metaContent);

                // Media Part
                var mediaContent = new ByteArrayContent(fileBytes);
                mediaContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                content.Add(mediaContent);

                request.Content = content;
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                progress?.Report((double)(i + 1) / files.Count * 100);
            }
        }

        public async Task PullDownAsync(IProgress<double> progress)
        {
            string token = GetSetting("GoogleDriveToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Google Drive not connected.");

            // 1. List files in drive
            var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files?q=mimeType!='application/vnd.google-apps.folder'&fields=files(id,name)");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var filesEl = doc.RootElement.GetProperty("files");
            
            int count = filesEl.GetArrayLength();
            for (int i = 0; i < count; i++)
            {
                var fileEl = filesEl[i];
                string fileId = fileEl.GetProperty("id").GetString();
                string fileName = fileEl.GetProperty("name").GetString();
                
                // Reconstruct local path
                string localPath = Path.Combine(JournalManager.Instance.DataDir, fileName);
                if (fileName.EndsWith(".rtf") && !fileName.StartsWith("Notes/"))
                {
                    localPath = Path.Combine(JournalManager.Instance.NotesDir, fileName);
                }

                string dir = Path.GetDirectoryName(localPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Download file content
                var dlRequest = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media");
                dlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var dlResponse = await _httpClient.SendAsync(dlRequest);
                if (dlResponse.IsSuccessStatusCode)
                {
                    byte[] data = await dlResponse.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localPath, data);
                }

                progress?.Report((double)(i + 1) / count * 100);
            }
        }

        private string GetSetting(string key) => ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var val) ? val?.ToString() : null;
        private void SaveSetting(string key, string val) => ApplicationData.Current.LocalSettings.Values[key] = val;
        private void RemoveSetting(string key) => ApplicationData.Current.LocalSettings.Values.Remove(key);
    }

    // ==========================================
    // MICROSOFT ONEDRIVE BACKUP PROVIDER
    // ==========================================
    public class OneDriveBackupProvider : IBackupProvider
    {
        private readonly HttpClient _httpClient = new HttpClient();
        public string Name => "OneDrive";
        public string DisplayName => "OneDrive";

        public bool IsConnected => !string.IsNullOrEmpty(GetSetting("OneDriveToken"));

        public async Task<bool> ConnectAsync(Dictionary<string, string> config)
        {
            if (config != null && config.TryGetValue("Token", out string token))
            {
                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        var response = await _httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            SaveSetting("OneDriveToken", token);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OneDrive validation error: {ex.Message}");
                    }
                }
            }
            return false;
        }

        public Task DisconnectAsync()
        {
            RemoveSetting("OneDriveToken");
            return Task.CompletedTask;
        }

        public async Task SyncUpAsync(List<(string LocalPath, string RemotePath, string ContentType)> files, IProgress<double> progress)
        {
            string token = GetSetting("OneDriveToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("OneDrive not connected.");

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (!File.Exists(file.LocalPath)) continue;

                byte[] fileBytes = await File.ReadAllBytesAsync(file.LocalPath);
                
                // PUT to Special/Apps/JournalApp or root:/Apps/JournalApp
                string url = $"https://graph.microsoft.com/v1.0/me/drive/root:/Apps/JournalApp/{file.RemotePath}:/content";
                var request = new HttpRequestMessage(HttpMethod.Put, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new ByteArrayContent(fileBytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                progress?.Report((double)(i + 1) / files.Count * 100);
            }
        }

        public async Task PullDownAsync(IProgress<double> progress)
        {
            string token = GetSetting("OneDriveToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("OneDrive not connected.");

            // List items in Apps/JournalApp recursively or fetch single files
            // For robust fallback, we just fetch standard notes.json, categories.json and all files in /Notes/
            var toDownload = new List<string> { "notes.json", "categories.json" };
            
            // Try to find all RTF files under Notes/
            try
            {
                var listRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive/root:/Apps/JournalApp/Notes:/children");
                listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var listResponse = await _httpClient.SendAsync(listRequest);
                if (listResponse.IsSuccessStatusCode)
                {
                    string json = await listResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("value", out var valueEl))
                    {
                        foreach (var item in valueEl.EnumerateArray())
                        {
                            string name = item.GetProperty("name").GetString();
                            if (name.EndsWith(".rtf"))
                            {
                                toDownload.Add($"Notes/{name}");
                            }
                        }
                    }
                }
            }
            catch { }

            for (int i = 0; i < toDownload.Count; i++)
            {
                string relPath = toDownload[i];
                string localPath = Path.Combine(JournalManager.Instance.DataDir, relPath);
                string dir = Path.GetDirectoryName(localPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var dlRequest = new HttpRequestMessage(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/me/drive/root:/Apps/JournalApp/{relPath}:/content");
                dlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var dlResponse = await _httpClient.SendAsync(dlRequest);
                if (dlResponse.IsSuccessStatusCode)
                {
                    byte[] data = await dlResponse.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localPath, data);
                }

                progress?.Report((double)(i + 1) / toDownload.Count * 100);
            }
        }

        private string GetSetting(string key) => ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var val) ? val?.ToString() : null;
        private void SaveSetting(string key, string val) => ApplicationData.Current.LocalSettings.Values[key] = val;
        private void RemoveSetting(string key) => ApplicationData.Current.LocalSettings.Values.Remove(key);
    }

    // ==========================================
    // DROPBOX BACKUP PROVIDER
    // ==========================================
    public class DropboxBackupProvider : IBackupProvider
    {
        private readonly HttpClient _httpClient = new HttpClient();
        public string Name => "Dropbox";
        public string DisplayName => "Dropbox";

        public bool IsConnected => !string.IsNullOrEmpty(GetSetting("DropboxToken"));

        public async Task<bool> ConnectAsync(Dictionary<string, string> config)
        {
            if (config != null && config.TryGetValue("Token", out string token))
            {
                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_current_account");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        var response = await _httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            SaveSetting("DropboxToken", token);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Dropbox validation error: {ex.Message}");
                    }
                }
            }
            return false;
        }

        public Task DisconnectAsync()
        {
            RemoveSetting("DropboxToken");
            return Task.CompletedTask;
        }

        public async Task SyncUpAsync(List<(string LocalPath, string RemotePath, string ContentType)> files, IProgress<double> progress)
        {
            string token = GetSetting("DropboxToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Dropbox not connected.");

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (!File.Exists(file.LocalPath)) continue;

                byte[] fileBytes = await File.ReadAllBytesAsync(file.LocalPath);
                
                var request = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/upload");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                // Format path cleanly for Dropbox
                string dropboxPath = "/" + file.RemotePath.TrimStart('/');
                var arg = new { path = dropboxPath, mode = "overwrite", autorename = false, mute = true };
                request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(arg));
                
                request.Content = new ByteArrayContent(fileBytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                progress?.Report((double)(i + 1) / files.Count * 100);
            }
        }

        public async Task PullDownAsync(IProgress<double> progress)
        {
            string token = GetSetting("DropboxToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Dropbox not connected.");

            var toDownload = new List<string> { "notes.json", "categories.json" };

            // Scan /Notes directory in Dropbox
            try
            {
                var listRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/files/list_folder");
                listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                listRequest.Content = new StringContent(JsonSerializer.Serialize(new { path = "/Notes" }), Encoding.UTF8, "application/json");
                var listResponse = await _httpClient.SendAsync(listRequest);
                if (listResponse.IsSuccessStatusCode)
                {
                    string json = await listResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("entries", out var entriesEl))
                    {
                        foreach (var entry in entriesEl.EnumerateArray())
                        {
                            string name = entry.GetProperty("name").GetString();
                            if (name.EndsWith(".rtf"))
                            {
                                toDownload.Add($"Notes/{name}");
                            }
                        }
                    }
                }
            }
            catch { }

            for (int i = 0; i < toDownload.Count; i++)
            {
                string relPath = toDownload[i];
                string localPath = Path.Combine(JournalManager.Instance.DataDir, relPath);
                string dir = Path.GetDirectoryName(localPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var dlRequest = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/download");
                dlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                string dropboxPath = "/" + relPath.TrimStart('/');
                var arg = new { path = dropboxPath };
                dlRequest.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(arg));

                var dlResponse = await _httpClient.SendAsync(dlRequest);
                if (dlResponse.IsSuccessStatusCode)
                {
                    byte[] data = await dlResponse.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localPath, data);
                }

                progress?.Report((double)(i + 1) / toDownload.Count * 100);
            }
        }

        private string GetSetting(string key) => ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out var val) ? val?.ToString() : null;
        private void SaveSetting(string key, string val) => ApplicationData.Current.LocalSettings.Values[key] = val;
        private void RemoveSetting(string key) => ApplicationData.Current.LocalSettings.Values.Remove(key);
    }
}
