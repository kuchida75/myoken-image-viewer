using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class BuildAssets
{
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(byte[] source, UIntPtr size, string sourceName, IntPtr defines,
        IntPtr include, string entryPoint, string target, uint flags1, uint flags2, out ID3DBlob code, out ID3DBlob errors);

    private static int Main(string[] args)
    {
        try
        {
            if (args[0] == "shader") CompileShader(args[1], args[2]);
            else CreateIcon(args[1], args[2]);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void CompileShader(string source, string destination)
    {
        ID3DBlob code = null, errors = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(source);
            int result = D3DCompile(bytes, new UIntPtr((uint)bytes.Length), source, IntPtr.Zero, IntPtr.Zero,
                "main", "ps_2_0", 1u << 15, 0, out code, out errors);
            if (result < 0) throw new Exception(errors == null ? "Shader compilation failed" : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            byte[] compiled = new byte[(int)code.GetBufferSize().ToUInt64()];
            Marshal.Copy(code.GetBufferPointer(), compiled, 0, compiled.Length);
            File.WriteAllBytes(destination, compiled);
        }
        finally
        {
            if (code != null) Marshal.ReleaseComObject(code);
            if (errors != null) Marshal.ReleaseComObject(errors);
        }
    }

    private static void CreateIcon(string source, string destination)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<byte[]>();
        using (var original = Image.FromFile(source))
        {
            foreach (int size in sizes)
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var stream = new MemoryStream())
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(original, new Rectangle(0, 0, size, size), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
                bitmap.Save(stream, ImageFormat.Png);
                frames.Add(stream.ToArray());
                if (size == 256) bitmap.Save(Path.Combine(Path.GetDirectoryName(destination), "AppIcon-256.png"), ImageFormat.Png);
            }
        }
        using (var writer = new BinaryWriter(File.Create(destination)))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
    }
}
