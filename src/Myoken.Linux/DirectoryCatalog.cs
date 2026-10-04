using Myoken.Core;

namespace Myoken.Linux;

internal sealed record FolderEntry(string Path, bool IsLink);

internal static class DirectoryCatalog
{
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool Contains(string root, string path) =>
        StringComparer.Ordinal.Equals(root, path) ||
        path.StartsWith(root == "/" ? "/" : root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    public static Task<FolderEntry[]> ReadAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0
        };
        var entries = new List<FolderEntry>();
        foreach (var child in Directory.EnumerateDirectories(path, "*", options))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                entries.Add(new FolderEntry(child, File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)));
            }
            catch (FileNotFoundException) { } // A directory may disappear during enumeration.
            catch (DirectoryNotFoundException) { }
        }
        entries.Sort((a, b) => NaturalNameComparer.Instance.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path)));
        token.ThrowIfCancellationRequested();
        return entries.ToArray();
    }, token);
}
