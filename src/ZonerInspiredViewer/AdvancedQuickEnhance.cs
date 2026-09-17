using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class RegionalTone
    {
        public double ExposureStops, Shadows, Highlights, Vibrance;

        public double Map(double value)
        {
            double gain = Math.Pow(2, ExposureStops);
            value = value * gain / (1 + value * (gain - 1));
            value += Shadows * value * Math.Pow(1 - value, 3);
            value -= Highlights * Math.Pow(value, 3) * (1 - value);
            return AdvancedQuickEnhance.Clamp(value, 0, 1);
        }
    }

    internal sealed class AdvancedEnhancementAnalysis
    {
        public EnhancementAnalysis Basic;
        public RegionalTone Subject, Background;
        public SubjectSharpnessAnalysis Sharpness;
        public BitmapSource Mask, ToneTable;
        public bool HasSubject, HasPerson;
        public string DetectionSummary;
    }

    internal static class AdvancedQuickEnhance
    {
        public static AdvancedEnhancementAnalysis Analyze(BitmapSource original, int workers, SubjectSegmentation detector, CancellationToken token)
        {
            EnhancementAnalysis basic = QuickEnhance.Analyze(original, workers, token);
            SubjectMask mask;
            try { mask = detector.Detect(original, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                mask = new SubjectMask { Description = "Subject detection unavailable; gentle global adjustments. " + ex.Message };
            }
            return AnalyzeRegions(original, basic, mask, workers, token);
        }

        internal static AdvancedEnhancementAnalysis AnalyzeRegions(BitmapSource original, EnhancementAnalysis basic, SubjectMask subject, int workers, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            double scale = Math.Min(1, 512.0 / Math.Max(original.PixelWidth, original.PixelHeight));
            int width = Math.Max(1, (int)Math.Round(original.PixelWidth * scale));
            int height = Math.Max(1, (int)Math.Round(original.PixelHeight * scale));
            byte[] pixels = SubjectSegmentation.ReadPixels(original, width, height);
            var foreground = new float[width * height];
            var skin = new float[foreground.Length];
            var luma = new float[foreground.Length];
            var saturation = new float[foreground.Length];
            bool hasSubject = SubjectSharpening.ValidMask(subject);
            bool hasPerson = hasSubject && subject.IsPerson;
            var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, workers)) };
            Parallel.For(0, height, options, delegate(int y)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x, p = i * 4;
                    double r = pixels[p + 2] / 255.0, g = pixels[p + 1] / 255.0, b = pixels[p] / 255.0;
                    luma[i] = (float)(r * 0.2126 + g * 0.7152 + b * 0.0722);
                    double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    saturation[i] = (float)((max - min) / Math.Max(0.001, max));
                    foreground[i] = hasSubject ? (float)Smooth(0.15, 0.85, subject.Sample((x + 0.5) / width, (y + 0.5) / height)) : 0;
                    skin[i] = (float)(SkinProtection(r, g, b) * (hasPerson ? 0.3 + 0.7 * foreground[i] : 0.75));
                }
            });
            var map = new byte[pixels.Length];
            // Small edge-aware feathering smooths model boundaries without spreading corrections across strong edges.
            Parallel.For(0, height, options, delegate(int y)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    double sum = 0, weight = 0;
                    for (int yy = Math.Max(0, y - 2); yy <= Math.Min(height - 1, y + 2); yy++)
                    for (int xx = Math.Max(0, x - 2); xx <= Math.Min(width - 1, x + 2); xx++)
                    {
                        int j = yy * width + xx;
                        double delta = luma[i] - luma[j];
                        double w = 1.0 / (1 + (xx - x) * (xx - x) + (yy - y) * (yy - y) + delta * delta * 600);
                        sum += foreground[j] * w; weight += w;
                    }
                    map[i * 4 + 2] = Byte(sum / weight);
                    map[i * 4 + 1] = Byte(skin[i]);
                    map[i * 4 + 3] = 255;
                }
            });
            var subjectStats = new ToneStatistics();
            var backgroundStats = new ToneStatistics();
            for (int i = 0; i < foreground.Length; i++)
            {
                if (i % width == 0) token.ThrowIfCancellationRequested();
                if (pixels[i * 4 + 3] < 128) continue;
                double f = map[i * 4 + 2] / 255.0;
                subjectStats.Add(luma[i], saturation[i], skin[i], f);
                backgroundStats.Add(luma[i], saturation[i], skin[i], 1 - f);
            }
            RegionalTone backgroundTone = backgroundStats.Build(basic, false, !hasSubject);
            RegionalTone subjectTone = hasSubject ? subjectStats.Build(basic, true, false) : backgroundTone;
            for (int i = 0; i < foreground.Length; i++)
            {
                double f = map[i * 4 + 2] / 255.0;
                int max = Math.Max(pixels[i * 4], Math.Max(pixels[i * 4 + 1], pixels[i * 4 + 2]));
                int min = Math.Min(pixels[i * 4], Math.Min(pixels[i * 4 + 1], pixels[i * 4 + 2]));
                map[i * 4] = Byte((backgroundTone.Vibrance * (1 - f) + subjectTone.Vibrance * f)
                    * (1 - skin[i] * 0.95) * (1 - (max - min) / 255.0));
            }
            SubjectSharpnessAnalysis sharpness = SubjectSharpening.Analyze(original, basic, subject, workers, token);
            var table = new byte[256 * 2 * 4];
            for (int y = 0; y < 2; y++)
            {
                RegionalTone tone = y == 0 ? backgroundTone : subjectTone;
                RegionalSharpness sharpen = y == 0 ? sharpness.Background : sharpness.Subject;
                for (int x = 0; x < 256; x++)
                {
                    int i = (y * 256 + x) * 4;
                    table[i + 2] = Byte(tone.Map(x / 255.0));
                    // Bake the four-neighbor kernel scaling into the lookup to fit WPF's shader budget.
                    table[i + 1] = Byte(sharpen.Amount * 0.25);
                    table[i] = Byte(sharpen.NoiseThreshold * 4);
                    table[i + 3] = 255;
                }
            }
            token.ThrowIfCancellationRequested();
            return new AdvancedEnhancementAnalysis { Basic = basic, HasSubject = hasSubject, HasPerson = hasPerson,
                Subject = subjectTone, Background = backgroundTone, Sharpness = sharpness, Mask = Bitmap(width, height, map), ToneTable = Bitmap(256, 2, table),
                DetectionSummary = subject == null ? "Gentle global adjustments" : subject.Description };
        }

        internal static double SkinProtection(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), chroma = max - min;
            if (chroma < 0.001 || max != r) return 0;
            double hue = 60 * (g - b) / chroma;
            double sat = chroma / Math.Max(0.001, max);
            // Hue/chroma protection, not identity or skin-type classification; no target complexion or whitening.
            return Smooth(-8, 8, hue) * (1 - Smooth(42, 65, hue))
                * Smooth(0.025, 0.12, sat) * (1 - Smooth(0.65, 0.92, sat));
        }

        private sealed class ToneStatistics
        {
            private readonly double[] _histogram = new double[256];
            private double _weight, _saturation, _skin;

            public void Add(double luma, double saturation, double skin, double weight)
            {
                _weight += weight; _saturation += saturation * weight; _skin += skin * weight;
                _histogram[Math.Min(255, (int)(luma * 255))] += weight;
            }

            private double Percentile(double fraction)
            {
                double sum = 0;
                for (int i = 0; i < 256; i++) { sum += _histogram[i]; if (sum >= _weight * fraction) return i / 255.0; }
                return 1;
            }

            public RegionalTone Build(EnhancementAnalysis basic, bool subject, bool gentle)
            {
                if (_weight < 32) return new RegionalTone();
                double low = Percentile(0.1), median = Percentile(0.5), high = Percentile(0.95);
                if (high - low < 0.025) return new RegionalTone();
                double skinShare = _skin / _weight;
                double target = Clamp(median, 0.30, 0.66);
                double local = Clamp(Math.Log((target / (1 - target)) * (1 - median) / Math.Max(0.004, median), 2), -0.75, 0.95);
                double strength = gentle ? 0.65 : 1;
                return new RegionalTone
                {
                    ExposureStops = Clamp((basic.ExposureStops * (gentle ? 0.65 : 0.2) + local * (gentle ? 0.35 : 0.8))
                        * (1 - skinShare * 0.25), -0.75, 0.95),
                    Shadows = Clamp((0.28 - low) * 2.5, 0, 0.65) * strength,
                    Highlights = Clamp((high - 0.72) * 2.5, 0, 0.60) * strength,
                    Vibrance = Clamp((0.48 - _saturation / _weight) * 0.6, 0, subject ? 0.16 : 0.22) * strength
                };
            }
        }

        private static BitmapSource Bitmap(int width, int height, byte[] pixels)
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze(); return bitmap;
        }

        private static byte Byte(double value) { return (byte)Math.Round(Clamp(value, 0, 1) * 255); }
        internal static double Clamp(double value, double low, double high) { return Math.Max(low, Math.Min(high, value)); }
        private static double Smooth(double low, double high, double value)
        {
            value = Clamp((value - low) / (high - low), 0, 1); return value * value * (3 - 2 * value);
        }
    }
}
