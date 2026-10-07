using System;
using System.Collections.Generic;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataDirectory = MetadataExtractor.Directory;

namespace Myoken.Core
{
    public static class PortableImageMetadataReader
    {
        private const int MaximumTags = 256;
        private const int MaximumDescriptionLength = 2048;

        public static ImageMetadataSnapshot Read(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(path).ToList();
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var sub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            ImageOrientation? orientation = null;
            int orientationValue;
            if (ifd0 != null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out orientationValue)
                && orientationValue >= 1 && orientationValue <= 8)
                orientation = (ImageOrientation)orientationValue;

            var tags = new List<ImageMetadataTag>();
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    if (tags.Count >= MaximumTags) break;
                    var description = tag.Description;
                    if (string.IsNullOrWhiteSpace(description)) continue;
                    var text = description!;
                    if (text.Length > MaximumDescriptionLength)
                        text = text.Substring(0, MaximumDescriptionLength) + "…";
                    tags.Add(new ImageMetadataTag(directory.Name ?? "Metadata", tag.Name ?? "Tag", text));
                }
                if (tags.Count >= MaximumTags) break;
            }

            return new ImageMetadataSnapshot
            {
                Orientation = orientation,
                Make = Description(ifd0, ExifDirectoryBase.TagMake),
                Model = Description(ifd0, ExifDirectoryBase.TagModel),
                DateTaken = Description(sub, ExifDirectoryBase.TagDateTimeOriginal)
                    ?? Description(ifd0, ExifDirectoryBase.TagDateTime),
                ExposureTime = Description(sub, ExifDirectoryBase.TagExposureTime),
                FNumber = Description(sub, ExifDirectoryBase.TagFNumber),
                Iso = Description(sub, ExifDirectoryBase.TagIsoEquivalent)
                    ?? Description(sub, ExifDirectoryBase.TagIsoSpeed),
                FocalLength = Description(sub, ExifDirectoryBase.TagFocalLength),
                Software = Description(ifd0, ExifDirectoryBase.TagSoftware),
                ColorSpace = Description(sub, ExifDirectoryBase.TagColorSpace),
                Description = Description(ifd0, ExifDirectoryBase.TagImageDescription),
                Tags = tags
            };
        }

        private static string? Description(MetadataDirectory? directory, int tag)
        {
            if (directory == null || !directory.ContainsTag(tag)) return null;
            var value = directory.GetDescription(tag);
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }
    }
}
