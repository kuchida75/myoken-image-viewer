using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal static class CommandPresentation
    {
        internal static readonly DependencyProperty ShortcutResolverProperty = DependencyProperty.RegisterAttached(
            "ShortcutResolver", typeof(Func<string, string>), typeof(CommandPresentation), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
        private static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
            "Description", typeof(Description), typeof(CommandPresentation));
        private sealed class Description { public string Name, Shortcut, Text; }
        internal static FrameworkElement Label(string glyph, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16, Width = 18, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (!String.IsNullOrEmpty(text)) panel.Children.Add(new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        internal static Button Button(string glyph, string name, string shortcut, string description, Action action, bool withText)
        {
            var button = AdvancedSearchWindow.IconButton(glyph, name, action);
            button.Content = Label(glyph, withText ? name : null);
            if (withText) { button.Width = Double.NaN; button.MinWidth = 32; button.Padding = new Thickness(8, 0, 8, 0); }
            Describe(button, name, shortcut, description); return button;
        }

        internal static void Describe(FrameworkElement control, string name, string shortcut, string description)
        {
            if (control.GetValue(DescriptionProperty) == null) control.ToolTipOpening += RefreshShortcutHint;
            control.SetValue(DescriptionProperty, new Description { Name = name, Shortcut = shortcut, Text = description });
            RenderDescription(control, name, shortcut, description);
        }

        private static void RefreshShortcutHint(object sender, ToolTipEventArgs e)
        {
            var control = (FrameworkElement)sender;
            var info = (Description)control.GetValue(DescriptionProperty);
            RenderDescription(control, info.Name, info.Shortcut, info.Text);
        }

        private static void RenderDescription(FrameworkElement control, string name, string shortcut, string description)
        {
            var resolver = control.GetValue(ShortcutResolverProperty) as Func<string, string>;
            if (resolver != null && !(name == "Close tab" && shortcut == null)) shortcut = resolver(name) ?? shortcut;
            var panel = new StackPanel { MaxWidth = 280 };
            var title = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(title);
            if (!String.IsNullOrEmpty(shortcut)) panel.Children.Add(new TextBlock { Text = shortcut, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), FontWeight = FontWeights.SemiBold });
            if (!String.IsNullOrEmpty(description)) panel.Children.Add(new TextBlock { Text = description,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            var tooltip = new ToolTip { Content = panel, Padding = new Thickness(12), FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
            ThemeManager.Bind(tooltip, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(tooltip, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(tooltip, Control.BorderBrushProperty, ThemeKeys.Border);
            control.ToolTip = tooltip;
            ToolTipService.SetShowOnDisabled(control, true); ToolTipService.SetInitialShowDelay(control, 350);
            ToolTipService.SetShowDuration(control, 30000);
            AutomationProperties.SetName(control, name);
            AutomationProperties.SetHelpText(control, description ?? name);
            AutomationProperties.SetAcceleratorKey(control, shortcut == "Unassigned" ? "" : shortcut ?? "");
        }
    }
}
