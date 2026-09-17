using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class GpuBudgetSample
    {
        internal bool Available;
        internal long Budget, Usage;
        internal string Error;
    }

    internal sealed class GpuImageStatistics
    {
        internal long Used, Target, WindowsBudget, ProcessUsage, Uploads, JpegDecodes, Hits, Evictions;
        internal int Count, Generation;
        internal bool ReleaseActive, BudgetAvailable;
        internal string Adapter, Status;
    }

    internal sealed class GpuImageLease : IDisposable
    {
        internal GpuImageTexture Texture;
        private readonly Action _released;
        internal GpuImageLease(GpuImageTexture texture, Action released) { Texture = texture; _released = released; Interlocked.Increment(ref texture.Pins); }
        public void Dispose()
        {
            GpuImageTexture texture = Interlocked.Exchange(ref Texture, null);
            if (texture != null) { Interlocked.Decrement(ref texture.Pins); _released(); }
        }
    }

    internal sealed class GpuImageCache : IDisposable
    {
        private sealed class DecodedStamp { internal string Path; internal bool ColorManaged; }
        private readonly ConditionalWeakTable<BitmapSource, DecodedStamp> _decoded = new ConditionalWeakTable<BitmapSource, DecodedStamp>();
        private readonly object _gate = new object();
        private readonly Dictionary<string, GpuImageTexture> _textures = new Dictionary<string, GpuImageTexture>(StringComparer.OrdinalIgnoreCase);
        private readonly List<GpuImageTexture> _retired = new List<GpuImageTexture>();
        private readonly Func<GpuAdapterMemoryInfo, GpuBudgetSample> _memory;
        private readonly Timer _timer;
        private GpuImageDevice _device;
        private volatile string _adapterKey = "Auto";
        private volatile bool _enabled = true, _jpegEnabled = true, _disposed;
        private int _generation, _deviceGeneration = -1, _limitMb = 4096;
        private long _used, _clock, _uploads, _decodes, _hits, _evictions;
        private DateTime _retryAfter;
        private string _status = "Waiting for an image";
        private volatile GpuImageStatistics _statistics = new GpuImageStatistics { Status = "Waiting for an image" };
        internal GpuImageStatistics Statistics { get { return _statistics; } }
        internal bool Enabled { get { return _enabled; } }
        internal bool JpegEnabled { get { return _jpegEnabled; } }
        internal int LimitMb { get { return _limitMb; } }
        internal string AdapterKey { get { return _adapterKey; } }

        internal void MarkDecoded(string path, BitmapSource pixels, bool colorManaged)
        { _decoded.GetValue(pixels, p => new DecodedStamp { Path = path, ColorManaged = colorManaged }); }

        private string ContentKey(string path, BitmapSource pixels)
        {
            DecodedStamp stamp;
            return _decoded.TryGetValue(pixels, out stamp) && String.Equals(stamp.Path, path, StringComparison.OrdinalIgnoreCase)
                ? (stamp.ColorManaged ? "decoded:icc" : "decoded:unmanaged") : null;
        }

        internal GpuImageCache(GpuMemoryPressureMonitor monitor, Func<GpuAdapterMemoryInfo, GpuBudgetSample> memory = null)
        {
            _memory = memory ?? monitor.Read;
            _timer = new Timer(delegate { Maintain(); }, null, 1000, 1000);
        }

        internal static long EffectiveBudget(int limitMb, long ownedBytes, GpuBudgetSample sample)
        {
            if (!sample.Available || sample.Budget <= 0) return 0;
            long headroom = Math.Min(256L * 1024 * 1024, sample.Budget / 10);
            long other = Math.Max(0, sample.Usage - ownedBytes);
            return Math.Max(0, Math.Min(limitMb * 1024L * 1024, sample.Budget - headroom - other));
        }

        internal void Configure(bool enabled, string adapterKey, int limitMb, bool jpegEnabled)
        {
            adapterKey = String.IsNullOrEmpty(adapterKey) ? "Auto" : adapterKey;
            limitMb = Math.Max(128, Math.Min(24576, limitMb));
            bool reset = _enabled != enabled || _adapterKey != adapterKey || _jpegEnabled != jpegEnabled;
            _enabled = enabled; _adapterKey = adapterKey; _limitMb = limitMb; _jpegEnabled = jpegEnabled;
            if (reset) Interlocked.Increment(ref _generation);
            int generation = _generation;
            Task.Run(delegate { lock (_gate) { if (!_disposed && generation == _generation) { if (reset && _deviceGeneration != generation) ResetDevice(); Trim(0); Publish(); } } });
        }

        private bool EnsureDevice()
        {
            if (_disposed || !_enabled) { _status = "WPF fallback selected"; return false; }
            if (_deviceGeneration != _generation) ResetDevice();
            if (DateTime.UtcNow < _retryAfter) return false;
            if (_device != null) return true;
            var adapter = GpuHardware.SelectProcessing(_adapterKey, GpuHardware.Devices);
            if (adapter == null) { _status = "WPF fallback: selected GPU unavailable"; return false; }
            var sample = _memory(adapter);
            if (!sample.Available || EffectiveBudget(_limitMb, _used, sample) < 32L * 1024 * 1024)
            { _status = "WPF fallback: " + (sample.Error ?? "Windows GPU budget pressure"); return false; }
            try { _device = new GpuImageDevice(adapter); _status = "GPU ready: " + adapter.Name; return true; }
            catch (Exception error) { Fail(error); return false; }
        }

        private void ResetDevice()
        {
            foreach (var value in _textures.Values) Retire(value);
            _textures.Clear(); Reap();
            if (_device != null) _device.Dispose(); _device = null;
            _deviceGeneration = _generation; _retryAfter = DateTime.MinValue;
        }

        private void Retire(GpuImageTexture texture)
        { texture.Retired = true; _retired.Add(texture); }
        private void Reap()
        {
            for (int i = _retired.Count - 1; i >= 0; i--)
                if (Volatile.Read(ref _retired[i].Pins) == 0)
                {
                    var texture = _retired[i]; _used -= texture.Bytes; texture.Dispose(); _retired.RemoveAt(i);
                }
        }
        private long Trim(long additional)
        {
            Reap();
            GpuBudgetSample sample = _device == null ? new GpuBudgetSample() : _memory(_device.Adapter);
            long target = EffectiveBudget(_limitMb, _used, sample);
            while (_used + additional > target)
            {
                var oldest = _textures.Values.Where(t => Volatile.Read(ref t.Pins) == 0).OrderBy(t => t.Access).FirstOrDefault();
                if (oldest == null) break;
                _textures.Remove(oldest.Path); Retire(oldest); _evictions++; Reap();
            }
            return target;
        }

        private bool Admit(long bytes)
        {
            long target = Trim(bytes);
            if (_used + bytes <= target) return true;
            _status = "WPF fallback: image exceeds VRAM limit or Windows budget"; return false;
        }

        private void LeaseReleased()
        {
            Task.Run(delegate { lock (_gate) { Reap(); if (!_disposed) { Trim(0); Publish(); } } });
        }

        internal GpuImageLease Acquire(string path, BitmapSource pixels, FileRevision revision, CancellationToken token)
        {
            lock (_gate)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!EnsureDevice() || !revision.Matches(path)) return null;
                    int generation = _deviceGeneration;
                    Trim(0);
                    GpuImageTexture texture;
                    if (_textures.TryGetValue(path, out texture))
                    {
                        string content = ContentKey(path, pixels);
                        if (texture.Revision.Equals(revision) && texture.Width == pixels.PixelWidth && texture.Height == pixels.PixelHeight
                            && (Object.ReferenceEquals(texture.CpuPixels.Target, pixels) || content != null && texture.ContentKey == content))
                        {
                            if (generation != _generation || !_enabled || _disposed) return null;
                            texture.CpuPixels = new WeakReference(pixels); if (content != null) texture.ContentKey = content;
                            texture.Access = ++_clock; _hits++; return new GpuImageLease(texture, LeaseReleased);
                        }
                        _textures.Remove(path); Retire(texture); Reap();
                    }
                    long bytes = GpuImageDevice.TextureBytes(pixels.PixelWidth, pixels.PixelHeight);
                    if (!Admit(bytes)) return null;
                    texture = _device.Create(pixels.PixelWidth, pixels.PixelHeight);
                    try
                    {
                        _device.Upload(texture, pixels, token);
                        token.ThrowIfCancellationRequested();
                        if (!revision.Matches(path) || generation != _generation || !_enabled || _disposed) return null;
                        Insert(texture, path, pixels, revision); _uploads++;
                        var lease = new GpuImageLease(texture, LeaseReleased); texture = null; return lease;
                    }
                    finally { if (texture != null) texture.Dispose(); }
                }
                catch (OperationCanceledException) { throw; }
                catch (NotSupportedException error) { _status = "WPF fallback: " + error.Message; return null; }
                catch (Exception error) { Fail(error); return null; }
                finally { Publish(); }
            }
        }

        internal bool TryDecodeJpeg(string path, CancellationToken token, out BitmapSource pixels)
        {
            pixels = null;
            if (!_enabled || !_jpegEnabled || _disposed) return false;
            FileRevision revision = FileRevision.Read(path);
            // Bounded compressed input and cancellation protect preloading from unusually large files.
            if (revision.Length < 0 || revision.Length > 128L * 1024 * 1024) return false;
            lock (_gate)
            {
                try
                {
                    token.ThrowIfCancellationRequested(); if (!EnsureDevice()) return false;
                    int generation = _deviceGeneration;
                    GpuImageTexture existing;
                    if (_textures.TryGetValue(path, out existing) && existing.Revision.Equals(revision) && existing.Backend.StartsWith("nvJPEG", StringComparison.Ordinal))
                    {
                        pixels = existing.CpuPixels.Target as BitmapSource;
                        if (pixels == null) { pixels = _device.Readback(existing); existing.CpuPixels = new WeakReference(pixels); MarkDecoded(path, pixels, false); }
                        existing.Access = ++_clock; _hits++; return true;
                    }
                    byte[] encoded;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var memory = new MemoryStream())
                    {
                        var buffer = new byte[65536]; int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            if (memory.Length + count > 128L * 1024 * 1024) return false;
                            memory.Write(buffer, 0, count);
                        }
                        encoded = memory.ToArray();
                    }
                    int width, height; _device.JpegSize(encoded, out width, out height);
                    long bytes = GpuImageDevice.TextureBytes(width, height);
                    // Allow both the raw CUDA output buffer and transient readback, plus codec workspace.
                    if (!Admit(bytes * 3 + 128L * 1024 * 1024)) return false;
                    var texture = _device.Create(width, height);
                    try
                    {
                        _device.DecodeJpeg(texture, encoded, token);
                        pixels = _device.Readback(texture);
                        token.ThrowIfCancellationRequested();
                        if (!revision.Matches(path)) { pixels = null; return false; }
                        if (generation != _generation || !_enabled || _disposed) return true;
                        MarkDecoded(path, pixels, false);
                        Insert(texture, path, pixels, revision); texture = null; _decodes++; return true;
                    }
                    finally { if (texture != null) texture.Dispose(); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { _status = "CPU JPEG fallback: " + error.Message; pixels = null; return false; }
                finally { Publish(); }
            }
        }

        private void Insert(GpuImageTexture texture, string path, BitmapSource pixels, FileRevision revision)
        {
            GpuImageTexture old;
            if (_textures.TryGetValue(path, out old)) { _textures.Remove(path); Retire(old); }
            texture.Path = path; texture.Revision = revision; texture.CpuPixels = new WeakReference(pixels); texture.Access = ++_clock;
            texture.ContentKey = ContentKey(path, pixels);
            _textures[path] = texture; _used += texture.Bytes; _status = texture.Backend; Reap();
        }
        private void Fail(Exception error)
        {
            ResetDevice(); _status = "WPF fallback: " + error.Message; _retryAfter = DateTime.UtcNow.AddSeconds(10);
        }
        private void Publish()
        {
            var sample = _device == null ? new GpuBudgetSample() : _memory(_device.Adapter);
            long target = EffectiveBudget(_limitMb, _used, sample);
            _statistics = new GpuImageStatistics { Used = _used, Target = target, WindowsBudget = sample.Budget, ProcessUsage = sample.Usage,
                Uploads = _uploads, JpegDecodes = _decodes, Hits = _hits, Evictions = _evictions, Count = _textures.Count,
                Generation = _generation, BudgetAvailable = sample.Available, ReleaseActive = _used > target || !_enabled || _retired.Any(t => t.Pins > 0),
                Adapter = _device == null ? null : _device.Adapter.Name, Status = _status };
        }
        private void Maintain()
        {
            if (_disposed || !Monitor.TryEnter(_gate)) return;
            try { if (!_disposed) { Trim(0); Publish(); } }
            catch (Exception error) { _status = "GPU budget monitor: " + error.Message; }
            finally { Monitor.Exit(_gate); }
        }
        internal void Remove(string path)
        {
            Task.Run(delegate { lock (_gate) { GpuImageTexture value; if (_textures.TryGetValue(path, out value))
                { _textures.Remove(path); Retire(value); Reap(); } if (!_disposed) Publish(); } });
        }
        internal void Clear()
        {
            int generation = Interlocked.Increment(ref _generation);
            Task.Run(delegate { lock (_gate) { if (!_disposed && generation == _generation)
                { if (_deviceGeneration != generation) ResetDevice(); Reap(); Publish(); } } });
        }
        public void Dispose()
        {
            _disposed = true; _timer.Dispose();
            lock (_gate) { ResetDevice(); Reap(); }
        }
    }
}
