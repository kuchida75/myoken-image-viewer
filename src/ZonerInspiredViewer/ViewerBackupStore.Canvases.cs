using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;

namespace ZonerInspiredViewer
{
    [DataContract] internal sealed class BackupCanvasEntry
    {
        [DataMember] internal string OriginalPath;
        [DataMember] internal string Entry;
    }
    internal static partial class ViewerBackupStore
    {
        private const long ArchiveLimit = 256L * 1024 * 1024;
        private static IEnumerable<SessionState> AllStates(ViewerBackupDocument document)
        { return new[] { document.Workspace }.Concat(document.SavedSessions.Select(s => s.State)); }
        private static IEnumerable<string> CanvasPaths(ViewerBackupDocument document)
        { return AllStates(document).SelectMany(s => (s.FaceCanvasPaths ?? new List<string>()).Concat(new[] { s.LastFaceCanvas })).Where(FaceCanvasStore.IsPath).Distinct(StringComparer.OrdinalIgnoreCase); }
        private static void SaveArchive(string path, ViewerBackupDocument document)
        {
            Validate(document, false); var paths = CanvasPaths(document).ToArray();
            if (paths.Length > 64) throw new InvalidDataException("A ZIP backup supports at most 64 distinct face canvases.");
            var files = new Dictionary<string, byte[]> { { "viewer-backup.json", DocumentArchive.Json(document, typeof(ViewerBackupDocument), (int)MaximumBytes) } };
            var manifest = new List<BackupCanvasEntry>();
            if (document.Workspace.IncludeFaceCanvasesInBackup != false)
                foreach (string file in paths)
                {
                    if (!File.Exists(file)) continue;
                    FileTransferService.CheckAncestors(file); byte[] bytes;
                    using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) bytes = DocumentArchive.ReadLimited(input, FaceCanvasStore.Limit);
                    FaceCanvasStore.LoadBytes(bytes);
                    string entry = "canvases/" + manifest.Count.ToString("D3") + ".myokenfaces";
                    files.Add(entry, bytes); manifest.Add(new BackupCanvasEntry { OriginalPath = file, Entry = entry });
                    if (files.Sum(p => (long)p.Value.Length) > ArchiveLimit) throw new InvalidDataException("Face canvases exceed the 256 MiB backup limit.");
                }
            files.Add("canvases.json", DocumentArchive.Json(manifest, typeof(List<BackupCanvasEntry>), 1024 * 1024));
            DocumentArchive.Save(path, files, ArchiveLimit, CancellationToken.None);
        }
        private static ViewerBackupDocument LoadArchive(string path)
        {
            var files = DocumentArchive.Read(path, ArchiveLimit, 66); byte[] json, index;
            if (!files.TryGetValue("viewer-backup.json", out json) || !files.TryGetValue("canvases.json", out index)) throw new InvalidDataException("Unsupported viewer ZIP backup.");
            var document = DocumentArchive.Json<ViewerBackupDocument>(json, (int)MaximumBytes); Validate(document, true);
            var manifest = DocumentArchive.Json<List<BackupCanvasEntry>>(index, 1024 * 1024);
            if (manifest == null || manifest.Count > 64 || files.Count != manifest.Count + 2) throw new InvalidDataException("Invalid canvas backup index.");
            document.CanvasFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var references = new HashSet<string>(CanvasPaths(document), StringComparer.OrdinalIgnoreCase); var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest)
            {
                byte[] bytes;
                if (item == null || !FaceCanvasStore.ValidPath(item.OriginalPath) || !references.Contains(item.OriginalPath) || !FaceCanvasStore.IsPath(item.Entry)
                    || !item.Entry.StartsWith("canvases/", StringComparison.Ordinal) || !entries.Add(item.Entry) || document.CanvasFiles.ContainsKey(item.OriginalPath) || !files.TryGetValue(item.Entry, out bytes))
                    throw new InvalidDataException("Invalid embedded face canvas.");
                FaceCanvasStore.LoadBytes(bytes); document.CanvasFiles.Add(item.OriginalPath, bytes);
            }
            return document;
        }
        internal static void RestoreCanvases(ViewerBackupDocument document, string profileRoot)
        {
            if (document.CanvasFiles == null || document.CanvasFiles.Count == 0) return;
            string root = Path.Combine(profileRoot, "face-canvases"); FileTransferService.CheckAncestors(profileRoot);
            if (Directory.Exists(root)) FileTransferService.CheckAncestors(root); Directory.CreateDirectory(root); FileTransferService.CheckAncestors(root);
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.CanvasFiles)
            {
                string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(item.Value)).Replace("-", "").ToLowerInvariant();
                string destination = Path.Combine(root, "canvas-" + hash + ".myokenfaces"); if (File.Exists(destination)) FileTransferService.CheckAncestors(destination);
                if (!File.Exists(destination))
                {
                    string temporary = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try { using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(item.Value, 0, item.Value.Length); output.Flush(true); } File.Move(temporary, destination); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                else
                {
                    using (var input = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var sha = SHA256.Create()) if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant() != hash) throw new IOException("Existing restored canvas has changed.");
                }
                replacements.Add(item.Key, destination);
            }
            foreach (var state in AllStates(document))
            {
                if (state.FaceCanvasPaths != null) state.FaceCanvasPaths = state.FaceCanvasPaths.Select(p => replacements.ContainsKey(p) ? replacements[p] : p).ToList();
                if (state.LastFaceCanvas != null && replacements.ContainsKey(state.LastFaceCanvas)) state.LastFaceCanvas = replacements[state.LastFaceCanvas];
            }
        }
    }
}
