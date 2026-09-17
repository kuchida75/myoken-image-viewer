using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZonerInspiredViewer
{
    internal enum ConflictAction { Rename, Skip, Cancel }

    internal sealed class TransferredItem
    {
        public string Source;
        public string Destination;
        public bool IsDirectory;
    }

    internal sealed class TransferResult
    {
        public readonly List<TransferredItem> Completed = new List<TransferredItem>();
        public readonly List<string> Errors = new List<string>();
        public int Skipped;
        public bool Canceled;
    }

    internal static class FileTransferService
    {
        internal static TransferredItem Rename(string path, string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name != name.TrimEnd(' ', '.')
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException("Enter a single valid name without a path or trailing spaces/dots.");
            path = Path.GetFullPath(path);
            if (Directory.GetParent(path) == null) throw new IOException("Drive roots cannot be renamed.");
            CheckAncestors(path);
            bool directory = Directory.Exists(path);
            string target = Path.Combine(Path.GetDirectoryName(path), name);
            if (!SamePath(path, target) && Exists(target)) throw new IOException("An item with that name already exists.");
            if (directory) Directory.Move(path, target); else File.Move(path, target);
            return new TransferredItem { Source = path, Destination = target, IsDirectory = directory };
        }

        internal static void Recycle(string path)
        {
            path = Path.GetFullPath(path);
            if (Directory.GetParent(path) == null) throw new IOException("Drive roots cannot be recycled.");
            CheckAncestors(path);
            if (Directory.Exists(path))
            {
                CheckTree(path);
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            }
            else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }

        public static bool SamePath(string left, string right)
        {
            return String.Equals(Path.GetFullPath(left).TrimEnd('\\'), Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWithin(string path, string folder)
        {
            return SamePath(path, folder) || Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static string[] TopLevelSources(IEnumerable<string> sources)
        {
            var unique = new HashSet<string>(sources.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p =>
            {
                string full = Path.GetFullPath(p);
                return full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd('\\', '/') : full;
            }), StringComparer.OrdinalIgnoreCase);
            return unique.Where(path =>
            {
                DirectoryInfo parent = Directory.GetParent(path);
                while (parent != null)
                {
                    if (unique.Contains(parent.FullName)) return false;
                    parent = parent.Parent;
                }
                return true;
            }).ToArray();
        }

        public static TransferResult Execute(IEnumerable<string> sources, string folder, bool move,
            Func<string, string, ConflictAction> confirmRename, Action<int, int, string> progress = null)
        {
            var result = new TransferResult();
            string[] paths;
            try
            {
                folder = Path.GetFullPath(folder);
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Destination folder is unavailable.");
                CheckAncestors(folder);
                paths = TopLevelSources(sources);
            }
            catch (Exception ex) { result.Errors.Add(ex.Message); return result; }

            for (int index = 0; index < paths.Length; index++)
            {
                string source = paths[index];
                try
                {
                    bool directory = Directory.Exists(source);
                    if (!directory && !File.Exists(source)) throw new FileNotFoundException("Source is unavailable.", source);
                    if (Directory.GetParent(source) == null) throw new IOException("Drive roots cannot be transferred.");
                    CheckAncestors(source);
                    if (directory && IsWithin(folder, source)) throw new IOException("A folder cannot be copied or moved into itself.");
                    string target = Path.Combine(folder, Path.GetFileName(source.TrimEnd('\\')));
                    if (move && SamePath(source, target)) { result.Skipped++; continue; }
                    if (directory) CheckTree(source);
                    if (progress != null) progress(index, paths.Length, Path.GetFileName(source));

                    while (Exists(target))
                    {
                        string proposed = UniqueName(folder, Path.GetFileName(source), directory);
                        ConflictAction action = confirmRename(source, proposed);
                        if (action == ConflictAction.Cancel) { result.Canceled = true; return result; }
                        if (action == ConflictAction.Skip) { result.Skipped++; target = null; break; }
                        target = proposed;
                    }
                    if (target == null) continue;
                    // The platform APIs are deliberately given overwrite:false, including late races.
                    if (directory)
                    {
                        if (move) FileSystem.MoveDirectory(source, target, false);
                        else FileSystem.CopyDirectory(source, target, false);
                    }
                    else
                    {
                        if (move) FileSystem.MoveFile(source, target, false);
                        else File.Copy(source, target, false);
                    }
                    result.Completed.Add(new TransferredItem { Source = source, Destination = target, IsDirectory = directory });
                }
                catch (Exception ex) { result.Errors.Add(Path.GetFileName(source) + ": " + ex.Message); }
            }
            return result;
        }

        private static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }

        private static string UniqueName(string folder, string name, bool directory)
        {
            string extension = directory ? "" : Path.GetExtension(name);
            string stem = directory ? name : Path.GetFileNameWithoutExtension(name);
            for (int suffix = 2; ; suffix++)
            {
                string path = Path.Combine(folder, stem + " (" + suffix + ")" + extension);
                if (!Exists(path)) return path;
            }
        }

        internal static void CheckAncestors(string path)
        {
            for (string current = path; current != null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files and junction folders are not supported by file transfer.");
        }

        private static void CheckTree(string source)
        {
            var pending = new Stack<string>();
            pending.Push(source);
            while (pending.Count > 0)
                foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The folder contains a linked file or junction: " + path);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
        }
    }
}
