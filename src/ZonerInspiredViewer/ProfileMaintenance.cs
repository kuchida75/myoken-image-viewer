using System;
using System.IO;

namespace ZonerInspiredViewer
{
    internal static class ProfileMaintenance
    {
        internal static void CheckPath(string root, string path)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            path = Path.GetFullPath(path);
            if (root.Length <= Path.GetPathRoot(root).Length
                || (!String.Equals(root, path, StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Maintenance target is outside its designated data folder.");
            for (string current = path; !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Maintenance will not follow linked folders or files: " + current);
        }

        internal static void ClearDirectory(string root)
        {
            root = Path.GetFullPath(root);
            CheckPath(root, root);
            if (!Directory.Exists(root)) return;
            // Validate the entire tree before deleting anything; never traverse junctions.
            ValidateTree(root, root);
            ClearContents(root, root);
        }

        private static void ValidateTree(string root, string folder)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
            {
                CheckPath(root, entry);
                if (Directory.Exists(entry)) ValidateTree(root, entry);
            }
        }

        private static void ClearContents(string root, string folder)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
            {
                CheckPath(root, entry);
                if (Directory.Exists(entry)) { ClearContents(root, entry); Directory.Delete(entry, false); }
                else File.Delete(entry);
            }
        }
    }
}
