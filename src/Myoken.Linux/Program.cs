using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Myoken.Linux;

internal static class Program
{
    public static string? StartPath { get; private set; }
    public static string? TestMode { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        TestMode = args.FirstOrDefault(a => a is "--smoke-test" or "--browser-test" or "--restore-test");
        StartPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (TestMode != null)
        {
            // Never run automated UI/session checks against the user's normal profile.
            var root = Environment.GetEnvironmentVariable("MYOKEN_TEST_ROOT");
            var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) ||
                string.IsNullOrWhiteSpace(state) || !Path.IsPathFullyQualified(state) ||
                !DirectoryCatalog.Contains(DirectoryCatalog.Normalize(root), DirectoryCatalog.Normalize(state)) ||
                string.IsNullOrWhiteSpace(StartPath) || !Path.IsPathFullyQualified(StartPath) ||
                !DirectoryCatalog.Contains(DirectoryCatalog.Normalize(root), DirectoryCatalog.Normalize(StartPath)))
                throw new InvalidOperationException("UI tests require an isolated MYOKEN_TEST_ROOT containing XDG_STATE_HOME and the fixture folder. Use bash linux/smoke.sh.");
        }
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
