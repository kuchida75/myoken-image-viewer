using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal static class EnhancedImageRenderer
    {
        internal static BitmapSource Render(BitmapSource source, Effect quick, ManualAdjustments manual, int turns,
            CancellationToken token, Action<double> progress, int tileSize = 1024)
        {
            int width = source.PixelWidth, height = source.PixelHeight, stride = checked(width * 4);
            byte[] pixels = new byte[checked(stride * height)];
            int count = ((width + tileSize - 1) / tileSize) * ((height + tileSize - 1) / tileSize), completed = 0;
            for (int y = 0; y < height; y += tileSize)
            for (int x = 0; x < width; x += tileSize)
            {
                token.ThrowIfCancellationRequested();
                int w = Math.Min(tileSize, width - x), h = Math.Min(tileSize, height - y);
                // Both sharpening stages sample neighbors. A guard band avoids visible tile joins.
                int left = Math.Max(0, x - 4), top = Math.Max(0, y - 4);
                int right = Math.Min(width, x + w + 4), bottom = Math.Min(height, y + h + 4);
                var crop = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top)); crop.Freeze();
                int cw = crop.PixelWidth, ch = crop.PixelHeight;
                var image = new Image { Source = crop, Width = cw, Height = ch, Stretch = Stretch.Fill };
                if (quick != null)
                {
                    Effect effect = (Effect)quick.CloneCurrentValue();
                    if (effect is QuickEnhanceEffect) effect.SetValue(QuickEnhanceEffect.TexelProperty, new Point(1.0 / cw, 1.0 / ch));
                    else if (effect is AdvancedEnhanceEffect)
                    {
                        effect.SetValue(AdvancedEnhanceEffect.TexelProperty, new Point(1.0 / cw, 1.0 / ch));
                        var mask = ((ImageBrush)effect.GetValue(AdvancedEnhanceEffect.MaskProperty)).CloneCurrentValue();
                        mask.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                        mask.Viewbox = new Rect(left / (double)width, top / (double)height, cw / (double)width, ch / (double)height);
                        effect.SetValue(AdvancedEnhanceEffect.MaskProperty, mask);
                    }
                    image.Effect = effect;
                }
                var surface = new Canvas { Width = cw, Height = ch, ClipToBounds = true };
                surface.Children.Add(image);
                if (manual != null && !manual.IsNeutral)
                { var effect = new ManualEnhanceEffect(); effect.Apply(manual); surface.Effect = effect; }
                surface.Measure(new Size(cw, ch)); surface.Arrange(new Rect(0, 0, cw, ch)); surface.UpdateLayout();
                var render = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32); render.Render(surface);
                byte[] tile = new byte[w * h * 4];
                render.CopyPixels(new Int32Rect(x - left, y - top, w, h), tile, w * 4, 0);
                for (int row = 0; row < h; row++) Buffer.BlockCopy(tile, row * w * 4, pixels, (y + row) * stride + x * 4, w * 4);
                surface.Effect = null; image.Effect = null; image.Source = null;
                if (progress != null) progress(++completed / (double)count);
            }
            token.ThrowIfCancellationRequested();
            BitmapSource output = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            output.Freeze(); turns = ImageViewport.NormalizeRotation(turns);
            if (turns != 0) { output = new TransformedBitmap(output, new RotateTransform(turns * 90)); output.Freeze(); }
            return output;
        }
    }
}
