using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class ImagePrintRequest
    {
        internal string Name;
        internal BitmapSource Pixels;
        internal Effect QuickEffect;
        internal ManualAdjustments Manual;
        internal int Rotation;
    }

    internal sealed class ImagePrintLayout
    {
        internal Size PageSize;
        internal Rect ContentArea, ImageArea;
        internal double EffectiveDpi;

        internal static ImagePrintLayout Calculate(Size pixels, Size paper, Rect imageable, double marginMm, bool fill)
        {
            if (!Valid(pixels.Width) || !Valid(pixels.Height) || !Valid(paper.Width) || !Valid(paper.Height)
                || Double.IsNaN(marginMm) || Double.IsInfinity(marginMm) || marginMm < 0 || marginMm > 100)
                throw new ArgumentException("Enter a margin from 0 to 100 mm and a valid paper size.");
            double margin = marginMm * 96 / 25.4;
            if (paper.Width <= margin * 2 || paper.Height <= margin * 2) throw new ArgumentException("Margins leave no room for the image.");
            Rect area = Rect.Intersect(new Rect(margin, margin, paper.Width - 2 * margin, paper.Height - 2 * margin), imageable);
            if (area.IsEmpty || !Valid(area.Width) || !Valid(area.Height)) throw new ArgumentException("The printer has no usable image area with these margins.");
            double scale = fill ? Math.Max(area.Width / pixels.Width, area.Height / pixels.Height)
                : Math.Min(area.Width / pixels.Width, area.Height / pixels.Height);
            var image = new Rect(area.X + (area.Width - pixels.Width * scale) / 2, area.Y + (area.Height - pixels.Height * scale) / 2,
                pixels.Width * scale, pixels.Height * scale);
            return new ImagePrintLayout { PageSize = paper, ContentArea = area, ImageArea = image, EffectiveDpi = 96 / scale };
        }

        private static bool Valid(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value > 0; }

        internal FixedPage CreatePage(BitmapSource bitmap)
        {
            var page = new FixedPage { Width = PageSize.Width, Height = PageSize.Height, Background = Brushes.White, ClipToBounds = true };
            var crop = new Canvas { Width = ContentArea.Width, Height = ContentArea.Height, ClipToBounds = true };
            FixedPage.SetLeft(crop, ContentArea.X); FixedPage.SetTop(crop, ContentArea.Y);
            var image = new Image { Source = bitmap, Width = ImageArea.Width, Height = ImageArea.Height, Stretch = Stretch.Fill };
            Canvas.SetLeft(image, ImageArea.X - ContentArea.X); Canvas.SetTop(image, ImageArea.Y - ContentArea.Y);
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            crop.Children.Add(image); page.Children.Add(crop);
            page.Measure(PageSize); page.Arrange(new Rect(PageSize)); page.UpdateLayout();
            return page;
        }

        internal FixedDocumentSequence CreateDocument(BitmapSource bitmap)
        {
            var document = new FixedDocument(); document.DocumentPaginator.PageSize = PageSize;
            document.Pages.Add(new PageContent { Child = CreatePage(bitmap) });
            var reference = new DocumentReference(); reference.SetDocument(document);
            var sequence = new FixedDocumentSequence(); sequence.References.Add(reference); return sequence;
        }
    }

    internal sealed class ImagePrintPaper
    {
        internal PageMediaSizeName? Kind;
        internal double Width, Height;
        internal string Name;
        internal PageMediaSize MediaSize { get { return Kind.HasValue ? new PageMediaSize(Kind.Value, Width, Height) : new PageMediaSize(Width, Height); } }
        public override string ToString() { return Name + String.Format(" ({0:0.#} x {1:0.#} mm)", Width * 25.4 / 96, Height * 25.4 / 96); }
        internal static ImagePrintPaper From(PageMediaSize media)
        {
            if (media == null || !media.Width.HasValue || !media.Height.HasValue || media.Width <= 0 || media.Height <= 0) return null;
            string name = media.PageMediaSizeName.HasValue ? media.PageMediaSizeName.ToString() : "Custom";
            if (name.StartsWith("ISO")) name = name.Substring(3);
            else if (name.StartsWith("NorthAmerica")) name = name.Substring(12);
            return new ImagePrintPaper { Kind = media.PageMediaSizeName, Width = media.Width.Value, Height = media.Height.Value, Name = name };
        }
        internal static List<ImagePrintPaper> Defaults()
        {
            return new List<ImagePrintPaper> {
                new ImagePrintPaper { Kind = PageMediaSizeName.ISOA4, Name = "A4", Width = 210 * 96 / 25.4, Height = 297 * 96 / 25.4 },
                new ImagePrintPaper { Kind = PageMediaSizeName.NorthAmericaLetter, Name = "Letter", Width = 816, Height = 1056 },
                new ImagePrintPaper { Kind = PageMediaSizeName.NorthAmerica4x6, Name = "4 x 6 in", Width = 384, Height = 576 }
            };
        }
    }

    internal sealed class ImagePrinterInfo
    {
        internal string Name;
        internal bool IsDefault;
        public override string ToString() { return Name; }
    }

    internal sealed class ImagePrinterProfile
    {
        internal string Name;
        internal List<ImagePrintPaper> Papers;
        internal PageMediaSizeName? DefaultPaper;
    }

    internal sealed class ImagePrintSettings
    {
        internal byte[] Ticket;
        internal Size PageSize;
        internal Rect ImageableArea;
    }

    internal sealed class ImagePrinterConnection : IDisposable
    {
        private readonly PrintServer _server;
        internal readonly PrintQueue Queue;
        internal ImagePrinterConnection(string fullName)
        {
            int split = fullName.LastIndexOf('\\');
            _server = fullName.StartsWith("\\\\") && split > 1 ? new PrintServer(fullName.Substring(0, split)) : new LocalPrintServer();
            try { Queue = new PrintQueue(_server, fullName.StartsWith("\\\\") ? fullName.Substring(split + 1) : fullName); }
            catch { _server.Dispose(); throw; }
        }
        public void Dispose() { Queue.Dispose(); _server.Dispose(); }
    }

    internal static class ImagePrinting
    {
        // Printer drivers and WPF rendering both need an STA; only frozen pixels/DTOs cross threads.
        internal static Task<T> RunSta<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>();
            var thread = new Thread(delegate()
            {
                try { completion.SetResult(action()); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception error) { completion.SetException(error); }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
        }

        internal static Task<BitmapSource> RenderAsync(ImagePrintRequest request, CancellationToken token)
        {
            return RunSta(delegate
            {
                token.ThrowIfCancellationRequested();
                return EnhancedImageRenderer.Render(request.Pixels, request.QuickEffect, request.Manual, request.Rotation, token, null);
            });
        }

        internal static List<ImagePrinterInfo> Printers()
        {
            using (var server = new LocalPrintServer())
            {
                string defaultName = null;
                try { using (var queue = server.DefaultPrintQueue) if (queue != null) defaultName = queue.FullName; } catch (PrintSystemException) { }
                using (var queues = server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections }))
                {
                    var result = new List<ImagePrinterInfo>();
                    foreach (var queue in queues)
                    {
                        using (queue) result.Add(new ImagePrinterInfo { Name = queue.FullName, IsDefault = queue.FullName == defaultName });
                    }
                    return result.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToList();
                }
            }
        }

        internal static ImagePrinterProfile LoadProfile(string name)
        {
            using (var connection = new ImagePrinterConnection(name))
            {
                PrintTicket ticket = connection.Queue.UserPrintTicket ?? connection.Queue.DefaultPrintTicket;
                var caps = connection.Queue.GetPrintCapabilities(ticket);
                var papers = caps.PageMediaSizeCapability.Select(ImagePrintPaper.From).Where(p => p != null).ToList();
                if (papers.Count == 0) throw new InvalidOperationException("This printer did not report any usable paper sizes.");
                return new ImagePrinterProfile { Name = name, Papers = papers, DefaultPaper = ticket.PageMediaSize == null ? null : ticket.PageMediaSize.PageMediaSizeName };
            }
        }

        internal static ImagePrintSettings Settings(string name, ImagePrintPaper paper, bool landscape, int copies)
        {
            using (var connection = new ImagePrinterConnection(name))
            {
                var basis = connection.Queue.UserPrintTicket ?? connection.Queue.DefaultPrintTicket;
                var delta = new PrintTicket { PageMediaSize = paper.MediaSize, PageOrientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait,
                    CopyCount = copies, PagesPerSheet = 1, PageScalingFactor = 100 };
                var ticket = connection.Queue.MergeAndValidatePrintTicket(basis, delta).ValidatedPrintTicket;
                var media = ImagePrintPaper.From(ticket.PageMediaSize);
                if (media == null || Math.Abs(media.Width - paper.Width) > 1 || Math.Abs(media.Height - paper.Height) > 1
                    || ticket.PageOrientation != delta.PageOrientation || (ticket.CopyCount ?? 1) != copies
                    || (ticket.PagesPerSheet ?? 1) != 1 || (ticket.PageScalingFactor ?? 100) != 100)
                    throw new InvalidOperationException("The printer cannot use these page settings. Choose another paper size, orientation or copy count.");
                var caps = connection.Queue.GetPrintCapabilities(ticket);
                var area = caps.PageImageableArea;
                if (area == null) throw new InvalidOperationException("The printer did not report its printable area. Preview is unavailable for this configuration.");
                var size = landscape ? new Size(media.Height, media.Width) : new Size(media.Width, media.Height);
                byte[] xml; using (var stream = ticket.GetXmlStream()) xml = stream.ToArray();
                return new ImagePrintSettings { Ticket = xml, PageSize = size,
                    ImageableArea = Rect.Intersect(new Rect(size), new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight)) };
            }
        }
    }
}
