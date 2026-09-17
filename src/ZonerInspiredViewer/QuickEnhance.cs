using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class EnhancementAnalysis
    {
        public double ExposureStops { get; set; }
        public double Sharpness { get; set; }
        public double NoiseThreshold { get; set; }
    }

    internal static class QuickEnhance
    {
        public static EnhancementAnalysis Analyze(BitmapSource original, int workers, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            double scale = Math.Min(1, 768.0 / Math.Max(original.PixelWidth, original.PixelHeight));
            BitmapSource sample = original;
            if (scale < 1) sample = new TransformedBitmap(original, new ScaleTransform(scale, scale));
            sample = new FormatConvertedBitmap(sample, PixelFormats.Bgra32, null, 0);
            int width = sample.PixelWidth, height = sample.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            sample.CopyPixels(pixels, stride, 0);
            token.ThrowIfCancellationRequested();
            var luma = new float[width * height];
            var histograms = new int[height][];
            var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, workers)) };
            Parallel.For(0, height, options, delegate(int y)
            {
                var histogram = new int[256];
                histograms[y] = histogram;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x, offset = index * 4;
                    if (pixels[offset + 3] < 128) { luma[index] = -1; continue; }
                    float value = (float)((pixels[offset] * 0.0722 + pixels[offset + 1] * 0.7152
                        + pixels[offset + 2] * 0.2126) / 255);
                    luma[index] = value;
                    histogram[Math.Min(255, (int)(value * 255))]++;
                }
            });
            var total = new int[256];
            foreach (int[] row in histograms)
                for (int i = 0; i < 256; i++) total[i] += row[i];
            int count = total.Sum();
            if (count == 0) return new EnhancementAnalysis();
            double low = Percentile(total, count, 0.02), median = Percentile(total, count, 0.5), high = Percentile(total, count, 0.98);
            // A bounded midtone curve preserves black/white endpoints; it cannot recover clipped detail.
            double exposure = Math.Log((0.46 / 0.54) * (1 - median) / Math.Max(0.004, median), 2);
            exposure = Clamp(exposure, -1.0, 1.25);
            if (high - low < 0.025 || Math.Abs(exposure) < 0.12) exposure = 0;

            var edges = new int[height][];
            var details = new int[height][];
            var noise = new int[height][];
            Parallel.For(0, height, options, delegate(int y)
            {
                edges[y] = new int[256]; details[y] = new int[256]; noise[y] = new int[256];
                if (y == 0 || y == height - 1) return;
                for (int x = 1; x < width - 1; x++)
                {
                    int i = y * width + x;
                    float c = luma[i], l = luma[i - 1], r = luma[i + 1], u = luma[i - width], d = luma[i + width];
                    if (Math.Min(c, Math.Min(Math.Min(l, r), Math.Min(u, d))) < 0) continue;
                    double edge = (Math.Abs(l - r) + Math.Abs(u - d)) / 2;
                    double detail = Math.Abs(c - (l + r + u + d) / 4);
                    int edgeBin = Math.Min(255, (int)(edge * 255)), detailBin = Math.Min(255, (int)(detail * 255));
                    edges[y][edgeBin]++; details[y][detailBin]++;
                    if (edge < 0.035) noise[y][detailBin]++;
                }
            });
            var edgeTotal = SumRows(edges); var detailTotal = SumRows(details); var noiseTotal = SumRows(noise);
            double edgeStrength = Percentile(edgeTotal, edgeTotal.Sum(), 0.9);
            double detailStrength = Percentile(detailTotal, detailTotal.Sum(), 0.9);
            double noiseLevel = Percentile(noiseTotal, noiseTotal.Sum(), 0.5);
            double amount = Clamp(0.7 - detailStrength * 7, 0.1, 0.65) * Clamp(1 - noiseLevel / 0.025, 0, 1);
            if (edgeStrength < 0.004 || high - low < 0.025) amount = 0;
            token.ThrowIfCancellationRequested();
            return new EnhancementAnalysis { ExposureStops = exposure, Sharpness = amount, NoiseThreshold = Clamp(noiseLevel * 2, 0.002, 0.025) };
        }

        private static int[] SumRows(int[][] rows)
        {
            var result = new int[256];
            foreach (int[] row in rows) for (int i = 0; i < 256; i++) result[i] += row[i];
            return result;
        }

        private static double Percentile(int[] histogram, int count, double percentile)
        {
            if (count == 0) return 0;
            int target = Math.Max(1, (int)Math.Ceiling(count * percentile)), sum = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                sum += histogram[i];
                if (sum >= target) return i / 255.0;
            }
            return 1;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    internal sealed class QuickEnhanceEffect : ShaderEffect
    {
        private static readonly PixelShader Shader = LoadShader();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(QuickEnhanceEffect), 0);
        public static readonly DependencyProperty AdjustmentsProperty = DependencyProperty.Register("Adjustments", typeof(Point), typeof(QuickEnhanceEffect),
            new UIPropertyMetadata(new Point(1, 0), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty TexelProperty = DependencyProperty.Register("Texel", typeof(Point), typeof(QuickEnhanceEffect),
            new UIPropertyMetadata(new Point(0.001, 0.001), PixelShaderConstantCallback(1)));
        public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register("Threshold", typeof(double), typeof(QuickEnhanceEffect),
            new UIPropertyMetadata(0.002, PixelShaderConstantCallback(2)));

        public QuickEnhanceEffect()
        {
            DdxUvDdyUvRegisterIndex = 3;
            PixelShader = Shader;
            UpdateShaderValue(InputProperty); UpdateShaderValue(AdjustmentsProperty);
            UpdateShaderValue(TexelProperty); UpdateShaderValue(ThresholdProperty);
        }

        public QuickEnhanceEffect(EnhancementAnalysis analysis, BitmapSource bitmap) : this()
        {
            SetValue(AdjustmentsProperty, new Point(Math.Pow(2, analysis.ExposureStops), analysis.Sharpness));
            SetValue(TexelProperty, new Point(1.0 / bitmap.PixelWidth, 1.0 / bitmap.PixelHeight));
            SetValue(ThresholdProperty, analysis.NoiseThreshold);
        }

        private static PixelShader LoadShader()
        {
            var shader = new PixelShader();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Viewer.QuickEnhance.ps"))
                shader.SetStreamSource(stream);
            shader.Freeze();
            return shader;
        }
    }
}
