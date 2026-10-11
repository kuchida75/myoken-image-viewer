using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class SelectedSlideshowPlaylist
    {
        private static readonly HashSet<string> ExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".viewer-rotation-backups", ".viewer-enhance-backups", ".viewer-orientation-backups", ".zen-batch-backups" };

        public string[] Paths { get; private set; }
        public int Skipped { get; private set; }

        private SelectedSlideshowPlaylist(string[] paths, int skipped)
        {
            Paths = paths;
            Skipped = skipped;
        }

        public static SelectedSlideshowPlaylist Build(IEnumerable<ImageFileItem> selection, CancellationToken token)
        {
            var paths = new List<string>();
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>(selection.Select(item => item.Path).Reverse());
            int skipped = 0;
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string path = pending.Pop();
                try
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!seenFolders.Add(path)) continue;
                        if (ExcludedFolders.Contains(Path.GetFileName(path))) continue;
                        string[] children = Directory.EnumerateFileSystemEntries(path).ToArray();
                        Array.Sort(children, (left, right) => BrowserSort.CompareNames(Path.GetFileName(left), Path.GetFileName(right)));
                        for (int i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
                    }
                    else if (ImageExtensions.IsBrowsableImage(path) && seenFiles.Add(path)) paths.Add(path);
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            return new SelectedSlideshowPlaylist(paths.ToArray(), skipped);
        }
    }
}
