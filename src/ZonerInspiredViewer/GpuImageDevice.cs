using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    // Immutable shared D3D9Ex/D3D11 surfaces let WPF composite resident pixels without CPU readback.
    // The cache serializes native work; D3D9 is explicitly multithreaded for WPF's compositor.
    internal sealed class GpuImageTexture : IDisposable
    {
        internal IntPtr Texture, Surface, Texture9;
        internal int Width, Height, Pins;
        internal long Bytes, Access;
        internal string Path, Backend, ContentKey;
        internal FileRevision Revision;
        internal WeakReference CpuPixels;
        internal bool Retired;
        public void Dispose()
        {
            GpuHardware.Release(ref Surface); GpuHardware.Release(ref Texture9); GpuHardware.Release(ref Texture);
        }
    }

    internal sealed class GpuImageDevice : IDisposable
    {
        internal IntPtr Device, Context;
        private IntPtr _d3d9, _device9, _vertexShader, _jpegShader, _constants;
        private NvJpegDecoder _jpeg;
        private bool _jpegUnavailable;
        internal string JpegStatus = "Not used yet";
        internal readonly GpuAdapterMemoryInfo Adapter;

        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] private struct PresentParameters
        {
            public uint Width, Height, Format, Count, Multisample, Quality, SwapEffect;
            public IntPtr Window;
            public int Windowed, AutoDepth;
            public uint DepthFormat, Flags, Refresh, Interval;
        }
        [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
        { public uint Width, Height, Mips, Array, Format, Samples, Quality, Usage, Bind, Cpu, Misc; }
        [StructLayout(LayoutKind.Sequential)] private struct BufferDescription
        { public uint Bytes, Usage, Bind, Cpu, Misc, Stride; }
        [StructLayout(LayoutKind.Sequential)] private struct BufferView
        { public uint Format, Dimension, First, Count, Flags, Padding; }
        [StructLayout(LayoutKind.Sequential)] private struct Mapped { public IntPtr Data; public uint Pitch, Depth; }
        [StructLayout(LayoutKind.Sequential)] private struct Viewport { public float X, Y, Width, Height, MinDepth, MaxDepth; }

        [DllImport("d3d9.dll", ExactSpelling = true)] private static extern int Direct3DCreate9Ex(uint sdk, out IntPtr d3d);
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int D3D11CreateDevice(IntPtr adapter, int driver,
            IntPtr software, uint flags, int[] levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern int D3DCompile(byte[] code, UIntPtr length,
            string name, IntPtr defines, IntPtr include, string entry, string target, uint flags, uint effects, out IntPtr blob, out IntPtr errors);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint AdapterCount(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AdapterId(IntPtr self, uint index, out Luid id);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateDevice9(IntPtr self, uint index, uint type, IntPtr window,
            uint flags, ref PresentParameters pp, IntPtr fullscreen, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture9(IntPtr self, uint width, uint height, uint levels,
            uint usage, uint format, uint pool, out IntPtr texture, ref IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSurface(IntPtr self, uint level, out IntPtr surface);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenShared(IntPtr self, IntPtr handle, ref Guid iid, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture11(IntPtr self, ref TextureDescription description, IntPtr initial, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBuffer(IntPtr self, ref BufferDescription description, IntPtr initial, out IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateRawView(IntPtr self, IntPtr buffer, ref BufferView description, out IntPtr view);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateView(IntPtr self, IntPtr texture, IntPtr description, out IntPtr view);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShader(IntPtr self, IntPtr code, UIntPtr length, IntPtr linkage, out IntPtr shader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobData(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobLength(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Update(IntPtr self, IntPtr resource, uint index, IntPtr box, IntPtr data, uint pitch, uint depth);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Command(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Resources(IntPtr self, uint start, uint count, IntPtr[] resources);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Targets(IntPtr self, uint count, IntPtr[] resources, IntPtr depth);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Shader(IntPtr self, IntPtr shader, IntPtr instances, uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Topology(IntPtr self, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetViewport(IntPtr self, uint count, ref Viewport viewport);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Draw(IntPtr self, uint count, uint first);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Copy(IntPtr self, IntPtr to, IntPtr from);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(IntPtr self, IntPtr resource, uint index, uint type, uint flags, out Mapped value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(IntPtr self, IntPtr resource, uint index);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateQuery(IntPtr self, ref long description, out IntPtr query);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void EndQuery(IntPtr self, IntPtr query);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryData(IntPtr self, IntPtr query, IntPtr data, uint size, uint flags);

        internal GpuImageDevice(GpuAdapterMemoryInfo adapter)
        {
            Adapter = adapter; IntPtr native = GpuHardware.Open(adapter);
            try
            {
                int level;
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(native, 0, IntPtr.Zero, 0x20, new[] { 0xb000 }, 1, 7, out Device, out level, out Context));
                Marshal.ThrowExceptionForHR(Direct3DCreate9Ex(32, out _d3d9));
                uint count = GpuHardware.Method<AdapterCount>(_d3d9, 4)(_d3d9), selected = UInt32.MaxValue;
                for (uint i = 0; i < count; i++)
                {
                    Luid id; Marshal.ThrowExceptionForHR(GpuHardware.Method<AdapterId>(_d3d9, 21)(_d3d9, i, out id));
                    if (String.Format("luid_0x{0:x8}_0x{1:x8}", unchecked((uint)id.High), id.Low) == adapter.Id) { selected = i; break; }
                }
                if (selected == UInt32.MaxValue) throw new NotSupportedException("Selected GPU has no D3D9Ex sharing adapter.");
                var pp = new PresentParameters { Width = 1, Height = 1, Format = 21, Count = 1, SwapEffect = 1,
                    Window = GetDesktopWindow(), Windowed = 1, Interval = 0x80000000 };
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateDevice9>(_d3d9, 20)(_d3d9, selected, 1, pp.Window, 0x46, ref pp, IntPtr.Zero, out _device9));
            }
            catch { Dispose(); throw; }
            finally { GpuHardware.Release(ref native); }
        }

        internal static long TextureBytes(int width, int height)
        { return checked((((width * 4L + 255) / 256 * 256) * height + 65535) / 65536 * 65536); }

        internal GpuImageTexture Create(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > 128L * 1024 * 1024)
                throw new NotSupportedException("Image exceeds shared GPU texture limits; using WPF fallback.");
            var result = new GpuImageTexture { Width = width, Height = height, Bytes = TextureBytes(width, height) };
            try
            {
                IntPtr shared = IntPtr.Zero;
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateTexture9>(_device9, 23)(_device9, (uint)width, (uint)height, 1, 1, 21, 0, out result.Texture9, ref shared));
                var id = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                Marshal.ThrowExceptionForHR(GpuHardware.Method<OpenShared>(Device, 28)(Device, shared, ref id, out result.Texture));
                Marshal.ThrowExceptionForHR(GpuHardware.Method<GetSurface>(result.Texture9, 18)(result.Texture9, 0, out result.Surface));
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        internal void Upload(GpuImageTexture target, BitmapSource bitmap, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var source = bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            byte[] pixels = new byte[checked(target.Width * target.Height * 4)]; source.CopyPixels(pixels, target.Width * 4, 0);
            token.ThrowIfCancellationRequested();
            UpdateBytes(target.Texture, pixels, target.Width * 4);
            Finish(token); target.Backend = "CPU decode / GPU-resident texture";
        }

        private void UpdateBytes(IntPtr resource, Array data, int pitch)
        {
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { GpuHardware.Method<Update>(Context, 48)(Context, resource, 0, IntPtr.Zero, pin.AddrOfPinnedObject(), (uint)pitch, 0); }
            finally { pin.Free(); }
        }

        internal void JpegSize(byte[] encoded, out int width, out int height)
        {
            if (_jpegUnavailable) throw new NotSupportedException(JpegStatus);
            if (_jpeg == null)
            {
                try { _jpeg = new NvJpegDecoder(Adapter); JpegStatus = "nvJPEG hybrid GPU decode"; }
                catch (Exception error) { _jpegUnavailable = true; JpegStatus = error.Message; throw; }
            }
            _jpeg.Dimensions(encoded, out width, out height);
        }

        internal void DecodeJpeg(GpuImageTexture target, byte[] encoded, CancellationToken token)
        {
            if (_vertexShader == IntPtr.Zero) InitializeJpegShaders();
            IntPtr buffer = IntPtr.Zero, input = IntPtr.Zero, output = IntPtr.Zero;
            try
            {
                uint bytes = checked((uint)(((long)target.Width * target.Height * 3 + 3) / 4 * 4));
                var bd = new BufferDescription { Bytes = bytes, Bind = 8, Misc = 0x20 };
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateBuffer>(Device, 3)(Device, ref bd, IntPtr.Zero, out buffer));
                var vd = new BufferView { Format = 39, Dimension = 11, Count = bytes / 4, Flags = 1 };
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateRawView>(Device, 7)(Device, buffer, ref vd, out input));
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateView>(Device, 9)(Device, target.Texture, IntPtr.Zero, out output));
                _jpeg.Decode(encoded, buffer, target.Width, token);
                token.ThrowIfCancellationRequested();
                UpdateBytes(_constants, new uint[] { (uint)target.Width, (uint)target.Height, 0, 0 }, 16);
                GpuHardware.Method<Targets>(Context, 33)(Context, 1, new[] { output }, IntPtr.Zero);
                var viewport = new Viewport { Width = target.Width, Height = target.Height, MaxDepth = 1 };
                GpuHardware.Method<SetViewport>(Context, 44)(Context, 1, ref viewport);
                GpuHardware.Method<Topology>(Context, 24)(Context, 4);
                GpuHardware.Method<Shader>(Context, 11)(Context, _vertexShader, IntPtr.Zero, 0);
                GpuHardware.Method<Shader>(Context, 9)(Context, _jpegShader, IntPtr.Zero, 0);
                GpuHardware.Method<Resources>(Context, 8)(Context, 0, 1, new[] { input });
                GpuHardware.Method<Resources>(Context, 16)(Context, 0, 1, new[] { _constants });
                GpuHardware.Method<Draw>(Context, 13)(Context, 3, 0);
                GpuHardware.Method<Command>(Context, 110)(Context);
                Finish(token); target.Backend = "nvJPEG GPU decode / GPU-resident texture";
            }
            finally
            {
                GpuHardware.Method<Command>(Context, 110)(Context);
                GpuHardware.Release(ref output); GpuHardware.Release(ref input); GpuHardware.Release(ref buffer);
            }
        }

        private void InitializeJpegShaders()
        {
            const string shader = @"
cbuffer Dimensions : register(b0) { uint imageWidth; uint imageHeight; uint2 padding; };
ByteAddressBuffer pixels : register(t0);
float4 vs(uint id : SV_VertexID) : SV_Position {
 float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),0,1);
}
uint channel(uint offset) { return (pixels.Load(offset&~3u)>>((offset&3u)*8))&255u; }
float4 ps(float4 position : SV_Position) : SV_Target {
 uint p=3*((uint)position.y*imageWidth+(uint)position.x);
 return float4(channel(p+2),channel(p+1),channel(p),255)/255.0;
}";
            _vertexShader = Compile(shader, "vs", 12); _jpegShader = Compile(shader, "ps", 15);
            var bd = new BufferDescription { Bytes = 16, Bind = 4 };
            Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateBuffer>(Device, 3)(Device, ref bd, IntPtr.Zero, out _constants));
        }

        private IntPtr Compile(string text, string entry, int slot)
        {
            IntPtr blob = IntPtr.Zero, errors = IntPtr.Zero;
            try
            {
                byte[] code = Encoding.ASCII.GetBytes(text);
                int hr = D3DCompile(code, (UIntPtr)code.Length, "JpegPresentation", IntPtr.Zero, IntPtr.Zero, entry, entry + "_5_0", 1 << 15, 0, out blob, out errors);
                if (hr < 0) throw new InvalidOperationException(errors == IntPtr.Zero ? "GPU shader compilation failed."
                    : Marshal.PtrToStringAnsi(GpuHardware.Method<BlobData>(errors, 3)(errors)));
                IntPtr shader;
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateShader>(Device, slot)(Device, GpuHardware.Method<BlobData>(blob, 3)(blob),
                    GpuHardware.Method<BlobLength>(blob, 4)(blob), IntPtr.Zero, out shader));
                return shader;
            }
            finally { GpuHardware.Release(ref errors); GpuHardware.Release(ref blob); }
        }

        private void Finish(CancellationToken token)
        {
            IntPtr query = IntPtr.Zero;
            try
            {
                long description = 0;
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateQuery>(Device, 24)(Device, ref description, out query));
                GpuHardware.Method<EndQuery>(Context, 28)(Context, query);
                GpuHardware.Method<Command>(Context, 111)(Context);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int result;
                while ((result = GpuHardware.Method<QueryData>(Context, 29)(Context, query, IntPtr.Zero, 0, 0)) == 1)
                {
                    token.ThrowIfCancellationRequested();
                    if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("GPU image work timed out.");
                    Thread.Sleep(1);
                }
                Marshal.ThrowExceptionForHR(result);
            }
            finally { GpuHardware.Release(ref query); }
        }

        internal BitmapSource Readback(GpuImageTexture image)
        {
            IntPtr staging = IntPtr.Zero;
            try
            {
                var td = new TextureDescription { Width = (uint)image.Width, Height = (uint)image.Height, Mips = 1, Array = 1,
                    Format = 87, Samples = 1, Usage = 3, Cpu = 0x20000 };
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateTexture11>(Device, 5)(Device, ref td, IntPtr.Zero, out staging));
                GpuHardware.Method<Copy>(Context, 47)(Context, staging, image.Texture);
                Mapped mapped; Marshal.ThrowExceptionForHR(GpuHardware.Method<Map>(Context, 14)(Context, staging, 0, 1, 0, out mapped));
                byte[] pixels = new byte[checked(image.Width * image.Height * 4)];
                try
                {
                    for (int y = 0; y < image.Height; y++)
                        Marshal.Copy(IntPtr.Add(mapped.Data, checked(y * (int)mapped.Pitch)), pixels, y * image.Width * 4, image.Width * 4);
                }
                finally { GpuHardware.Method<Unmap>(Context, 15)(Context, staging, 0); }
                var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, image.Width * 4);
                bitmap.Freeze(); return bitmap;
            }
            finally { GpuHardware.Release(ref staging); }
        }

        public void Dispose()
        {
            if (_jpeg != null) { _jpeg.Dispose(); _jpeg = null; }
            if (Context != IntPtr.Zero) { GpuHardware.Method<Command>(Context, 110)(Context); GpuHardware.Method<Command>(Context, 111)(Context); }
            GpuHardware.Release(ref _constants); GpuHardware.Release(ref _jpegShader); GpuHardware.Release(ref _vertexShader);
            GpuHardware.Release(ref Context); GpuHardware.Release(ref Device); GpuHardware.Release(ref _device9); GpuHardware.Release(ref _d3d9);
        }
    }
}
