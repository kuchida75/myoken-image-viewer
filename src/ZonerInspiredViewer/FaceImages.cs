using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ZonerInspiredViewer
{
    internal sealed class FacePixels
    {
        internal readonly int Width, Height;
        internal readonly byte[] Data;
        internal FacePixels(int w, int h, byte[] data) { Width = w; Height = h; Data = data; }
        internal BitmapSource Bitmap()
        {
            var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Data, Width * 4); bitmap.Freeze(); return bitmap;
        }
        internal double Sample(double x, double y, int c)
        {
            x = Math.Max(0, Math.Min(Width - 1, x)); y = Math.Max(0, Math.Min(Height - 1, y));
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(Width - 1, x0 + 1), y1 = Math.Min(Height - 1, y0 + 1);
            double ax = x - x0, ay = y - y0;
            return (Data[(y0 * Width + x0) * 4 + c] * (1 - ax) + Data[(y0 * Width + x1) * 4 + c] * ax) * (1 - ay)
                + (Data[(y1 * Width + x0) * 4 + c] * (1 - ax) + Data[(y1 * Width + x1) * 4 + c] * ax) * ay;
        }
        internal FacePixels Resize(int edge)
        {
            double scale = Math.Min(1, edge / (double)Math.Max(Width, Height));
            int w = Math.Max(1, (int)Math.Round(Width * scale)), h = Math.Max(1, (int)Math.Round(Height * scale));
            var data = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++) data[(y * w + x) * 4 + c] = (byte)Math.Round(Sample((x + .5) / scale - .5, (y + .5) / scale - .5, c));
                data[(y * w + x) * 4 + 3] = 255;
            }
            return new FacePixels(w, h, data);
        }
        internal FacePixels Crop(int x, int y, int width, int height)
        {
            var data = new byte[width * height * 4];
            for (int row = 0; row < height; row++) Buffer.BlockCopy(Data, ((y + row) * Width + x) * 4, data, row * width * 4, width * 4);
            return new FacePixels(width, height, data);
        }
        internal FacePixels Rotate(int quarters)
        {
            int w = quarters % 2 == 1 ? Height : Width, h = quarters % 2 == 1 ? Width : Height;
            var data = new byte[w * h * 4];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int dx = quarters == 1 ? Height - 1 - y : quarters == 2 ? Width - 1 - x : y;
                int dy = quarters == 1 ? x : quarters == 2 ? Height - 1 - y : Width - 1 - x;
                Buffer.BlockCopy(Data, (y * Width + x) * 4, data, (dy * w + dx) * 4, 4);
            }
            return new FacePixels(w, h, data);
        }
    }

    internal sealed class FaceSource
    {
        internal string Path, Hash;
        internal long Length;
        internal DateTime Modified;
        internal int Width, Height;
    }
    internal sealed class FaceCrop
    {
        internal FaceSource Source;
        internal FaceBox Box;
        internal Int32Rect Crop;
        internal FacePixels Pixels;
        internal FaceCropOptions Framing = new FaceCropOptions();
        public BitmapSource Thumbnail { get; internal set; }
        public string Name { get { return System.IO.Path.GetFileName(Source.Path); } }
        public string Description { get { return "Detection " + Box.Score.ToString("P0") + "  |  " + Crop.Width + " x " + Crop.Height; } }
        public string FullPath { get { return Source.Path; } }
    }
    internal sealed class FaceScanResult
    {
        internal readonly List<FaceCrop> Faces = new List<FaceCrop>();
        internal readonly List<string> Errors = new List<string>();
        internal string Backend, ExecutionEvidence;
        internal int Images, Skipped;
        internal bool Limited;
    }
    internal sealed class FaceCanvasOptions
    {
        internal int Width = 2048, Height = 2048, Columns = 8;
        internal double CellAspect = 1;
        internal bool AutoColumns, AutoHeight = true;
        internal FaceCanvasOptions Copy() { return (FaceCanvasOptions)MemberwiseClone(); }
        internal void Validate(int count)
        {
            if (count < 1 || count > 1024) throw new InvalidDataException("Review 1-1024 faces before creating a canvas.");
            if (AutoColumns) Columns = AutoRowCounts(count).Max();
            if (Double.IsNaN(CellAspect) || Double.IsInfinity(CellAspect) || CellAspect < .5 || CellAspect > 2)
                throw new InvalidDataException("Invalid face-cell aspect ratio.");
            if (Columns < 1 || Columns > 128 || Width < 128 || Width > 16000 || Width / Columns < 8 || CellHeight(Columns) < 8)
                throw new InvalidDataException("Use widths of 128-16000 px, 1-128 columns and cells of at least 8 px.");
            if (AutoHeight) Height = Math.Max(128, AutoColumns ? AutoRowCounts(count).Sum(c => CellHeight(c)) : (int)Math.Ceiling(count / (double)Columns) * CellHeight(Columns));
            if (Height < 128 || Height > 16000 || (long)Width * Height > 64000000) throw new InvalidDataException("Canvas must be 128-16000 px per side and at most 64 megapixels.");
            if (!AutoColumns && (int)Math.Ceiling(count / (double)Columns) * CellHeight(Columns) > Height)
                throw new InvalidDataException("Height is too small for every face. Enable automatic height or increase it.");
        }
        internal int CellHeight(int columns) { return Math.Max(1, (int)Math.Floor(Width / (double)columns / CellAspect)); }
        internal static int[] AutoRowCounts(int count)
        {
            int rows = Math.Max(1, (int)Math.Round(Math.Sqrt(count))), columns = count / rows, extra = count % rows;
            return Enumerable.Range(0, rows).Select(row => columns + (row < extra ? 1 : 0)).ToArray();
        }
    }

    internal static class FaceImages
    {
        internal static readonly SemaphoreSlim WorkGate = new SemaphoreSlim(1, 1);
        internal const int MaxImages = 2048, MaxEntries = 8192, MaxFaces = 1024;
        private static readonly HashSet<string> ExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".viewer-rotation-backups", ".viewer-enhance-backups", ".viewer-orientation-backups", ".zen-batch-backups" };
        private const long ByteLimit = 128L * 1024 * 1024;
        private static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""); }
        private static byte[] Read(string path, CancellationToken token, out FaceSource source)
        {
            path = System.IO.Path.GetFullPath(path); FileTransferService.CheckAncestors(path);
            if (!ImageExtensions.IsBrowsableImage(path)) throw new NotSupportedException("Only supported image files can be scanned.");
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < 1 || input.Length > ByteLimit) throw new IOException("Image exceeds the 128 MiB encoded-size limit.");
                var data = new byte[(int)input.Length]; int at = 0;
                while (at < data.Length)
                { token.ThrowIfCancellationRequested(); int read = input.Read(data, at, Math.Min(1024 * 1024, data.Length - at)); if (read == 0) throw new EndOfStreamException(); at += read; }
                source = new FaceSource { Path = path, Hash = Hash(data), Length = data.Length, Modified = File.GetLastWriteTimeUtc(path) }; return data;
            }
        }
        private static MagickImage Decode(byte[] data, string path, CancellationToken token)
        {
            ModernImageDecoder.Initialize(); token.ThrowIfCancellationRequested();
            var settings = new MagickReadSettings { Format = EnhancedImageStore.Format(path), FrameCount = 2 };
            using (var headers = new MagickImageCollection())
            {
                headers.Ping(data, settings);
                if (headers.Count != 1) throw new NotSupportedException("Animations and multi-image files are not supported by Faces.");
                var header = headers[0];
                if (header.Width == 0 || header.Height == 0 || (ulong)header.Width * header.Height > 64000000) throw new IOException("Image exceeds the 64-megapixel Faces limit.");
                if (settings.Format == MagickFormat.Png) using (var input = new MemoryStream(data, false))
                    if (ImageRotationService.IsAnimatedPng(input)) throw new NotSupportedException("Animated PNG is not supported by Faces.");
            }
            var image = new MagickImage();
            try
            {
                settings.FrameCount = 1; image.Read(data, settings); token.ThrowIfCancellationRequested(); image.AutoOrient();
                if (image.GetColorProfile() != null) image.TransformColorSpace(ColorProfiles.SRGB); else image.ColorSpace = ColorSpace.sRGB;
                image.BackgroundColor = MagickColors.White; image.Alpha(AlphaOption.Remove); return image;
            }
            catch { image.Dispose(); throw; }
        }
        private static FacePixels Pixels(IMagickImage<byte> image)
        {
            using (var pixels = image.GetPixels()) return new FacePixels((int)image.Width, (int)image.Height, pixels.ToByteArray(PixelMapping.BGRA));
        }
        internal static FaceSource Inspect(string path, CancellationToken token)
        {
            FaceSource source; byte[] bytes = Read(path, token, out source);
            using (var image = Decode(bytes, path, token)) { source.Width = (int)image.Width; source.Height = (int)image.Height; }
            return source;
        }
        internal static BitmapSource OpenOriginal(string path, CancellationToken token)
        {
            WorkGate.Wait(token);
            try
            {
                FaceSource source; byte[] bytes = Read(path, token, out source);
                using (var image = Decode(bytes, path, token)) { token.ThrowIfCancellationRequested(); return Pixels(image).Bitmap(); }
            }
            finally { WorkGate.Release(); }
        }
        private static MagickImage Verified(FaceSource source, CancellationToken token)
        {
            FaceSource current; byte[] bytes = Read(source.Path, token, out current);
            if (current.Hash != source.Hash || current.Length != source.Length || current.Modified != source.Modified)
                throw new IOException("Source changed since review: " + System.IO.Path.GetFileName(source.Path) + ". Scan again.");
            var image = Decode(bytes, source.Path, token);
            if (image.Width != source.Width || image.Height != source.Height) { image.Dispose(); throw new IOException("Source dimensions changed."); }
            return image;
        }
        internal static Int32Rect CropBounds(FaceBox box, int width, int height, FaceCropOptions options = null)
        {
            options = options ?? new FaceCropOptions(); options.Validate();
            if (!box.Valid || width < 1 || height < 1) throw new InvalidDataException("Invalid source or face bounds.");
            double w = Math.Max(box.Width * width, box.Height * height * options.Aspect) * (1 + options.Surroundings / 100.0), h = w / options.Aspect;
            double fit = Math.Min(1, Math.Min(width / w, height / h));
            int cw = Math.Max(1, Math.Min(width, (int)Math.Round(w * fit))), ch = Math.Max(1, Math.Min(height, (int)Math.Round(h * fit)));
            int left = Math.Max(0, Math.Min(width - cw, (int)Math.Round((box.X + box.Width / 2) * width - cw / 2.0)));
            int top = Math.Max(0, Math.Min(height - ch, (int)Math.Round((box.Y + box.Height / 2) * height - ch / 2.0)));
            return new Int32Rect(left, top, cw, ch);
        }
        private static FaceCrop MakeCrop(FaceSource source, FaceBox box, FacePixels image, FaceCropOptions options)
        {
            var bounds = CropBounds(box, image.Width, image.Height, options);
            var pixels = image.Crop(bounds.X, bounds.Y, bounds.Width, bounds.Height).Resize(128);
            return new FaceCrop { Source = source, Box = box, Crop = CropBounds(box, source.Width, source.Height, options), Pixels = pixels,
                Thumbnail = pixels.Bitmap(), Framing = options.Copy() };
        }
        internal static FaceCrop[] Reframe(IList<FaceCrop> faces, FaceCropOptions options, int workers, CancellationToken token, Action<string> progress)
        {
            options = options.Copy(); options.Validate(); WorkGate.Wait(token);
            try
            {
                var result = new FaceCrop[faces.Count]; int done = 0;
                var groups = faces.Select((face, index) => new { Face = face, Index = index }).GroupBy(f => f.Face.Source).ToArray();
                Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(2, workers)), CancellationToken = token }, group =>
                {
                    token.ThrowIfCancellationRequested();
                    using (var decoded = Verified(group.Key, token))
                    {
                        if (Math.Max(decoded.Width, decoded.Height) > 2048) decoded.Resize(new MagickGeometry(2048, 2048));
                        var image = Pixels(decoded);
                        foreach (var item in group) { token.ThrowIfCancellationRequested(); result[item.Index] = MakeCrop(group.Key, item.Face.Box, image, options); }
                    }
                    int completed = Interlocked.Increment(ref done); if (progress != null) progress("Updating face framing: " + completed + " / " + groups.Length + " originals");
                });
                token.ThrowIfCancellationRequested(); return result;
            }
            finally { WorkGate.Release(); }
        }
        internal static List<string> CollectPaths(string folder, bool includeSubfolders, FaceScanResult result, CancellationToken token, Action<string> progress)
        {
            folder = System.IO.Path.GetFullPath(folder); token.ThrowIfCancellationRequested(); FileTransferService.CheckAncestors(folder);
            var paths = new List<string>(); var pending = new Stack<string>(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pending.Push(folder); int entries = 0;
            if (progress != null) progress("Finding images...");
            while (pending.Count > 0 && !result.Limited)
            {
                token.ThrowIfCancellationRequested(); string current = pending.Pop();
                if (!visited.Add(current)) continue;
                try
                {
                    FileTransferService.CheckAncestors(current);
                    foreach (string path in Directory.EnumerateFileSystemEntries(current))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++entries > MaxEntries) { result.Limited = true; break; }
                        if (progress != null && entries % 128 == 0) progress("Finding images: " + paths.Count + " images, " + visited.Count + " folders");
                        try
                        {
                            var attributes = File.GetAttributes(path);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                if (includeSubfolders && !ExcludedFolders.Contains(System.IO.Path.GetFileName(path))) pending.Push(path);
                                continue;
                            }
                            if (!ImageExtensions.IsBrowsableImage(path)) continue;
                            if (paths.Count == MaxImages) { result.Limited = true; break; }
                            paths.Add(path);
                        }
                        catch (IOException error) { ScanError(result, path, error); }
                        catch (UnauthorizedAccessException error) { ScanError(result, path, error); }
                    }
                }
                catch (IOException error) { if (String.Equals(current, folder, StringComparison.OrdinalIgnoreCase)) throw; ScanError(result, current, error); }
                catch (UnauthorizedAccessException error) { if (String.Equals(current, folder, StringComparison.OrdinalIgnoreCase)) throw; ScanError(result, current, error); }
            }
            token.ThrowIfCancellationRequested(); paths.Sort(BrowserSort.CompareNames); return paths;
        }
        private static void ScanError(FaceScanResult result, string path, Exception error)
        { result.Skipped++; if (result.Errors.Count < 40) result.Errors.Add(System.IO.Path.GetFileName(path) + ": " + error.Message); }
        internal static FaceScanResult Scan(string folder, int workers, string adapterKey, int budget, CancellationToken token, Action<string> progress, bool includeSubfolders = false, FaceCropOptions cropOptions = null)
        {
            var framing = (cropOptions ?? new FaceCropOptions()).Copy(); framing.Validate();
            WorkGate.Wait(token);
            try
            {
                var result = new FaceScanResult(); var paths = CollectPaths(folder, includeSubfolders, result, token, progress); result.Images = paths.Count;
                using (var detector = new FaceDetection(workers, adapterKey, budget, token))
                using (var inference = new SemaphoreSlim(1, 1))
                {
                    result.Backend = detector.Backend; result.ExecutionEvidence = detector.ExecutionEvidence;
                    if (progress != null) progress(detector.Backend); int done = 0; object sync = new object();
                    Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(2, workers)), CancellationToken = token }, path =>
                    {
                        try
                        {
                            lock (sync) if (result.Faces.Count >= MaxFaces) { result.Limited = true; return; }
                            FaceSource source; byte[] bytes = Read(path, token, out source);
                            FacePixels image;
                            using (var decoded = Decode(bytes, path, token))
                            {
                                source.Width = (int)decoded.Width; source.Height = (int)decoded.Height;
                                if (Math.Max(decoded.Width, decoded.Height) > 2048) decoded.Resize(new MagickGeometry(2048, 2048));
                                image = Pixels(decoded);
                            }
                            bytes = null;
                            FaceBox[] boxes;
                            inference.Wait(token);
                            try { boxes = detector.Detect(image, token); } finally { inference.Release(); }
                            foreach (var box in boxes)
                            {
                                var face = MakeCrop(source, box, image, framing);
                                lock (sync) { if (result.Faces.Count == MaxFaces) { result.Limited = true; break; } result.Faces.Add(face); }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (IOException error) { lock (sync) { result.Skipped++; if (result.Errors.Count < 40) result.Errors.Add(System.IO.Path.GetFileName(path) + ": " + error.Message); } }
                        catch (MagickException error) { lock (sync) { result.Skipped++; if (result.Errors.Count < 40) result.Errors.Add(System.IO.Path.GetFileName(path) + ": " + error.Message); } }
                        catch (NotSupportedException error) { lock (sync) { result.Skipped++; if (result.Errors.Count < 40) result.Errors.Add(System.IO.Path.GetFileName(path) + ": " + error.Message); } }
                        catch (UnauthorizedAccessException error) { lock (sync) { result.Skipped++; if (result.Errors.Count < 40) result.Errors.Add(error.Message); } }
                        int completed = Interlocked.Increment(ref done);
                        if (progress != null) progress(completed + " / " + paths.Count + " images scanned");
                    });
                }
                result.Faces.Sort((a, b) => { int c = StringComparer.OrdinalIgnoreCase.Compare(a.Source.Path, b.Source.Path); return c != 0 ? c : a.Box.Y.CompareTo(b.Box.Y); });
                return result;
            }
            finally { WorkGate.Release(); }
        }
        private sealed class Tile { internal int Face; internal int X, Y, Width, Height; }
        internal static Int32Rect[] ReferenceLayout(FaceCanvasOptions o, int count)
        {
            if (!o.AutoColumns)
                return Enumerable.Range(0, count).Select(i => new Int32Rect(i % o.Columns * o.Width / o.Columns, i / o.Columns * o.CellHeight(o.Columns),
                    (i % o.Columns + 1) * o.Width / o.Columns - i % o.Columns * o.Width / o.Columns, o.CellHeight(o.Columns))).ToArray();
            var rows = FaceCanvasOptions.AutoRowCounts(count); int naturalHeight = rows.Sum(c => o.CellHeight(c)), naturalY = 0, index = 0;
            var cells = new Int32Rect[count];
            foreach (int columns in rows)
            {
                int top = naturalY * o.Height / naturalHeight; naturalY += o.CellHeight(columns);
                int bottom = naturalY * o.Height / naturalHeight;
                for (int col = 0; col < columns; col++)
                {
                    int left = col * o.Width / columns, right = (col + 1) * o.Width / columns;
                    cells[index++] = new Int32Rect(left, top, right - left, bottom - top);
                }
            }
            return cells;
        }
        private static MagickImage FromPixels(FacePixels pixels)
        {
            var image = new MagickImage();
            try { image.ReadPixels(pixels.Data, new PixelReadSettings((uint)pixels.Width, (uint)pixels.Height, StorageType.Char, PixelMapping.BGRA)); return image; }
            catch { image.Dispose(); throw; }
        }
        private static MagickImage WhiteCanvas(int width, int height)
        {
            // Import raw pixels directly; do not enable ImageMagick pseudo-file coders.
            var bytes = new byte[width * height * 4]; var row = new byte[width * 4];
            for (int i = 0; i < row.Length; i++) row[i] = 255;
            for (int y = 0; y < height; y++) Buffer.BlockCopy(row, 0, bytes, y * row.Length, row.Length);
            return FromPixels(new FacePixels(width, height, bytes));
        }
        private static MagickImage Render(IList<FaceCrop> faces, FaceCanvasOptions o, bool preview, CancellationToken token, Action<string> progress)
        {
            o.Validate(faces.Count); token.ThrowIfCancellationRequested();
            var tiles = ReferenceLayout(o, faces.Count).Select((cell, index) => new Tile { Face = index, X = cell.X, Y = cell.Y, Width = cell.Width, Height = cell.Height }).ToList();
            double scale = preview ? Math.Min(1, 2048.0 / Math.Max(o.Width, o.Height)) : 1;
            int width = Math.Max(1, (int)Math.Round(o.Width * scale)), height = Math.Max(1, (int)Math.Round(o.Height * scale));
            var canvas = WhiteCanvas(width, height);
            try
            {
                int completed = 0;
                foreach (var sourceGroup in tiles.GroupBy(t => faces[t.Face].Source))
                {
                    token.ThrowIfCancellationRequested();
                    using (var original = preview ? null : Verified(sourceGroup.Key, token))
                        foreach (var faceGroup in sourceGroup.GroupBy(t => t.Face))
                        {
                            var face = faces[faceGroup.Key];
                            using (var fullCrop = preview ? FromPixels(face.Pixels) : original.Clone())
                            {
                                if (!preview) { fullCrop.Crop(new MagickGeometry(face.Crop.X, face.Crop.Y, (uint)face.Crop.Width, (uint)face.Crop.Height)); fullCrop.ResetPage(); }
                                // Integer grid rounding produces a few cell sizes. Reuse a resized
                                // crop per size, so fractional columns never leave white seams.
                                foreach (var sizeGroup in faceGroup.GroupBy(t => new {
                                    Width = Math.Max(1, (int)Math.Round((t.X + t.Width) * scale) - (int)Math.Round(t.X * scale)),
                                    Height = Math.Max(1, (int)Math.Round((t.Y + t.Height) * scale) - (int)Math.Round(t.Y * scale)) }))
                                using (var crop = fullCrop.Clone())
                                {
                                    uint tw = (uint)sizeGroup.Key.Width, th = (uint)sizeGroup.Key.Height; crop.FilterType = FilterType.Lanczos;
                                    crop.Resize(new MagickGeometry(tw, th));
                                    foreach (var tile in sizeGroup)
                                    {
                                        token.ThrowIfCancellationRequested(); int x = (int)Math.Round(tile.X * scale), y = (int)Math.Round(tile.Y * scale);
                                        canvas.Composite(crop, x + ((int)tw - (int)crop.Width) / 2, y + ((int)th - (int)crop.Height) / 2, CompositeOperator.Over);
                                        completed++;
                                    }
                                }
                            }
                        }
                    if (progress != null) progress(completed + " / " + tiles.Count + " tiles rendered");
                }
                token.ThrowIfCancellationRequested(); canvas.ColorSpace = ColorSpace.sRGB; canvas.Depth = 8; return canvas;
            }
            catch { canvas.Dispose(); throw; }
        }
        internal static BitmapSource Preview(IList<FaceCrop> faces, FaceCanvasOptions options, CancellationToken token)
        {
            WorkGate.Wait(token);
            try { using (var canvas = Render(faces, options, true, token, null)) return Pixels(canvas).Bitmap(); }
            finally { WorkGate.Release(); }
        }
        internal static void Export(IList<FaceCrop> faces, FaceCanvasOptions options, string destination, CancellationToken token, Action<string> progress)
        {
            WorkGate.Wait(token); string temporary = null; var sourceLocks = new List<FileStream>();
            try
            {
                destination = System.IO.Path.GetFullPath(destination);
                if (!String.Equals(System.IO.Path.GetExtension(destination), ".png", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a PNG filename.");
                string folder = System.IO.Path.GetDirectoryName(destination); FileTransferService.CheckAncestors(folder);
                if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Destination exists. Choose a new filename; Faces never overwrites files.");
                temporary = System.IO.Path.Combine(folder, ".myoken-faces-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var canvas = Render(faces, options, false, token, progress))
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { canvas.Write(stream, MagickFormat.Png); if (stream.Length > ByteLimit) throw new IOException("PNG exceeds the 128 MiB output limit."); stream.Flush(true); }
                var sources = faces.Select(f => f.Source)
                    .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase);
                foreach (var source in sources)
                {
                    token.ThrowIfCancellationRequested(); FileTransferService.CheckAncestors(source.Path);
                    var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read); sourceLocks.Add(input);
                    using (var sha = SHA256.Create())
                        if (input.Length != source.Length || File.GetLastWriteTimeUtc(source.Path) != source.Modified
                            || BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "") != source.Hash)
                            throw new IOException("Source changed during export: " + System.IO.Path.GetFileName(source.Path) + ". Scan again.");
                }
                token.ThrowIfCancellationRequested(); FileTransferService.CheckAncestors(folder);
                // Same-folder move is atomic and cannot replace an existing file, including a late conflict.
                File.Move(temporary, destination); temporary = null;
            }
            finally
            {
                try
                {
                    if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                    foreach (var sourceLock in sourceLocks) sourceLock.Dispose();
                }
                finally { WorkGate.Release(); }
            }
        }
    }
}
