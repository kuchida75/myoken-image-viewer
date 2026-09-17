using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunSubjectSharpeningChecks()
    {
        Directory.CreateDirectory(Root);
        const int width = 384, height = 256;
        SubjectMask mask = HalfSubjectMask(width, height, false);
        BitmapSource sharp = FocusFixture(width, height, 0, 0, false);
        BitmapSource soft = FocusFixture(width, height, 1.4, 0, false);
        BitmapSource noisy = FocusFixture(width, height, 1.4, 0.055, false);
        var sharpAnalysis = AdvancedQuickEnhance.AnalyzeRegions(sharp, QuickEnhance.Analyze(sharp, 8, CancellationToken.None), mask, 8, CancellationToken.None);
        var softAnalysis = AdvancedQuickEnhance.AnalyzeRegions(soft, QuickEnhance.Analyze(soft, 8, CancellationToken.None), mask, 8, CancellationToken.None);
        var noisyAnalysis = AdvancedQuickEnhance.AnalyzeRegions(noisy, QuickEnhance.Analyze(noisy, 8, CancellationToken.None), mask, 8, CancellationToken.None);
        Console.WriteLine("Subject focus metrics: sharp=" + FocusDescription(sharpAnalysis.Sharpness.Subject)
            + "; soft=" + FocusDescription(softAnalysis.Sharpness.Subject) + "; noisy=" + FocusDescription(noisyAnalysis.Sharpness.Subject));
        Assert(softAnalysis.Sharpness.Subject.HasEvidence && sharpAnalysis.Sharpness.Subject.HasEvidence, "subject analysis has native edge evidence");
        Assert(softAnalysis.Sharpness.Subject.Softness > sharpAnalysis.Sharpness.Subject.Softness + 0.3, "blurry subject detected independently of sharp background");
        Assert(softAnalysis.Sharpness.Subject.Amount > sharpAnalysis.Sharpness.Subject.Amount + 0.3, "soft subject gets stronger adaptive sharpening than already-sharp subject");
        Assert(softAnalysis.Sharpness.Subject.Amount > softAnalysis.Sharpness.Background.Amount + 0.3, "subject boost is not applied to sharp background");
        Assert(Math.Abs(softAnalysis.Sharpness.Background.Amount - sharpAnalysis.Sharpness.Background.Amount) < 0.02, "background estimate does not follow subject blur");
        Assert(noisyAnalysis.Sharpness.Subject.NoiseLevel > softAnalysis.Sharpness.Subject.NoiseLevel + 0.005,
            "native subject noise is measured regionally");
        Assert(noisyAnalysis.Sharpness.Subject.Amount < softAnalysis.Sharpness.Subject.Amount * 0.65, "noise suppresses subject sharpening gain");
        Assert(noisyAnalysis.Sharpness.Subject.NoiseThreshold > softAnalysis.Sharpness.Subject.NoiseThreshold, "noisy subject gets a larger detail threshold");
        var serial = AdvancedQuickEnhance.AnalyzeRegions(soft, softAnalysis.Basic, mask, 1, CancellationToken.None);
        Assert(BitmapBytes(serial.ToneTable).SequenceEqual(BitmapBytes(softAnalysis.ToneTable)), "parallel native patch analysis merges deterministically");

        AdvancedEnhancementAnalysis onlySharp = SharpeningOnly(softAnalysis, true, true);
        byte[] baseline = RenderAdvanced(soft, SharpeningOnly(softAnalysis, false, true));
        byte[] enhanced = RenderAdvanced(soft, onlySharp);
        double subjectChange = FocusDifference(enhanced, baseline, width, height, 8, width / 2 - 8);
        double backgroundChange = FocusDifference(enhanced, baseline, width, height, width / 2 + 8, width - 8);
        Console.WriteLine("Rendered sharpening change: subject=" + subjectChange.ToString("0.000") + ", background=" + backgroundChange.ToString("0.000"));
        Assert(subjectChange > 0.1, "GPU shader actually sharpens the soft subject");
        Assert(backgroundChange < 1.0, "background sharpening remains bounded");
        Assert(FocusEdgeEnergy(enhanced, width, height, 12, width / 2 - 12) > FocusEdgeEnergy(baseline, width, height, 12, width / 2 - 12),
            "subject sharpening increases edge contrast");
        for (int i = 0; i < enhanced.Length; i += 4)
        {
            Assert(Math.Abs(enhanced[i] - baseline[i]) <= 8, "luminance halo guard limits sharpened neutral-pixel excursion");
            Assert(enhanced[i] == enhanced[i + 1] && enhanced[i] == enhanced[i + 2], "sharpening does not tint grayscale images");
        }
        SaveAdvancedBitmap(soft, Path.Combine(Root, "subject-sharpen-original.png"));
        SaveAdvancedBitmap(AdvancedBitmap(width, height, enhanced), Path.Combine(Root, "subject-sharpen-result.png"));

        byte[] flatPixels = new byte[width * height * 4];
        for (int i = 0; i < flatPixels.Length; i += 4)
        {
            byte value = i / 4 % width < width / 2 ? (byte)100 : (byte)190;
            flatPixels[i] = flatPixels[i + 1] = flatPixels[i + 2] = value; flatPixels[i + 3] = 255;
        }
        var flat = AdvancedBitmap(width, height, flatPixels);
        var flatAnalysis = SubjectSharpening.Analyze(flat, new EnhancementAnalysis { Sharpness = 0.65, NoiseThreshold = 0.002 }, mask, 8, CancellationToken.None);
        Assert(flatAnalysis.Subject.Amount == 0 && flatAnalysis.Background.Amount == 0 && !flatAnalysis.Subject.HasEvidence,
            "flat areas and the subject silhouette alone do not trigger blur sharpening");
        var tiny = AdvancedBitmap(1, 1, new byte[] { 90, 90, 90, 255 });
        var tinyMask = new SubjectMask { Width = 1, Height = 1, Values = new float[] { 1 } };
        Assert(SubjectSharpening.Analyze(tiny, new EnhancementAnalysis(), tinyMask, 8, CancellationToken.None).Subject.Amount == 0,
            "tiny subjects without evidence are not sharpened");
        var fallback = SubjectSharpening.Analyze(soft, new EnhancementAnalysis { Sharpness = 0.42, NoiseThreshold = 0.012 }, null, 8, CancellationToken.None);
        Assert(!fallback.HasSubject && fallback.Subject.Amount == 0.42 && fallback.Background.NoiseThreshold == 0.012,
            "missing subject detection retains basic sharpening without an invented subject");
        var malformed = new SubjectMask { Width = 20, Height = 20, Values = new float[2] };
        Assert(!SubjectSharpening.Analyze(soft, softAnalysis.Basic, malformed, 4, CancellationToken.None).HasSubject, "invalid mask shape falls back safely");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); bool stopped = false;
            try { SubjectSharpening.Analyze(soft, softAnalysis.Basic, mask, 8, cancel.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Assert(stopped, "subject sharpening honors cancellation");
        }

        BitmapSource large = FocusFixture(2048, 1536, 0, 0, false);
        var native = SubjectSharpening.Analyze(large, new EnhancementAnalysis(), HalfSubjectMask(64, 48, false), 8, CancellationToken.None);
        Assert(native.PatchCount <= 40 && native.Subject.HasEvidence && native.Subject.Softness < 0.1,
            "large images are evaluated at native pixel scale, without resampling away sharp detail");
        var smallMask = new SubjectMask { Width = 100, Height = 100, Values = new float[10000] };
        for (int y = 16; y < 20; y++) for (int x = 87; x < 91; x++) smallMask.Values[y * 100 + x] = 1;
        var patches = SubjectSharpening.SamplePatches(4000, 3000, smallMask, CancellationToken.None);
        Assert(patches.Length <= 40 && patches.All(patch => patch.Width <= 128 && patch.Height <= 128), "native patch count and size are bounded");
        Assert(patches.Any(patch => patch.X < 3560 && patch.X + patch.Width > 3560 && patch.Y < 540 && patch.Y + patch.Height > 540),
            "mask-guided native samples cover small off-center subjects");

        CheckSubjectSharpenSkinAndAlpha();
        Console.WriteLine("PASS: subject-adaptive native sharpness analysis, noise/confidence guards, regional GPU pixels, skin/alpha, bounded sampling and deterministic cancellation-safe analysis.");
    }

    private static string FocusDescription(RegionalSharpness stats)
    {
        return String.Format("amount {0:0.000}, softness {1:0.000}, noise {2:0.0000}, edges {3}", stats.Amount, stats.Softness, stats.NoiseLevel, stats.EdgeSamples);
    }

    private static SubjectMask HalfSubjectMask(int width, int height, bool person)
    {
        var mask = new SubjectMask { Width = width, Height = height, Values = new float[width * height], IsPerson = person, Description = "Generated subject/background mask" };
        for (int y = 0; y < height; y++) for (int x = 0; x < width / 2; x++) mask.Values[y * width + x] = 1;
        return mask;
    }

    private static BitmapSource FocusFixture(int width, int height, double sigma, double noise, bool skin)
    {
        var luminance = new double[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            luminance[y * width + x] = (x / 12 + y / 16) % 2 == 0 ? 0.25 : 0.75;
        double[] blurred = luminance;
        if (sigma > 0)
        {
            int radius = (int)Math.Ceiling(sigma * 3);
            var weights = Enumerable.Range(-radius, radius * 2 + 1).Select(i => Math.Exp(-i * i / (2 * sigma * sigma))).ToArray();
            double sum = weights.Sum(); for (int i = 0; i < weights.Length; i++) weights[i] /= sum;
            var horizontal = new double[luminance.Length]; blurred = new double[luminance.Length];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                for (int i = -radius; i <= radius; i++) horizontal[y * width + x] += luminance[y * width + Math.Max(0, Math.Min(width - 1, x + i))] * weights[i + radius];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                for (int i = -radius; i <= radius; i++) blurred[y * width + x] += horizontal[Math.Max(0, Math.Min(height - 1, y + i)) * width + x] * weights[i + radius];
        }
        var pixels = new byte[width * height * 4]; var random = new Random(531);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            double value = x < width / 2 ? blurred[i] : luminance[i];
            if (x < width / 2 && noise > 0) value += Math.Sqrt(-2 * Math.Log(Math.Max(0.000001, random.NextDouble()))) * Math.Cos(2 * Math.PI * random.NextDouble()) * noise;
            value = Math.Max(0, Math.Min(1, value));
            pixels[i * 4] = (byte)Math.Round(value * (skin ? 0.41 : 1) * 255);
            pixels[i * 4 + 1] = (byte)Math.Round(value * (skin ? 0.52 : 1) * 255);
            pixels[i * 4 + 2] = (byte)Math.Round(value * (skin ? 0.72 : 1) * 255);
            pixels[i * 4 + 3] = 255;
        }
        return AdvancedBitmap(width, height, pixels);
    }

    private static AdvancedEnhancementAnalysis SharpeningOnly(AdvancedEnhancementAnalysis analysis, bool enabled, bool skinProtection)
    {
        byte[] tones = BitmapBytes(analysis.ToneTable), masks = BitmapBytes(analysis.Mask);
        for (int y = 0; y < 2; y++) for (int x = 0; x < 256; x++)
        {
            int i = (y * 256 + x) * 4; tones[i + 2] = (byte)x;
            if (!enabled) tones[i + 1] = 0;
        }
        for (int i = 0; i < masks.Length; i += 4) { masks[i] = 0; if (!skinProtection) masks[i + 1] = 0; }
        return new AdvancedEnhancementAnalysis { Basic = analysis.Basic, Sharpness = analysis.Sharpness,
            Mask = AdvancedBitmap(analysis.Mask.PixelWidth, analysis.Mask.PixelHeight, masks), ToneTable = AdvancedBitmap(256, 2, tones) };
    }

    private static double FocusDifference(byte[] left, byte[] right, int width, int height, int start, int end)
    {
        double total = 0; int count = 0;
        for (int y = 4; y < height - 4; y++) for (int x = start; x < end; x++)
        { total += Math.Abs(left[(y * width + x) * 4] - right[(y * width + x) * 4]); count++; }
        return total / Math.Max(1, count);
    }

    private static double FocusEdgeEnergy(byte[] pixels, int width, int height, int start, int end)
    {
        double total = 0;
        for (int y = 4; y < height - 4; y++) for (int x = start; x < end; x++)
        {
            int i = (y * width + x) * 4;
            total += Math.Pow(pixels[i + 4] - pixels[i], 2) + Math.Pow(pixels[i + width * 4] - pixels[i], 2);
        }
        return total;
    }

    private static void CheckSubjectSharpenSkinAndAlpha()
    {
        const int width = 384, height = 256;
        var source = FocusFixture(width, height, 1.4, 0, true);
        var analysis = AdvancedQuickEnhance.AnalyzeRegions(source, QuickEnhance.Analyze(source, 8, CancellationToken.None), HalfSubjectMask(width, height, true), 8, CancellationToken.None);
        byte[] original = RenderAdvanced(source, SharpeningOnly(analysis, false, true));
        byte[] protectedPixels = RenderAdvanced(source, SharpeningOnly(analysis, true, true));
        byte[] unprotected = RenderAdvanced(source, SharpeningOnly(analysis, true, false));
        Assert(FocusDifference(protectedPixels, original, width, height, 8, width / 2 - 8)
            < FocusDifference(unprotected, original, width, height, 8, width / 2 - 8), "skin-colored areas get gentler sharpening than unprotected areas");
        double hueError = 0; int changed = 0;
        for (int y = 8; y < height - 8; y++) for (int x = 8; x < width / 2 - 8; x++)
        {
            int i = (y * width + x) * 4;
            if (original[i] == protectedPixels[i] && original[i + 2] == protectedPixels[i + 2]) continue;
            hueError += Math.Abs(Hue(original[i + 2], original[i + 1], original[i]) - Hue(protectedPixels[i + 2], protectedPixels[i + 1], protectedPixels[i])); changed++;
        }
        Assert(changed > 0 && hueError / changed < 3, "subject sharpening retains natural skin hue");
        byte[] transparent = BitmapBytes(source);
        for (int y = 0; y < height; y++) for (int x = 0; x < 16; x++) transparent[(y * width + x) * 4 + 3] = (byte)(x < 8 ? 0 : 128);
        var alpha = AdvancedBitmap(width, height, transparent);
        var alphaAnalysis = AdvancedQuickEnhance.AnalyzeRegions(alpha, analysis.Basic, HalfSubjectMask(width, height, true), 4, CancellationToken.None);
        byte[] a = RenderAdvanced(alpha, SharpeningOnly(alphaAnalysis, false, true)), b = RenderAdvanced(alpha, SharpeningOnly(alphaAnalysis, true, true));
        for (int i = 0; i < b.Length; i += 4) Assert(a[i + 3] == b[i + 3], "adaptive sharpening preserves image alpha");
        for (int y = 4; y < height - 4; y++)
        {
            int p = (y * width + 16) * 4;
            Assert(a[p] == b[p] && a[p + 1] == b[p + 1] && a[p + 2] == b[p + 2], "sharpening is suppressed beside partial-alpha edges");
        }
    }
}
