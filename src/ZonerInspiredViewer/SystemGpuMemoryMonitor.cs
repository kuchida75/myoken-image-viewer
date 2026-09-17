using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZonerInspiredViewer
{
    internal sealed class SystemGpuMemorySnapshot
    {
        public GpuAdapterUsage[] Adapters = new GpuAdapterUsage[0];
        public long? UsedBytes, CapacityBytes;
        public string Details, Error;
        public DateTime SampledUtc;
        public string Summary
        {
            get { return "System VRAM " + UsageText; }
        }
        public string UsageText
        {
            get
            {
                return !UsedBytes.HasValue ? "unavailable"
                    : ImageExtensions.FormatBytes(UsedBytes.Value)
                    + (CapacityBytes.HasValue ? " / " + ImageExtensions.FormatBytes(CapacityBytes.Value) : " used");
            }
        }
        public string AdapterSummary { get { return String.Join("    ", Adapters.Select(a => a.Summary)); } }
    }

    internal sealed class GpuAdapterUsage
    {
        public GpuAdapterMemoryInfo Adapter;
        public long? DedicatedUsed, SharedUsed;
        public string DedicatedText { get { return Used(DedicatedUsed); } }
        public string SharedText { get { return Used(SharedUsed); } }
        public string Summary
        {
            get { return "GPU " + Adapter.Index + " - " + Adapter.ShortName
                + " VRAM " + DedicatedText + (Adapter.CapacityBytes > 0 ? " / " + ImageExtensions.FormatBytes(Adapter.CapacityBytes) : "")
                + " | shared " + SharedText; }
        }
        private static string Used(long? value) { return value.HasValue ? ImageExtensions.FormatBytes(value.Value) : "unavailable"; }
    }

    internal sealed class GpuAdapterMemoryInfo
    {
        public string Id, Name, Key;
        public int Index;
        public bool? IsIntegrated;
        public long CapacityBytes, SharedCapacityBytes;
        public string ShortName { get { return IsIntegrated == true ? "iGPU" : (Name ?? "GPU").Replace("NVIDIA GeForce ", ""); } }
        public string DisplayName { get { return "GPU " + Index + ": " + Name + (IsIntegrated == true ? " (integrated)" : ""); } }
    }

    internal sealed class SystemGpuMemoryMonitor
    {
        public static readonly SystemGpuMemoryMonitor Shared = new SystemGpuMemoryMonitor(ReadSystem);
        private readonly object _gate = new object();
        private readonly Func<SystemGpuMemorySnapshot> _read;
        private bool _reading;
        private DateTime _nextRead;
        private SystemGpuMemorySnapshot _snapshot = new SystemGpuMemorySnapshot { Details = "Waiting for GPU telemetry" };
        private static readonly Regex Instance = new Regex(@"^(luid_0x[0-9a-f]{8}_0x[0-9a-f]{8})_phys_[0-9]+$", RegexOptions.IgnoreCase);

        internal SystemGpuMemoryMonitor(Func<SystemGpuMemorySnapshot> read) { _read = read; }
        public SystemGpuMemorySnapshot Snapshot { get { lock (_gate) return _snapshot; } }

        public void Refresh()
        {
            lock (_gate)
            {
                if (_reading || DateTime.UtcNow < _nextRead) return;
                _reading = true;
            }
            Task.Run(delegate
            {
                SystemGpuMemorySnapshot result;
                try { result = _read(); }
                catch (Exception ex)
                {
                    result = new SystemGpuMemorySnapshot { Error = ex.Message, Details = "GPU counters unavailable",
                        Adapters = GpuHardware.Devices.Select(a => new GpuAdapterUsage { Adapter = a }).ToArray() };
                }
                result.SampledUtc = DateTime.UtcNow;
                lock (_gate)
                {
                    _snapshot = result;
                    _nextRead = DateTime.UtcNow.AddSeconds(result.UsedBytes.HasValue ? 2 : 10);
                    _reading = false;
                }
            });
        }

        internal static SystemGpuMemorySnapshot ReadSystem()
        {
            GpuAdapterMemoryInfo[] adapters = ReadAdapters();
            // Adapter counters are system-wide. Summing process counters would double-count shared allocations.
            var category = new PerformanceCounterCategory("GPU Adapter Memory");
            var counters = category.ReadCategory();
            var counter = counters["Dedicated Usage"];
            if (counter == null) throw new InvalidOperationException("Windows did not expose dedicated GPU memory counters.");
            var usage = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (string instance in counter.Keys)
            {
                if (!Instance.IsMatch(instance)) continue;
                long bytes = counter[instance].RawValue;
                if (bytes < 0) throw new InvalidOperationException("Invalid GPU memory sample.");
                usage[instance] = bytes;
            }
            var sharedUsage = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var shared = counters["Shared Usage"];
            if (shared != null) foreach (string instance in shared.Keys)
                if (Instance.IsMatch(instance) && shared[instance].RawValue >= 0) sharedUsage[instance] = shared[instance].RawValue;
            return Aggregate(adapters, usage, sharedUsage);
        }

        internal static SystemGpuMemorySnapshot Aggregate(GpuAdapterMemoryInfo[] adapters, IDictionary<string, long> usage, IDictionary<string, long> shared = null)
        {
            var grouped = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in usage)
            {
                Match match = Instance.Match(entry.Key);
                if (!match.Success || entry.Value < 0) throw new InvalidOperationException("Invalid GPU adapter sample.");
                string id = match.Groups[1].Value;
                long previous; grouped.TryGetValue(id, out previous);
                grouped[id] = checked(previous + entry.Value);
            }
            bool complete = grouped.Keys.All(id => adapters.Any(a => String.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)))
                && adapters.Where(a => a.CapacityBytes > 0).All(a => grouped.ContainsKey(a.Id));
            long capacity = adapters.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Sum(group => group.First().CapacityBytes);
            var lines = new List<string>();
            var perAdapter = new List<GpuAdapterUsage>();
            foreach (var adapter in adapters)
            {
                long bytes; long? sharedBytes = null;
                if (shared != null)
                {
                    var matches = shared.Where(p => Instance.IsMatch(p.Key) && String.Equals(Instance.Match(p.Key).Groups[1].Value, adapter.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length > 0) sharedBytes = matches.Sum(p => p.Value);
                }
                var sample = new GpuAdapterUsage { Adapter = adapter, DedicatedUsed = grouped.TryGetValue(adapter.Id, out bytes) ? (long?)bytes : null, SharedUsed = sharedBytes };
                perAdapter.Add(sample);
                lines.Add(adapter.DisplayName + (adapter.IsIntegrated == false ? " (discrete)" : "") + "\n" + sample.Summary
                    + (adapter.SharedCapacityBytes > 0 ? " / " + ImageExtensions.FormatBytes(adapter.SharedCapacityBytes) + " shared capacity" : ""));
            }
            foreach (var group in grouped)
            {
                var adapter = adapters.FirstOrDefault(a => String.Equals(a.Id, group.Key, StringComparison.OrdinalIgnoreCase));
                if (adapter != null) continue;
                lines.Add((adapter == null ? "Unidentified adapter" : adapter.Name) + "\n" + ImageExtensions.FormatBytes(group.Value)
                    + (adapter != null && adapter.CapacityBytes > 0 ? " / " + ImageExtensions.FormatBytes(adapter.CapacityBytes) : " used"));
            }
            return new SystemGpuMemorySnapshot { Adapters = perAdapter.ToArray(), UsedBytes = grouped.Count > 0 ? (long?)grouped.Values.Sum() : null, CapacityBytes = complete && capacity > 0 ? (long?)capacity : null,
                Details = String.Join("\n\n", lines), SampledUtc = DateTime.UtcNow };
        }

        internal static GpuAdapterMemoryInfo[] ReadAdapters()
        {
            return GpuHardware.Devices;
        }
    }
}
