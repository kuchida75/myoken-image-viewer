using System.Diagnostics;

namespace Myoken.Linux;

internal static class DesktopTrash
{
    // GIO implements the desktop Trash policy, including mount-specific trash.
    // Never fall back to File.Delete: unsupported/unavailable Trash is an error.
    internal static async Task MoveAsync(string path, string executable = "gio")
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("trash");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(Path.GetFullPath(path));
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Could not start GNOME Trash service.");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            await output;
            var detail = await error;
            if (process.ExitCode != 0)
                throw new IOException("Move to Trash failed; no permanent deletion was attempted. " + detail.Trim());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException("GNOME Trash requires gio (Ubuntu libglib2.0-bin). No permanent deletion was attempted.", ex);
        }
    }
}
