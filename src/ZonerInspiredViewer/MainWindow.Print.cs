using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Button _printButton;
        private PrintPreviewWindow _printPreview;

        private Button BuildPrintButton()
        {
            _printButton = CommandPresentation.Button("\uE749", "Print preview", "Ctrl+P",
                "Preview and print the full current image with visible rotation, Enhance and applied AI upscale. No file is changed.", ShowPrintPreview, false);
            _printButton.IsEnabled = false; return _printButton;
        }

        private ImagePrintRequest CapturePrintRequest()
        {
            if (!CanSaveEnhancement()) throw new InvalidOperationException("Wait for the image and its adjustments to finish before printing.");
            var pixels = (BitmapSource)_mainImage.Source;
            if (!pixels.IsFrozen) { pixels = pixels.CloneCurrentValue(); pixels.Freeze(); }
            var quick = _mainImage.Effect == null ? null : (Effect)_mainImage.Effect.CloneCurrentValue();
            if (quick != null) quick.Freeze();
            return new ImagePrintRequest { Name = Path.GetFileName(_activeTabPath), Pixels = pixels, QuickEffect = quick,
                Manual = ManualAdjustments.Copy(CurrentTabState().ManualAdjustments), Rotation = CurrentTabState().RotationQuarterTurns };
        }

        private void ShowPrintPreview()
        {
            if (_printPreview != null) { _printPreview.Activate(); return; }
            if (!CanSaveEnhancement()) return;
            StopSlideshow(); EndImagePan(); _imageNavigator.EndDrag();
            string path = _activeTabPath;
            bool resumeAnimation = _animationTimer != null && _animationTimer.IsEnabled;
            if (resumeAnimation) _animationTimer.Stop();
            try
            {
                _printPreview = new PrintPreviewWindow(CapturePrintRequest()) { Owner = this };
                UpdateEnhanceSaveButtons(); _printPreview.ShowDialog();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Print preview", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally
            {
                _printPreview = null; UpdateEnhanceSaveButtons();
                if (!_isClosed && resumeAnimation && path == _activeTabPath && _imageView.IsVisible && _animation != null && _animationTimer != null)
                    _animationTimer.Start();
                if (!_isClosed) RestoreImageFocusAfterSave(IsActive);
            }
        }
    }
}
