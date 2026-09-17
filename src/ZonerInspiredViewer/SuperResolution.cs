using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ZonerInspiredViewer
{
    internal sealed class SuperResolutionRequest
    {
        internal BitmapSource Source;
        internal Effect QuickEffect;
        internal ManualAdjustments Manual;
        internal int Rotation, Workers = 4, SoftLimitMb = 2048;
        internal double Scale = 1.6;
        internal GpuAdapterMemoryInfo Adapter;
        internal string ModelDirectory = null;
        internal string ModelId = UpscaleModels.Basic;
        internal SupirRuntimeConfig Supir;
    }

    internal sealed class SuperResolutionResult
    {
        internal BitmapSource Pixels, Comparison;
        internal string Backend, Analysis;
        internal string ModelId;
        internal int Tiles;
    }

    internal sealed class SuperResolution : IDisposable
    {
        internal static readonly double[] Scales = { 1, 1.25, 1.4, 1.6, 1.8, 2, 2.5, 3 };
        internal const long MaximumInputPixels = 8000000, MaximumOutputPixels = 32000000;
        internal const string ModelHash = "85F36FF88CC504A24AF5E0602148BC56A8AA09A58ECA8C0DA2756F3E8186035E";
        private readonly UpscaleModel _definition;
        private int Tile { get { return _definition.Tile; } }
        private int Halo { get { return _definition.Halo; } }
        private int Core { get { return Tile - Halo * 2; } }
        private int NativeScale { get { return _definition.NativeScale; } }
        private static readonly SemaphoreSlim Jobs = new SemaphoreSlim(1, 1);
        private readonly SuperResolutionRequest _request;
        private readonly string _model;
        private InferenceSession _session;
        private bool _gpu;
        internal string Backend { get; private set; }

        private SuperResolution(SuperResolutionRequest request)
        {
            _request = request;
            _definition = UpscaleModels.Get(request.ModelId);
            _model = Path.Combine(request.ModelDirectory ?? UpscaleModels.DirectoryPath, _definition.File);
            using (var input = File.OpenRead(_model))
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "") != _definition.Hash)
                    throw new InvalidDataException("AI upscale model checksum mismatch. Reinstall the complete viewer package.");
            CreateSession(request.Adapter != null);
        }

        internal static Size OutputSize(int width, int height, double scale)
        {
            if (Array.IndexOf(Scales, scale) < 0) throw new ArgumentOutOfRangeException("scale", "Choose a supported scale from 1x to 3x.");
            if (width < 1 || height < 1 || (long)width * height > MaximumInputPixels)
                throw new InvalidOperationException("AI upscale supports source images up to 8 megapixels.");
            int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
            if ((long)w * h > MaximumOutputPixels) throw new InvalidOperationException("Choose a smaller scale: the output limit is 32 megapixels.");
            return new Size(w, h);
        }

        internal static double AutomaticScale(Size image, Size framePixels)
        {
            if (!ImageViewport.IsFinite(image.Width) || !ImageViewport.IsFinite(image.Height)
                || !ImageViewport.IsFinite(framePixels.Width) || !ImageViewport.IsFinite(framePixels.Height)
                || image.Width < 1 || image.Height < 1 || framePixels.Width < 1 || framePixels.Height < 1
                || image.Width * image.Height > MaximumInputPixels) return 1;
            double needed = Math.Min(framePixels.Width / image.Width, framePixels.Height / image.Height);
            if (needed <= 1.1) return 1;
            double allowed = 1;
            foreach (double scale in Scales)
            {
                if (Math.Round(image.Width * scale) * Math.Round(image.Height * scale) > MaximumOutputPixels) break;
                allowed = scale;
                if (scale >= needed) break;
            }
            return allowed;
        }

        internal static Task<SuperResolutionResult> RunAsync(SuperResolutionRequest request, CancellationToken token, Action<double, string> progress)
        {
            var completion = new TaskCompletionSource<SuperResolutionResult>();
            var worker = new Thread(delegate()
            {
                bool entered = false;
                try
                {
                    Jobs.Wait(token); entered = true;
                    completion.SetResult(Process(request, token, progress));
                }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception error) { completion.SetException(error); }
                finally { if (entered) Jobs.Release(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
            return completion.Task;
        }

        private static SuperResolutionResult Process(SuperResolutionRequest request, CancellationToken token, Action<double, string> progress)
        {
            token.ThrowIfCancellationRequested();
            UpscaleModel definition = UpscaleModels.Get(request.ModelId);
            Size size = OutputSize(request.Source.PixelWidth, request.Source.PixelHeight, request.Scale);
            Action<double, string> report = progress ?? delegate { };
            BitmapSource comparison = EnhancedImageRenderer.Render(request.Source, request.QuickEffect, request.Manual, request.Rotation,
                token, p => report(p * 0.08, "Preparing original comparison"));
            if (request.Scale == 1)
                return new SuperResolutionResult { Pixels = comparison, Comparison = comparison, Backend = "Original (AI bypassed)", ModelId = definition.Id, Analysis = "1x: current adjustments, no invented detail" };
            if (!UpscaleModels.IsInstalled(definition.Id, request.ModelDirectory, request.Supir))
                throw new InvalidOperationException(definition.Name + " is not installed. Open the AI upscale model menu for setup. No other model was substituted.");
            if (definition.Id == UpscaleModels.Supir)
            {
                SupirProcess.Validate(request);
                if (Math.Ceiling(size.Width / 64) * 64 * Math.Ceiling(size.Height / 64) * 64 > 4200000)
                    throw new InvalidOperationException("SUPIR previews are limited to 4.2 MP. Choose a smaller factor.");
            }
            report(0.08, "Detecting subject and background");
            SubjectMask mask;
            using (var detector = new SubjectSegmentation(request.Workers, request.Adapter != null, request.ModelDirectory, request.Adapter, request.SoftLimitMb))
                mask = detector.Detect(request.Source, token);
            token.ThrowIfCancellationRequested();
            if (definition.Id == UpscaleModels.Supir)
            {
                BitmapSource restored = SupirProcess.Run(request, (int)size.Width, (int)size.Height, token, report);
                BitmapSource protectedPixels = BlendRestored(request.Source, restored, mask, request.Workers, token);
                return new SuperResolutionResult { Pixels = EnhancedImageRenderer.Render(protectedPixels, request.QuickEffect, request.Manual, request.Rotation,
                    token, p => report(0.9 + p * 0.1, "Applying Enhance adjustments")), Comparison = comparison, ModelId = definition.Id,
                    Backend = "CUDA: " + request.Adapter.Name, Analysis = "SUPIR experimental; source chroma and alpha retained; generated detail may differ" };
            }
            using (var engine = new SuperResolution(request))
            {
                int tiles;
                BitmapSource upscaled = engine.Upscale(request.Source, (int)size.Width, (int)size.Height, mask, token,
                    p => report(0.18 + p * 0.64, "AI detail reconstruction | " + engine.Backend), out tiles);
                BitmapSource result = EnhancedImageRenderer.Render(upscaled, request.QuickEffect, request.Manual, request.Rotation,
                    token, p => report(0.82 + p * 0.18, "Applying Enhance adjustments"));
                return new SuperResolutionResult { Pixels = result, Comparison = comparison, Backend = engine.Backend, Tiles = tiles, ModelId = definition.Id,
                    Analysis = mask.Description + "; source chroma retained; gentle skin-color detail" };
            }
        }

        private void CreateSession(bool preferGpu)
        {
            if (_session != null) { _session.Dispose(); _session = null; }
            _gpu = false; Backend = "CPU"; string reason = null;
            if (preferGpu && GpuHardware.CanAllocate(_request.Adapter, (long)_definition.AdmissionMb * 1024 * 1024, _request.SoftLimitMb, out reason))
            {
                try
                {
                    using (var options = Options())
                    {
                        options.EnableMemoryPattern = false; options.AppendExecutionProvider_DML(_request.Adapter.Index);
                        _session = new InferenceSession(_model, options); _gpu = true; Backend = "DirectML: " + _request.Adapter.Name;
                    }
                }
                catch (OnnxRuntimeException) { reason = "DirectML unavailable"; }
                catch (DllNotFoundException) { reason = "DirectML unavailable"; }
                catch (EntryPointNotFoundException) { reason = "DirectML unavailable"; }
            }
            if (_session == null)
            {
                using (var options = Options()) _session = new InferenceSession(_model, options);
                Backend = reason == null ? "CPU" : "CPU fallback: " + reason;
            }
        }

        private SessionOptions Options()
        {
            OrtEnv.Instance().DisableTelemetryEvents();
            var options = new SessionOptions { ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Max(1, Math.Min(8, _request.Workers)), LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); return options;
        }

        private float[] Predict(float[] data, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); string reason;
            if (_gpu && !GpuHardware.CanAllocate(_request.Adapter, 0, _request.SoftLimitMb, out reason))
            { CreateSession(false); Backend = "CPU fallback: " + reason; }
            var tensor = new DenseTensor<float>(data, new[] { 1, _definition.Channels, Tile, Tile });
            using (var options = new RunOptions())
            using (token.Register(delegate { options.Terminate = true; }))
            {
                try
                {
                    using (var result = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_session.InputMetadata.Keys.First(), tensor) },
                        new[] { _session.OutputMetadata.Keys.First() }, options))
                    {
                        var output = result.First().AsTensor<float>();
                        if (!output.Dimensions.ToArray().SequenceEqual(new[] { 1, _definition.Channels, Tile * NativeScale, Tile * NativeScale }))
                            throw new InvalidDataException("Unexpected AI upscale model output dimensions.");
                        var values = output.ToArray();
                        if (values.Any(v => Single.IsNaN(v) || Single.IsInfinity(v))) throw new InvalidDataException("Invalid AI upscale output.");
                        return values;
                    }
                }
                catch (OnnxRuntimeException)
                {
                    token.ThrowIfCancellationRequested();
                    if (!_gpu) throw;
                    CreateSession(false); Backend = "CPU fallback: GPU inference failed";
                    return Predict(data, token);
                }
            }
        }

        private BitmapSource Upscale(BitmapSource original, int width, int height, SubjectMask mask, CancellationToken token,
            Action<double> progress, out int tiles)
        {
            int sw = original.PixelWidth, sh = original.PixelHeight;
            byte[] input = SubjectSegmentation.ReadPixels(original, sw, sh);
            byte[] pixels = SubjectSegmentation.ReadPixels(original, width, height);
            double sx = (double)width / sw, sy = (double)height / sh;
            bool segmented = mask != null && mask.Values != null;
            int count = ((sw + Core - 1) / Core) * ((sh + Core - 1) / Core); tiles = 0;
            var parallel = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Math.Min(30, _request.Workers)) };
            for (int y = 0; y < sh; y += Core)
            for (int x = 0; x < sw; x += Core)
            {
                token.ThrowIfCancellationRequested();
                int left = x - Halo, top = y - Halo;
                var data = new float[Tile * Tile * _definition.Channels];
                for (int ty = 0; ty < Tile; ty++)
                for (int tx = 0; tx < Tile; tx++)
                {
                    int at = (Reflect(top + ty, sh) * sw + Reflect(left + tx, sw)) * 4;
                    double alpha = input[at + 3] / 255.0;
                    if (_definition.Channels == 1)
                        data[ty * Tile + tx] = (float)((Luma(input[at + 2], input[at + 1], input[at]) / 255) * alpha + 0.5 * (1 - alpha));
                    else for (int c = 0; c < 3; c++)
                        data[c * Tile * Tile + ty * Tile + tx] = (float)(input[at + 2 - c] / 255.0 * alpha + 0.5 * (1 - alpha));
                }
                float[] prediction = Predict(data, token);
                int x0 = (int)Math.Round(x * sx), x1 = (int)Math.Round(Math.Min(sw, x + Core) * sx);
                int y0 = (int)Math.Round(y * sy), y1 = (int)Math.Round(Math.Min(sh, y + Core) * sy);
                Parallel.For(y0, y1, parallel, delegate(int row)
                {
                    for (int col = x0; col < x1; col++)
                    {
                        int at = (row * width + col) * 4;
                        if (pixels[at + 3] == 0) continue;
                        double px = col * NativeScale / sx - left * NativeScale, py = row * NativeScale / sy - top * NativeScale;
                        double pw = NativeScale / sx, ph = NativeScale / sy;
                        double luminance = _definition.Channels == 1 ? AreaSample(prediction, px, py, pw, ph, 0) * 255
                            : Luma(AreaSample(prediction, px, py, pw, ph, 0), AreaSample(prediction, px, py, pw, ph, 1), AreaSample(prediction, px, py, pw, ph, 2)) * 255;
                        double foreground = segmented ? mask.Sample((col + 0.5) / width, (row + 0.5) / height) : 0.35;
                        BlendDetail(pixels, at, luminance, foreground, mask != null && mask.IsPerson);
                    }
                });
                progress(++tiles / (double)count);
            }
            token.ThrowIfCancellationRequested();
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze(); return bitmap;
        }

        // Reconstruct detail only in luminance: a common RGB offset preserves chroma until gamut limits.
        internal static void BlendDetail(byte[] pixels, int at, double luminance, double foreground, bool person)
        {
            double r = pixels[at + 2], g = pixels[at + 1], b = pixels[at];
            double skin = AdvancedQuickEnhance.SkinProtection(r / 255, g / 255, b / 255) * (person ? 0.3 + 0.7 * foreground : 0.75);
            double strength = (0.5 + 0.35 * foreground) * (1 - 0.65 * skin) * pixels[at + 3] / 255.0;
            double delta = Math.Max(-32, Math.Min(32, luminance - Luma(r, g, b))) * strength;
            delta = Math.Max(-Math.Min(r, Math.Min(g, b)), Math.Min(255 - Math.Max(r, Math.Max(g, b)), delta));
            for (int c = 0; c < 3; c++) pixels[at + c] = (byte)Math.Round(pixels[at + c] + delta);
        }

        private static double Luma(double r, double g, double b) { return 0.299 * r + 0.587 * g + 0.114 * b; }
        private static int Reflect(int value, int length)
        {
            if (length == 1) return 0;
            int period = 2 * (length - 1); value = ((value % period) + period) % period;
            return value < length ? value : period - value;
        }

        private double AreaSample(float[] values, double x, double y, double width, double height, int channel)
        {
            int side = Tile * NativeScale, offset = channel * side * side;
            double sum = 0;
            for (int row = (int)Math.Floor(y); row < Math.Ceiling(y + height); row++)
            for (int col = (int)Math.Floor(x); col < Math.Ceiling(x + width); col++)
                sum += values[offset + Math.Max(0, Math.Min(side - 1, row)) * side + Math.Max(0, Math.Min(side - 1, col))]
                    * Math.Max(0, Math.Min(row + 1, y + height) - Math.Max(row, y))
                    * Math.Max(0, Math.Min(col + 1, x + width) - Math.Max(col, x));
            return sum / (width * height);
        }

        private static BitmapSource BlendRestored(BitmapSource original, BitmapSource restored, SubjectMask mask, int workers, CancellationToken token)
        {
            int width = restored.PixelWidth, height = restored.PixelHeight;
            byte[] pixels = SubjectSegmentation.ReadPixels(original, width, height), enhanced = SubjectSegmentation.ReadPixels(restored, width, height);
            Parallel.For(0, height, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Math.Min(30, workers)) }, row =>
            {
                for (int col = 0; col < width; col++)
                {
                    int at = (row * width + col) * 4;
                    double foreground = mask != null && mask.Values != null ? mask.Sample((col + 0.5) / width, (row + 0.5) / height) : 0.35;
                    BlendDetail(pixels, at, Luma(enhanced[at + 2], enhanced[at + 1], enhanced[at]), foreground, mask != null && mask.IsPerson);
                }
            });
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); result.Freeze(); return result;
        }

        public void Dispose() { if (_session != null) { _session.Dispose(); _session = null; } }
    }
}
