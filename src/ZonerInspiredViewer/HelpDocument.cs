using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal static class HelpDocument
    {
        internal static RichTextBox View(FlowDocument document)
        {
            var view = new RichTextBox { Document = document, IsReadOnly = true, IsDocumentEnabled = true,
                BorderThickness = new Thickness(0), Padding = new Thickness(16, 12, 16, 20),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            ThemeManager.Bind(view, Control.BackgroundProperty, ThemeKeys.WorkspaceBackground);
            ThemeManager.Bind(view, Control.ForegroundProperty, ThemeKeys.Text);
            view.SizeChanged += delegate { SizeTables(view); };
            view.Loaded += delegate { SizeTables(view); };
            return view;
        }

        private static void SizeTables(RichTextBox view)
        {
            double width = Math.Max(360, view.ActualWidth - view.Padding.Left - view.Padding.Right - SystemParameters.VerticalScrollBarWidth - 8);
            // FlowDocument tables do not distribute star widths like a layout Grid.
            foreach (var table in view.Document.Blocks.OfType<Table>())
            {
                table.Columns[0].Width = new GridLength(185);
                table.Columns[1].Width = new GridLength(Math.Max(175, width - 185));
            }
        }

        internal static FlowDocument Create()
        {
            var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, LineHeight = 22,
                PagePadding = new Thickness(20, 12, 20, 24), ColumnWidth = Double.PositiveInfinity };
            doc.SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Text); return doc;
        }

        private static Paragraph Paragraph(string text)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
            foreach (string part in Regex.Split(text, @"(`[^`]+`|\*\*[^*]+\*\*)"))
            {
                if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 1)
                    paragraph.Inlines.Add(new Run(part.Substring(1, part.Length - 2)) { FontFamily = new FontFamily("Consolas"), FontSize = 13 });
                else if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 3)
                    paragraph.Inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
                else paragraph.Inlines.Add(new Run(part));
            }
            return paragraph;
        }

        private static Paragraph Heading(string text, int level)
        {
            var heading = Paragraph(text);
            heading.FontWeight = FontWeights.SemiBold; heading.FontSize = level == 1 ? 22 : 17;
            heading.Margin = new Thickness(0, 18, 0, 10);
            heading.BorderThickness = new Thickness(0, 0, 0, 1); heading.Padding = new Thickness(0, 0, 0, 8);
            heading.SetResourceReference(Block.BorderBrushProperty, ThemeKeys.Border);
            heading.KeepWithNext = true; return heading;
        }

        internal static FlowDocument Markdown(string text)
        {
            var doc = Create(); List list = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) { list = null; continue; }
                if (line.StartsWith("#"))
                {
                    int level = 0; while (level < line.Length && line[level] == '#') level++;
                    doc.Blocks.Add(Heading(line.Substring(level).Trim(), level)); list = null;
                }
                else if (line.StartsWith("- "))
                {
                    if (list == null) { list = new List { Margin = new Thickness(18, 0, 0, 12), Padding = new Thickness(0), MarkerStyle = TextMarkerStyle.Disc }; doc.Blocks.Add(list); }
                    list.ListItems.Add(new ListItem(Paragraph(line.Substring(2))));
                }
                else { list = null; doc.Blocks.Add(Paragraph(line)); }
            }
            return doc;
        }

        internal static FlowDocument Shortcuts(string text)
        {
            var doc = Create(); Table table = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim(); if (line.Length == 0) continue;
                if (Regex.IsMatch(line, @"^[A-Z /()]+$"))
                {
                    string title = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(line.ToLowerInvariant());
                    doc.Blocks.Add(Heading(title, 2)); table = null; continue;
                }
                Match pair = Regex.Match(line, @"^(.+?)\s{2,}(.+)$");
                if (!pair.Success) { doc.Blocks.Add(Paragraph(line)); table = null; continue; }
                if (table == null)
                {
                    table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 16) };
                    table.Columns.Add(new TableColumn { Width = new GridLength(185) });
                    table.Columns.Add(new TableColumn { Width = new GridLength(320) });
                    table.RowGroups.Add(new TableRowGroup()); doc.Blocks.Add(table);
                }
                var row = new TableRow();
                var key = new TableCell(Paragraph(pair.Groups[1].Value)) { Padding = new Thickness(0, 6, 14, 4), FontWeight = FontWeights.SemiBold };
                var description = new TableCell(Paragraph(pair.Groups[2].Value)) { Padding = new Thickness(0, 6, 0, 4) };
                key.SetResourceReference(TableCell.ForegroundProperty, ThemeKeys.Accent);
                foreach (var cell in new[] { key, description })
                {
                    cell.BorderThickness = new Thickness(0, 0, 0, 1);
                    cell.SetResourceReference(TableCell.BorderBrushProperty, ThemeKeys.Border); row.Cells.Add(cell);
                }
                table.RowGroups[0].Rows.Add(row);
            }
            return doc;
        }

        internal static string Text(RichTextBox view) { return new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text; }
    }
}
