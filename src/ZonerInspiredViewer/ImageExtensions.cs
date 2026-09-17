using System;
using System.Collections.Generic;
using System.IO;

namespace ZonerInspiredViewer
{
    internal static class ImageExtensions
    {
        private static readonly HashSet<string> Browsable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".heic",
            ".heif",
            ".avif",
            ".jxl",
            ".gif"
        };

        public static bool IsBrowsableImage(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Browsable.Contains(Path.GetExtension(path));
        }

        public static string FormatBytes(long value)
        {
            double size = value;
            string[] units = { "B", "KB", "MB", "GB" };
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size = size / 1024;
                unit++;
            }

            return size.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }
    }
}
