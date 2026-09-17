using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class GpuThumbnailProcessor : IDisposable
    {
        internal static readonly string[] Modes = { "Auto", "CPU", "Integrated GPU" };
        private sealed class Settings { public string Mode; public int LimitMb, Generation; }
        private sealed class Timing { public int Samples; public double Cpu, Gpu; public bool UseGpu { get { return Samples >= 3 && Gpu < Cpu * 0.8; } } }
        private readonly object _computeGate = new object();
        private readonly Dictionary<string, Timing> _timings = new Dictionary<string, Timing>();
        private volatile Settings _settings = new Settings { Mode = "CPU", LimitMb = 128 };
        private GpuThumbnailResizer _resizer;
        private int _generation, _activeGeneration = -1, _disposed;
        private long _gpuJobs;
        private DateTime _retryAfter;
        private volatile string _status = "CPU";
        internal string Status { get { return _status; } }
        internal long GpuJobs { get { return Interlocked.Read(ref _gpuJobs); } }
        internal string Mode { get { return _settings.Mode; } }
        internal int LimitMb { get { return _settings.LimitMb; } }

        internal void Configure(string mode, int limitMb, bool resetBenchmark = false)
        {
            mode = Array.IndexOf(Modes, mode) >= 0 ? mode : "Auto";
            limitMb = limitMb >= 16 && limitMb <= 1024 ? limitMb : 128;
            if (!resetBenchmark && mode == _settings.Mode && limitMb == _settings.LimitMb) return;
            var settings = new Settings { Mode = mode, LimitMb = limitMb, Generation = Interlocked.Increment(ref _generation) };
            _settings = settings; _status = mode == "CPU" ? "CPU" : "Waiting for an uncached thumbnail";
            Task.Run(delegate { lock (_computeGate) { if (_settings == settings && _activeGeneration != settings.Generation) ResetDevice(settings.Generation); } });
        }

        private void ResetDevice(int generation)
        {
            if (_resizer != null) _resizer.Dispose(); _resizer = null;
            _timings.Clear(); _retryAfter = DateTime.MinValue; _activeGeneration = generation;
        }

        internal BitmapSource Process(string path, int size, Func<int, BitmapSource> cpu, CancellationToken token)
        {
            Settings settings = _settings;
            Action<string> report = value => { if (_settings == settings) _status = value; };
            if (settings.Mode == "CPU" || _disposed != 0) return cpu(size);
            // Never queue CPU workers behind the iGPU: a busy background device uses the normal CPU path.
            if (!Monitor.TryEnter(_computeGate)) return cpu(size);
            try
            {
                if (_disposed != 0 || _settings != settings) return cpu(size);
                if (_activeGeneration != settings.Generation) ResetDevice(settings.Generation);
                var adapter = GpuHardware.SelectIntegrated(GpuHardware.Devices);
                if (adapter == null) { report("CPU: no supported integrated GPU"); return cpu(size); }
                if (DateTime.UtcNow < _retryAfter) return cpu(size);
                string key = Path.GetExtension(path).ToLowerInvariant() + (size < 512 ? ":small" : ":large");
                Timing timing;
                if (!_timings.TryGetValue(key, out timing)) { timing = new Timing(); _timings.Add(key, timing); }
                if (settings.Mode == "Auto" && timing.Samples >= 3 && !timing.UseGpu)
                { report("Auto: CPU faster for " + key); return cpu(size); }
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (_resizer == null) _resizer = new GpuThumbnailResizer(adapter);
                    BitmapSource baseline = null;
                    double cpuMilliseconds = 0;
                    var watch = new Stopwatch();
                    if (settings.Mode == "Auto" && timing.Samples < 3)
                    {
                        watch.Start(); baseline = cpu(size); watch.Stop(); cpuMilliseconds = watch.Elapsed.TotalMilliseconds;
                    }
                    watch.Restart();
                    BitmapSource source = cpu(Math.Min(4096, size * 2));
                    var result = _resizer.Resize(source, size, settings.LimitMb, token);
                    watch.Stop(); Interlocked.Increment(ref _gpuJobs);
                    if (baseline != null)
                    {
                        timing.Cpu += cpuMilliseconds; timing.Gpu += watch.Elapsed.TotalMilliseconds; timing.Samples++;
                        report(timing.Samples < 3 ? "Auto: comparing CPU / " + adapter.Name + " (" + timing.Samples + "/3)"
                            : "Auto: " + (timing.UseGpu ? adapter.Name : "CPU") + " for " + key
                                + String.Format(" (CPU {0:F1} ms, GPU path {1:F1} ms)", timing.Cpu / 3, timing.Gpu / 3));
                        if (!timing.UseGpu) return baseline;
                    }
                    else report(adapter.Name + " (GPU resize; CPU decode/cache)");
                    return result;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    if (_resizer != null) _resizer.Dispose(); _resizer = null;
                    _retryAfter = DateTime.UtcNow.AddSeconds(30); report("CPU fallback: " + error.Message);
                    return cpu(size);
                }
            }
            finally { Monitor.Exit(_computeGate); }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            Task.Run(delegate { lock (_computeGate) ResetDevice(_generation); });
        }
    }
}
