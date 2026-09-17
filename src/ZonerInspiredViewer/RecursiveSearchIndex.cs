using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZonerInspiredViewer
{
    internal sealed class SearchSnapshot
    {
        public string Root { get; set; }
        public ImageFileItem[] Items { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public int SkippedFolders { get; set; }
        public int SkippedLinks { get; set; }
        public bool FromCache { get; set; }
        public string CacheWarning { get; set; }
    }

    internal sealed class SearchProgress
    {
        public int Folders { get; set; }
        public int Items { get; set; }
    }

    internal sealed class RecursiveSearchIndex
    {
        private const int Magic = 0x5A534931;
        private const int MaxItems = 2000000;
        private readonly string _excludedRoot;
        private readonly string _cacheDirectory;
        internal int WorkerCount { get; private set; }

        public RecursiveSearchIndex(string profile, int workers)
        {
            _excludedRoot = CanonicalPath(profile);
            _cacheDirectory = Path.Combine(profile, "search-index");
            WorkerCount = Math.Max(1, Math.Min(8, workers));
        }

        internal static string CanonicalPath(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }

        internal static bool InScope(string path, string root)
        {
            return String.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsExcluded(string path) { return InScope(path, _excludedRoot); }

        internal string CachePath(string root)
        {
            using (var sha = SHA256.Create())
                return Path.Combine(_cacheDirectory, BitConverter.ToString(sha.ComputeHash(
                    Encoding.UTF8.GetBytes(CanonicalPath(root).ToUpperInvariant()))).Replace("-", "") + ".idx");
        }

        public SearchSnapshot Load(string root, CancellationToken token)
        {
            root = CanonicalPath(root);
            try
            {
                using (var stream = new FileStream(CachePath(root), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (stream.Length > 512L * 1024 * 1024 || reader.ReadInt32() != Magic) return null;
                    if (!String.Equals(ReadText(reader), root, StringComparison.OrdinalIgnoreCase)) return null;
                    var result = new SearchSnapshot { Root = root, UpdatedUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
                        SkippedFolders = reader.ReadInt32(), SkippedLinks = reader.ReadInt32(), FromCache = true };
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaxItems || result.SkippedFolders < 0 || result.SkippedLinks < 0) return null;
                    var items = new ImageFileItem[count];
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string path = ReadText(reader);
                        bool directory = reader.ReadBoolean();
                        long length = reader.ReadInt64();
                        DateTime created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                        DateTime modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                        if (!Path.IsPathRooted(path) || !String.Equals(CanonicalPath(path), path, StringComparison.OrdinalIgnoreCase)
                            || !InScope(path, root) || String.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                            || IsExcluded(path) || !seen.Add(path) || length < 0 || (!directory && !ImageExtensions.IsBrowsableImage(path))) return null;
                        items[i] = new ImageFileItem { Path = path, Name = Path.GetFileName(path), IsDirectory = directory,
                            Extension = directory ? "" : Path.GetExtension(path).ToLowerInvariant(), Length = length,
                            CreatedUtc = created, LastWriteUtc = modified };
                    }
                    if (stream.Position != stream.Length) return null;
                    result.Items = items;
                    return result;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (SecurityException) { return null; }
            catch (ArgumentException) { return null; }
        }

        // Explicit string lengths keep a damaged cache from requesting an unbounded allocation.
        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 32768 * 4) throw new InvalidDataException("Invalid index string.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            writer.Write(bytes.Length); writer.Write(bytes);
        }

        private void Save(SearchSnapshot snapshot, CancellationToken token)
        {
            Directory.CreateDirectory(_cacheDirectory);
            string path = CachePath(snapshot.Root), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(Magic); WriteText(writer, snapshot.Root); writer.Write(snapshot.UpdatedUtc.Ticks);
                    writer.Write(snapshot.SkippedFolders); writer.Write(snapshot.SkippedLinks); writer.Write(snapshot.Items.Length);
                    foreach (ImageFileItem item in snapshot.Items)
                    {
                        token.ThrowIfCancellationRequested();
                        WriteText(writer, item.Path); writer.Write(item.IsDirectory); writer.Write(item.Length);
                        writer.Write(item.CreatedUtc.Ticks); writer.Write(item.LastWriteUtc.Ticks);
                    }
                    writer.Flush(); stream.Flush(true);
                }
                using (new SharedFileGate(path))
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public async Task<SearchSnapshot> RefreshAsync(string root, Action<SearchProgress> progress, CancellationToken token)
        {
            root = CanonicalPath(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The search folder is unavailable.");
            var batches = new ConcurrentBag<List<ImageFileItem>>();
            int pending = 1, directories = 0, count = 0, skipped = 0, links = 0;
            long nextReport = 0;
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var queue = new BlockingCollection<string>())
            {
                queue.Add(root);
                var workers = new List<Task>();
                for (int i = 0; i < WorkerCount; i++)
                    workers.Add(Task.Factory.StartNew(delegate
                    {
                        ThreadPriority previous = Thread.CurrentThread.Priority;
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                        var items = new List<ImageFileItem>();
                        batches.Add(items);
                        try
                        {
                            foreach (string folder in queue.GetConsumingEnumerable(stop.Token))
                            {
                                try
                                {
                                    bool linked = !String.Equals(folder, root, StringComparison.OrdinalIgnoreCase)
                                        && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;
                                    if (linked) Interlocked.Increment(ref links);
                                    if (!linked && !IsExcluded(folder))
                                        foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                                        {
                                            stop.Token.ThrowIfCancellationRequested();
                                            try
                                            {
                                                string path = entry.FullName;
                                                if (IsExcluded(path)) continue;
                                                FileAttributes attributes = entry.Attributes;
                                                bool directory = (attributes & FileAttributes.Directory) != 0;
                                                bool link = (attributes & FileAttributes.ReparsePoint) != 0;
                                                if (link) { Interlocked.Increment(ref links); continue; }
                                                if (directory || ImageExtensions.IsBrowsableImage(path))
                                                {
                                                    if (Interlocked.Increment(ref count) > MaxItems)
                                                        throw new InvalidOperationException("The search index limit of 2,000,000 entries was reached. Choose a smaller starting folder.");
                                                    items.Add(new ImageFileItem { Path = path, Name = entry.Name, IsDirectory = directory,
                                                        Extension = directory ? "" : entry.Extension.ToLowerInvariant(),
                                                        Length = directory ? 0 : ((FileInfo)entry).Length,
                                                        CreatedUtc = entry.CreationTimeUtc, LastWriteUtc = entry.LastWriteTimeUtc });
                                                }
                                                if (directory)
                                                {
                                                    Interlocked.Increment(ref pending);
                                                    queue.Add(path, stop.Token);
                                                }
                                            }
                                            catch (IOException) { Interlocked.Increment(ref skipped); }
                                            catch (UnauthorizedAccessException) { Interlocked.Increment(ref skipped); }
                                            catch (SecurityException) { Interlocked.Increment(ref skipped); }
                                        }
                                }
                                catch (IOException) { Interlocked.Increment(ref skipped); }
                                catch (UnauthorizedAccessException) { Interlocked.Increment(ref skipped); }
                                catch (SecurityException) { Interlocked.Increment(ref skipped); }
                                finally
                                {
                                    int done = Interlocked.Increment(ref directories);
                                    long now = DateTime.UtcNow.Ticks, last = Interlocked.Read(ref nextReport);
                                    if (progress != null && now > last && Interlocked.CompareExchange(ref nextReport,
                                        now + TimeSpan.TicksPerMillisecond * 350, last) == last)
                                        progress(new SearchProgress { Folders = done, Items = Volatile.Read(ref count) });
                                    if (Interlocked.Decrement(ref pending) == 0) queue.CompleteAdding();
                                }
                            }
                        }
                        catch { stop.Cancel(); throw; }
                        finally { Thread.CurrentThread.Priority = previous; }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The search folder is unavailable.");
            var snapshot = new SearchSnapshot { Root = root, Items = batches.SelectMany(batch => batch).ToArray(),
                UpdatedUtc = DateTime.UtcNow, SkippedFolders = skipped, SkippedLinks = links };
            try { await Task.Run(delegate { Save(snapshot, token); }, token).ConfigureAwait(false); }
            catch (IOException) { snapshot.CacheWarning = "Index could not be saved; results are available for this window."; }
            catch (UnauthorizedAccessException) { snapshot.CacheWarning = "Index could not be saved; results are available for this window."; }
            catch (SecurityException) { snapshot.CacheWarning = "Index could not be saved; results are available for this window."; }
            return snapshot;
        }

        public Task<List<ImageFileItem>> QueryAsync(SearchSnapshot snapshot, string query, BrowserSortField sort, bool descending, CancellationToken token)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                if (snapshot == null || snapshot.Items.Length == 0 || String.IsNullOrWhiteSpace(query)) return new List<ImageFileItem>();
                query = query.Trim();
                var matches = new ConcurrentBag<List<ImageFileItem>>();
                Parallel.ForEach(Partitioner.Create(0, snapshot.Items.Length, 4096),
                    new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = WorkerCount }, range =>
                    {
                        var batch = new List<ImageFileItem>();
                        for (int i = range.Item1; i < range.Item2; i++)
                        {
                            if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                            ImageFileItem item = snapshot.Items[i];
                            if (item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) batch.Add(item);
                        }
                        matches.Add(batch);
                    });
                var result = matches.SelectMany(batch => batch).ToList();
                token.ThrowIfCancellationRequested();
                result.Sort(new BrowserSort(sort, descending));
                token.ThrowIfCancellationRequested();
                return result;
            }, token);
        }
    }
}
