using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class AdvancedEnhanceEffect : ShaderEffect
    {
        private static readonly PixelShader Shader = LoadShader();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(AdvancedEnhanceEffect), 0);
        public static readonly DependencyProperty MaskProperty = RegisterPixelShaderSamplerProperty("Mask", typeof(AdvancedEnhanceEffect), 1, SamplingMode.Bilinear);
        public static readonly DependencyProperty TonesProperty = RegisterPixelShaderSamplerProperty("Tones", typeof(AdvancedEnhanceEffect), 2, SamplingMode.Bilinear);
        public static readonly DependencyProperty TexelProperty = DependencyProperty.Register("Texel", typeof(Point), typeof(AdvancedEnhanceEffect),
            new UIPropertyMetadata(new Point(0.001, 0.001), PixelShaderConstantCallback(1)));

        public AdvancedEnhanceEffect()
        {
            PixelShader = Shader; DdxUvDdyUvRegisterIndex = 2;
            UpdateShaderValue(InputProperty); UpdateShaderValue(MaskProperty); UpdateShaderValue(TonesProperty);
            UpdateShaderValue(TexelProperty);
        }

        public AdvancedEnhanceEffect(AdvancedEnhancementAnalysis analysis, BitmapSource bitmap) : this()
        {
            SetValue(MaskProperty, new ImageBrush(analysis.Mask) { Stretch = Stretch.Fill });
            SetValue(TonesProperty, new ImageBrush(analysis.ToneTable) { Stretch = Stretch.Fill });
            SetValue(TexelProperty, new Point(1.0 / bitmap.PixelWidth, 1.0 / bitmap.PixelHeight));
        }

        private static PixelShader LoadShader()
        {
            var shader = new PixelShader();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Viewer.AdvancedEnhance.ps")) shader.SetStreamSource(stream);
            shader.Freeze(); return shader;
        }
    }
}
