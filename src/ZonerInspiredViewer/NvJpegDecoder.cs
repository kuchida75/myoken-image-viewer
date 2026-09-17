using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZonerInspiredViewer
{
    // nvJPEG is hybrid: entropy parsing may use the CPU; reconstruction writes directly into a CUDA/D3D11 buffer.
    internal sealed class NvJpegDecoder : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr library, string name);
        private static readonly Lazy<IntPtr> Runtime = new Lazy<IntPtr>(() => Load("cudart64_12.dll"));
        private static readonly Lazy<IntPtr> Library = new Lazy<IntPtr>(() => Load("nvjpeg64_12.dll"));
        [StructLayout(LayoutKind.Sequential)] private struct NvImage
        { public IntPtr Channel0, Channel1, Channel2, Channel3; public UIntPtr Pitch0, Pitch1, Pitch2, Pitch3; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CudaAdapter(out int ordinal, IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDevice(int ordinal);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StreamCreate(out IntPtr stream, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OneHandle(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Register(out IntPtr resource, IntPtr buffer, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MapResources(int count, ref IntPtr resource, IntPtr stream);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MappedPointer(out IntPtr pointer, out UIntPtr size, IntPtr resource);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateState(IntPtr handle, out IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetInfo(IntPtr handle, byte[] data, UIntPtr length,
            out int channels, out int sampling, [Out] int[] widths, [Out] int[] heights);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DecodeImage(IntPtr handle, IntPtr state, byte[] data,
            UIntPtr length, int format, ref NvImage output, IntPtr stream);

        private readonly SetDevice _setDevice;
        private readonly OneHandle _synchronize, _destroyStream, _unregister, _destroyState, _destroy;
        private readonly Register _register;
        private readonly MapResources _map, _unmap;
        private readonly MappedPointer _pointer;
        private readonly GetInfo _info;
        private readonly DecodeImage _decode;
        private IntPtr _handle, _state, _stream;
        private int _ordinal;

        private static IntPtr Load(string name)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GpuRuntime", name);
            IntPtr value = LoadLibraryEx(path, IntPtr.Zero, 0x1100);
            if (value == IntPtr.Zero) throw new NotSupportedException("Bundled GPU JPEG runtime unavailable (" + name + ", error " + Marshal.GetLastWin32Error() + ").");
            return value;
        }
        private static T Export<T>(IntPtr library, string name) where T : class
        {
            IntPtr address = GetProcAddress(library, name);
            if (address == IntPtr.Zero) throw new NotSupportedException("GPU JPEG export unavailable: " + name);
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }
        private static void Check(int result, string operation)
        { if (result != 0) throw new InvalidOperationException(operation + " failed (" + result + "); using CPU decode."); }

        internal NvJpegDecoder(GpuAdapterMemoryInfo adapter)
        {
            IntPtr runtime = Runtime.Value, library = Library.Value;
            _setDevice = Export<SetDevice>(runtime, "cudaSetDevice");
            _synchronize = Export<OneHandle>(runtime, "cudaStreamSynchronize");
            _destroyStream = Export<OneHandle>(runtime, "cudaStreamDestroy");
            _register = Export<Register>(runtime, "cudaGraphicsD3D11RegisterResource");
            _unregister = Export<OneHandle>(runtime, "cudaGraphicsUnregisterResource");
            _map = Export<MapResources>(runtime, "cudaGraphicsMapResources");
            _unmap = Export<MapResources>(runtime, "cudaGraphicsUnmapResources");
            _pointer = Export<MappedPointer>(runtime, "cudaGraphicsResourceGetMappedPointer");
            _destroyState = Export<OneHandle>(library, "nvjpegJpegStateDestroy");
            _destroy = Export<OneHandle>(library, "nvjpegDestroy");
            _info = Export<GetInfo>(library, "nvjpegGetImageInfo");
            _decode = Export<DecodeImage>(library, "nvjpegDecode");
            IntPtr native = GpuHardware.Open(adapter);
            try
            {
                Check(Export<CudaAdapter>(runtime, "cudaD3D11GetDevice")(out _ordinal, native), "CUDA adapter selection");
                Check(_setDevice(_ordinal), "CUDA device selection");
                Check(Export<StreamCreate>(runtime, "cudaStreamCreateWithFlags")(out _stream, 1), "CUDA stream creation");
                Check(Export<Create>(library, "nvjpegCreateSimple")(out _handle), "nvJPEG initialization");
                Check(Export<CreateState>(library, "nvjpegJpegStateCreate")(_handle, out _state), "nvJPEG state creation");
            }
            catch { Dispose(); throw; }
            finally { GpuHardware.Release(ref native); }
        }

        internal void Dimensions(byte[] encoded, out int width, out int height)
        {
            Check(_setDevice(_ordinal), "CUDA device selection");
            var widths = new int[4]; var heights = new int[4]; int channels, sampling;
            Check(_info(_handle, encoded, (UIntPtr)encoded.Length, out channels, out sampling, widths, heights), "nvJPEG header");
            if (channels != 1 && channels != 3) throw new NotSupportedException("JPEG color layout uses CPU decoding.");
            width = widths[0]; height = heights[0];
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > 128L * 1024 * 1024)
                throw new NotSupportedException("JPEG exceeds GPU image limits.");
        }

        internal void Decode(byte[] encoded, IntPtr buffer, int width, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Check(_setDevice(_ordinal), "CUDA device selection");
            IntPtr resource = IntPtr.Zero; bool mapped = false;
            try
            {
                Check(_register(out resource, buffer, 0), "CUDA/D3D11 registration");
                Check(_map(1, ref resource, _stream), "CUDA/D3D11 mapping"); mapped = true;
                IntPtr data; UIntPtr length; Check(_pointer(out data, out length, resource), "CUDA output buffer");
                var output = new NvImage { Channel0 = data, Pitch0 = (UIntPtr)(uint)(width * 3) };
                Check(_decode(_handle, _state, encoded, (UIntPtr)encoded.Length, 6, ref output, _stream), "nvJPEG decode");
                Check(_synchronize(_stream), "nvJPEG completion");
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                if (mapped) _unmap(1, ref resource, _stream);
                if (resource != IntPtr.Zero) _unregister(resource);
            }
        }

        public void Dispose()
        {
            if (_setDevice != null) _setDevice(_ordinal);
            if (_stream != IntPtr.Zero) _synchronize(_stream);
            if (_state != IntPtr.Zero) { _destroyState(_state); _state = IntPtr.Zero; }
            if (_handle != IntPtr.Zero) { _destroy(_handle); _handle = IntPtr.Zero; }
            if (_stream != IntPtr.Zero) { _destroyStream(_stream); _stream = IntPtr.Zero; }
        }
    }
}
