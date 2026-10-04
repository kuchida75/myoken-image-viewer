using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace Myoken.Linux;

// Opt-in integration checks run in real X11 windows under Xvfb with generated fixtures.
// The entry point refuses these modes unless an isolated test profile is supplied.
internal sealed partial class MainWindow
{
    private static void CheckUi(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
    private static async Task WaitUiAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException(description);
            await Task.Delay(30);
        }
    }
    private async Task SelectForTestAsync(DocumentTab tab)
    {
        _starting = true; _tabs.SelectedItem = tab; _starting = false; _dirty = true;
        await ShowSelectedImageAsync();
    }
    private void CaptureForTest(string name)
    {
        var output = Environment.GetEnvironmentVariable("MYOKEN_TEST_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
        bitmap.Render(this); bitmap.Save(Path.Combine(output, name + ".png"));
    }

    private async Task RunUiChecksAsync(string mode)
    {
        CheckUi(_files.Length >= 3, "fixture folder enumerated");
        var originals = _files;
        var folder = _folder;
        if (mode == "--restore-test")
        {
            CheckUi(StringComparer.Ordinal.Equals(folder, Program.ExpectedTestFolder), "folder restored in second process without a path override");
            CheckUi(_tabItems.Skip(1).Select(t => t.Path).SequenceEqual(new[] { originals[0], originals[2] }),
                "second process restores exact tab order and excludes closed tab");
            CheckUi(_tabs.SelectedItem?.Path == originals[2], "second process restores active image tab");
            CheckUi(_tabs.SelectedItem?.Content is ImageViewer { HasImage: true }, "restored active image decoded");
            await WaitUiAsync(() => _tabs.SelectedHeaderVisible, "restored tab header was not revealed");
            CheckUi(_tabs.HeadersShareRow && _tabs.ContentTop == 44, "restored tabs retain the fixed single-row strip");
            await SelectForTestAsync(_browser);
            CheckUi(_status.Text == $"{originals.Length:N0} images in {folder}", "returning to Browser restores folder/count status");
            Console.WriteLine("PASS: cross-process UI session regression");
            return;
        }

        await SelectForTestAsync(_browser);
        await WaitUiAsync(() => _thumbnails.LoadedCount > 0, "initial thumbnails did not load");
        await _thumbnails.WaitForLoadsAsync();
        CheckUi(_thumbnails.RealizedCount > 0 && _thumbnails.RealizedCount < 100, "initial viewport realises a bounded set of controls");
        CaptureForTest("browser-initial");
        if (mode == "--browser-test")
        {
            _thumbnails.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.End });
            await WaitUiAsync(() => _thumbnails.EndRealized == originals.Length && _thumbnails.FirstRealized > 0,
                "End key did not reach the last image");
            await _thumbnails.WaitForLoadsAsync();
            CheckUi(_thumbnails.LoadedCount > 0, "continuous scrolling reaches and decodes final images");
            var oldColumns = _thumbnails.Columns;
            Width = 860;
            await WaitUiAsync(() => _thumbnails.Columns < oldColumns, "narrow window did not reflow columns");
            CheckUi(_thumbnails.RealizedCount < 100, "resize preserves bounded realisation");
            Width = 1200;
            await WaitUiAsync(() => _thumbnails.Columns == oldColumns, "wide window did not reflow columns");

            var many = Enumerable.Range(0, 100_000).Select(i => originals[i % originals.Length]).ToArray();
            _thumbnails.SetFiles(many);
            await WaitUiAsync(() => _thumbnails.LoadedCount > 0, "synthetic viewport failed to load");
            foreach (var index in new[] { 50_000, 99_999, 0, 99_999 })
            {
                _thumbnails.ScrollToIndex(index);
                await WaitUiAsync(() => _thumbnails.FirstRealized <= index && _thumbnails.EndRealized > index, "synthetic scroll range incorrect");
                await _thumbnails.WaitForLoadsAsync();
                CheckUi(_thumbnails.RealizedCount < 100, $"100k synthetic entries at index {index}: {_thumbnails.RealizedCount} live tiles");
            }
            _thumbnails.SetFiles(new[] { Path.Combine(folder, "..", "corrupt.png"), Path.Combine(folder, "missing.png") });
            await WaitUiAsync(() => _thumbnails.FailedCount == 2, "corrupt/missing image placeholders did not appear");
            CheckUi(_thumbnails.LoadedCount == 0, "cancelled old decodes cannot populate replacement tiles");
            _thumbnails.SetFiles(originals);
            await WaitUiAsync(() => _thumbnails.LoadedCount > 0, "browser did not recover after bad images");

            var nested = Path.Combine(folder, "nested folder", "日本");
            await NavigateAsync(nested);
            CheckUi(_folders.SelectedPath == nested && _folders.IsExpanded(folder), "tree reveals child and retains expanded ancestors");
            CheckUi(_folders.SelectPath(folder), "tree parent remains selectable");
            await WaitUiAsync(() => _folder == folder && _files.Length == originals.Length, "parent-tree selection did not navigate");
            var cancelled = NavigateAsync(nested);
            await NavigateAsync(folder); await cancelled;
            CheckUi(_folder == folder, "rapid navigation retains newest folder");
            await WaitUiAsync(() => _thumbnails.LoadedCount > 0, "thumbnails did not recover after navigation");
            await _thumbnails.WaitForLoadsAsync();
            CaptureForTest("browser-tree");
            await RunTabStripChecksAsync(folder);
            await RunViewerChecksAsync(folder);
            await SelectForTestAsync(_browser);
        }

        _thumbnails.ScrollToIndex(0);
        await WaitUiAsync(() => _thumbnails.FirstRealized == 0 && _thumbnails.LoadedCount > 0, "first thumbnail unavailable");
        _thumbnails.ActivateForTest(0);
        await WaitUiAsync(() => _tabs.SelectedItem?.Content is ImageViewer { HasImage: true }, "thumbnail click did not open image");
        CheckUi(_thumbnails.RealizedCount == 0, "hidden browser releases thumbnail bitmaps and controls");
        var first = AddImageTab(originals[0]);
        var second = AddImageTab(originals[1]);
        var third = AddImageTab(originals[2]);
        CheckUi(ReferenceEquals(first, AddImageTab(originals[0])), "one tab per exact Linux path");
        _tabs.CloseButtonFor(second).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        CheckUi(!_tabItems.Contains(second), "close-button route removes only the requested tab");
        await SelectForTestAsync(third);
        CheckUi(third.Content is ImageViewer { HasImage: true }, "selected image preview decoded");
        CaptureForTest("image-preview");
        SaveSession();
        CheckUi(_sessions?.CanWrite == true, "isolated session writable");
        var saved = _sessions!.Load();
        CheckUi(saved.ImagePaths.SequenceEqual(new[] { originals[0], originals[2] }) && saved.ActiveImagePath == originals[2],
            "session saves remaining tab order and active tab without schema changes");
        Console.WriteLine("PASS: Linux browser/tree/thumbnail/tab regression; ready for fresh-process restoration check");
    }
}
