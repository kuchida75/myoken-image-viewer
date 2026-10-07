using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Myoken.Linux;

internal static class FileOperationDialogs
{
    public static async Task<string?> PromptRenameAsync(Window owner, string path)
    {
        var current = Path.GetFileName(path);
        var box = new TextBox { Text = current, MinWidth = 430 };
        var ok = new Button { Content = "Rename", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "New filename:", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(box); panel.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "Rename image", Width = 500, Height = 170, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel
        };
        ok.Click += (_, _) => dialog.Close(box.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(null); }
            else if (e.Key == Key.Enter) { e.Handled = true; dialog.Close(box.Text); }
        };
        dialog.Opened += (_, _) =>
        {
            box.Focus();
            var stem = Path.GetFileNameWithoutExtension(current);
            box.Select(0, stem.Length);
        };
        return await dialog.ShowDialog<string?>(owner);
    }

    public static async Task<bool> ConfirmPermanentDeleteAsync(Window owner, string path)
    {
        var yes = new Button { Content = "Delete permanently", MinWidth = 130 };
        var no = new Button { Content = "Cancel", MinWidth = 90 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(no); buttons.Children.Add(yes);
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Permanently delete this image?\n\n" + path
                + "\n\nThis bypasses the desktop Trash and cannot be undone by Myoken.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "Delete image permanently", Width = 560, Height = 240, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel
        };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(false); }
        };
        return await dialog.ShowDialog<bool>(owner);
    }
}
