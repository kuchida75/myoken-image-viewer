using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    // A private D3D11 compute device, independent of WPF's display adapter. All calls are serialized by its owner.
    internal sealed class GpuThumbnailResizer : IDisposable
    {
        private IntPtr _device, _context, _shader;
        internal readonly GpuAdapterMemoryInfo Adapter;
        private const string Shader = @"
Texture2D<float4> sourceImage : register(t0);
RWTexture2D<float4> targetImage : register(u0);
[numthreads(8,8,1)] void main(uint3 id : SV_DispatchThreadID) {
 uint sw,sh,tw,th; sourceImage.GetDimensions(sw,sh); targetImage.GetDimensions(tw,th);
 if(id.x>=tw || id.y>=th) return;
 float2 scale=float2(sw,sh)/float2(tw,th);
 float2 lo=id.xy*scale, hi=(id.xy+1)*scale;
 float4 sum=0; float weight=0;
 for(int y=(int)floor(lo.y); y<min((int)ceil(hi.y),(int)sh); y++)
  for(int x=(int)floor(lo.x); x<min((int)ceil(hi.x),(int)sw); x++) {
   float w=max(0,min(hi.x,x+1)-max(lo.x,x))*max(0,min(hi.y,y+1)-max(lo.y,y));
   float4 p=sourceImage.Load(int3(x,y,0)); p.rgb*=p.a; sum+=p*w; weight+=w;
  }
 sum/=max(weight,0.00001); if(sum.a>0.00001) sum.rgb/=sum.a;
 targetImage[id.xy]=sum;
}";
        [StructLayout(LayoutKind.Sequential)] private struct TextureDescription
        { public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc; }
        [StructLayout(LayoutKind.Sequential)] private struct InitialData { public IntPtr Data; public uint Pitch, SlicePitch; }
        [StructLayout(LayoutKind.Sequential)] private struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }
        [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int D3D11CreateDevice(IntPtr adapter, int driverType,
            IntPtr software, uint flags, int[] levels, uint levelCount, uint sdk, out IntPtr device, out int selectedLevel, out IntPtr context);
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern int D3DCompile(byte[] code, UIntPtr size,
            string name, IntPtr defines, IntPtr include, string entry, string target, uint flags, uint effectFlags, out IntPtr blob, out IntPtr errors);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPointer(IntPtr blob);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobSize(IntPtr blob);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr device, ref TextureDescription desc, IntPtr data, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateView(IntPtr device, IntPtr resource, IntPtr desc, out IntPtr view);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShader(IntPtr device, IntPtr bytes, UIntPtr size, IntPtr linkage, out IntPtr shader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetResources(IntPtr context, uint first, uint count, IntPtr[] views);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetOutputs(IntPtr context, uint first, uint count, IntPtr[] views, IntPtr initialCounts);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetShader(IntPtr context, IntPtr shader, IntPtr instances, uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Dispatch(IntPtr context, uint x, uint y, uint z);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(IntPtr context, IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(IntPtr context, IntPtr resource, uint index, uint type, uint flags, out Mapped mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(IntPtr context, IntPtr resource, uint index);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ContextCommand(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetPriority(IntPtr device, int priority);

        internal GpuThumbnailResizer(GpuAdapterMemoryInfo adapter)
        {
            Adapter = adapter; IntPtr native = GpuHardware.Open(adapter), blob = IntPtr.Zero, errors = IntPtr.Zero;
            try
            {
                int level;
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(native, 0, IntPtr.Zero, 0, new[] { 0xb000 }, 1, 7, out _device, out level, out _context));
                IntPtr dxgi = IntPtr.Zero;
                try
                {
                    var id = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
                    if (Marshal.QueryInterface(_device, ref id, out dxgi) >= 0) GpuHardware.Method<SetPriority>(dxgi, 10)(dxgi, -5);
                }
                finally { GpuHardware.Release(ref dxgi); }
                byte[] code = Encoding.ASCII.GetBytes(Shader);
                int hr = D3DCompile(code, (UIntPtr)code.Length, "ThumbnailResize", IntPtr.Zero, IntPtr.Zero, "main", "cs_5_0", 1 << 15, 0, out blob, out errors);
                if (hr < 0) throw new InvalidOperationException(errors == IntPtr.Zero ? "Thumbnail shader compilation failed."
                    : Marshal.PtrToStringAnsi(GpuHardware.Method<BlobPointer>(errors, 3)(errors)));
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateShader>(_device, 18)(_device, GpuHardware.Method<BlobPointer>(blob, 3)(blob),
                    GpuHardware.Method<BlobSize>(blob, 4)(blob), IntPtr.Zero, out _shader));
            }
            catch { Dispose(); throw; }
            finally { GpuHardware.Release(ref native); GpuHardware.Release(ref blob); GpuHardware.Release(ref errors); }
        }

        internal static long WorkingBytes(int width, int height, int targetWidth, int targetHeight)
        { return checked(((width * 4L + 255) / 256 * 256) * height + 2 * ((targetWidth * 4L + 255) / 256 * 256) * targetHeight); }

        internal BitmapSource Resize(BitmapSource source, int size, int limitMb, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            double scale = Math.Min(1, (double)size / Math.Max(source.PixelWidth, source.PixelHeight));
            int width = Math.Max(1, (int)Math.Ceiling(source.PixelWidth * scale - 0.0000001));
            int height = Math.Max(1, (int)Math.Ceiling(source.PixelHeight * scale - 0.0000001));
            if (source.PixelWidth > 16384 || source.PixelHeight > 16384) throw new NotSupportedException("Thumbnail input exceeds GPU texture limits.");
            long bytes = WorkingBytes(source.PixelWidth, source.PixelHeight, width, height);
            string reason;
            if (bytes > limitMb * 1024L * 1024) throw new InvalidOperationException("Thumbnail GPU working-memory limit");
            if (!GpuHardware.CanAllocate(Adapter, bytes, 0, out reason)) throw new InvalidOperationException(reason);
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
            converted.CopyPixels(pixels, source.PixelWidth * 4, 0);
            IntPtr input = IntPtr.Zero, output = IntPtr.Zero, staging = IntPtr.Zero, view = IntPtr.Zero, target = IntPtr.Zero;
            try
            {
                input = Texture(source.PixelWidth, source.PixelHeight, 0, 8, 0, pixels);
                output = Texture(width, height, 0, 128, 0, null);
                staging = Texture(width, height, 3, 0, 0x20000, null);
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateView>(_device, 7)(_device, input, IntPtr.Zero, out view));
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateView>(_device, 8)(_device, output, IntPtr.Zero, out target));
                token.ThrowIfCancellationRequested();
                GpuHardware.Method<SetResources>(_context, 67)(_context, 0, 1, new[] { view });
                GpuHardware.Method<SetOutputs>(_context, 68)(_context, 0, 1, new[] { target }, IntPtr.Zero);
                GpuHardware.Method<SetShader>(_context, 69)(_context, _shader, IntPtr.Zero, 0);
                GpuHardware.Method<Dispatch>(_context, 41)(_context, (uint)(width + 7) / 8, (uint)(height + 7) / 8, 1);
                GpuHardware.Method<ContextCommand>(_context, 110)(_context);
                GpuHardware.Method<CopyResource>(_context, 47)(_context, staging, output);
                Mapped mapped;
                Marshal.ThrowExceptionForHR(GpuHardware.Method<Map>(_context, 14)(_context, staging, 0, 1, 0, out mapped));
                byte[] result = new byte[checked(width * height * 4)];
                try
                {
                    for (int y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(mapped.Data, checked(y * (int)mapped.RowPitch)), result, y * width * 4, width * 4);
                }
                finally { GpuHardware.Method<Unmap>(_context, 15)(_context, staging, 0); }
                token.ThrowIfCancellationRequested();
                var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, result, width * 4); bitmap.Freeze(); return bitmap;
            }
            finally
            {
                GpuHardware.Method<ContextCommand>(_context, 110)(_context);
                GpuHardware.Release(ref target); GpuHardware.Release(ref view); GpuHardware.Release(ref staging);
                GpuHardware.Release(ref output); GpuHardware.Release(ref input);
            }
        }

        private IntPtr Texture(int width, int height, uint usage, uint bind, uint cpu, byte[] pixels)
        {
            var desc = new TextureDescription { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = 28, SampleCount = 1, Usage = usage, BindFlags = bind, CpuAccess = cpu };
            IntPtr data = IntPtr.Zero, texture; GCHandle pin = new GCHandle();
            try
            {
                if (pixels != null)
                {
                    pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    data = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(InitialData)));
                    Marshal.StructureToPtr(new InitialData { Data = pin.AddrOfPinnedObject(), Pitch = (uint)width * 4 }, data, false);
                }
                Marshal.ThrowExceptionForHR(GpuHardware.Method<CreateTexture>(_device, 5)(_device, ref desc, data, out texture)); return texture;
            }
            finally { if (pin.IsAllocated) pin.Free(); if (data != IntPtr.Zero) Marshal.FreeHGlobal(data); }
        }
        public void Dispose()
        {
            if (_context != IntPtr.Zero) { GpuHardware.Method<ContextCommand>(_context, 110)(_context); GpuHardware.Method<ContextCommand>(_context, 111)(_context); }
            GpuHardware.Release(ref _shader); GpuHardware.Release(ref _context); GpuHardware.Release(ref _device);
        }
    }
}
