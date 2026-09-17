using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class BatchRenameRecord
    {
        [DataMember] public string Source, Destination, Temporary, Current;
        [DataMember] public bool IsDirectory;
    }
    internal static class BatchExecutor
    {
        internal static BatchResult Execute(IList<BatchItem> plan, BatchOptions options, string profile, CancellationToken token, Action<BatchItem, string> progress)
        {
            options.Validate();
            if (plan.Any(item => item.Included && item.Status.StartsWith("Error:", StringComparison.Ordinal))) throw new IOException("Fix or exclude preview errors before running the batch.");
            if (options.Kind == BatchKind.Rename) return Rename(plan, profile, token, progress);
            var result = new BatchResult { Skipped = plan.Count(item => !item.Ready || !item.Included) };
            try
            {
                Parallel.ForEach(plan.Where(item => item.Ready && item.Included), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 4)), CancellationToken = token }, item =>
                {
                    try
                    {
                        Report(item, "Processing", progress); BatchImages.Export(item, options, token);
                        lock (result) result.Completed.Add(new TransferredItem { Source = item.Source, Destination = item.Destination });
                        Report(item, "Completed", progress);
                    }
                    catch (OperationCanceledException) { Report(item, "Canceled", progress); throw; }
                    catch (Exception error) { lock (result) result.Errors.Add(item.CurrentName + ": " + error.Message); Report(item, "Failed: " + error.Message, progress); }
                });
            }
            catch (OperationCanceledException) { result.Canceled = true; }
            return result;
        }

        private static BatchResult Rename(IList<BatchItem> plan, string profile, CancellationToken token, Action<BatchItem, string> progress)
        {
            var result = new BatchResult { Skipped = plan.Count(item => !item.Ready || !item.Included) };
            var items = plan.Where(item => item.Ready && item.Included).ToList();
            if (items.Count == 0) return result;
            var moving = new HashSet<string>(items.Select(item => item.Source), StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
                if ((File.Exists(item.Destination) || Directory.Exists(item.Destination)) && !moving.Contains(item.Destination))
                { result.Errors.Add("Destination belongs to an excluded or changed item: " + item.Destination); return result; }
            var records = items.Select(item => new BatchRenameRecord { Source = item.Source, Destination = item.Destination,
                Temporary = Path.Combine(Path.GetDirectoryName(item.Source), ".zen-rename-" + Guid.NewGuid().ToString("N")), Current = item.Source, IsDirectory = item.IsDirectory }).ToList();
            string journalFolder = Path.Combine(profile, "batch-logs"); ProfileMaintenance.CheckPath(profile, journalFolder); Directory.CreateDirectory(journalFolder);
            result.Journal = Path.Combine(journalFolder, "rename-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            using (new SharedFileGate(Path.Combine(profile, "batch-rename")))
            {
                // A durable source/temp/destination map exists before the first move, including for crash recovery.
                SessionStore.WriteObject(result.Journal, records, typeof(List<BatchRenameRecord>));
                try
                {
                    foreach (var item in items)
                    {
                        token.ThrowIfCancellationRequested(); FileTransferService.CheckAncestors(item.Source);
                        if (item.IsDirectory && FileTransferService.IsWithin(profile, item.Source)) throw new IOException("The active viewer profile and its parents cannot be renamed.");
                        if (item.IsDirectory ? !Directory.Exists(item.Source) || Directory.GetLastWriteTimeUtc(item.Source) != item.DirectoryModified : !item.Revision.Matches(item.Source))
                            throw new IOException("Source changed since preview: " + item.CurrentName);
                        if (!item.IsDirectory && (File.GetAttributes(item.Source) & FileAttributes.ReadOnly) != 0) throw new IOException("Source is read-only: " + item.CurrentName);
                    }
                    for (int i = 0; i < records.Count; i++)
                    {
                        token.ThrowIfCancellationRequested(); var record = records[i];
                        Move(record.Current, record.Temporary, record.IsDirectory); record.Current = record.Temporary; Report(items[i], "Preparing rename", progress);
                    }
                    for (int i = 0; i < records.Count; i++)
                    {
                        token.ThrowIfCancellationRequested(); var record = records[i];
                        Move(record.Current, record.Destination, record.IsDirectory); record.Current = record.Destination; Report(items[i], "Completed", progress);
                    }
                }
                catch (Exception error)
                {
                    result.Canceled = error is OperationCanceledException;
                    if (!result.Canceled) result.Errors.Add(error.Message);
                    // Return committed names to staging first so cycles can be rolled back without clobbering.
                    foreach (var record in records.Where(record => record.Current == record.Destination))
                        try { Move(record.Current, record.Temporary, record.IsDirectory); record.Current = record.Temporary; }
                        catch (Exception restore) { result.Errors.Add("Recovery required: " + record.Current + ": " + restore.Message); }
                    foreach (var record in records.Where(record => record.Current == record.Temporary))
                        try { Move(record.Current, record.Source, record.IsDirectory); record.Current = record.Source; }
                        catch (Exception restore) { result.Errors.Add("Recovery required: " + record.Current + ": " + restore.Message); }
                    for (int i = 0; i < records.Count; i++) Report(items[i], records[i].Current == records[i].Source ? "Rolled back" : "Recovery required: " + records[i].Current, progress);
                }
                foreach (var record in records.Where(record => record.Current != record.Source)) result.Completed.Add(new TransferredItem { Source = record.Source, Destination = record.Current, IsDirectory = record.IsDirectory });
                try { SessionStore.WriteObject(result.Journal, records, typeof(List<BatchRenameRecord>)); }
                catch (Exception error) { result.Errors.Add("Could not update recovery log: " + error.Message); }
            }
            return result;
        }
        private static void Move(string source, string destination, bool directory)
        { FileTransferService.CheckAncestors(source); FileTransferService.CheckAncestors(Path.GetDirectoryName(destination)); if (directory) Directory.Move(source, destination); else File.Move(source, destination); }
        private static void Report(BatchItem item, string status, Action<BatchItem, string> progress)
        { if (progress == null) item.Status = status; else progress(item, status); }
    }
}
