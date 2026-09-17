using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class RegionalSharpness
    {
        public double Amount, NoiseThreshold, Softness, Confidence, NoiseLevel;
        public int EdgeSamples, SampleCount;
        public bool HasEvidence { get { return Confidence >= 0.5; } }
    }

    internal sealed class SubjectSharpnessAnalysis
    {
        public RegionalSharpness Subject, Background;
        public int PatchCount;
        public bool HasSubject;

        public string Summary
        {
            get
            {
                if (!HasSubject) return "No reliable subject mask; basic sharpening retained";
                string estimate = !Subject.HasEvidence ? "insufficient edge detail"
                    : Subject.Softness >= 0.4 ? "soft edges detected" : "already defined edges";
                return String.Format("Subject sharpening {0:0}% ({1}); background {2:0}%",
                    Subject.Amount * 100, estimate, Background.Amount * 100);
            }
        }
    }

    internal static class SubjectSharpening
    {
        private const int PatchSize = 128;
        private const int HistogramBins = 512;

        private struct Edge
        {
            public double Strength, Detail, Concentration;
        }

        private sealed class Statistics
        {
            public int Samples;
            public readonly int[] Noise = new int[HistogramBins];
            public readonly List<Edge> Edges = new List<Edge>();

            public void Merge(Statistics other)
            {
                Samples += other.Samples;
                for (int i = 0; i < Noise.Length; i++) Noise[i] += other.Noise[i];
                Edges.AddRange(other.Edges);
            }

            public RegionalSharpness Build(bool subject)
            {
                double noise = Percentile(Noise, 0.5) * 0.125;
                var concentration = new int[HistogramBins];
                int edges = 0;
                foreach (Edge edge in Edges)
                    if (edge.Strength > Math.Max(0.06, noise * 8) && edge.Detail > Math.Max(0.001, noise * 2.5))
                    {
                        concentration[Bin(edge.Concentration / 1.5)]++; edges++;
                    }
                double confidence = Clamp(edges / Math.Max(24.0, Math.Min(160, Samples * 0.004)), 0, 1)
                    * Clamp(Samples / 256.0, 0, 1);
                double softness = edges > 0 ? Clamp((0.85 - Percentile(concentration, 0.6) * 1.5) / 0.32, 0, 1) : 0;
                double noiseGuard = Math.Pow(Clamp(1 - noise / 0.035, 0, 1), 2);
                double amount = confidence >= 0.5
                    ? confidence * (subject ? 0.12 + softness * 0.78 : 0.10 + softness * 0.20) * noiseGuard : 0;
                return new RegionalSharpness { Amount = amount, NoiseThreshold = Clamp(noise * 2.5, 0.002, 0.04),
                    Softness = softness, Confidence = confidence, NoiseLevel = noise, EdgeSamples = edges, SampleCount = Samples };
            }
        }

        public static SubjectSharpnessAnalysis Analyze(BitmapSource original, EnhancementAnalysis basic, SubjectMask mask, int workers, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ValidMask(mask))
            {
                var fallback = new RegionalSharpness { Amount = basic.Sharpness, NoiseThreshold = basic.NoiseThreshold };
                return new SubjectSharpnessAnalysis { Subject = fallback, Background = fallback };
            }
            Int32Rect[] patches = SamplePatches(original.PixelWidth, original.PixelHeight, mask, token);
            var subject = new Statistics[patches.Length];
            var background = new Statistics[patches.Length];
            Parallel.For(0, patches.Length, new ParallelOptions { CancellationToken = token,
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, workers)) }, delegate(int index)
            {
                var foregroundStats = new Statistics(); var backgroundStats = new Statistics();
                AnalyzePatch(original, patches[index], mask, foregroundStats, backgroundStats, token);
                subject[index] = foregroundStats; background[index] = backgroundStats;
            });
            var subjectTotal = new Statistics(); var backgroundTotal = new Statistics();
            for (int i = 0; i < patches.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                subjectTotal.Merge(subject[i]); backgroundTotal.Merge(background[i]);
            }
            token.ThrowIfCancellationRequested();
            return new SubjectSharpnessAnalysis { Subject = subjectTotal.Build(true), Background = backgroundTotal.Build(false),
                HasSubject = true, PatchCount = patches.Length };
        }

        internal static bool ValidMask(SubjectMask mask)
        {
            return mask != null && mask.Values != null && mask.Width > 0 && mask.Height > 0
                && (long)mask.Width * mask.Height == mask.Values.Length;
        }

        internal static Int32Rect[] SamplePatches(int width, int height, SubjectMask mask, CancellationToken token)
        {
            var patches = new List<Int32Rect>();
            int columns = Math.Min(6, Math.Max(1, width / 32)), rows = Math.Min(6, Math.Max(1, height / 32));
            int patchWidth = Math.Min(PatchSize, Math.Max(1, width / columns));
            int patchHeight = Math.Min(PatchSize, Math.Max(1, height / rows));
            Action<double, double> add = delegate(double x, double y)
            {
                var rect = new Int32Rect((int)Clamp(Math.Round(x * width - patchWidth / 2.0), 0, width - patchWidth),
                    (int)Clamp(Math.Round(y * height - patchHeight / 2.0), 0, height - patchHeight), patchWidth, patchHeight);
                if (!patches.Contains(rect)) patches.Add(rect);
            };
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++) add((x + 0.5) / columns, (y + 0.5) / rows);
            // Extra mask-guided locations avoid missing a small/off-center subject between the regular samples.
            int left = mask.Width, right = -1, top = mask.Height, bottom = -1;
            for (int y = 0; y < mask.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < mask.Width; x++)
                    if (mask.Values[y * mask.Width + x] >= 0.85)
                    { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            }
            if (right >= left)
                for (int q = 0; q < 4; q++)
                {
                    double targetX = left + (right - left) * (q % 2 == 0 ? 0.25 : 0.75);
                    double targetY = top + (bottom - top) * (q < 2 ? 0.25 : 0.75), best = Double.MaxValue;
                    int chosen = -1;
                    for (int y = top; y <= bottom; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int x = left; x <= right; x++)
                        {
                            if (mask.Values[y * mask.Width + x] < 0.85) continue;
                            double distance = (x - targetX) * (x - targetX) + (y - targetY) * (y - targetY);
                            if (distance < best) { best = distance; chosen = y * mask.Width + x; }
                        }
                    }
                    if (chosen >= 0) add((chosen % mask.Width + 0.5) / mask.Width, (chosen / mask.Width + 0.5) / mask.Height);
                }
            return patches.ToArray();
        }

        private static void AnalyzePatch(BitmapSource original, Int32Rect patch, SubjectMask mask,
            Statistics subject, Statistics background, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var crop = new CroppedBitmap(original, patch);
            var converted = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[patch.Width * patch.Height * 4];
            converted.CopyPixels(pixels, patch.Width * 4, 0);
            int width = patch.Width, height = patch.Height;
            var luma = new float[width * height];
            var regions = new byte[luma.Length];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x, p = i * 4;
                    luma[i] = (float)((pixels[p] * 0.0722 + pixels[p + 1] * 0.7152 + pixels[p + 2] * 0.2126) / 255);
                    if (pixels[p + 3] < 250) continue;
                    double value = mask.Sample((patch.X + x + 0.5) / original.PixelWidth, (patch.Y + y + 0.5) / original.PixelHeight);
                    regions[i] = value >= 0.85 ? (byte)1 : value <= 0.15 ? (byte)2 : (byte)0;
                }
            }
            for (int y = 2; y < height - 2; y += 2)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 2; x < width - 2; x += 2)
                {
                    int i = y * width + x; byte region = regions[i];
                    if (region == 0) continue;
                    bool interior = true;
                    for (int yy = y - 2; yy <= y + 2 && interior; yy++)
                        for (int xx = x - 2; xx <= x + 2; xx++)
                            if (regions[yy * width + xx] != region) { interior = false; break; }
                    if (!interior) continue;
                    Statistics stats = region == 1 ? subject : background;
                    stats.Samples++;
                    double c = luma[i], l = luma[i - 1], r = luma[i + 1], u = luma[i - width], d = luma[i + width];
                    double dx = luma[i + 2] - luma[i - 2], dy = luma[i + width * 2] - luma[i - width * 2];
                    double coarse = Math.Abs(dx) + Math.Abs(dy);
                    double detail = Math.Abs(c - (l + r + u + d) * 0.25);
                    if (coarse < 0.05) stats.Noise[Bin(detail / 0.125)]++;
                    if (coarse < 0.06 || c <= 0.015 || c >= 0.985) continue;
                    double direction = Math.Abs(dx) >= Math.Abs(dy) ? (r - c) * (c - l) : (d - c) * (c - u);
                    if (direction < -0.00003) continue;
                    stats.Edges.Add(new Edge { Strength = coarse, Detail = detail,
                        Concentration = (Math.Abs(r - l) + Math.Abs(d - u)) / coarse });
                }
            }
        }

        private static int Bin(double value) { return (int)Math.Round(Clamp(value, 0, 1) * (HistogramBins - 1)); }
        private static double Percentile(int[] histogram, double fraction)
        {
            int total = histogram.Sum();
            if (total == 0) return 0;
            double target = total * fraction; int sum = 0;
            for (int i = 0; i < histogram.Length; i++) { sum += histogram[i]; if (sum >= target) return i / (double)(histogram.Length - 1); }
            return 1;
        }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
    }
}
