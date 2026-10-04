using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Myoken.Linux;

// Lazily enumerated directory tree. Existing nodes survive ordinary folder navigation.
// This is not a virtualised tree: a very large expanded sibling set is still future work.
internal sealed class FolderTree : UserControl, IDisposable
{
    private sealed class Node
    {
        public required string Path { get; init; }
        public required TreeViewItem Item { get; init; }
        public bool IsLink { get; init; }
        public bool Loaded { get; set; }
        public Task? Loading { get; set; }
        public List<Node> Children { get; } = new();
    }

    private readonly TreeView _tree = new() { AutoScrollToSelectedItem = true };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Node _home, _filesystem;
    private bool _selecting, _disposed;
    public event Action<string>? FolderSelected;
    internal string? SelectedPath => (_tree.SelectedItem as TreeViewItem)?.Tag is Node n ? n.Path : null;

    public FolderTree()
    {
        var root = new DockPanel();
        var heading = new TextBlock { Text = "Folders", Margin = new Thickness(10, 8), FontWeight = FontWeight.SemiBold };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        root.Children.Add(_tree); Content = root;
        _home = CreateNode(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Home", false);
        _filesystem = CreateNode("/", "File system", false);
        _tree.Items.Add(_home.Item); _tree.Items.Add(_filesystem.Item);
        _tree.SelectionChanged += (_, _) =>
        {
            if (!_selecting && !_disposed && SelectedPath is string path) FolderSelected?.Invoke(path);
        };
    }

    private Node CreateNode(string path, string label, bool isLink)
    {
        var item = new TreeViewItem
        {
            Header = new TextBlock { Text = label + (isLink ? " ↗" : ""), TextTrimming = TextTrimming.CharacterEllipsis }
        };
        ToolTip.SetTip(item, path + (isLink ? " (symbolic link; select to browse)" : ""));
        var node = new Node { Path = DirectoryCatalog.Normalize(path), Item = item, IsLink = isLink };
        item.Tag = node;
        if (!isLink) item.Items.Add(Placeholder("Expand to load…"));
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty && item.IsExpanded && !_disposed)
                _ = EnsureLoadedAsync(node); // LoadAsync handles errors and cancellation.
        };
        return node;
    }

    private static TreeViewItem Placeholder(string message) => new() { Header = message, IsEnabled = false };

    private Task EnsureLoadedAsync(Node node)
    {
        if (node.Loaded || node.IsLink || _disposed) return Task.CompletedTask;
        return node.Loading ??= LoadAsync(node);
    }

    private async Task LoadAsync(Node node)
    {
        try
        {
            var children = await DirectoryCatalog.ReadAsync(node.Path, _lifetime.Token);
            if (_disposed) return;
            node.Item.Items.Clear(); node.Children.Clear();
            foreach (var entry in children)
            {
                var child = CreateNode(entry.Path, System.IO.Path.GetFileName(entry.Path), entry.IsLink);
                node.Children.Add(child); node.Item.Items.Add(child.Item);
            }
            node.Loaded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                node.Item.Items.Clear();
                node.Item.Items.Add(Placeholder("Unavailable — use Refresh to retry"));
                ToolTip.SetTip(node.Item, node.Path + "\n" + ex.Message);
            }
        }
    }

    public async Task RevealAsync(string path, CancellationToken token)
    {
        path = DirectoryCatalog.Normalize(path);
        var node = DirectoryCatalog.Contains(_home.Path, path) ? _home : _filesystem;
        var relative = System.IO.Path.GetRelativePath(node.Path, path);
        var segments = relative == "." ? Array.Empty<string>() : relative.Split(System.IO.Path.DirectorySeparatorChar);
        foreach (var segment in segments)
        {
            token.ThrowIfCancellationRequested();
            node.Item.IsExpanded = true;
            await EnsureLoadedAsync(node).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            var next = node.Children.FirstOrDefault(n => StringComparer.Ordinal.Equals(System.IO.Path.GetFileName(n.Path), segment));
            if (next == null) break; // Links are deliberately leaves; never recursively follow cycles.
            node = next;
        }
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        _selecting = true;
        try { _tree.SelectedItem = node.Item; }
        finally { _selecting = false; }
        if (!node.IsLink)
        {
            node.Item.IsExpanded = true;
            await EnsureLoadedAsync(node).WaitAsync(token);
        }
    }

    public async Task RefreshSelectedAsync()
    {
        if (_disposed || (_tree.SelectedItem as TreeViewItem)?.Tag is not Node node || node.IsLink) return;
        if (node.Loading != null) await node.Loading;
        if (_disposed) return;
        node.Loaded = false; node.Loading = null;
        await EnsureLoadedAsync(node);
    }

    // Used by the isolated UI regression to verify retained ancestors and selection routing.
    internal bool IsExpanded(string path) => Find(_home, path)?.Item.IsExpanded == true || Find(_filesystem, path)?.Item.IsExpanded == true;
    internal bool SelectPath(string path)
    {
        var node = Find(_home, path) ?? Find(_filesystem, path);
        if (node == null) return false;
        _tree.SelectedItem = node.Item;
        return true;
    }
    private static Node? Find(Node node, string path)
    {
        if (StringComparer.Ordinal.Equals(node.Path, path)) return node;
        foreach (var child in node.Children)
            if (DirectoryCatalog.Contains(child.Path, path) && Find(child, path) is Node found) return found;
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
    }
}
