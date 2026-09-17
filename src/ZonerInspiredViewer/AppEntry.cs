using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal static class AppEntry
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ThreadPool.SetMinThreads(Math.Min(32, Math.Max(8, Environment.ProcessorCount * 2)), 8);
            RenderOptions.ProcessRenderMode = RenderMode.Default;

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            ThemeManager.Initialize(app, true);

            try
            {
                using (var services = AppServices.Create())
                    app.Run(new MainWindow(services));
            }
            catch (Exception ex)
            {
                MessageBox.Show("The viewer could not start or continue.\n\n" + ex.Message,
                    BuildInfo.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.ExitCode = 1;
            }
        }
    }
}
