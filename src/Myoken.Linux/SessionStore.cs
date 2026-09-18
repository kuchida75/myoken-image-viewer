using System.Text.Json;
using Myoken.Core;

namespace Myoken.Linux;

internal sealed class SessionStore : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly FileStream? _lock;
    public bool CanWrite { get; private set; }
    public string? Warning { get; private set; }

    public SessionStore(string? directory = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var stateHome = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg)
            ? xdg : Path.Combine(home, ".local", "state");
        _directory = directory ?? Path.Combine(stateHome, "myoken-linux");
        _path = Path.Combine(_directory, "session.json");
        Directory.CreateDirectory(_directory);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            _lock = new FileStream(Path.Combine(_directory, "session.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            CanWrite = true;
        }
        catch (IOException)
        {
            Warning = "Session is read-only: another instance may be running.";
        }
        catch (UnauthorizedAccessException)
        {
            Warning = "Session is read-only: permission denied.";
        }
    }

    public SessionState Load()
    {
        if (!File.Exists(_path)) return new SessionState();
        try
        {
            if (new FileInfo(_path).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Session exceeds the 4 MiB safety limit.");
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Empty session document.");
            if (state.SchemaVersion != 1)
                throw new InvalidDataException("Unsupported session schema; original preserved.");
            state.ImagePaths ??= new List<string>();
            state.FolderPath ??= string.Empty;
            return state;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            CanWrite = false;
            Warning = "Session not loaded; original preserved: " + ex.Message;
            return new SessionState();
        }
    }

    public void Save(SessionState state)
    {
        if (!CanWrite) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true });
        if (json.Length > 4 * 1024 * 1024)
            throw new IOException("Session exceeds 4 MiB; existing session preserved.");
        var temp = Path.Combine(_directory, "session." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Dispose() => _lock?.Dispose();
}
