using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class ManualAdjustments
    {
        internal static readonly string[] Names = { "Vibrance", "Exposure", "Brilliance", "Contrast", "Brightness", "Black point", "Saturation", "Sharpness", "Highlights", "Shadows" };
        [DataMember] public double[] Values { get; set; }
        public ManualAdjustments() { Values = new double[Names.Length]; }
        internal static double Minimum(int index) { return index == 7 ? 0 : -100; }
        internal bool IsNeutral { get { return Values.All(v => v == 0); } }
        internal static bool IsValid(ManualAdjustments value)
        {
            return value == null || value.Values != null && value.Values.Length == Names.Length
                && value.Values.Select((v, i) => ImageViewport.IsFinite(v) && v >= Minimum(i) && v <= 100).All(v => v);
        }
        internal static ManualAdjustments Copy(ManualAdjustments value)
        { return value == null || !IsValid(value) ? new ManualAdjustments() : new ManualAdjustments { Values = (double[])value.Values.Clone() }; }

        internal double Tone(double x)
        {
            double exposure = Math.Pow(2, Values[1] * 0.03);
            double y = Math.Pow(Math.Min(1, Math.Pow(x, 2.2) * exposure), 1 / 2.2);
            double brilliance = Values[2] / 100;
            double shadows = Values[9] / 100 + brilliance * 0.3;
            double highlights = Values[8] / 100 - brilliance * 0.25;
            double low = 1 - y, high = y;
            y += shadows * 0.65 * low * low * y + highlights * 0.65 * high * high * (1 - y);
            y += brilliance * 0.22 * y * (1 - y);
            y = (y - 0.5) * Math.Pow(2, Values[3] / 100) + 0.5;
            y += Values[4] / 100 * 0.4;
            double black = Values[5] / 100 * 0.3;
            y = black >= 0 ? (y - black) / (1 - black) : y - black * (1 - y);
            return Math.Max(0, Math.Min(1, y));
        }

        internal BitmapSource ToneTable()
        {
            byte[] pixels = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                byte y = (byte)Math.Round(Tone(i / 255.0) * 255);
                pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = y; pixels[i * 4 + 3] = 255;
            }
            var bitmap = BitmapSource.Create(256, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 1024);
            bitmap.Freeze(); return bitmap;
        }
    }
}
