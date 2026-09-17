using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static ImageFileItem SortFixture(string name, bool folder = false)
    {
        return new ImageFileItem { Name = name, Path = Path.Combine(Root, "sort-fixtures", name),
            Extension = folder ? "" : Path.GetExtension(name), IsDirectory = folder };
    }

    private static void RunNaturalSortChecks()
    {
        var items = new[] { "xxxx11.png", "xxxx2.png", "xxxx1.png" }.Select(n => SortFixture(n)).ToList();
        foreach (BrowserSortField field in Enum.GetValues(typeof(BrowserSortField)))
        {
            items.Sort(new BrowserSort(field, false));
            Assert(items.Select(i => i.Name).SequenceEqual(new[] { "xxxx1.png", "xxxx2.png", "xxxx11.png" }), "natural Name order and metadata ties: " + field);
            items.Sort(new BrowserSort(field, true));
            Assert(items.Select(i => i.Name).SequenceEqual(new[] { "xxxx11.png", "xxxx2.png", "xxxx1.png" }), "descending natural order: " + field);
        }
        items = new[] { "photo11-part1.jpg", "photo2-part11.jpg", "photo2-part2.jpg" }.Select(n => SortFixture(n)).ToList();
        items.Sort(new BrowserSort(BrowserSortField.Name, false));
        Assert(items.Select(i => i.Name).SequenceEqual(new[] { "photo2-part2.jpg", "photo2-part11.jpg", "photo11-part1.jpg" }), "every numeric filename segment is compared naturally");
        items.AddRange(new[] { "Album11", "Album2", "Album1" }.Select(n => SortFixture(n, true)));
        items.Sort(new BrowserSort(BrowserSortField.Name, true));
        Assert(items.Take(3).All(i => i.IsDirectory) && items.Take(3).Select(i => i.Name).SequenceEqual(new[] { "Album1", "Album2", "Album11" }),
            "folders remain first and naturally ascending when images descend");
        var comparer = new BrowserSort(BrowserSortField.Name, false);
        var tricky = new[] { "Image01.jpg", "Image1.jpg", "image1.JPG", "Image0001.jpg", "Image-2.jpg", "Image-11.jpg", "Image.jpg" }
            .Select(n => SortFixture(n)).ToList();
        foreach (var a in tricky)
        foreach (var b in tricky)
            Assert(comparer.Compare(a, b) == -comparer.Compare(b, a), "case/leading-zero/punctuation comparison is symmetric");
        tricky.Sort(comparer);
        for (int i = 1; i < tricky.Count; i++) Assert(comparer.Compare(tricky[i - 1], tricky[i]) <= 0, "mixed name order is stable");
        var watch = Stopwatch.StartNew();
        var large = Enumerable.Range(1, 100000).Reverse().Select(i => SortFixture("Image" + i + ".png")).ToList();
        large.Sort(comparer);
        Assert(large.Select((item, i) => item.Name == "Image" + (i + 1) + ".png").All(correct => correct), "100k unpadded filenames sort in numeric order");
        Console.WriteLine("PASS: natural Name/Number sorting, metadata ties, folders first, descending/multiple numeric segments and 100k names (" + watch.ElapsedMilliseconds + " ms).");
    }
}
