using System.Text;
using Avalonia;
using Avalonia.Controls;
using Myoken.Core;

namespace Myoken.Linux;

internal sealed class ImageMetadataPanel : Border, IDisposable
{
    private readonly string _path;
    private readonly TextBlock _text = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        FontSize = 12
    };
    private CancellationTokenSource? _loadCts;
    private Task _loadTask = Task.CompletedTask;
    private bool _started, _disposed;
    private int _loadGeneration;
    private ImageMetadataSnapshot? _metadata;
    private string? _error;
    private int _displayWidth, _displayHeight, _encodedWidth, _encodedHeight;
    private ImageOrientation _orientation = ImageOrientation.TopLeft;

    public ImageMetadataPanel(string path)
    {
        _path = path;
        Width = 340;
        Padding = new Thickness(12);
        BorderThickness = new Thickness(1, 0, 0, 0);
        BorderBrush = Avalonia.Media.Brushes.DimGray;
        IsVisible = false;
        Child = new ScrollViewer
        {
            Content = _text,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        RenderText();
    }

    internal string Text => _text.Text ?? string.Empty;
    internal Task LoadTask => _loadTask;

    public void SetDecodeInfo(ViewerImage image)
    {
        _displayWidth = image.Width;
        _displayHeight = image.Height;
        _encodedWidth = image.EncodedWidth;
        _encodedHeight = image.EncodedHeight;
        _orientation = image.Orientation;
        RenderText();
    }

    public void Toggle()
    {
        if (_disposed) return;
        IsVisible = !IsVisible;
        if (IsVisible) EnsureLoaded();
    }

    public void Invalidate()
    {
        if (_disposed) return;
        _loadGeneration++;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _loadTask = Task.CompletedTask;
        _started = false;
        _metadata = null;
        _error = null;
        RenderText();
        if (IsVisible) EnsureLoaded();
    }

    private void EnsureLoaded()
    {
        if (_started || _disposed) return;
        _started = true;
        _error = null;
        _loadCts = new CancellationTokenSource();
        var generation = _loadGeneration;
        _text.Text = BuildText() + "\n\nReading metadata…";
        _loadTask = LoadAsync(generation, _loadCts.Token);
    }

    private async Task LoadAsync(int generation, CancellationToken token)
    {
        try
        {
            var metadata = await Task.Run(() => PortableImageMetadataReader.Read(_path), token);
            if (token.IsCancellationRequested || _disposed || generation != _loadGeneration) return;
            _metadata = metadata;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_disposed && generation == _loadGeneration)
                _error = ex.Message;
        }
        if (!_disposed && generation == _loadGeneration) RenderText();
    }

    private void RenderText() => _text.Text = BuildText();

    private string BuildText()
    {
        var sb = new StringBuilder();
        var file = new FileInfo(_path);
        file.Refresh();
        sb.AppendLine("File");
        sb.AppendLine("  " + Path.GetFileName(_path));
        sb.AppendLine("  " + _path);
        if (file.Exists)
        {
            sb.AppendLine("  Size: " + FormatBytes(file.Length));
            sb.AppendLine("  Modified: " + file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (_displayWidth > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Image");
            sb.AppendLine($"  Display: {_displayWidth} × {_displayHeight}");
            sb.AppendLine($"  Encoded: {_encodedWidth} × {_encodedHeight}");
            sb.AppendLine("  Orientation: " + ImageOrientationInfo.DisplayName(_orientation));
        }

        if (_metadata != null)
        {
            sb.AppendLine();
            sb.AppendLine("Camera / EXIF");
            Append(sb, "Camera", Join(_metadata.Make, _metadata.Model));
            Append(sb, "Taken", _metadata.DateTaken);
            Append(sb, "Exposure", _metadata.ExposureTime);
            Append(sb, "Aperture", _metadata.FNumber);
            Append(sb, "ISO", _metadata.Iso);
            Append(sb, "Focal length", _metadata.FocalLength);
            Append(sb, "Software", _metadata.Software);
            Append(sb, "Color space", _metadata.ColorSpace);
            Append(sb, "Description", _metadata.Description);
            if (_metadata.Orientation.HasValue && _metadata.Orientation.Value != _orientation)
                Append(sb, "Metadata orientation", ImageOrientationInfo.DisplayName(_metadata.Orientation.Value));

            if (_metadata.Tags.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Metadata details");
                foreach (var tag in _metadata.Tags)
                    sb.AppendLine($"  {tag.DirectoryName} · {tag.Name}: {tag.Description}");
            }
        }

        if (!string.IsNullOrEmpty(_error))
        {
            sb.AppendLine();
            sb.AppendLine("Metadata: " + _error);
        }
        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"  {label}: {value}");
    }

    private static string? Join(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return a + " " + b;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB" };
        double value = bytes; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadGeneration++;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
