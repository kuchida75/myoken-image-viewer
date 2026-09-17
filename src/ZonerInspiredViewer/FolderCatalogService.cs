using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZonerInspiredViewer
{
    internal sealed class FolderScanSummary
    {
        public int FileCount;
        public int ImageCount;
        public int FolderCount;
    }

    internal sealed class FolderCatalogService
    {
        public Task<FolderScanSummary> ScanFolderAsync(
            string folder,
            Action<List<ImageFileItem>> report,
            CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(
                delegate
                {
                    var summary = new FolderScanSummary();
                    var batch = new List<ImageFileItem>(512);
                    foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            bool isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                            if (isDirectory) summary.FolderCount++;
                            else summary.FileCount++;
                            if (isDirectory || ImageExtensions.IsBrowsableImage(entry.FullName))
                            {
                                if (!isDirectory) summary.ImageCount++;
                                batch.Add(new ImageFileItem
                                {
                                    Path = entry.FullName,
                                    Name = entry.Name,
                                    Extension = isDirectory ? "" : entry.Extension.ToLowerInvariant(),
                                    IsDirectory = isDirectory,
                                    Length = isDirectory ? 0 : ((FileInfo)entry).Length,
                                    CreatedUtc = entry.CreationTimeUtc,
                                    LastWriteUtc = entry.LastWriteTimeUtc
                                });
                            }
                        }
                        catch
                        {
                        }

                        if (batch.Count >= 512)
                        {
                            report(batch);
                            batch = new List<ImageFileItem>(512);
                        }
                    }

                    if (batch.Count > 0)
                    {
                        report(batch);
                    }
                    return summary;
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }
}
