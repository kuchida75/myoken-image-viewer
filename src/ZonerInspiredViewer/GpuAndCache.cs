using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class GpuMemoryPressureMonitor
    {
        private DateTime _lastHighPressureUtc;

        public bool IsUnderPressure
        {
            get
            {
                GpuBudgetSample sample = Read(GpuHardware.SelectProcessing("Auto", GpuHardware.Devices));
                return (DateTime.UtcNow - _lastHighPressureUtc).TotalSeconds < 3
                    || sample.Available && sample.Usage > sample.Budget * 0.9;
            }
        }

        public string StatusText
        {
            get { return "Live DXGI process usage and Windows GPU budget"; }
        }

        public void NotifySoftPressure()
        {
            _lastHighPressureUtc = DateTime.UtcNow;
        }

        internal GpuBudgetSample Read(GpuAdapterMemoryInfo adapter)
        {
            GpuHardware.MemoryInfo local, shared; string error;
            if (!GpuHardware.TryMemory(adapter, out local, out shared, out error))
                return new GpuBudgetSample { Error = error };
            if (local.Budget == 0 && adapter.IsIntegrated == true) local = shared;
            return new GpuBudgetSample { Available = local.Budget > 0, Budget = (long)Math.Min((ulong)Int64.MaxValue, local.Budget),
                Usage = (long)Math.Min((ulong)Int64.MaxValue, local.CurrentUsage), Error = local.Budget == 0 ? "Windows GPU budget unavailable" : null };
        }
    }

    internal sealed class VramImageCache
    {
        internal event Action<string> Invalidated;
        internal event Action Cleared;
        private sealed class Entry
        {
            public BitmapSource Bitmap;
            public long Bytes;
            public DateTime LastAccessUtc;
            public FileRevision Revision;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries;
        private readonly GpuMemoryPressureMonitor _pressureMonitor;
        private long _budgetBytes;
        private long _usedBytes;

        public VramImageCache(int budgetMb, GpuMemoryPressureMonitor pressureMonitor)
        {
            _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            _pressureMonitor = pressureMonitor;
            SetBudgetMb(budgetMb);
        }

        public int BudgetMb
        {
            get
            {
                lock (_gate)
                {
                    return (int)(_budgetBytes / 1024 / 1024);
                }
            }
        }

        public long UsedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _usedBytes;
                }
            }
        }

        public void SetBudgetMb(int budgetMb)
        {
            lock (_gate)
            {
                if (budgetMb < 128)
                {
                    budgetMb = 128;
                }

                _budgetBytes = (long)budgetMb * 1024L * 1024L;
                EvictIfNeededLocked();
            }
        }

        public bool TryGet(string path, out BitmapSource bitmap)
        {
            lock (_gate)
            {
                Entry entry;
                if (_entries.TryGetValue(path, out entry))
                {
                    if (!entry.Revision.Matches(path))
                    { _usedBytes -= entry.Bytes; _entries.Remove(path); bitmap = null; return false; }
                    entry.LastAccessUtc = DateTime.UtcNow;
                    bitmap = entry.Bitmap;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        public void Add(string path, BitmapSource bitmap)
        { Add(path, bitmap, FileRevision.Read(path)); }

        public void Add(string path, BitmapSource bitmap, FileRevision revision)
        {
            if (bitmap == null)
            {
                return;
            }

            lock (_gate)
            {
                if (!revision.Matches(path)) return;
                Entry existing;
                if (_entries.TryGetValue(path, out existing))
                {
                    _usedBytes -= existing.Bytes;
                }

                long bytes = EstimateBytes(bitmap);
                _entries[path] = new Entry
                {
                    Bitmap = bitmap,
                    Revision = revision,
                    Bytes = bytes,
                    LastAccessUtc = DateTime.UtcNow
                };
                _usedBytes += bytes;
                EvictIfNeededLocked();
            }
        }

        public void Remove(string path)
        {
            lock (_gate)
            {
                Entry existing;
                if (_entries.TryGetValue(path, out existing))
                {
                    _usedBytes -= existing.Bytes;
                    _entries.Remove(path);
                }
            }
            var handler = Invalidated; if (handler != null) handler(path);
        }

        internal void Clear()
        {
            lock (_gate) { _entries.Clear(); _usedBytes = 0; }
            var handler = Cleared; if (handler != null) handler();
        }

        public void TrimForPressure()
        {
            lock (_gate)
            {
                long target = Math.Max(_budgetBytes / 2, 64L * 1024L * 1024L);
                while (_usedBytes > target && _entries.Count > 0)
                {
                    RemoveOldestLocked();
                }
            }
        }

        private void EvictIfNeededLocked()
        {
            if (_pressureMonitor.IsUnderPressure)
            {
                TrimForPressure();
                return;
            }

            while (_usedBytes > _budgetBytes && _entries.Count > 0)
            {
                RemoveOldestLocked();
            }
        }

        private void RemoveOldestLocked()
        {
            string oldestKey = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (pair.Value.LastAccessUtc < oldest)
                {
                    oldest = pair.Value.LastAccessUtc;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey != null)
            {
                Entry entry = _entries[oldestKey];
                _usedBytes -= entry.Bytes;
                _entries.Remove(oldestKey);
            }
        }

        private static long EstimateBytes(BitmapSource bitmap)
        {
            long bytesPerPixel = Math.Max(4, (bitmap.Format.BitsPerPixel + 7) / 8);
            return (long)bitmap.PixelWidth * (long)bitmap.PixelHeight * bytesPerPixel;
        }
    }
}
