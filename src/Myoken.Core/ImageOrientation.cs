namespace Myoken.Core
{
    // Values deliberately match the EXIF Orientation tag and Skia's encoded-origin values.
    public enum ImageOrientation
    {
        TopLeft = 1,
        TopRight = 2,
        BottomRight = 3,
        BottomLeft = 4,
        LeftTop = 5,
        RightTop = 6,
        RightBottom = 7,
        LeftBottom = 8
    }

    public static class ImageOrientationInfo
    {
        public static ImageOrientation Normalize(int value) =>
            value >= 1 && value <= 8 ? (ImageOrientation)value : ImageOrientation.TopLeft;

        public static bool SwapsAxes(ImageOrientation orientation) =>
            orientation == ImageOrientation.LeftTop || orientation == ImageOrientation.RightTop
            || orientation == ImageOrientation.RightBottom || orientation == ImageOrientation.LeftBottom;

        public static string DisplayName(ImageOrientation orientation)
        {
            switch (orientation)
            {
                case ImageOrientation.TopLeft: return "Normal (1)";
                case ImageOrientation.TopRight: return "Mirror horizontal (2)";
                case ImageOrientation.BottomRight: return "Rotate 180° (3)";
                case ImageOrientation.BottomLeft: return "Mirror vertical (4)";
                case ImageOrientation.LeftTop: return "Transpose (5)";
                case ImageOrientation.RightTop: return "Rotate 90° CW (6)";
                case ImageOrientation.RightBottom: return "Transverse (7)";
                case ImageOrientation.LeftBottom: return "Rotate 90° CCW (8)";
                default: return "Normal (1)";
            }
        }
    }
}
