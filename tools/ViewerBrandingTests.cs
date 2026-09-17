using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractBrandIcons(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private static void RunBrandingChecks()
    {
        string root = Path.Combine(Root, "branding-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "Zen-preview.png"); MakeImage(photo, 640, 420, 96);
        string executable = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ZenImageViewer", "ZenImageViewer.exe"));
        var metadata = FileVersionInfo.GetVersionInfo(executable);
        Assert(metadata.ProductName == "Zen Image Viewer" && metadata.FileDescription == "Zen Image Viewer", "Windows file details carry the new app name");
        Assert(metadata.ProductVersion == BuildInfo.Version && metadata.FileVersion == BuildInfo.Version + ".0", "renamed executable retains build version metadata");
        Assert(BuildInfo.WindowTitle == "Zen Image Viewer v" + BuildInfo.Version, "new title includes version");
        string iconPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "src", "ZonerInspiredViewer", "Assets", "AppIcon.ico"));
        var decoder = new IconBitmapDecoder(new Uri(iconPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert(decoder.Frames.Select(frame => frame.PixelWidth).SequenceEqual(new[] { 16, 24, 32, 48, 64, 128, 256 }), "all seven native icon sizes exist");
        foreach (var frame in decoder.Frames) CheckBrandPalette(frame);
        using (var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(iconPath), "AppIcon.png")))
        {
            var master = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert(master.PixelWidth >= 1024 && master.PixelHeight == master.PixelWidth, "square high-resolution master retained");
        }
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            uint extracted = ExtractBrandIcons(executable, 0, out large, out small, 1);
            Assert(extracted > 0 && extracted != UInt32.MaxValue && large != IntPtr.Zero && small != IntPtr.Zero,
                "Windows extracts both taskbar and title-bar icon resources");
            CheckBrandPalette(Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()));
            CheckBrandPalette(Imaging.CreateBitmapSourceFromHIcon(small, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()));
        }
        finally { if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero) DestroyIcon(small); }
        RenderBrandIconSizes(decoder);

        using (var services = AppServices.Create(Path.Combine(root, "LegacyProfile")))
        {
            var state = new SessionState { LastFolder = photos, ActiveTabId = photo, WindowWidth = 1100, WindowHeight = 760 };
            state.Tabs.Add(new SessionTabDto { Id = photo, Path = photo, FolderPath = photos }); services.Sessions.Save(state);
            var window = new MainWindow(services); window.Show();
            try
            {
                Wait(delegate { return Ready(window, photo); }, "renamed viewer image"); WaitScan(window);
                Assert(window.Title == BuildInfo.WindowTitle, "live window has Zen name");
                var icon = window.Icon as BitmapFrame;
                Assert(icon != null && icon.PixelWidth == 256 && icon.Decoder.Frames.Count == 7, "WPF keeps the full multi-size icon decoder");
                CheckBrandPalette(icon);
                Invoke(window, "ShowHelp"); var help = Field<HelpWindow>(window, "_helpWindow"); Pump();
                Assert(help.Title == "Help - " + BuildInfo.WindowTitle && help.Icon == window.Icon, "Help uses matching name and icon");
                var header = ((DockPanel)help.Content).Children.OfType<TextBlock>().First();
                Assert(header.Text.StartsWith("Zen Image Viewer"), "Help heading renamed");
                Render((FrameworkElement)help.Content, "zen-help-dark.png");
                ThemeManager.SetDarkTheme(false); Pump(); Render((FrameworkElement)help.Content, "zen-help-light.png");
                ThemeManager.SetDarkTheme(true); help.Close();
                Assert(Capture(window).Tabs.Any(tab => tab.Path == photo), "saved image tab survives new display identity");
            }
            finally { window.Close(); Pump(); }
        }
        Console.WriteLine("PASS: Zen window/Help/Windows metadata, seven transparent teal/ivory icon sizes, native small/large icon extraction and dark/light previews.");
    }

    private static void CheckBrandPalette(BitmapSource image)
    {
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int opaque = 0, light = 0, teal = 0, warm = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 240) continue;
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2]; opaque++;
            if (r > 210 && g > 210 && b > 200) light++;
            if (g > r + 30 && b > r + 25) teal++;
            if (r > g + 35 && r > b + 35) warm++;
        }
        Assert(pixels[3] == 0 && pixels[pixels.Length - 1] == 0, "transparent icon corners at " + bitmap.PixelWidth);
        Assert(opaque > bitmap.PixelWidth * bitmap.PixelHeight / 3 && light > opaque / 4 && teal > opaque / 4,
            "bold visible light/teal regions at " + bitmap.PixelWidth);
        Assert(warm == 0, "no red/orange color-wheel regions at " + bitmap.PixelWidth);
    }

    private static void RenderBrandIconSizes(IconBitmapDecoder decoder)
    {
        var board = new StackPanel { Width = 820, Background = Brushes.White };
        foreach (bool dark in new[] { true, false })
        {
            var section = new StackPanel { Margin = new Thickness(20) };
            var text = dark ? Brushes.WhiteSmoke : Brushes.Black;
            section.Children.Add(new TextBlock { Text = "Zen Image Viewer", FontSize = 22, Foreground = text, Margin = new Thickness(0, 0, 0, 12) });
            var sizes = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var frame in decoder.Frames)
            {
                var cell = new StackPanel { Width = frame.PixelWidth + 18 };
                var holder = new Grid { Height = 256 };
                holder.Children.Add(new Image { Source = frame, Width = frame.PixelWidth, Height = frame.PixelHeight, Stretch = Stretch.None });
                cell.Children.Add(holder);
                cell.Children.Add(new TextBlock { Text = frame.PixelWidth.ToString(), Foreground = text, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
                sizes.Children.Add(cell);
            }
            section.Children.Add(sizes);
            board.Children.Add(new Border { Background = dark ? new SolidColorBrush(Color.FromRgb(28, 31, 34)) : Brushes.White, Child = section });
        }
        board.Measure(new Size(820, Double.PositiveInfinity)); board.Arrange(new Rect(new Point(), board.DesiredSize)); board.UpdateLayout();
        Render(board, "zen-icon-sizes.png");
    }
}
