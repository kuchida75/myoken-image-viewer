using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal interface IImageDecoder
    {
        string Name { get; }
        bool CanDecode(string extension);
        BitmapSource Decode(string path, int decodePixelWidth, CancellationToken cancellationToken);
        ImageMetadata ReadMetadata(string path);
    }

    internal sealed class DecoderRegistry
    {
        private readonly List<IImageDecoder> _decoders;
        private readonly Func<bool> _icc;
        internal GpuImageCache GpuImages { get; set; }

        public DecoderRegistry(Func<bool> lowPriorityIccEnabled)
        {
            _icc = lowPriorityIccEnabled;
            _decoders = new List<IImageDecoder>();
            _decoders.Add(new WicImageDecoder(lowPriorityIccEnabled));
            _decoders.Add(new ModernImageDecoder(lowPriorityIccEnabled));
        }

        public bool IsKnownImage(string path)
        {
            return ImageExtensions.IsBrowsableImage(path);
        }

        public BitmapSource Decode(string path, int decodePixelWidth, CancellationToken cancellationToken)
        {
            IImageDecoder decoder = FindDecoder(path);
            bool gpu = decodePixelWidth == 0 && GpuImages != null && GpuImages.Enabled
                && !System.IO.Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            BitmapSource bitmap;
            string extension = System.IO.Path.GetExtension(path);
            if (gpu && !_icc() && (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                && GpuImages.TryDecodeJpeg(path, cancellationToken, out bitmap)) return bitmap;
            FileRevision revision = FileRevision.Read(path);
            bitmap = decoder.Decode(path, decodePixelWidth, cancellationToken);
            if (gpu)
            {
                GpuImages.MarkDecoded(path, bitmap, _icc());
                using (GpuImageLease lease = GpuImages.Acquire(path, bitmap, revision, cancellationToken)) { }
            }
            return bitmap;
        }

        public ImageMetadata ReadMetadata(string path)
        {
            IImageDecoder decoder = FindDecoder(path);
            return decoder.ReadMetadata(path);
        }

        public string DecoderNameFor(string path)
        {
            return FindDecoder(path).Name;
        }

        private IImageDecoder FindDecoder(string path)
        {
            string extension = Path.GetExtension(path);
            for (int i = 0; i < _decoders.Count; i++)
            {
                if (_decoders[i].CanDecode(extension))
                {
                    return _decoders[i];
                }
            }

            throw new NotSupportedException("Unsupported file type: " + extension);
        }
    }

    internal sealed class WicImageDecoder : IImageDecoder
    {
        private readonly Func<bool> _lowPriorityIccEnabled;

        public WicImageDecoder(Func<bool> lowPriorityIccEnabled)
        {
            _lowPriorityIccEnabled = lowPriorityIccEnabled;
        }

        public string Name
        {
            get { return "WIC decoder"; }
        }

        public bool CanDecode(string extension)
        {
            return String.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
        }

        public BitmapSource Decode(string path, int decodePixelWidth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = _lowPriorityIccEnabled()
                ? BitmapCreateOptions.PreservePixelFormat
                : (BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile);
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }

            // Explicit ownership also releases the handle when a damaged image fails to decode.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();

            cancellationToken.ThrowIfCancellationRequested();
            return image;
        }

        public ImageMetadata ReadMetadata(string path)
        {
            var metadata = CreateFileMetadata(path);
            metadata.Decoder = Name;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        BitmapFrame frame = decoder.Frames[0];
                        metadata.PixelWidth = frame.PixelWidth;
                        metadata.PixelHeight = frame.PixelHeight;
                        metadata.FileType = decoder is PngBitmapDecoder ? "PNG" : decoder is JpegBitmapDecoder ? "JPEG" : decoder.CodecInfo.FriendlyName;
                        metadata.BitDepth = frame.Format.BitsPerPixel > 0
                            ? frame.Format.BitsPerPixel + " bits/pixel (" + frame.Format + ")" : null;
                        metadata.Add("Bit depth", metadata.BitDepth);
                        metadata.Add("Dimensions", frame.PixelWidth + " x " + frame.PixelHeight);
                        metadata.Add("DPI", frame.DpiX.ToString("0.#") + " x " + frame.DpiY.ToString("0.#"));
                        metadata.Add("Pixel format", frame.Format.ToString());

                        BitmapMetadata bitmapMetadata = frame.Metadata as BitmapMetadata;
                        if (bitmapMetadata != null)
                        {
                            metadata.Add("Date taken", bitmapMetadata.DateTaken);
                            metadata.Add("Camera maker", bitmapMetadata.CameraManufacturer);
                            metadata.Add("Camera model", bitmapMetadata.CameraModel);
                            AddQuery(metadata, bitmapMetadata, "Lens", "/app1/ifd/exif/{ushort=42036}");
                            metadata.Add("Application", bitmapMetadata.ApplicationName);
                            metadata.Add("Title", bitmapMetadata.Title);
                            metadata.Add("Subject", bitmapMetadata.Subject);
                            metadata.Add("Comment", bitmapMetadata.Comment);
                            metadata.Add("Copyright", bitmapMetadata.Copyright);
                            AddQuery(metadata, bitmapMetadata, "Exposure time", "/app1/ifd/exif/{ushort=33434}");
                            AddQuery(metadata, bitmapMetadata, "F-number", "/app1/ifd/exif/{ushort=33437}");
                            AddQuery(metadata, bitmapMetadata, "ISO", "/app1/ifd/exif/{ushort=34855}");
                            AddQuery(metadata, bitmapMetadata, "Focal length", "/app1/ifd/exif/{ushort=37386}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                metadata.Add("Metadata warning", ex.Message);
            }

            metadata.Add("Decoder", metadata.Decoder);
            metadata.Add("Modified", metadata.ModifiedUtc.ToLocalTime());
            metadata.Add("File size", ImageExtensions.FormatBytes(metadata.FileSize));
            return metadata;
        }

        private static ImageMetadata CreateFileMetadata(string path)
        {
            var info = new FileInfo(path);
            return new ImageMetadata
            {
                Path = path,
                FileSize = info.Exists ? info.Length : 0,
                ModifiedUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue
            };
        }

        private static void AddQuery(ImageMetadata metadata, BitmapMetadata bitmapMetadata, string name, string query)
        {
            try
            {
                object value = bitmapMetadata.GetQuery(query);
                metadata.Add(name, value);
            }
            catch
            {
            }
        }
    }

}
