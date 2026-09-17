using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace ZonerInspiredViewer
{
    internal enum UpscaleStatus { None, Working, Ready, Bypassed, Canceled, Error }

    internal static class UpscalePresentation
    {
        internal static string TextKey(UpscaleStatus state)
        {
            switch (state)
            {
                case UpscaleStatus.Working:
                case UpscaleStatus.Canceled: return ThemeKeys.UpscaleWorkingText;
                case UpscaleStatus.Ready: return ThemeKeys.UpscaleReadyText;
                case UpscaleStatus.Bypassed: return ThemeKeys.UpscaleBypassedText;
                case UpscaleStatus.Error: return ThemeKeys.UpscaleErrorText;
                default: return ThemeKeys.MutedText;
            }
        }

        internal static void PrepareButton(Button button)
        {
            // Preserve the state fill on hover and press; only the border/opacity changes.
            button.Template = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
    <Border Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
            BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='2'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Theme.Accent}'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.8'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>");
        }

        internal static void ApplyButton(Button button, UpscaleStatus state)
        {
            if (button == null) return;
            string background = state == UpscaleStatus.Working ? ThemeKeys.UpscaleWorkingBackground
                : state == UpscaleStatus.Ready ? ThemeKeys.UpscaleReadyBackground
                : state == UpscaleStatus.Bypassed ? ThemeKeys.UpscaleBypassedBackground : ThemeKeys.ControlBackground;
            ThemeManager.Bind(button, Control.BackgroundProperty, background);
            ThemeManager.Bind(button, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(button, Control.BorderBrushProperty, state == UpscaleStatus.None ? ThemeKeys.Border : TextKey(state));
        }
    }
}
