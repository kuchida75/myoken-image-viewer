namespace Myoken.Linux;

internal static class PreviewPreloadPolicy
{
    // Prefer the next image, then the previous image, then the second next image.
    // Targets never wrap around the open-tab list.
    public static string[] Targets(IReadOnlyList<string> orderedPaths, string selectedPath)
    {
        ArgumentNullException.ThrowIfNull(orderedPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        var index = -1;
        for (var i = 0; i < orderedPaths.Count; i++)
            if (StringComparer.Ordinal.Equals(orderedPaths[i], selectedPath)) { index = i; break; }
        if (index < 0) return Array.Empty<string>();

        var result = new List<string>(3);
        void Add(int candidate)
        {
            if ((uint)candidate >= (uint)orderedPaths.Count) return;
            var path = orderedPaths[candidate];
            if (!StringComparer.Ordinal.Equals(path, selectedPath) && !result.Contains(path, StringComparer.Ordinal))
                result.Add(path);
        }
        Add(index + 1);
        Add(index - 1);
        Add(index + 2);
        return result.ToArray();
    }
}
