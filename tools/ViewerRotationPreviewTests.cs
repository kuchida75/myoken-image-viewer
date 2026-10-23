using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private sealed class RotationCountingDecoder : IImageDecoder
    {
        private readonly WicImageDecoder _inner = new WicImageDecoder(() => false);
        internal int FullDecodes;
        internal string Target;
        public string Name { get { return "Rotation decode counter"; } }
        public bool CanDecode(string extension) { return _inner.CanDecode(extension); }
        public BitmapSource Decode(string path, int width, CancellationToken token)
        {
            if (width == 0 && String.Equals(path, Target, StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref FullDecodes);
            return _inner.Decode(path, width, token);
        }
        public ImageMetadata ReadMetadata(string path) { return _inner.ReadMetadata(path); }
    }

    private static void RunRotationPreviewChecks()
    {
        string root = Path.Combine(Root, "rotation-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        RunRotationStoreChecks(root);
        foreach (string extension in new[] { ".png", ".jpg" })
        {
            string folder = Path.Combine(root, extension.Substring(1)); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Image" + extension);
            MakeImage(path, 720, 480, 96);
            if (extension == ".jpg")
            {
                BitmapSource input = ReadToolImage(path);
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(input));
                using (var output = File.Create(path)) encoder.Save(output);
            }
            byte[] original = File.ReadAllBytes(path);
            var result = ImageRotationService.SaveWithPreview(path, 1, FileRevision.Read(path), CancellationToken.None, false);
            Assert(result.Pixels != null && result.Pixels.IsFrozen && result.Revision.Matches(path), "verified rotation preview has committed revision and is thread-safe");
            Assert(result.Pixels.PixelWidth == 480 && result.Pixels.PixelHeight == 720
                && ToolPixels(result.Pixels).SequenceEqual(ToolPixels(ReadToolImage(path))), "preview is actual saved output including JPEG re-encoding");
            Assert(original.SequenceEqual(File.ReadAllBytes(result.Backup)), "preview save keeps the byte-identical original backup");
            var managed = ImageRotationService.SaveWithPreview(path, 3, FileRevision.Read(path), CancellationToken.None, true);
            Assert(managed.Pixels == null && managed.Revision.Matches(path), "ICC path retains normal profile-aware decoding");
            var cache = new VramImageCache(128, new GpuMemoryPressureMonitor());
            cache.Add(path, result.Pixels, result.Revision);
            BitmapSource stale;
            Assert(!cache.TryGet(path, out stale), "older verified preview cannot populate the cache after another save");

            using (var services = AppServices.Create(Path.Combine(folder, "Profile")))
            {
                services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1240, WindowHeight = 920 });
                var counter = new RotationCountingDecoder { Target = path };
                Field<List<IImageDecoder>>(services.Decoders, "_decoders").Insert(0, counter);
                services.Decoders.GpuImages = null;
                var window = new MainWindow(services); window.Show(); WaitScan(window);
                try
                {
                    Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "rotation preview source");
                    int before = counter.FullDecodes;
                    string id = Field<string>(window, "_activeTabId");
                    Invoke(window, "RotateImage", 1);
                    Task save = (Task)Invoke(window, "SaveRotationAsync", path, 1, FileRevision.Read(path), false);
                    WaitBackup(save); Wait(delegate { return Ready(window, path); }, "cached saved rotation display");
                    Assert(counter.FullDecodes == before, "viewer reuses verified saved pixels without another full decode");
                    Assert(Field<string>(window, "_activeTabId") == id && Field<Grid>(window, "_imageView").IsVisible, "optimized save retains viewer and tab");
                    Assert(ToolPixels((BitmapSource)Field<Image>(window, "_mainImage").Source).SequenceEqual(ToolPixels(ReadToolImage(path))), "fast viewer matches file pixels");
                    CheckCentered(window);
                    MakeImage(path, 410, 290, 96);
                    Wait(delegate { return Ready(window, path) && ((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == 410; }, "external edit after optimized save");
                    Assert(counter.FullDecodes > before, "watcher still reloads external changes after optimized save");
                }
                finally { window.Close(); Pump(); }
            }
        }
        Console.WriteLine("PASS: verified rotation previews, actual JPEG/PNG pixels, original backups, ICC fallback, stale cache rejection, no redundant viewer decode, same-tab focus and external watcher edits.");
    }
}
