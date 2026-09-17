using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ZonerInspiredViewer
{
    internal sealed class SubjectMask
    {
        public float[] Values;
        public int Width;
        public int Height;
        public bool IsPerson;
        public string Description;

        public double Sample(double x, double y)
        {
            double px = Math.Max(0, Math.Min(Width - 1, x * Width - 0.5));
            double py = Math.Max(0, Math.Min(Height - 1, y * Height - 0.5));
            int x0 = (int)px, y0 = (int)py, x1 = Math.Min(Width - 1, x0 + 1), y1 = Math.Min(Height - 1, y0 + 1);
            double a = Values[y0 * Width + x0] * (1 - px + x0) + Values[y0 * Width + x1] * (px - x0);
            double b = Values[y1 * Width + x0] * (1 - px + x0) + Values[y1 * Width + x1] * (px - x0);
            return a * (1 - py + y0) + b * (py - y0);
        }
    }

    internal sealed class SubjectSegmentation : IDisposable
    {
        private static readonly SemaphoreSlim InferenceGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _gate = InferenceGate;
        private readonly bool _preferGpu;
        private readonly int _workers;
        private readonly string _modelDirectory;
        private readonly GpuAdapterMemoryInfo _adapter;
        private readonly int _softLimitMb;
        private volatile string _backend = "Not started";
        internal string Backend { get { return _backend; } }
        private InferenceSession _people, _general;
        private bool _peopleGpu, _generalGpu;
        private int _disposed;

        public SubjectSegmentation(int workers, bool preferGpu = true, string modelDirectory = null, GpuAdapterMemoryInfo adapter = null, int softLimitMb = 2048)
        {
            _workers = Math.Max(1, Math.Min(8, workers));
            _preferGpu = preferGpu;
            _adapter = preferGpu ? adapter ?? GpuHardware.SelectProcessing("Auto", GpuHardware.Devices) : null;
            _softLimitMb = softLimitMb;
            _modelDirectory = modelDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        }

        public SubjectMask Detect(BitmapSource original, CancellationToken token)
        {
            _gate.Wait(token);
            try
            {
                if (_disposed != 0) throw new OperationCanceledException();
                token.ThrowIfCancellationRequested();
                string reason;
                if ((_peopleGpu || _generalGpu) && !GpuHardware.CanAllocate(_adapter, 0, _softLimitMb, out reason))
                {
                    if (_peopleGpu) { _people.Dispose(); _people = null; _peopleGpu = false; }
                    if (_generalGpu) { _general.Dispose(); _general = null; _generalGpu = false; }
                    _backend = "CPU fallback: " + reason;
                }
                var person = Predict(original, true, token);
                if (IsReliable(person.Values)) return person;
                var subject = Predict(original, false, token);
                if (IsReliable(subject.Values)) return subject;
                return new SubjectMask { Description = "No confident subject; gentle global adjustments" };
            }
            finally { _gate.Release(); }
        }

        private static bool IsReliable(float[] values)
        {
            int foreground = 0, background = 0;
            foreach (float value in values)
            {
                if (value > 0.85) foreground++;
                if (value < 0.15) background++;
            }
            return foreground > values.Length * 0.005 && background > values.Length * 0.03;
        }

        private SubjectMask Predict(BitmapSource original, bool person, CancellationToken token)
        {
            int size = person ? 192 : 320;
            byte[] pixels = ReadPixels(original, size, size);
            int plane = size * size;
            float divisor = 255;
            if (!person)
            {
                divisor = 1;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i + 3] >= 128) divisor = Math.Max(divisor, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])));
            }
            var data = new float[plane * 3];
            double[] mean = person ? new[] { 0.5, 0.5, 0.5 } : new[] { 0.485, 0.456, 0.406 };
            double[] std = person ? new[] { 0.5, 0.5, 0.5 } : new[] { 0.229, 0.224, 0.225 };
            for (int i = 0; i < plane; i++)
                for (int c = 0; c < 3; c++)
                {
                    double alpha = pixels[i * 4 + 3] / 255.0;
                    double value = pixels[i * 4 + 2 - c] / divisor * alpha + 0.5 * (1 - alpha);
                    data[c * plane + i] = (float)((value - mean[c]) / std[c]);
                }
            token.ThrowIfCancellationRequested();
            bool gpu = person ? _peopleGpu : _generalGpu;
            InferenceSession session = person ? _people : _general;
            if (session == null)
            {
                string file = Path.Combine(_modelDirectory, person ? "humanseg.onnx" : "u2netp.onnx");
                VerifyModel(file, person);
                session = CreateSession(file, _preferGpu, out gpu);
                if (person) { _people = session; _peopleGpu = gpu; }
                else { _general = session; _generalGpu = gpu; }
            }
            token.ThrowIfCancellationRequested();
            float[] output;
            try { output = Run(session, data, size, token); }
            catch (OnnxRuntimeException)
            {
                token.ThrowIfCancellationRequested();
                if (!gpu) throw;
                session.Dispose();
                if (person) _people = null; else _general = null;
                string file = Path.Combine(_modelDirectory, person ? "humanseg.onnx" : "u2netp.onnx");
                session = CreateSession(file, false, out gpu);
                if (person) { _people = session; _peopleGpu = false; }
                else { _general = session; _generalGpu = false; }
                output = Run(session, data, size, token);
            }
            if (output.Length != plane * (person ? 2 : 1)) throw new InvalidDataException("Unexpected subject model output shape.");
            var mask = new float[plane];
            for (int i = 0; i < plane; i++)
            {
                float value = output[(person ? plane : 0) + i];
                if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidDataException("Invalid subject confidence.");
                mask[i] = Math.Max(0, Math.Min(1, value));
            }
            token.ThrowIfCancellationRequested();
            return new SubjectMask { Values = mask, Width = size, Height = size, IsPerson = person,
                Description = (person ? "People" : "Foreground subject") + (gpu ? " / DirectML GPU (" + _adapter.Name + ")" : " / CPU") };
        }

        private static float[] Run(InferenceSession session, float[] data, int size, CancellationToken token)
        {
            var tensor = new DenseTensor<float>(data, new[] { 1, 3, size, size });
            var input = NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), tensor);
            using (var options = new RunOptions())
            using (token.Register(delegate { options.Terminate = true; }))
            {
                try
                {
                    using (var results = session.Run(new[] { input }, new[] { session.OutputMetadata.Keys.First() }, options))
                        return results.First().AsTensor<float>().ToArray();
                }
                catch (OnnxRuntimeException) { token.ThrowIfCancellationRequested(); throw; }
            }
        }

        private InferenceSession CreateSession(string path, bool preferGpu, out bool gpu)
        {
            gpu = false;
            _backend = "CPU";
            string reason = null;
            if (preferGpu && _adapter != null && GpuHardware.CanAllocate(_adapter, 256L * 1024 * 1024, _softLimitMb, out reason))
            {
                try
                {
                    using (var options = Options())
                    {
                        options.EnableMemoryPattern = false;
                        options.AppendExecutionProvider_DML(_adapter.Index);
                        var session = new InferenceSession(path, options);
                        gpu = true;
                        _backend = "DirectML: " + _adapter.Name;
                        return session;
                    }
                }
                catch (OnnxRuntimeException) { _backend = "CPU fallback: DirectML unavailable"; }
                catch (DllNotFoundException) { _backend = "CPU fallback: DirectML unavailable"; }
                catch (EntryPointNotFoundException) { _backend = "CPU fallback: DirectML unavailable"; }
            }
            else if (preferGpu) _backend = "CPU fallback: " + (reason ?? "selected GPU unavailable");
            using (var options = Options()) return new InferenceSession(path, options);
        }

        private SessionOptions Options()
        {
            OrtEnv.Instance().DisableTelemetryEvents();
            var options = new SessionOptions { ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = _workers, InterOpNumThreads = 1, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            return options;
        }

        private static void VerifyModel(string path, bool person)
        {
            string expected = person ? "552D8A984054E59B5D773D24B9B12022B22046CEB2BBC4C9AAEACEB36A9DDF24"
                : "309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8";
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "") != expected)
                    throw new InvalidDataException("Subject model checksum mismatch; reinstall the complete viewer package.");
        }

        internal static byte[] ReadPixels(BitmapSource original, int width, int height)
        {
            BitmapSource resized = new TransformedBitmap(original, new ScaleTransform((double)width / original.PixelWidth, (double)height / original.PixelHeight));
            var converted = new FormatConvertedBitmap(resized, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // A canceled native run finishes before its sessions are released; closing a window never blocks on inference.
            Task.Run(delegate
            {
                _gate.Wait();
                try
                {
                    if (_people != null) _people.Dispose();
                    if (_general != null) _general.Dispose();
                    _people = _general = null;
                }
                finally { _gate.Release(); }
            });
        }
    }
}
