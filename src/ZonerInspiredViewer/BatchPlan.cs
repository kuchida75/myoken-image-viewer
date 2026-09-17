using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZonerInspiredViewer
{
    internal static class BatchPlan
    {
        internal static void ValidateName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 240 || name == "." || name == ".." || name != name.TrimEnd(' ', '.')
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("Invalid filename, trailing space/dot or name too long.");
            string stem = name.Split('.')[0].TrimEnd(' ');
            if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9\u00b9\u00b2\u00b3]|LPT[1-9\u00b9\u00b2\u00b3])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new IOException("Windows reserves this filename.");
        }

        internal static string Rename(string path, bool directory, int index, BatchOptions options)
        {
            string original = directory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            long sequence = checked((long)options.Start + (long)index * options.Step);
            string name = options.UseTemplate ? Regex.Replace(options.Template, @"#+|\*|\{([a-z]+)(?::([^{}]+))?\}", match =>
            {
                if (match.Value == "*") return original;
                if (match.Value[0] == '#') return options.Letters ? Letters(sequence).PadLeft(match.Length, 'A') : sequence.ToString("D" + match.Length, CultureInfo.InvariantCulture);
                string format = match.Groups[2].Success ? match.Groups[2].Value : "yyyyMMdd";
                switch (match.Groups[1].Value)
                {
                    case "name": return original;
                    case "folder": return new DirectoryInfo(Path.GetDirectoryName(path)).Name;
                    case "modified": return File.GetLastWriteTime(path).ToString(format, CultureInfo.InvariantCulture);
                    case "created": return File.GetCreationTime(path).ToString(format, CultureInfo.InvariantCulture);
                    case "width": case "height": case "taken":
                        if (directory) throw new IOException("Image metadata tokens cannot be used on folders.");
                        return BatchImages.Token(path, match.Groups[1].Value, format);
                    default: throw new IOException("Unknown metadata token: " + match.Value);
                }
            }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) : original;
            if (name.IndexOf('{') >= 0 || name.IndexOf('}') >= 0) throw new IOException("Invalid metadata token.");
            if (options.Find.Length > 0) name = ReplaceText(name, options.Find, options.Replace, options.MatchCase);
            if (options.RemoveText.Length > 0) name = ReplaceText(name, options.RemoveText, "", options.MatchCase);
            int first = Math.Min(name.Length, options.RemoveStart), count = Math.Max(0, name.Length - first - options.RemoveEnd);
            name = options.Prefix + name.Substring(first, count) + options.Suffix;
            if (options.StripSpaces) name = Regex.Replace(name, @"\s+", "");
            if (options.Case == BatchCase.Lowercase) name = name.ToLowerInvariant();
            if (options.Case == BatchCase.Uppercase) name = name.ToUpperInvariant();
            if (options.Case == BatchCase.TitleCase) name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
            if (name.Length == 0) throw new IOException("The filename would be empty.");
            // Keep the real extension outside every text operation; renaming is not conversion.
            name += directory ? "" : Path.GetExtension(path); ValidateName(name); return name;
        }

        private static string ReplaceText(string value, string find, string replacement, bool matchCase)
        { return Regex.Replace(value, Regex.Escape(find), match => replacement, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromSeconds(1)); }
        private static string Letters(long value)
        {
            if (value < 1) throw new IOException("Letter sequences start at 1 (A).");
            string text = ""; do { value--; text = (char)('A' + value % 26) + text; value /= 26; } while (value > 0); return text;
        }

        internal static List<BatchItem> Create(string[] paths, BatchOptions options, CancellationToken token)
        {
            options.Validate();
            var top = new HashSet<string>(FileTransferService.TopLevelSources(paths), StringComparer.OrdinalIgnoreCase);
            string[] ordered = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(top.Contains).ToArray();
            var rows = new BatchItem[ordered.Length];
            Parallel.For(0, ordered.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount), CancellationToken = token }, index =>
            {
                string source = ordered[index]; var item = new BatchItem { Source = source, IsDirectory = Directory.Exists(source), Status = "Ready", Ready = true }; rows[index] = item;
                try
                {
                    token.ThrowIfCancellationRequested(); FileTransferService.CheckAncestors(source);
                    if (Path.GetDirectoryName(source) == null) throw new IOException("Drive roots are not supported.");
                    item.Revision = FileRevision.Read(source); item.DirectoryModified = Directory.GetLastWriteTimeUtc(source);
                    if (options.Kind == BatchKind.Rename)
                        item.Destination = Path.Combine(Path.GetDirectoryName(source), Rename(source, item.IsDirectory, index, options));
                    else
                    {
                        if (item.IsDirectory) { item.Ready = false; item.Status = "Skipped: folder (not recursive)"; return; }
                        BatchImages.Inspect(item, options, token);
                        string folder = String.IsNullOrWhiteSpace(options.OutputFolder) ? Path.GetDirectoryName(source) : Path.GetFullPath(options.OutputFolder);
                        if (options.Subfolder.Length > 0) folder = Path.Combine(folder, options.Subfolder);
                        string name = Path.GetFileNameWithoutExtension(source) + options.OutputSuffix
                            + (options.Kind == BatchKind.Resize && options.KeepFormat ? Path.GetExtension(source) : options.Format);
                        ValidateName(name); item.Destination = Path.Combine(folder, name);
                        BatchImages.CheckOutputFolder(folder);
                    }
                    if (item.Destination.Length >= 248) throw new IOException("The full output path is too long for this build.");
                    if (options.Kind == BatchKind.Rename && String.Equals(source, item.Destination, StringComparison.Ordinal))
                    { item.Ready = false; item.Status = "Unchanged"; }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { item.Ready = false; item.Status = (options.Kind != BatchKind.Rename && error is NotSupportedException ? "Skipped: " : "Error: ") + error.Message; }
            });
            var sources = new Dictionary<string, BatchItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows) sources.Add(row.Source, row);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var suffixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in rows.Where(row => row.Ready))
            {
                token.ThrowIfCancellationRequested();
                bool selectedTarget = sources.ContainsKey(item.Destination);
                bool occupied = reserved.Contains(item.Destination) || File.Exists(item.Destination) || Directory.Exists(item.Destination);
                bool availableRename = options.Kind == BatchKind.Rename && selectedTarget && !reserved.Contains(item.Destination);
                if (occupied && !availableRename)
                {
                    if (options.Conflict == BatchConflict.KeepBoth)
                    {
                        string initial = item.Destination, extension = item.IsDirectory ? "" : Path.GetExtension(initial);
                        string stem = initial.Substring(0, initial.Length - extension.Length); int number;
                        if (!suffixes.TryGetValue(initial, out number)) number = 2;
                        do { item.Destination = stem + " (" + number++ + ")" + extension; }
                        while (reserved.Contains(item.Destination) || sources.ContainsKey(item.Destination) || File.Exists(item.Destination) || Directory.Exists(item.Destination));
                        suffixes[initial] = number; item.Status = "Ready: keep both";
                    }
                    else if (options.Conflict == BatchConflict.Overwrite && !reserved.Contains(item.Destination) && !Directory.Exists(item.Destination)
                        && (!selectedTarget || FileTransferService.SamePath(item.Source, item.Destination)))
                    { item.Overwrite = true; item.DestinationRevision = FileRevision.Read(item.Destination); item.Status = "Overwrite (backup kept)"; }
                    else { item.Ready = false; item.Status = options.Conflict == BatchConflict.Skip ? "Skipped: name conflict" : "Error: output name conflicts with another item"; }
                }
                if (item.Ready && item.Destination.Length >= 248) { item.Ready = false; item.Status = "Error: output path is too long"; }
                if (item.Ready) reserved.Add(item.Destination);
            }
            if (options.Kind == BatchKind.Rename)
            {
                // Propagate blocked rename dependencies once, including unchanged selected targets.
                var reverse = rows.Where(row => row.Ready && sources.ContainsKey(row.Destination)).ToLookup(row => row.Destination, StringComparer.OrdinalIgnoreCase);
                var blocked = new Queue<BatchItem>(rows.Where(row => !row.Ready));
                while (blocked.Count > 0) foreach (var dependent in reverse[blocked.Dequeue().Source])
                    if (dependent.Ready) { dependent.Ready = false; dependent.Status = "Error: destination belongs to an item that will not move"; blocked.Enqueue(dependent); }
            }
            return rows.ToList();
        }
    }
}
