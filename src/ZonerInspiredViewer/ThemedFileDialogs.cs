using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed class FilePickerFilter
    {
        internal string Label;
        internal string[] Patterns;
        private Regex[] _matchers;
        internal bool Matches(string name)
        {
            return _matchers.Any(pattern => pattern.IsMatch(name));
        }
        internal string Extension { get { return Patterns.Where(p => p.StartsWith("*.") && p.IndexOfAny(new[] { '*', '?' }, 2) < 0).Select(p => p.Substring(1)).FirstOrDefault(); } }
        public override string ToString() { return Label; }
        internal static List<FilePickerFilter> Parse(string filter)
        {
            if (String.IsNullOrEmpty(filter)) filter = "All files|*.*";
            var parts = filter.Split('|'); if (parts.Length % 2 != 0) throw new ArgumentException("Invalid file type filter.");
            var result = new List<FilePickerFilter>();
            for (int i = 0; i < parts.Length; i += 2)
            {
                var patterns = parts[i + 1].Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                if (patterns.Length == 0) throw new ArgumentException("Empty file type filter.");
                result.Add(new FilePickerFilter { Label = parts[i], Patterns = patterns, _matchers = patterns.Select(p => new Regex("^" + Regex.Escape(p == "*.*" ? "*" : p).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray() });
            }
            return result;
        }
    }
    internal static class ThemedFileDialogs
    {
        private static string _lastDirectory;
        internal static bool Show(FileDialog options, Window owner, ThumbnailCacheService thumbnails = null)
        {
            var open = options as OpenFileDialog;
            if (open != null && open.Multiselect) throw new NotSupportedException("This file chooser selects one file at a time.");
            string initial = options.InitialDirectory;
            if (!Directory.Exists(initial) && Path.IsPathRooted(options.FileName)) initial = Path.GetDirectoryName(options.FileName);
            if (!Directory.Exists(initial)) initial = _lastDirectory;
            if (!Directory.Exists(initial)) initial = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var picker = new FolderPickerWindow(initial, thumbnails, options) { Owner = owner, Icon = owner.Icon };
            picker.ShowDialog();
            if (picker.SelectedFile == null) return false;
            options.FileName = picker.SelectedFile; options.FilterIndex = picker.SelectedFilterIndex; _lastDirectory = Path.GetDirectoryName(picker.SelectedFile); return true;
        }
        internal static string Folder(Window owner, string initial, string title, ThumbnailCacheService thumbnails = null)
        {
            var picker = new FolderPickerWindow(initial, thumbnails) { Owner = owner, Icon = owner.Icon, Title = title };
            picker.ShowDialog(); return picker.SelectedFolder;
        }
    }
    internal sealed class FileReplaceWindow : Window
    {
        internal FileReplaceWindow(Window owner, string path, bool replace)
        {
            Owner = owner; Icon = owner.Icon; Title = replace ? "Replace existing file?" : "Create new file?";
            Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ThemeManager.PrepareWindow(this); var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
            panel.Children.Add(new TextBlock { Text = replace ? "A file with this name already exists." : "This file does not exist yet.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) });
            var location = new TextBlock { Text = Path.GetDirectoryName(path), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path };
            ThemeManager.Bind(location, TextBlock.ForegroundProperty, ThemeKeys.MutedText); panel.Children.Add(location);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; panel.Children.Add(actions);
            actions.Children.Add(CommandPresentation.Button("\uE74E", replace ? "Replace" : "Create", null, replace ? "Allow this save to replace the existing file." : "Use this new filename.", Accept, true));
            actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, IsDefault = true, MinWidth = 80, Height = 30 });
        }
        internal void Accept() { DialogResult = true; }
    }
}
