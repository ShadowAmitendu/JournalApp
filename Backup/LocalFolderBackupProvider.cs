using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace JournalApp.Backup
{
    public class LocalFolderBackupProvider : IBackupProvider
    {
        public string Name => "Local";
        public string DisplayName => "Local/Network Drive";

        public bool IsConnected
        {
            get
            {
                string path = GetSetting("LocalBackupPath");
                return !string.IsNullOrEmpty(path) && Directory.Exists(path);
            }
        }

        public Task<bool> ConnectAsync(Dictionary<string, string> config)
        {
            if (config != null && config.TryGetValue("Path", out string path))
            {
                if (!string.IsNullOrEmpty(path))
                {
                    Directory.CreateDirectory(path); // Ensure it exists
                    SaveSetting("LocalBackupPath", path);
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        public Task DisconnectAsync()
        {
            RemoveSetting("LocalBackupPath");
            return Task.CompletedTask;
        }

        public async Task SyncUpAsync(List<(string LocalPath, string RemotePath, string ContentType)> files, IProgress<double> progress)
        {
            string targetBaseDir = GetSetting("LocalBackupPath");
            if (string.IsNullOrEmpty(targetBaseDir) || !Directory.Exists(targetBaseDir))
            {
                throw new DirectoryNotFoundException("Local backup folder path not found or inaccessible.");
            }

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (!File.Exists(file.LocalPath)) continue;

                string destPath = Path.Combine(targetBaseDir, file.RemotePath.Replace('/', Path.DirectorySeparatorChar));
                string destDir = Path.GetDirectoryName(destPath);
                
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                // Copy file, overwrite if existing
                await Task.Run(() => File.Copy(file.LocalPath, destPath, true));
                
                progress?.Report((double)(i + 1) / files.Count * 100);
            }
        }

        public async Task PullDownAsync(IProgress<double> progress)
        {
            string sourceBaseDir = GetSetting("LocalBackupPath");
            if (string.IsNullOrEmpty(sourceBaseDir) || !Directory.Exists(sourceBaseDir))
            {
                throw new DirectoryNotFoundException("Local backup source folder not found or inaccessible.");
            }

            // Sync from backup source back to local app data dir
            string localBaseDir = JournalManager.Instance.DataDir;
            
            // Collect all files in source backup
            var files = new List<string>();
            if (File.Exists(Path.Combine(sourceBaseDir, "notes.json")))
                files.Add("notes.json");
            if (File.Exists(Path.Combine(sourceBaseDir, "categories.json")))
                files.Add("categories.json");

            string sourceNotesDir = Path.Combine(sourceBaseDir, "Notes");
            if (Directory.Exists(sourceNotesDir))
            {
                foreach (var noteFile in Directory.GetFiles(sourceNotesDir, "*.rtf"))
                {
                    files.Add(Path.Combine("Notes", Path.GetFileName(noteFile)));
                }
            }

            for (int i = 0; i < files.Count; i++)
            {
                string relPath = files[i];
                string srcPath = Path.Combine(sourceBaseDir, relPath);
                string destPath = Path.Combine(localBaseDir, relPath);
                string destDir = Path.GetDirectoryName(destPath);

                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                await Task.Run(() => File.Copy(srcPath, destPath, true));
                progress?.Report((double)(i + 1) / files.Count * 100);
            }
        }

        private string GetSetting(string key)
        {
            var settings = ApplicationData.Current.LocalSettings;
            return settings.Values.ContainsKey(key) ? settings.Values[key]?.ToString() : null;
        }

        private void SaveSetting(string key, string value)
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[key] = value;
        }

        private void RemoveSetting(string key)
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey(key))
            {
                settings.Values.Remove(key);
            }
        }
    }
}
