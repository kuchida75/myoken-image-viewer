namespace Myoken.Linux;

// Pure layout arithmetic. No controls or decoded images are allocated for off-screen files.
internal readonly record struct ThumbnailRange(int Columns, int First, int End, double ExtentHeight);

internal static class ThumbnailLayout
{
    public const double CellWidth = 184;
    public const double CellHeight = 184;
    public const int OverscanRows = 1;

    public static ThumbnailRange Calculate(int count, double width, double height, double offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        width = double.IsFinite(width) ? Math.Max(0, width) : 0;
        height = double.IsFinite(height) ? Math.Max(0, height) : 0;
        var columns = Math.Max(1, (int)Math.Min(4096, Math.Floor(width / CellWidth)));
        var rows = ((long)count + columns - 1) / columns;
        var extent = rows * CellHeight;
        if (count == 0 || width == 0 || height == 0)
            return new(columns, 0, 0, extent);
        offset = double.IsFinite(offset) ? Math.Clamp(offset, 0, Math.Max(0, extent - height)) : 0;
        var firstRow = Math.Max(0, (long)Math.Floor(offset / CellHeight) - OverscanRows);
        var endRow = Math.Min(rows, (long)Math.Ceiling((offset + height) / CellHeight) + OverscanRows);
        return new(columns, (int)Math.Min(count, firstRow * columns),
            (int)Math.Min(count, endRow * columns), extent);
    }

    public static string TabCaption(string name)
    {
        // Preserve the distinguishing end of screenshot filenames as well as the prefix.
        return name.Length <= 34 ? name : name[..12] + "…" + name[^21..];
    }
}
