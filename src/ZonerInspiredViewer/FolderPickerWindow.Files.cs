using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed partial class FolderPickerWindow
    {
        private readonly FileDialog _fileDialog;
        private List<FilePickerFilter> _fileFilters;
        private TextBox _fileName;
        private ComboBox _fileType;
        internal string SelectedFile { get; private set; }
        internal int SelectedFilterIndex { get { return _fileType == null ? 1 : _fileType.SelectedIndex + 1; } }
        private FrameworkElement BuildFileControls()
        {
            _fileFilters = FilePickerFilter.Parse(_fileDialog.Filter);
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _fileName = new TextBox { Text = Path.GetFileName(_fileDialog.FileName), Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            _fileType = new ComboBox { ItemsSource = _fileFilters, SelectedIndex = Math.Max(0, Math.Min(_fileFilters.Count - 1, _fileDialog.FilterIndex - 1)), Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_fileName, "File name"); AutomationProperties.SetName(_fileType, "File type");
            panel.Children.Add(FileControlRow("File name", _fileName)); panel.Children.Add(FileControlRow("File type", _fileType));
            _fileName.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Confirm(); e.Handled = true; } };
            _fileType.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (_fileDialog is SaveFileDialog && e.RemovedItems.Count > 0)
                {
                    var previous = (FilePickerFilter)e.RemovedItems[0]; var next = _fileType.SelectedItem as FilePickerFilter;
                    if (next != null && next.Extension != null && previous.Matches(_fileName.Text)) _fileName.Text = Path.ChangeExtension(_fileName.Text, next.Extension);
                }
                if (_requestedPath != null) Navigate(_requestedPath);
            };
            return panel;
        }
        private static Grid FileControlRow(string label, FrameworkElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(control, 1); row.Children.Add(control); return row;
        }
        private void SelectFilename(string path)
        { if (_fileName != null && !Directory.Exists(path)) _fileName.Text = Path.GetFileName(path); }
        private void ActivatePath(string path)
        {
            if (Directory.Exists(path)) Navigate(path);
            else if (_fileDialog != null) { SelectFilename(path); Confirm(); }
        }
        private void ConfirmFile()
        {
            try
            {
                string text = _fileName.Text ?? "";
                if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"') text = text.Substring(1, text.Length - 2);
                if (String.IsNullOrWhiteSpace(text)) throw new IOException("Enter a filename or select a file.");
                string selected = SelectedPath();
                bool selectedFile = selected != null && File.Exists(selected) && String.Equals(Path.GetFileName(selected), text, StringComparison.Ordinal);
                if (!selectedFile && text.IndexOfAny(new[] { '\\', '/' }) >= 0) text = Environment.ExpandEnvironmentVariables(text);
                string path = selectedFile ? selected : Path.IsPathRooted(text) ? text : Path.Combine(_current, text);
                if (Directory.Exists(path)) { _fileName.Clear(); Navigate(path); return; }
                ValidateLeaf(Path.GetFileName(path)); path = Path.GetFullPath(path);
                string extension = Path.GetExtension(path);
                if (String.IsNullOrEmpty(extension) && _fileDialog.AddExtension)
                {
                    extension = ((FilePickerFilter)_fileType.SelectedItem).Extension;
                    if (String.IsNullOrEmpty(extension) && !String.IsNullOrEmpty(_fileDialog.DefaultExt)) extension = "." + _fileDialog.DefaultExt.TrimStart('.');
                    if (!String.IsNullOrEmpty(extension)) path += extension;
                }
                ValidateLeaf(Path.GetFileName(path));
                string parent = Path.GetDirectoryName(path);
                if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The destination folder is unavailable.");
                FileTransferService.CheckAncestors(parent);
                if (File.Exists(path)) FileTransferService.CheckAncestors(path);
                if (!_fileFilters.Any(f => f.Matches(Path.GetFileName(path)))) throw new IOException("Choose a filename with a supported file type.");
                if (_fileDialog.CheckFileExists && !File.Exists(path)) throw new FileNotFoundException("This file does not exist.");
                var save = _fileDialog as SaveFileDialog;
                if (save != null && File.Exists(path) && save.OverwritePrompt && new FileReplaceWindow(this, path, true).ShowDialog() != true) return;
                if (save != null && !File.Exists(path) && save.CreatePrompt && new FileReplaceWindow(this, path, false).ShowDialog() != true) return;
                SelectedFile = path; Close();
            }
            catch (Exception error) { _status.Text = error.Message; _status.ToolTip = error.Message; }
        }
        private static void ValidateLeaf(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name != name.TrimEnd(' ', '.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || Regex.IsMatch(name.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new IOException("Enter a valid name without reserved characters or trailing spaces/dots.");
        }
        private void ChooseNewFolder()
        {
            if (_current == null || _request != null) return;
            string name = TextPromptWindow.Prompt(this, "New folder", "Folder name", "New folder"); if (name != null) CreateFolder(name);
        }
        internal bool CreateFolder(string name)
        {
            try
            {
                ValidateLeaf(name); FileTransferService.CheckAncestors(_current); string path = Path.Combine(_current, name);
                if (Directory.Exists(path) || File.Exists(path)) throw new IOException("An item with that name already exists.");
                Directory.CreateDirectory(path); return Navigate(path);
            }
            catch (Exception error) { _status.Text = error.Message; _status.ToolTip = error.Message; return false; }
        }
    }
}
