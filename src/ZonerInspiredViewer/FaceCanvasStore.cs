using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class FaceCanvasProject
    {
        internal FaceCrop[] Faces;
        internal FaceCanvasOptions Options;
        internal string Folder;
        internal bool Recursive, Fit = true;
        internal double Zoom = 1, X, Y;
    }
    [DataContract] internal sealed class FaceCanvasDocument
    {
        [DataMember] internal string Format = "Myoken.FaceCanvas";
        [DataMember] internal int Version = 1;
        [DataMember] internal int Width, Height, Columns, Surroundings;
        [DataMember] internal bool AutoColumns, AutoHeight, Recursive, Fit;
        [DataMember] internal double Aspect, Zoom, X, Y;
        [DataMember] internal string Folder;
        [DataMember] internal List<FaceCanvasSource> Sources;
        [DataMember] internal List<FaceCanvasCrop> Faces;
    }
    [DataContract] internal sealed class FaceCanvasSource
    {
        [DataMember] internal string Path, Hash;
        [DataMember] internal long Length, Modified;
        [DataMember] internal int Width, Height;
    }
    [DataContract] internal sealed class FaceCanvasCrop
    {
        [DataMember] internal int Source, X, Y, Width, Height;
        [DataMember] internal double FaceX, FaceY, FaceWidth, FaceHeight, Score;
    }
    internal static class FaceCanvasStore
    {
        internal const long Limit = 64L * 1024 * 1024;
        internal static bool IsPath(string path) { return !String.IsNullOrEmpty(path) && String.Equals(Path.GetExtension(path), ".myokenfaces", StringComparison.OrdinalIgnoreCase); }
        internal static bool ValidPath(string path)
        {
            return !String.IsNullOrWhiteSpace(path) && path.Length <= 4096 && Path.IsPathRooted(path)
                && (path.StartsWith("\\\\", StringComparison.Ordinal) || (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/')))
                && path.IndexOfAny(Path.GetInvalidPathChars()) < 0 && !path.StartsWith("\\\\?\\", StringComparison.Ordinal) && !path.StartsWith("\\\\.\\", StringComparison.Ordinal)
                && path.Substring(2).IndexOf(':') < 0;
        }
        internal static void Save(string path, FaceCanvasProject project, CancellationToken token)
        {
            if (!IsPath(path)) throw new InvalidDataException("Choose a .myokenfaces filename.");
            project.Options.Validate(project.Faces.Length);
            var framing = project.Faces[0].Framing; framing.Validate();
            var sources = project.Faces.Select(f => f.Source).Distinct().ToList();
            var document = new FaceCanvasDocument { Width = project.Options.Width, Height = project.Options.Height, Columns = project.Options.Columns,
                AutoColumns = project.Options.AutoColumns, AutoHeight = project.Options.AutoHeight, Surroundings = framing.Surroundings, Aspect = framing.Aspect,
                Folder = project.Folder, Recursive = project.Recursive, Fit = project.Fit, Zoom = project.Zoom, X = project.X, Y = project.Y,
                Sources = sources.Select(s => new FaceCanvasSource { Path = s.Path, Hash = s.Hash, Length = s.Length, Modified = s.Modified.Ticks, Width = s.Width, Height = s.Height }).ToList(),
                Faces = project.Faces.Select(f => new FaceCanvasCrop { Source = sources.IndexOf(f.Source), X = f.Crop.X, Y = f.Crop.Y, Width = f.Crop.Width, Height = f.Crop.Height,
                    FaceX = f.Box.X, FaceY = f.Box.Y, FaceWidth = f.Box.Width, FaceHeight = f.Box.Height, Score = f.Box.Score }).ToList() };
            Validate(document);
            var entries = new Dictionary<string, byte[]> { { "canvas.json", DocumentArchive.Json(document, typeof(FaceCanvasDocument), 8 * 1024 * 1024) } };
            for (int i = 0; i < project.Faces.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                using (var output = new MemoryStream())
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(project.Faces[i].Pixels.Resize(128).Bitmap())); encoder.Save(output);
                    entries.Add(ThumbnailName(i), output.ToArray());
                }
            }
            DocumentArchive.Save(path, entries, Limit, token);
        }
        internal static FaceCanvasProject Load(string path)
        { if (!IsPath(path)) throw new InvalidDataException("Choose a .myokenfaces file."); return LoadEntries(DocumentArchive.Read(path, Limit, 1025)); }
        internal static FaceCanvasProject LoadBytes(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > Limit) throw new InvalidDataException("Invalid canvas size.");
            using (var input = new MemoryStream(bytes, false)) return LoadEntries(DocumentArchive.Read(input, Limit, 1025));
        }
        private static FaceCanvasProject LoadEntries(Dictionary<string, byte[]> entries)
        {
            byte[] json; if (!entries.TryGetValue("canvas.json", out json)) throw new InvalidDataException("Canvas metadata is missing.");
            var document = DocumentArchive.Json<FaceCanvasDocument>(json, 8 * 1024 * 1024); Validate(document);
            if (entries.Count != document.Faces.Count + 1) throw new InvalidDataException("Unexpected canvas entries.");
            var sources = document.Sources.Select(s => new FaceSource { Path = Path.GetFullPath(s.Path), Hash = s.Hash.ToUpperInvariant(), Length = s.Length,
                Modified = new DateTime(s.Modified, DateTimeKind.Utc), Width = s.Width, Height = s.Height }).ToArray();
            var framing = new FaceCropOptions { Aspect = document.Aspect, Surroundings = document.Surroundings };
            var faces = new FaceCrop[document.Faces.Count];
            for (int i = 0; i < faces.Length; i++)
            {
                byte[] data; if (!entries.TryGetValue(ThumbnailName(i), out data)) throw new InvalidDataException("A cached face thumbnail is missing.");
                var pixels = ReadThumbnail(data); var f = document.Faces[i];
                faces[i] = new FaceCrop { Source = sources[f.Source], Box = new FaceBox(f.FaceX, f.FaceY, f.FaceWidth, f.FaceHeight, f.Score),
                    Crop = new Int32Rect(f.X, f.Y, f.Width, f.Height), Pixels = pixels, Thumbnail = pixels.Bitmap(), Framing = framing.Copy() };
            }
            return new FaceCanvasProject { Faces = faces, Folder = document.Folder, Recursive = document.Recursive, Fit = document.Fit, Zoom = document.Zoom, X = document.X, Y = document.Y,
                Options = new FaceCanvasOptions { Width = document.Width, Height = document.Height, Columns = document.Columns, AutoColumns = document.AutoColumns, AutoHeight = document.AutoHeight, CellAspect = document.Aspect } };
        }
        private static string ThumbnailName(int index) { return "faces/" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".png"; }
        private static FacePixels ReadThumbnail(byte[] data)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (data.Length < 33 || data.Length > 262144 || !data.Take(8).SequenceEqual(signature) || data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
                throw new InvalidDataException("Invalid cached face PNG.");
            int width = BigEndian(data, 16), height = BigEndian(data, 20);
            if (width < 1 || height < 1 || width > 128 || height > 128) throw new InvalidDataException("Cached face dimensions exceed 128 pixels.");
            using (var input = new MemoryStream(data, false))
            {
                var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth != width || decoder.Frames[0].PixelHeight != height) throw new InvalidDataException("Invalid cached face frame.");
                var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0); var pixels = new byte[width * height * 4]; converted.CopyPixels(pixels, width * 4, 0);
                return new FacePixels(width, height, pixels);
            }
        }
        private static int BigEndian(byte[] data, int at) { return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]; }
        private static void Validate(FaceCanvasDocument d)
        {
            if (d == null || d.Format != "Myoken.FaceCanvas" || d.Version != 1 || d.Faces == null || d.Sources == null || d.Sources.Count < 1 || d.Sources.Count > 1024)
                throw new InvalidDataException("Unsupported face-canvas document.");
            new FaceCropOptions { Aspect = d.Aspect, Surroundings = d.Surroundings }.Validate();
            if (!new[] { 1.0, .8, 2.0 / 3, 4.0 / 3, 1.5, 16.0 / 9 }.Any(a => Math.Abs(a - d.Aspect) < .000001))
                throw new InvalidDataException("Unsupported canvas crop shape.");
            new FaceCanvasOptions { Width = d.Width, Height = d.Height, Columns = d.Columns, AutoColumns = d.AutoColumns, AutoHeight = d.AutoHeight, CellAspect = d.Aspect }.Validate(d.Faces.Count);
            if ((!String.IsNullOrEmpty(d.Folder) && !ValidPath(d.Folder)) || !Finite(d.Zoom, .01, 8) || !Finite(d.X, 0, 128000) || !Finite(d.Y, 0, 128000))
                throw new InvalidDataException("Invalid canvas viewport or folder.");
            foreach (var s in d.Sources)
                if (s == null || !ValidPath(s.Path) || !ImageExtensions.IsBrowsableImage(s.Path) || s.Hash == null || s.Hash.Length != 64 || s.Hash.Any(c => !Uri.IsHexDigit(c))
                    || s.Length < 1 || s.Length > 128L * 1024 * 1024 || s.Modified < 0 || s.Modified > DateTime.MaxValue.Ticks || s.Width < 1 || s.Height < 1 || (long)s.Width * s.Height > 64000000)
                    throw new InvalidDataException("Invalid canvas source record.");
            foreach (var f in d.Faces)
            {
                if (f == null || f.Source < 0 || f.Source >= d.Sources.Count || !new FaceBox(f.FaceX, f.FaceY, f.FaceWidth, f.FaceHeight, f.Score).Valid)
                    throw new InvalidDataException("Invalid canvas face record.");
                var s = d.Sources[f.Source];
                if (f.X < 0 || f.Y < 0 || f.Width < 1 || f.Height < 1 || (long)f.X + f.Width > s.Width || (long)f.Y + f.Height > s.Height)
                    throw new InvalidDataException("Canvas crop extends outside its source.");
            }
        }
        private static bool Finite(double value, double min, double max) { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max; }
    }
}
