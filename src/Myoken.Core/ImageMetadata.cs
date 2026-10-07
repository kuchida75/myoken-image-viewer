using System.Collections.Generic;

namespace Myoken.Core
{
    public sealed class ImageMetadataTag
    {
        public ImageMetadataTag(string directoryName, string name, string description)
        {
            DirectoryName = directoryName;
            Name = name;
            Description = description;
        }

        public string DirectoryName { get; }
        public string Name { get; }
        public string Description { get; }
    }

    // Portable, UI-independent metadata snapshot. It intentionally contains no
    // Avalonia/Skia types so Windows can adopt it later without replacing WPF.
    public sealed class ImageMetadataSnapshot
    {
        public ImageOrientation? Orientation { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? DateTaken { get; set; }
        public string? ExposureTime { get; set; }
        public string? FNumber { get; set; }
        public string? Iso { get; set; }
        public string? FocalLength { get; set; }
        public string? Software { get; set; }
        public string? ColorSpace { get; set; }
        public string? Description { get; set; }
        public IReadOnlyList<ImageMetadataTag> Tags { get; set; } = new List<ImageMetadataTag>();
    }
}
