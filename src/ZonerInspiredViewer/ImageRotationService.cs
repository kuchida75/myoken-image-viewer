using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal struct FileRevision
    {
        [DataMember] public long Length;
        [DataMember] public long Modified;
        public static FileRevision Read(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? new FileRevision { Length = info.Length, Modified = info.LastWriteTimeUtc.Ticks }
                    : new FileRevision { Length = -1 };
            }
            catch (IOException) { return new FileRevision { Length = -1 }; }
            catch (UnauthorizedAccessException) { return new FileRevision { Length = -1 }; }
        }
        public bool Matches(string path) { return Length >= 0 && Equals(Read(path)); }
    }

    internal static class ImageRotationService
    {
        public static bool CanSave(string path)
        {
            string extension = Path.GetExtension(path ?? "");
            return String.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
        }

        public static string BackupFolder(string path) { return Path.Combine(Path.GetDirectoryName(path), ".viewer-rotation-backups"); }

        public static string Save(string path, int turns, FileRevision expected, CancellationToken token)
        {
            turns = ImageViewport.NormalizeRotation(turns);
            if (turns == 0) return null;
            if (!CanSave(path)) throw new NotSupportedException("Saving rotation currently supports single-frame JPEG and PNG only. The view rotation is retained.");
            path = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".viewer-rotation-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new SharedFileGate(path))
            {
                token.ThrowIfCancellationRequested();
                if (!expected.Matches(path)) throw new IOException("The file changed since it was displayed. Reopen it before saving rotation.");
                if ((File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("Read-only files and file links cannot be overwritten. The view rotation is retained.");
                string backups = BackupFolder(path);
                Directory.CreateDirectory(backups);
                if ((File.GetAttributes(backups) & FileAttributes.ReparsePoint) != 0) throw new IOException("The backup directory cannot be a link.");
                string name = Path.GetFileName(path);
                string backup = Path.Combine(backups, name.Substring(0, Math.Min(80, name.Length)) + "."
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak");
                try
                {
                    byte[] digest;
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
                        input.Position = 0;
                        bool png = String.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
                        if (png && IsAnimatedPng(input)) throw new NotSupportedException("Animated PNG rotation cannot be saved. The original animation was not changed.");
                        input.Position = 0;
                        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                        if (png ? !(decoder is PngBitmapDecoder) : !(decoder is JpegBitmapDecoder))
                            throw new NotSupportedException("The file content does not match its JPEG/PNG extension. It was not converted or overwritten.");
                        if (decoder.Frames.Count != 1) throw new NotSupportedException("Multi-frame images cannot be overwritten by the rotation saver.");
                        BitmapFrame frame = decoder.Frames[0];
                        var rotated = new TransformedBitmap(frame, new RotateTransform(turns * 90)); rotated.Freeze();
                        BitmapMetadata metadata = frame.Metadata as BitmapMetadata;
                        if (metadata != null)
                        {
                            metadata = metadata.Clone();
                            if (!png)
                            {
                                metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);
                                SetExisting(metadata, "/app1/ifd/exif/{ushort=40962}", (uint)rotated.PixelWidth);
                                SetExisting(metadata, "/app1/ifd/exif/{ushort=40963}", (uint)rotated.PixelHeight);
                                SetExisting(metadata, "/app1/ifd/{ushort=256}", (uint)rotated.PixelWidth);
                                SetExisting(metadata, "/app1/ifd/{ushort=257}", (uint)rotated.PixelHeight);
                                SetExisting(metadata, "/xmp/tiff:Orientation", "1");
                                if (metadata.ContainsQuery("/app1/ifd/next")) metadata.RemoveQuery("/app1/ifd/next");
                            }
                        }
                        BitmapEncoder encoder = png ? (BitmapEncoder)new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 95 };
                        encoder.Frames.Add(BitmapFrame.Create(rotated, null, metadata, frame.ColorContexts));
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { encoder.Save(output); output.Flush(true); }
                        token.ThrowIfCancellationRequested();
                        using (var verify = File.OpenRead(temporary))
                        {
                            var decoded = BitmapDecoder.Create(verify, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                            if (decoded.Frames.Count != 1 || decoded.Frames[0].PixelWidth != rotated.PixelWidth || decoded.Frames[0].PixelHeight != rotated.PixelHeight)
                                throw new IOException("The rotated output could not be verified.");
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (!expected.Matches(path)) throw new IOException("The file changed while rotation was being saved. It was not overwritten.");
                    // Reject concurrent edits even if another editor preserved timestamp and length.
                    using (var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    using (var sha = SHA256.Create())
                    {
                        if (!System.Linq.Enumerable.SequenceEqual(digest, sha.ComputeHash(current)))
                            throw new IOException("The source content changed during rotation. It was not overwritten.");
                        File.Replace(temporary, path, backup, false);
                    }
                    return backup;
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static void SetExisting(BitmapMetadata metadata, string query, object value)
        { if (metadata.ContainsQuery(query)) metadata.SetQuery(query, value); }

        internal static bool IsAnimatedPng(Stream input)
        {
            input.Position = 8;
            var reader = new BinaryReader(input);
            while (input.Position + 12 <= input.Length)
            {
                byte[] length = reader.ReadBytes(4);
                long count = ((long)length[0] << 24) | ((long)length[1] << 16) | ((long)length[2] << 8) | length[3];
                string kind = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (kind == "acTL") return true;
                if (kind == "IDAT" || kind == "IEND") return false;
                if (count > input.Length - input.Position - 4) throw new FileFormatException("Invalid PNG chunk length.");
                input.Seek(count + 4, SeekOrigin.Current);
            }
            throw new FileFormatException("Invalid PNG image.");
        }
    }
}
