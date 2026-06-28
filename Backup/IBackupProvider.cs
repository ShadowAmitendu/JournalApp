using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace JournalApp.Backup
{
    public interface IBackupProvider
    {
        string Name { get; }
        string DisplayName { get; }
        bool IsConnected { get; }
        
        Task<bool> ConnectAsync(Dictionary<string, string> config);
        Task DisconnectAsync();
        
        Task SyncUpAsync(List<(string LocalPath, string RemotePath, string ContentType)> files, IProgress<double> progress);
        Task PullDownAsync(IProgress<double> progress);
    }
}
