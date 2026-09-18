using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Myoken.Linux;

internal static class Program
{
    public static string? StartPath { get; private set; }
    public static bool SmokeTest { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        SmokeTest = args.Contains("--smoke-test", StringComparer.Ordinal);
        StartPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
