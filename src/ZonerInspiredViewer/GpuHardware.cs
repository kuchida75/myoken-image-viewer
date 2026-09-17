using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ZonerInspiredViewer
{
    internal static class GpuHardware
    {
        private static readonly Lazy<GpuAdapterMemoryInfo[]> Catalog = new Lazy<GpuAdapterMemoryInfo[]>(delegate
        {
            try { return Enumerate(); } catch { return new GpuAdapterMemoryInfo[0]; }
        });
        internal static GpuAdapterMemoryInfo[] Devices { get { return Catalog.Value; } }
        internal static T Method<T>(IntPtr instance, int slot) where T : class
        { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T)) as T; }
        internal static void Release(ref IntPtr value) { if (value != IntPtr.Zero) { Marshal.Release(value); value = IntPtr.Zero; } }

        internal static GpuAdapterMemoryInfo SelectProcessing(string key, IEnumerable<GpuAdapterMemoryInfo> devices)
        {
            if (key == "CPU") return null;
            if (!String.IsNullOrEmpty(key) && key != "Auto") return devices.FirstOrDefault(d => d.Key == key);
            return devices.OrderBy(d => d.IsIntegrated == true ? 1 : 0).ThenByDescending(d => d.CapacityBytes).FirstOrDefault();
        }
        internal static GpuAdapterMemoryInfo SelectIntegrated(IEnumerable<GpuAdapterMemoryInfo> devices)
        { return devices.Where(d => d.IsIntegrated == true).OrderByDescending(d => d.SharedCapacityBytes).FirstOrDefault(); }

        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Description
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
            public uint Vendor, Device, Subsystem, Revision;
            public UIntPtr DedicatedVideo, DedicatedSystem, Shared;
            public Luid Id;
            public uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Architecture { public uint Node; public int TileBased, Uma, CacheCoherent; }
        [StructLayout(LayoutKind.Sequential)] internal struct MemoryInfo { public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation; }
        [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
        [DllImport("d3d12.dll", ExactSpelling = true)] private static extern int D3D12CreateDevice(IntPtr adapter, int level, ref Guid id, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr factory, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out Description value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetArchitecture(IntPtr device, int feature, ref Architecture value, uint size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryMemory(IntPtr adapter, uint node, int segment, out MemoryInfo value);

        private static IntPtr Factory()
        {
            var guid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); IntPtr result;
            Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref guid, out result)); return result;
        }
        private static bool? Integrated(IntPtr adapter)
        {
            IntPtr device = IntPtr.Zero;
            try
            {
                var guid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
                if (D3D12CreateDevice(adapter, 0xb000, ref guid, out device) < 0) return null;
                var architecture = new Architecture();
                if (Method<GetArchitecture>(device, 13)(device, 1, ref architecture, 16) < 0) return null;
                return architecture.Uma != 0;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            finally { Release(ref device); }
        }
        private static GpuAdapterMemoryInfo[] Enumerate()
        {
            var result = new List<GpuAdapterMemoryInfo>();
            var counts = new Dictionary<string, int>(); IntPtr factory = Factory();
            try
            {
                for (uint i = 0; ; i++)
                {
                    IntPtr adapter; int hr = Method<EnumAdapter>(factory, 12)(factory, i, out adapter);
                    if (hr == unchecked((int)0x887A0002)) break;
                    Marshal.ThrowExceptionForHR(hr);
                    try
                    {
                        Description desc; Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out desc));
                        if ((desc.Flags & 2) != 0) continue;
                        string hardware = String.Format("{0:X4}:{1:X4}:{2:X8}:{3:X8}", desc.Vendor, desc.Device, desc.Subsystem, desc.Revision);
                        int ordinal; counts.TryGetValue(hardware, out ordinal); counts[hardware] = ordinal + 1;
                        result.Add(new GpuAdapterMemoryInfo { Name = desc.Name, Index = (int)i, Key = hardware + ":" + ordinal,
                            Id = String.Format("luid_0x{0:x8}_0x{1:x8}", unchecked((uint)desc.Id.High), desc.Id.Low),
                            CapacityBytes = checked((long)desc.DedicatedVideo.ToUInt64()), SharedCapacityBytes = checked((long)desc.Shared.ToUInt64()),
                            IsIntegrated = Integrated(adapter) });
                    }
                    finally { Release(ref adapter); }
                }
            }
            finally { Release(ref factory); }
            return result.ToArray();
        }

        internal static IntPtr Open(GpuAdapterMemoryInfo device)
        {
            IntPtr factory = Factory(), adapter = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(Method<EnumAdapter>(factory, 12)(factory, (uint)device.Index, out adapter));
                Description desc; Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out desc));
                string id = String.Format("luid_0x{0:x8}_0x{1:x8}", unchecked((uint)desc.Id.High), desc.Id.Low);
                if (id != device.Id) throw new InvalidOperationException("GPU adapter changed; restart the viewer to refresh devices.");
                IntPtr result = adapter; adapter = IntPtr.Zero; return result;
            }
            finally { Release(ref adapter); Release(ref factory); }
        }

        internal static bool CanAllocate(GpuAdapterMemoryInfo device, long additional, int softLimitMb, out string reason)
        {
            reason = null; IntPtr adapter = IntPtr.Zero, adapter3 = IntPtr.Zero;
            try
            {
                adapter = Open(device); var id = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(adapter, ref id, out adapter3));
                MemoryInfo local, shared;
                var query = Method<QueryMemory>(adapter3, 14);
                Marshal.ThrowExceptionForHR(query(adapter3, 0, 0, out local));
                Marshal.ThrowExceptionForHR(query(adapter3, 0, 1, out shared));
                ulong used = checked(local.CurrentUsage + shared.CurrentUsage);
                if (softLimitMb > 0 && used + (ulong)additional > (ulong)softLimitMb * 1024 * 1024)
                    reason = "GPU soft memory limit";
                else if (local.Budget == 0 || local.CurrentUsage + (ulong)additional > local.Budget * 0.9)
                    reason = "DXGI memory pressure";
                return reason == null;
            }
            catch (Exception error) { reason = "GPU memory budget unavailable: " + error.Message; return false; }
            finally { Release(ref adapter3); Release(ref adapter); }
        }

        internal static bool TryMemory(GpuAdapterMemoryInfo device, out MemoryInfo local, out MemoryInfo shared, out string error)
        {
            local = new MemoryInfo(); shared = new MemoryInfo(); error = null;
            IntPtr adapter = IntPtr.Zero, adapter3 = IntPtr.Zero;
            try
            {
                if (device == null) throw new NotSupportedException("No GPU adapter available.");
                adapter = Open(device); var id = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(adapter, ref id, out adapter3));
                var query = Method<QueryMemory>(adapter3, 14);
                Marshal.ThrowExceptionForHR(query(adapter3, 0, 0, out local));
                Marshal.ThrowExceptionForHR(query(adapter3, 0, 1, out shared));
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { Release(ref adapter3); Release(ref adapter); }
        }
    }
}
