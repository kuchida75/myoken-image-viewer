using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal static class ThemeKeys
    {
        public const string WindowBackground = "Theme.WindowBackground";
        public const string ToolbarBackground = "Theme.ToolbarBackground";
        public const string PaneBackground = "Theme.PaneBackground";
        public const string WorkspaceBackground = "Theme.WorkspaceBackground";
        public const string TabBarBackground = "Theme.TabBarBackground";
        public const string ControlBackground = "Theme.ControlBackground";
        public const string ControlHover = "Theme.ControlHover";
        public const string ControlPressed = "Theme.ControlPressed";
        public const string Text = "Theme.Text";
        public const string MutedText = "Theme.MutedText";
        public const string SubtleText = "Theme.SubtleText";
        public const string Border = "Theme.Border";
        public const string Splitter = "Theme.Splitter";
        public const string TileBackground = "Theme.TileBackground";
        public const string TileImageBackground = "Theme.TileImageBackground";
        public const string SelectedBackground = "Theme.SelectedBackground";
        public const string SelectedBorder = "Theme.SelectedBorder";
        public const string ActiveThumbnailBackground = "Theme.ActiveThumbnailBackground";
        public const string ActiveThumbnailBorder = "Theme.ActiveThumbnailBorder";
        public const string Accent = "Theme.Accent";
        public const string AccentText = "Theme.AccentText";
        public const string ImageBackground = "Theme.ImageBackground";
        public const string GpuDedicatedValue = "Theme.GpuDedicatedValue";
        public const string GpuSharedValue = "Theme.GpuSharedValue";
        public const string UpscaleWorkingText = "Theme.UpscaleWorkingText";
        public const string UpscaleReadyText = "Theme.UpscaleReadyText";
        public const string UpscaleBypassedText = "Theme.UpscaleBypassedText";
        public const string UpscaleErrorText = "Theme.UpscaleErrorText";
        public const string UpscaleWorkingBackground = "Theme.UpscaleWorkingBackground";
        public const string UpscaleReadyBackground = "Theme.UpscaleReadyBackground";
        public const string UpscaleBypassedBackground = "Theme.UpscaleBypassedBackground";
    }

    internal static class ThemeManager
    {
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmUseImmersiveDarkMode = 20;
        private const string ControlTemplateResources = @"
<ResourceDictionary
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Style TargetType=""{x:Type ToggleButton}"">
        <Setter Property=""Background"" Value=""{DynamicResource Theme.ControlBackground}"" />
        <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}"" />
        <Setter Property=""BorderBrush"" Value=""{DynamicResource Theme.Border}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ToggleButton}"">
                    <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                            BorderThickness=""1"" CornerRadius=""2"">
                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property=""IsChecked"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.SelectedBackground}"" /></Trigger>
            <Trigger Property=""IsMouseOver"" Value=""True""><Setter Property=""BorderBrush"" Value=""{DynamicResource Theme.Accent}"" /></Trigger>
            <Trigger Property=""IsEnabled"" Value=""False""><Setter Property=""Opacity"" Value=""0.45"" /></Trigger>
        </Style.Triggers>
    </Style>
    <Style TargetType=""{x:Type ComboBox}"">
        <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}"" />
        <Setter Property=""Background"" Value=""{DynamicResource Theme.ControlBackground}"" />
        <Setter Property=""BorderBrush"" Value=""{DynamicResource Theme.Border}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ComboBox}"">
                    <Grid>
                        <ToggleButton Focusable=""False"" ClickMode=""Press""
                                      Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                                      IsChecked=""{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType=""{x:Type ToggleButton}"">
                                    <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""1"" CornerRadius=""2"">
                                        <TextBlock Text=""&#x25BE;"" HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,8,0""
                                                   Foreground=""{DynamicResource Theme.Text}"" />
                                    </Border>
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <ContentPresenter Content=""{TemplateBinding SelectionBoxItem}"" ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""
                                          Margin=""8,0,28,0"" VerticalAlignment=""Center"" IsHitTestVisible=""False"" />
                        <Popup x:Name=""PART_Popup"" Placement=""Bottom"" IsOpen=""{TemplateBinding IsDropDownOpen}"" AllowsTransparency=""True"" Focusable=""False"">
                            <Border Background=""{DynamicResource Theme.ControlBackground}"" BorderBrush=""{DynamicResource Theme.Border}"" BorderThickness=""1""
                                    MinWidth=""{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"">
                                <ScrollViewer MaxHeight=""280"" CanContentScroll=""True"">
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation=""Contained"" />
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType=""{x:Type ComboBoxItem}"">
        <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}"" />
        <Setter Property=""Background"" Value=""{DynamicResource Theme.ControlBackground}"" />
        <Setter Property=""Padding"" Value=""8,6"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ComboBoxItem}"">
                    <Border Background=""{TemplateBinding Background}"" Padding=""{TemplateBinding Padding}""><ContentPresenter /></Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property=""IsHighlighted"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.SelectedBackground}"" /></Trigger>
        </Style.Triggers>
    </Style>
    <Style TargetType=""{x:Type ScrollBar}"">
        <Setter Property=""Background"" Value=""{DynamicResource Theme.ControlBackground}"" />
        <Setter Property=""Foreground"" Value=""{DynamicResource Theme.SubtleText}"" />
        <Setter Property=""Width"" Value=""14"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ScrollBar}"">
                    <Grid Background=""{TemplateBinding Background}"">
                        <Track x:Name=""PART_Track""
                               IsDirectionReversed=""True""
                               Maximum=""{TemplateBinding Maximum}""
                               Minimum=""{TemplateBinding Minimum}""
                               Value=""{TemplateBinding Value}""
                               ViewportSize=""{TemplateBinding ViewportSize}"">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command=""{x:Static ScrollBar.PageUpCommand}""
                                              Focusable=""False"" Opacity=""0"" />
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Background=""{TemplateBinding Foreground}"" MinHeight=""24"">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType=""{x:Type Thumb}"">
                                            <Border Background=""{TemplateBinding Background}""
                                                    CornerRadius=""3"" Margin=""3,2"" />
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Command=""{x:Static ScrollBar.PageDownCommand}""
                                              Focusable=""False"" Opacity=""0"" />
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property=""Orientation"" Value=""Horizontal"">
                <Setter Property=""Width"" Value=""Auto"" />
                <Setter Property=""Height"" Value=""14"" />
                <Setter Property=""Template"">
                    <Setter.Value>
                        <ControlTemplate TargetType=""{x:Type ScrollBar}"">
                            <Grid Background=""{TemplateBinding Background}"">
                                <Track x:Name=""PART_Track""
                                       IsDirectionReversed=""False""
                                       Maximum=""{TemplateBinding Maximum}""
                                       Minimum=""{TemplateBinding Minimum}""
                                       Value=""{TemplateBinding Value}""
                                       ViewportSize=""{TemplateBinding ViewportSize}"">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command=""{x:Static ScrollBar.PageLeftCommand}""
                                                      Focusable=""False"" Opacity=""0"" />
                                    </Track.DecreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Background=""{TemplateBinding Foreground}"" MinWidth=""24"">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType=""{x:Type Thumb}"">
                                                    <Border Background=""{TemplateBinding Background}""
                                                            CornerRadius=""3"" Margin=""2,3"" />
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command=""{x:Static ScrollBar.PageRightCommand}""
                                                      Focusable=""False"" Opacity=""0"" />
                                    </Track.IncreaseRepeatButton>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>
    <Style TargetType=""{x:Type Slider}"">
        <Setter Property=""Background"" Value=""{DynamicResource Theme.Border}"" />
        <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Accent}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type Slider}"">
                    <Grid Background=""Transparent"" Height=""22"">
                        <Border Height=""4"" CornerRadius=""2""
                                Background=""{TemplateBinding Background}""
                                VerticalAlignment=""Center"" />
                        <Track x:Name=""PART_Track"">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command=""{x:Static Slider.DecreaseLarge}""
                                              Focusable=""False"" Opacity=""0"" />
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Width=""12"" Height=""18""
                                       Background=""{TemplateBinding Foreground}"">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType=""{x:Type Thumb}"">
                                            <Border Background=""{TemplateBinding Background}""
                                                    BorderBrush=""{DynamicResource Theme.SelectedBorder}""
                                                    BorderThickness=""1"" CornerRadius=""3"" />
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Command=""{x:Static Slider.IncreaseLarge}""
                                              Focusable=""False"" Opacity=""0"" />
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>";
        private static bool _initialized;

        public static bool IsDarkTheme { get; private set; }

        public static void Initialize(Application application, bool darkTheme)
        {
            if (!_initialized)
            {
                InstallControlStyles(application.Resources);
                _initialized = true;
            }

            SetDarkTheme(darkTheme);
        }

        public static void PrepareWindow(Window window)
        {
            Bind(window, Control.BackgroundProperty, ThemeKeys.WindowBackground);
            Bind(window, Control.ForegroundProperty, ThemeKeys.Text);
            window.SourceInitialized += delegate { ApplyDarkTitleBar(window); };
        }

        public static void SetDarkTheme(bool darkTheme)
        {
            IsDarkTheme = darkTheme;
            ResourceDictionary resources = Application.Current.Resources;

            if (darkTheme)
            {
                SetPalette(resources,
                    "#181A1D", "#202328", "#1D2024", "#191C20", "#25292E",
                    "#2C3137", "#373D44", "#1D756B", "#F2F4F5", "#A9B0B7",
                    "#7F8992", "#3A4047", "#30353B", "#202328", "#15181B",
                    "#21443F", "#38B7A6", "#38B7A6", "#071412", "#0E1012");
            }
            else
            {
                SetPalette(resources,
                    "#FFFFFF", "#F6F7F8", "#F9FAFB", "#FFFFFF", "#EEF1F3",
                    "#FFFFFF", "#F0F2F4", "#D9EDE9", "#1B2127", "#4B545C",
                    "#646C74", "#D5DADE", "#DEE2E6", "#FFFFFF", "#F4F6F8",
                    "#E0F3EF", "#1F7A6F", "#1F7A6F", "#FFFFFF", "#121416");
            }

            resources[ThemeKeys.ActiveThumbnailBackground] = Brush(darkTheme ? "#2B4862" : "#D7EAFC");
            resources[ThemeKeys.GpuDedicatedValue] = Brush(darkTheme ? "#76D9EF" : "#00647A");
            resources[ThemeKeys.GpuSharedValue] = Brush(darkTheme ? "#F4C477" : "#805000");
            resources[ThemeKeys.UpscaleWorkingText] = Brush(darkTheme ? "#FFD078" : "#805000");
            resources[ThemeKeys.UpscaleReadyText] = Brush(darkTheme ? "#78E0A4" : "#17643B");
            resources[ThemeKeys.UpscaleBypassedText] = Brush(darkTheme ? "#83C7FF" : "#1D62A0");
            resources[ThemeKeys.UpscaleErrorText] = Brush(darkTheme ? "#FF9C9C" : "#B42332");
            resources[ThemeKeys.UpscaleWorkingBackground] = Brush(darkTheme ? "#514020" : "#FFF0CD");
            resources[ThemeKeys.UpscaleReadyBackground] = Brush(darkTheme ? "#155D46" : "#CEEEDB");
            resources[ThemeKeys.UpscaleBypassedBackground] = Brush(darkTheme ? "#234E71" : "#D8EAFE");
            resources[ThemeKeys.ActiveThumbnailBorder] = Brush(darkTheme ? "#74B9F5" : "#2878B7");
            string[] tabAccents = darkTheme
                ? new[] { "#55BDB2", "#6DB0F1", "#E8B950", "#DC849B", "#A3C879", "#B8A0DF" }
                : new[] { "#20796F", "#316EA6", "#946400", "#A34964", "#526F2D", "#73569D" };
            resources["Folder.Fill"] = Brush(darkTheme ? "#B49A55" : "#F0D482");
            resources["Folder.Border"] = Brush(darkTheme ? "#DDC47E" : "#A98A36");
            resources["Folder.PreviewBackground"] = Brush(darkTheme ? "#423D2F" : "#DDC477");
            string[] tabFills = darkTheme
                ? new[] { "#243A39", "#29394B", "#403923", "#412E36", "#323E29", "#383146" }
                : new[] { "#D7EBE6", "#DBE8F5", "#F6E9C5", "#F2DFE6", "#E4EED6", "#E9E1F3" };
            string[] activeFills = darkTheme
                ? new[] { "#305853", "#355475", "#61532D", "#633F4F", "#495C35", "#53446C" }
                : new[] { "#B4DAD1", "#B8D3EE", "#EBD498", "#E3B8C8", "#CDDFB3", "#D4C3E8" };
            for (int i = 0; i < tabAccents.Length; i++)
            {
                resources["Tab.Accent." + i] = Brush(tabAccents[i]);
                resources["Tab.Fill." + i] = Brush(tabFills[i]);
                resources["Tab.Active." + i] = Brush(activeFills[i]);
            }

            resources[SystemColors.WindowBrushKey] = resources[ThemeKeys.WindowBackground];
            resources[SystemColors.WindowTextBrushKey] = resources[ThemeKeys.Text];
            resources[SystemColors.ControlBrushKey] = resources[ThemeKeys.ControlBackground];
            resources[SystemColors.ControlTextBrushKey] = resources[ThemeKeys.Text];
            resources[SystemColors.HighlightBrushKey] = resources[ThemeKeys.SelectedBackground];
            resources[SystemColors.HighlightTextBrushKey] = resources[ThemeKeys.Text];
            resources[SystemColors.GrayTextBrushKey] = resources[ThemeKeys.SubtleText];
            resources[SystemColors.ActiveBorderBrushKey] = resources[ThemeKeys.Border];
            resources[SystemColors.InactiveBorderBrushKey] = resources[ThemeKeys.Border];
            resources[SystemColors.MenuBrushKey] = resources[ThemeKeys.ControlBackground];
            resources[SystemColors.MenuTextBrushKey] = resources[ThemeKeys.Text];

            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    ApplyDarkTitleBar(window);
                }
            }
        }

        public static void Bind(FrameworkElement element, DependencyProperty property, string resourceKey)
        {
            element.SetResourceReference(property, resourceKey);
        }

        public static int TabColorIndex(string path)
        {
            // A stable hash keeps tab colors the same after restarting the process.
            uint hash = 2166136261;
            foreach (char character in path.ToUpperInvariant())
                hash = unchecked((hash ^ character) * 16777619);
            return (int)(hash % 6);
        }

        private static void SetPalette(
            ResourceDictionary resources,
            string windowBackground,
            string toolbarBackground,
            string paneBackground,
            string workspaceBackground,
            string tabBarBackground,
            string controlBackground,
            string controlHover,
            string controlPressed,
            string text,
            string mutedText,
            string subtleText,
            string border,
            string splitter,
            string tileBackground,
            string tileImageBackground,
            string selectedBackground,
            string selectedBorder,
            string accent,
            string accentText,
            string imageBackground)
        {
            resources[ThemeKeys.WindowBackground] = Brush(windowBackground);
            resources[ThemeKeys.ToolbarBackground] = Brush(toolbarBackground);
            resources[ThemeKeys.PaneBackground] = Brush(paneBackground);
            resources[ThemeKeys.WorkspaceBackground] = Brush(workspaceBackground);
            resources[ThemeKeys.TabBarBackground] = Brush(tabBarBackground);
            resources[ThemeKeys.ControlBackground] = Brush(controlBackground);
            resources[ThemeKeys.ControlHover] = Brush(controlHover);
            resources[ThemeKeys.ControlPressed] = Brush(controlPressed);
            resources[ThemeKeys.Text] = Brush(text);
            resources[ThemeKeys.MutedText] = Brush(mutedText);
            resources[ThemeKeys.SubtleText] = Brush(subtleText);
            resources[ThemeKeys.Border] = Brush(border);
            resources[ThemeKeys.Splitter] = Brush(splitter);
            resources[ThemeKeys.TileBackground] = Brush(tileBackground);
            resources[ThemeKeys.TileImageBackground] = Brush(tileImageBackground);
            resources[ThemeKeys.SelectedBackground] = Brush(selectedBackground);
            resources[ThemeKeys.SelectedBorder] = Brush(selectedBorder);
            resources[ThemeKeys.Accent] = Brush(accent);
            resources[ThemeKeys.AccentText] = Brush(accentText);
            resources[ThemeKeys.ImageBackground] = Brush(imageBackground);
        }

        private static SolidColorBrush Brush(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value);
            brush.Freeze();
            return brush;
        }

        private static DynamicResourceExtension Dynamic(string key)
        {
            return new DynamicResourceExtension(key);
        }

        private static void InstallControlStyles(ResourceDictionary resources)
        {
            resources.MergedDictionaries.Add(
                (ResourceDictionary)XamlReader.Parse(ControlTemplateResources));

            var windowStyle = new Style(typeof(Window));
            windowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.WindowBackground)));
            windowStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            resources[typeof(Window)] = windowStyle;

            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.ControlBackground)));
            buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            buttonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Dynamic(ThemeKeys.Border)));
            buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            resources[typeof(Button)] = buttonStyle;

            var textBoxStyle = new Style(typeof(TextBox));
            textBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.ControlBackground)));
            textBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Dynamic(ThemeKeys.Border)));
            textBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            textBoxStyle.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Dynamic(ThemeKeys.Accent)));
            textBoxStyle.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Dynamic(ThemeKeys.Text)));
            resources[typeof(TextBox)] = textBoxStyle;

            var checkBoxStyle = new Style(typeof(CheckBox));
            checkBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            checkBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.ControlBackground)));
            resources[typeof(CheckBox)] = checkBoxStyle;

            var treeStyle = new Style(typeof(TreeView));
            treeStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.PaneBackground)));
            treeStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            treeStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Dynamic(ThemeKeys.Border)));
            resources[typeof(TreeView)] = treeStyle;

            var treeItemStyle = new Style(typeof(TreeViewItem));
            treeItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            treeItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            resources[typeof(TreeViewItem)] = treeItemStyle;

            var contextMenuStyle = new Style(typeof(ContextMenu));
            contextMenuStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.ControlBackground)));
            contextMenuStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            contextMenuStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Dynamic(ThemeKeys.Border)));
            contextMenuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            resources[typeof(ContextMenu)] = contextMenuStyle;

            var menuItemStyle = new Style(typeof(MenuItem));
            menuItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.ControlBackground)));
            menuItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            resources[typeof(MenuItem)] = menuItemStyle;

            var toolTipStyle = new Style(typeof(ToolTip));
            toolTipStyle.Setters.Add(new Setter(Control.BackgroundProperty, Dynamic(ThemeKeys.ControlBackground)));
            toolTipStyle.Setters.Add(new Setter(Control.ForegroundProperty, Dynamic(ThemeKeys.Text)));
            toolTipStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Dynamic(ThemeKeys.Border)));
            resources[typeof(ToolTip)] = toolTipStyle;
        }

        private static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                int enabled = IsDarkTheme ? 1 : 0;
                int result = DwmSetWindowAttribute(
                    handle,
                    DwmUseImmersiveDarkMode,
                    ref enabled,
                    Marshal.SizeOf(typeof(int)));
                if (result != 0)
                {
                    DwmSetWindowAttribute(
                        handle,
                        DwmUseImmersiveDarkModeBefore20H1,
                        ref enabled,
                        Marshal.SizeOf(typeof(int)));
                }
            }
            catch
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);
    }
}
