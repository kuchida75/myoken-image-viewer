using PhotoSauce.MagicScaler;

namespace Myoken.Linux;

// PhotoSauce CodecManager is process-global. Keep every Myoken-owned advanced
// codec in one registration so opening one format never disables another.
internal static class AdvancedCodecRegistry
{
    private static readonly Lazy<bool> Registered = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureRegistered() => _ = Registered.Value;

    private static bool Register()
    {
        CodecManager.Configure(codecs =>
        {
            PhotoSauce.NativeCodecs.Libheif.CodecCollectionExtensions.UseLibheif(codecs);
            PhotoSauce.NativeCodecs.Libjxl.CodecCollectionExtensions.UseLibjxl(codecs);
        });
        return true;
    }
}
