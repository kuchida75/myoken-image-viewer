namespace Myoken.Linux;

// Metadata is re-read on acquisition, not trusted from a prior directory scan.
// This deliberately is not a content hash or a filesystem watcher. Same-size,
// same-mtime edits require explicit Refresh/F5, which clears the cache.
internal readonly record struct ThumbnailFileStamp(long Length, long ModifiedUtcTicks)
{
    public static ThumbnailFileStamp Read(string fullPath)
    {
        var file = new FileInfo(fullPath);
        file.Refresh();
        if (!file.Exists) throw new FileNotFoundException("Image is missing or inaccessible.", fullPath);
        return new(file.Length, file.LastWriteTimeUtc.Ticks);
    }
}

// Record string equality is ordinal/case-sensitive, independently of UI sorting.
internal readonly record struct ThumbnailKey(string Path, int Edge, ThumbnailFileStamp Stamp);
