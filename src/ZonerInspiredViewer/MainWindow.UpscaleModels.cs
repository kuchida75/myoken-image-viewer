using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private string _upscaleModelId = UpscaleModels.Basic;
        private SupirRuntimeConfig _supirConfig;
        private Button _upscaleModelButton;
        private ComboBox _configureUpscaleModel;
        private bool _syncUpscaleModel;

        private FrameworkElement BuildUpscaleControl()
        {
            _supirConfig = SupirRuntimeConfig.Load(_services.AppDataRoot);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
            _upscaleButton = BuildUpscaleButton(true); _upscaleButton.Margin = new Thickness(0);
            UpscalePresentation.PrepareButton(_upscaleButton);
            _upscaleButton.BorderThickness = new Thickness(1, 1, 0, 1);
            group.Children.Add(_upscaleButton);
            _upscaleModelButton = CommandPresentation.Button("\uE70D", "AI upscale model", null, "Select the local upscaling model.", delegate
            {
                var menu = BuildUpscaleModelMenu(); menu.PlacementTarget = _upscaleModelButton;
                menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
            }, false);
            _upscaleModelButton.Width = 24; _upscaleModelButton.MinWidth = 24; _upscaleModelButton.Padding = new Thickness(2, 0, 2, 0);
            UpscalePresentation.PrepareButton(_upscaleModelButton);
            _upscaleModelButton.Margin = new Thickness(0); group.Children.Add(_upscaleModelButton);
            return group;
        }

        private ContextMenu BuildUpscaleModelMenu()
        {
            var menu = new ContextMenu();
            ThemeManager.Bind(menu, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(menu, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(menu, Control.BorderBrushProperty, ThemeKeys.Border);
            foreach (UpscaleModel model in UpscaleModels.All)
            {
                string id = model.Id;
                bool installed = UpscaleModels.IsInstalled(id, null, _supirConfig);
                var item = new MenuItem { Header = model.Name + (installed ? id == UpscaleModels.Supir ? " (experimental)" : "" : " (setup required)"),
                    Tag = id, IsCheckable = true, IsChecked = id == _upscaleModelId };
                CommandPresentation.Describe(item, model.Name, null, model.Description);
                item.Click += delegate { ChooseUpscaleModel(id); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var setup = new MenuItem { Header = "SUPIR setup..." }; setup.Click += delegate { ShowSupirSetup(); }; menu.Items.Add(setup);
            return menu;
        }

        private void BuildUpscaleModelConfiguration()
        {
            _configureUpscaleModel = new ComboBox { MinWidth = 180 };
            foreach (var model in UpscaleModels.All) _configureUpscaleModel.Items.Add(new ComboBoxItem { Content = model.Name, Tag = model.Id });
            _configureUpscaleModel.SelectionChanged += delegate
            {
                var item = _configureUpscaleModel.SelectedItem as ComboBoxItem;
                if (!_syncUpscaleModel && item != null) ChooseUpscaleModel((string)item.Tag);
            };
            AddConfigurationRow(3, "Upscale model", _configureUpscaleModel);
            AddConfigurationRow(3, "Optional SUPIR", CommandPresentation.Button("\uE713", "Setup...", null,
                "Connect a separately installed, trusted SUPIR environment. Checkpoints are not bundled or downloaded.", delegate { ShowSupirSetup(); }, true));
            RefreshUpscaleModelControls();
        }

        private void ChooseUpscaleModel(string id)
        {
            if (_enhancementSaving || _rotationSaving || _backupBusy || _fileTransferBusy) { RefreshUpscaleModelControls(); return; }
            if (!UpscaleModels.IsInstalled(id, null, _supirConfig))
            {
                if (id != UpscaleModels.Supir || !ShowSupirSetup())
                {
                    if (id != UpscaleModels.Supir) MessageBox.Show(this, "The selected model file is missing. Reinstall the complete viewer package.", "AI upscale", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshUpscaleModelControls(); return;
                }
            }
            SetUpscaleModel(id, true);
        }

        private void SetUpscaleModel(string id, bool rebuild)
        {
            string selected = UpscaleModels.Get(id).Id;
            if (selected == _upscaleModelId) { RefreshUpscaleModelControls(); return; }
            bool active = rebuild && HasUpscaleSource() && (_autoUpscaleOriginal != null || AutomaticUpscalePending);
            var original = active ? _autoUpscaleOriginal ?? (BitmapSource)_mainImage.Source : null;
            double scale = active ? SelectedUpscaleSize() : 1;
            if (active) { ClearAutomaticUpscale(); SetAutomaticUpscalePixels(original); }
            _upscaleModelId = selected; RefreshUpscaleModelControls();
            if (rebuild) ScheduleSessionSave();
            if (active) StartAutomaticUpscale(original, scale);
        }

        private void RefreshUpscaleModelControls()
        {
            var model = UpscaleModels.Get(_upscaleModelId);
            if (_configureUpscaleModel != null)
            {
                _syncUpscaleModel = true;
                foreach (ComboBoxItem item in _configureUpscaleModel.Items) if ((string)item.Tag == model.Id) _configureUpscaleModel.SelectedItem = item;
                _syncUpscaleModel = false;
            }
            if (_upscaleButton != null) CommandPresentation.Describe(_upscaleButton, "AI upscale", "S", "Selected model: " + model.Name
                + ". Automatically upscale small images for this frame. Press again to cancel or restore the original. No file is changed.");
            if (_upscaleModelButton != null) CommandPresentation.Describe(_upscaleModelButton, "AI upscale model", null, "Selected: " + model.Name + ". " + model.Description);
        }

        private bool ShowSupirSetup()
        {
            var dialog = new Window { Title = "SUPIR local setup", Owner = _configureWindow != null && _configureWindow.IsVisible ? _configureWindow : this,
                Width = 600, MinWidth = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            ThemeManager.PrepareWindow(dialog);
            var panel = new StackPanel { Margin = new Thickness(20) }; dialog.Content = panel;
            panel.Children.Add(new TextBlock { Text = "SUPIR (experimental)", FontSize = 18, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "Separate installation required: CUDA-enabled Python, the trusted SUPIR repository, SDXL and SUPIR-v0F checkpoints, and local text-encoder files. Configure their paths in SUPIR_v0.yaml / CKPT_PTH.py. No downloads are performed here.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
            panel.Children.Add(new TextBlock { Text = "Python executable" });
            var python = new TextBox { Text = _supirConfig.PythonPath ?? "", Margin = new Thickness(0, 4, 0, 8) }; panel.Children.Add(python);
            var browsePython = CommandPresentation.Button("\uE838", "Browse Python...", null, "Choose python.exe from the SUPIR environment.", delegate
            { var picker = new OpenFileDialog { Filter = "Python executable|python.exe", CheckFileExists = true }; if (picker.ShowDialog(dialog) == true) python.Text = picker.FileName; }, true);
            panel.Children.Add(browsePython);
            panel.Children.Add(new TextBlock { Text = "SUPIR repository folder", Margin = new Thickness(0, 12, 0, 0) });
            var repository = new TextBox { Text = _supirConfig.RepositoryPath ?? "", Margin = new Thickness(0, 4, 0, 8) }; panel.Children.Add(repository);
            panel.Children.Add(CommandPresentation.Button("\uE838", "Browse folder...", null, "Choose the trusted local SUPIR source folder.", delegate
            { using (var picker = new System.Windows.Forms.FolderBrowserDialog()) { picker.ShowNewFolderButton = false; if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) repository.Text = picker.SelectedPath; } }, true));
            panel.Children.Add(new TextBlock { Text = "Requires the NVIDIA GPU and at least 12288 MB AI allowance. Preview limit: 4.2 MP. Generative detail may change faces or textures. Connecting authorizes running this local Python installation and repository; paths are not exported in viewer backups. Runtime/checkpoints are unverified until a preview succeeds.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12) });
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) });
            var connect = new Button { Content = "Connect", MinWidth = 80 }; buttons.Children.Add(connect); panel.Children.Add(buttons);
            connect.Click += delegate
            {
                try
                {
                    var config = new SupirRuntimeConfig { PythonPath = python.Text.Trim(), RepositoryPath = repository.Text.Trim() };
                    config.Save(_services.AppDataRoot); _supirConfig = config; dialog.DialogResult = true;
                }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            return dialog.ShowDialog() == true;
        }
    }
}
