using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace ZonerInspiredViewer
{
    internal static class DocumentArchive
    {
        internal static byte[] Json(object value, Type type, int limit)
        {
            using (var output = new MemoryStream())
            {
                new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 }).WriteObject(output, value);
                if (output.Length > limit) throw new InvalidDataException("Document metadata is too large.");
                return output.ToArray();
            }
        }
        internal static T Json<T>(byte[] data, int limit)
        {
            if (data.Length == 0 || data.Length > limit) throw new InvalidDataException("Invalid document metadata size.");
            using (var input = new MemoryStream(data, false))
                return (T)new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 }).ReadObject(input);
        }
        internal static byte[] ReadLimited(Stream stream, long limit)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[65536]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                { if (output.Length + count > limit) throw new InvalidDataException("Document exceeds its safety limit."); output.Write(buffer, 0, count); }
                return output.ToArray();
            }
        }
        internal static Dictionary<string, byte[]> Read(Stream stream, long limit, int countLimit)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long total = 0;
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                if (archive.Entries.Count > countLimit) throw new InvalidDataException("Too many document entries.");
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (name.Length == 0 || name.Length > 200 || name.Contains('\\') || name.Contains(':') || name.StartsWith("/")
                        || name.Split('/').Any(p => p == ".." || p == "." || p.Length == 0) || result.ContainsKey(name)
                        || entry.Length < 0 || entry.Length > limit - total) throw new InvalidDataException("Unsafe, duplicate or oversized document entry.");
                    using (var input = entry.Open()) { byte[] data = ReadLimited(input, limit - total); total += data.Length; result.Add(name, data); }
                }
            }
            return result;
        }
        internal static Dictionary<string, byte[]> Read(string path, long limit, int countLimit)
        {
            FileTransferService.CheckAncestors(path);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            { if (input.Length < 1 || input.Length > limit) throw new InvalidDataException("Invalid document size."); return Read(input, limit, countLimit); }
        }
        internal static void Save(string path, IDictionary<string, byte[]> entries, long limit, CancellationToken token)
        {
            path = Path.GetFullPath(path); FileTransferService.CheckAncestors(Path.GetDirectoryName(path));
            if (File.Exists(path) || Directory.Exists(path)) FileTransferService.CheckAncestors(path);
            if (entries.Sum(e => (long)e.Value.Length) > limit) throw new InvalidDataException("Document exceeds its safety limit.");
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".myoken-document-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using (var archive = new ZipArchive(file, ZipArchiveMode.Create, true))
                        foreach (var item in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                        {
                            token.ThrowIfCancellationRequested();
                            using (var output = archive.CreateEntry(item.Key, CompressionLevel.Optimal).Open()) output.Write(item.Value, 0, item.Value.Length);
                        }
                    if (file.Length > limit) throw new InvalidDataException("Compressed document exceeds its safety limit."); file.Flush(true);
                }
                token.ThrowIfCancellationRequested(); FileTransferService.CheckAncestors(Path.GetDirectoryName(path));
                if (File.Exists(path) || Directory.Exists(path)) FileTransferService.CheckAncestors(path);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                temporary = null;
            }
            finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
