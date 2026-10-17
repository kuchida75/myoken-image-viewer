using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ZonerInspiredViewer
{
    internal struct FaceBox
    {
        internal double X, Y, Width, Height, Score;
        internal FaceBox(double x, double y, double w, double h, double score) { X = x; Y = y; Width = w; Height = h; Score = score; }
        internal bool Valid { get { return !Double.IsNaN(X + Y + Width + Height + Score) && !Double.IsInfinity(X + Y + Width + Height + Score)
            && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001 && Score >= .85 && Score <= 1; } }
    }

    // YuNet geometry follows the Ubuntu implementation. Neural inference is RTX-only;
    // decoding, tensor preparation and crop composition remain bounded CPU work.
    internal sealed class FaceDetection : IDisposable
    {
        internal const int Edge = 512;
        internal const string ModelHash = "EBAFCE4E3C118D6554634BE5C27AB333B4C047A9A8C3FAF1D7CF93101C22F0F0";
        private static readonly string[] Names = { "cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32", "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32" };
        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly GpuAdapterMemoryInfo _adapter;
        private readonly int _budget;
        internal string Backend { get; private set; }
        internal string ExecutionEvidence { get; private set; }

        internal static GpuAdapterMemoryInfo SelectRtx(string key, IEnumerable<GpuAdapterMemoryInfo> devices)
        {
            var rtx = devices.Where(d => d.Key != null && d.Key.StartsWith("10DE:", StringComparison.OrdinalIgnoreCase)
                && d.Name != null && d.Name.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0 && d.IsIntegrated != true);
            return key == "Auto" || String.IsNullOrEmpty(key) ? rtx.OrderByDescending(d => d.CapacityBytes).FirstOrDefault() : rtx.FirstOrDefault(d => d.Key == key);
        }

        internal FaceDetection(int workers, string adapterKey, int budgetMb, CancellationToken token, string model = null)
        {
            token.ThrowIfCancellationRequested();
            _adapter = SelectRtx(adapterKey, GpuHardware.Devices); _budget = budgetMb;
            if (_adapter == null) throw new NotSupportedException("Faces requires an NVIDIA RTX GPU. Select Auto or an RTX adapter under Configure > Performance.");
            CheckBudget(256L * 1024 * 1024);
            string path = model ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yunet-face.onnx");
            byte[] bytes = VerifyModel(path);
            string scratch = Path.Combine(Path.GetTempPath(), "myoken-faces-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            InferenceSession session = null;
            try
            {
                OrtEnv.Instance().DisableTelemetryEvents();
                using (var options = new SessionOptions { ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, EnableMemoryPattern = false,
                    IntraOpNumThreads = Math.Max(1, Math.Min(8, workers)), InterOpNumThreads = 1, EnableProfiling = true,
                    ProfileOutputPathPrefix = Path.Combine(scratch, "execution"), LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR })
                {
                    options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                    options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
                    options.AddFreeDimensionOverrideByName("height", Edge); options.AddFreeDimensionOverrideByName("width", Edge);
                    options.AppendExecutionProvider_DML(_adapter.Index);
                    session = new InferenceSession(bytes, options);
                }
                if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 12 || Names.Any(n => !session.OutputMetadata.ContainsKey(n)))
                    throw new InvalidDataException("Unexpected YuNet output contract.");
                var input = session.InputMetadata.Single();
                if (input.Value.ElementType != typeof(float) || input.Value.Dimensions.Length != 4 || input.Value.Dimensions[0] != 1 || input.Value.Dimensions[1] != 3)
                    throw new InvalidDataException("Unexpected YuNet input contract.");
                _input = input.Key; _session = session;
                Run(new float[3 * Edge * Edge], token);
                string profile = session.EndProfiling();
                using (var stream = File.OpenRead(profile))
                    ExecutionEvidence = VerifyExecutionProfile(stream);
                Backend = "GPU " + _adapter.Index + " / " + _adapter.Name + " / DirectML (verified RTX inference)";
                token.ThrowIfCancellationRequested();
                session = null;
            }
            finally
            {
                if (session != null) session.Dispose();
                Array.Clear(bytes, 0, bytes.Length);
                foreach (string file in Directory.EnumerateFiles(scratch)) { try { File.Delete(file); } catch (IOException) { } }
                try { Directory.Delete(scratch); } catch (IOException) { }
            }
        }

        internal static byte[] VerifyModel(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != 229738) throw new InvalidDataException("Face model is missing or damaged; restore the complete model package.");
                var data = new byte[229738]; int at = 0, count;
                while (at < data.Length && (count = stream.Read(data, at, data.Length - at)) > 0) at += count;
                using (var sha = SHA256.Create())
                    if (at != data.Length || BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "") != ModelHash)
                        throw new InvalidDataException("Face model checksum mismatch; restore the complete model package.");
                return data;
            }
        }

        [DataContract] private sealed class ProfileEvent { [DataMember(Name = "args")] public ProfileArguments Args { get; set; } }
        [DataContract] private sealed class ProfileArguments { [DataMember(Name = "provider")] public string Provider { get; set; } }
        internal static string VerifyExecutionProfile(Stream profile)
        {
            var events = (ProfileEvent[])new DataContractJsonSerializer(typeof(ProfileEvent[])).ReadObject(profile);
            string[] providers = events.Where(e => e.Args != null && !String.IsNullOrEmpty(e.Args.Provider)).Select(e => e.Args.Provider).ToArray();
            if (!providers.Contains("DmlExecutionProvider") || providers.Any(p => p != "DmlExecutionProvider"))
                throw new NotSupportedException("RTX execution could not be verified. No CPU fallback is used for face detection.");
            return providers.Length + " profiled DirectML kernel event(s); no CPU inference kernels";
        }

        private void CheckBudget(long extra)
        {
            string reason;
            if (!GpuHardware.CanAllocate(_adapter, extra, _budget, out reason))
                throw new InvalidOperationException("Face detection paused: " + reason + ". Free GPU memory or adjust the AI GPU memory limit in Configure, then scan again.");
        }

        internal FaceBox[] Detect(FacePixels image, CancellationToken token)
        {
            var found = DetectOne(image, token).ToList();
            if (Math.Max(image.Width, image.Height) > 1200)
                foreach (int y in Starts(image.Height)) foreach (int x in Starts(image.Width))
                {
                    token.ThrowIfCancellationRequested();
                    int width = Math.Min(1024, image.Width), height = Math.Min(1024, image.Height);
                    foreach (var b in DetectOne(image.Crop(x, y, width, height), token))
                        found.Add(new FaceBox((x + b.X * width) / image.Width, (y + b.Y * height) / image.Height,
                            b.Width * width / image.Width, b.Height * height / image.Height, b.Score));
                }
            if (found.Count == 0)
            {
                var small = image.Resize(Edge);
                for (int q = 1; q <= 3; q++) foreach (var b in DetectOne(small.Rotate(q), token)) found.Add(Unrotate(b, q));
            }
            return Suppress(found);
        }
        private static int[] Starts(int size) { return size <= 1024 ? new[] { 0 } : size <= 1792 ? new[] { 0, size - 1024 } : new[] { 0, 768, size - 1024 }; }

        private FaceBox[] DetectOne(FacePixels image, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); CheckBudget(32L * 1024 * 1024);
            double scale = Math.Min(Edge / (double)image.Width, Edge / (double)image.Height);
            double px = (Edge - image.Width * scale) / 2, py = (Edge - image.Height * scale) / 2;
            var data = new float[3 * Edge * Edge];
            for (int y = 0; y < Edge; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < Edge; x++)
                {
                    double sx = (x + .5 - px) / scale - .5, sy = (y + .5 - py) / scale - .5;
                    if (x + .5 < px || y + .5 < py || x + .5 >= Edge - px || y + .5 >= Edge - py) continue;
                    for (int c = 0; c < 3; c++) data[c * Edge * Edge + y * Edge + x] = (float)image.Sample(sx, sy, c);
                }
            }
            float[][] heads;
            try { heads = Run(data, token); } finally { Array.Clear(data, 0, data.Length); }
            var candidates = new List<FaceBox>();
            for (int level = 0; level < 3; level++)
            {
                int stride = 8 << level, columns = Edge / stride;
                for (int i = 0; i < columns * columns; i++)
                {
                    double score = Math.Sqrt(Clamp(heads[level][i]) * Clamp(heads[level + 3][i])); if (score < .85) continue;
                    var b = heads[level + 6]; double bw = Math.Exp(b[i * 4 + 2]) * stride, bh = Math.Exp(b[i * 4 + 3]) * stride;
                    double cx = (i % columns + b[i * 4]) * stride, cy = (i / columns + b[i * 4 + 1]) * stride;
                    double left = Clamp((cx - bw / 2 - px) / scale / image.Width), top = Clamp((cy - bh / 2 - py) / scale / image.Height);
                    double right = Clamp((cx + bw / 2 - px) / scale / image.Width), bottom = Clamp((cy + bh / 2 - py) / scale / image.Height);
                    var box = new FaceBox(left, top, right - left, bottom - top, score); if (box.Valid) candidates.Add(box);
                }
            }
            return Suppress(candidates);
        }
        private float[][] Run(float[] data, CancellationToken token)
        {
            using (var run = new RunOptions())
            using (token.Register(delegate { run.Terminate = true; }))
            {
                try
                {
                    using (var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<float>(data, new[] { 1, 3, Edge, Edge })) }, Names, run))
                        return results.Select((r, i) =>
                        {
                            var tensor = r.AsTensor<float>(); int stride = 8 << (i % 3), count = Edge / stride * (Edge / stride), channels = i < 6 ? 1 : i < 9 ? 4 : 10;
                            if (!tensor.Dimensions.SequenceEqual(new[] { 1, count, channels })) throw new InvalidDataException("Unexpected face output shape.");
                            var output = tensor.ToArray(); if (output.Any(v => Single.IsNaN(v) || Single.IsInfinity(v))) throw new InvalidDataException("Invalid face prediction.");
                            return output;
                        }).ToArray();
                }
                catch (OnnxRuntimeException) { token.ThrowIfCancellationRequested(); throw; }
            }
        }
        private static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
        internal static FaceBox Unrotate(FaceBox b, int q)
        {
            if (q == 1) return new FaceBox(b.Y, Math.Max(0, 1 - b.X - b.Width), b.Height, b.Width, b.Score);
            if (q == 2) return new FaceBox(Math.Max(0, 1 - b.X - b.Width), Math.Max(0, 1 - b.Y - b.Height), b.Width, b.Height, b.Score);
            if (q == 3) return new FaceBox(Math.Max(0, 1 - b.Y - b.Height), b.X, b.Height, b.Width, b.Score);
            return b;
        }
        internal static FaceBox[] Suppress(IEnumerable<FaceBox> boxes)
        {
            var kept = new List<FaceBox>();
            foreach (var b in boxes.Where(b => b.Valid).OrderByDescending(b => b.Score).ThenBy(b => b.Y).ThenBy(b => b.X).Take(1024))
            {
                if (kept.Any(k => Overlap(k, b) > .3)) continue;
                kept.Add(b); if (kept.Count == 64) break;
            }
            return kept.OrderBy(b => b.Y).ThenBy(b => b.X).ToArray();
        }
        private static double Overlap(FaceBox a, FaceBox b)
        {
            double overlap = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
            return overlap / (a.Width * a.Height + b.Width * b.Height - overlap);
        }
        public void Dispose() { _session.Dispose(); }
    }
}
