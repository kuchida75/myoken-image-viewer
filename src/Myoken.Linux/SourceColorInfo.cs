using SkiaSharp;

namespace Myoken.Linux;

internal readonly record struct SourceColorInfo(
    string Name,
    bool DecoderColorSpaceKnown,
    bool ConvertToSrgb)
{
    public static SourceColorInfo Inspect(SKColorSpace? colorSpace)
    {
        if (colorSpace == null)
            return new("Unspecified (assumed sRGB)", false, false);
        if (colorSpace.IsSrgb)
            return new("sRGB", true, false);

        var xyz = colorSpace.ToColorSpaceXyz();
        if (Near(xyz, SKColorSpaceXyz.DisplayP3))
            return new("Display P3", true, true);
        if (Near(xyz, SKColorSpaceXyz.AdobeRgb))
            return new("Adobe RGB", true, true);
        if (Near(xyz, SKColorSpaceXyz.Rec2020))
            return new("Rec. 2020", true, true);
        if (Near(xyz, SKColorSpaceXyz.Srgb))
            return new("sRGB primaries / non-sRGB transfer", true, true);
        return new("Custom / ICC color space", true, true);
    }

    private static bool Near(SKColorSpaceXyz left, SKColorSpaceXyz right)
    {
        var a = left.Values; var b = right.Values;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > 0.0025f) return false;
        return true;
    }
}
