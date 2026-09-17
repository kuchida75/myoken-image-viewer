using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ZonerInspiredViewer
{
    internal sealed class ManualEnhanceEffect : ShaderEffect
    {
        private static readonly PixelShader Shader = LoadShader();
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(ManualEnhanceEffect), 0);
        public static readonly DependencyProperty TonesProperty = RegisterPixelShaderSamplerProperty("Tones", typeof(ManualEnhanceEffect), 1, SamplingMode.Bilinear);
        public static readonly DependencyProperty ColorProperty = DependencyProperty.Register("Color", typeof(Point), typeof(ManualEnhanceEffect),
            new UIPropertyMetadata(new Point(), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty SharpnessProperty = DependencyProperty.Register("Sharpness", typeof(double), typeof(ManualEnhanceEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(1)));

        internal ManualEnhanceEffect()
        {
            PixelShader = Shader; DdxUvDdyUvRegisterIndex = 2;
            UpdateShaderValue(InputProperty); UpdateShaderValue(TonesProperty); UpdateShaderValue(ColorProperty); UpdateShaderValue(SharpnessProperty);
        }
        internal void Apply(ManualAdjustments adjustments)
        {
            SetValue(TonesProperty, new ImageBrush(adjustments.ToneTable()) { Stretch = Stretch.Fill });
            SetValue(ColorProperty, new Point(adjustments.Values[0] / 100, adjustments.Values[6] / 100));
            SetValue(SharpnessProperty, adjustments.Values[7] / 100 * 2);
        }
        private static PixelShader LoadShader()
        {
            var shader = new PixelShader();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Viewer.ManualEnhance.ps")) shader.SetStreamSource(stream);
            shader.Freeze(); return shader;
        }
    }
}
