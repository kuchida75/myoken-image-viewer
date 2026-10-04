using Myoken.Linux;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

foreach (var count in new[] { 0, 1, 72, 73, 100_000, int.MaxValue })
foreach (var width in new[] { 0.0, 1.0, 184.0, 923.0, 5120.0 })
foreach (var height in new[] { 0.0, 1.0, 736.0, 2160.0 })
foreach (var offset in new[] { -100.0, 0.0, 18_400.0, double.MaxValue, double.NaN })
{
    var r = ThumbnailLayout.Calculate(count, width, height, offset);
    if (r.Columns < 1 || r.First < 0 || r.End > count || r.End < r.First || !double.IsFinite(r.ExtentHeight))
        throw new InvalidOperationException("Invalid viewport bounds.");
    var maximum = ((long)Math.Ceiling(height / ThumbnailLayout.CellHeight) + 1 + 2 * ThumbnailLayout.OverscanRows) * r.Columns;
    if (r.End - r.First > maximum) throw new InvalidOperationException("Viewport allocation is not bounded.");
}
Console.WriteLine("PASS: viewport bounds and bounded realisation across 3,000 edge-case combinations");
var bottom = ThumbnailLayout.Calculate(100_000, 920, 736, double.MaxValue);
Check(bottom.End == 100_000 && bottom.First > 99_900, "100k viewport reaches final entries without a page boundary");
Check(ThumbnailLayout.Calculate(100_000, 920, 0, 0).End == 0, "zero-height viewport realises no controls");
var first = ThumbnailLayout.Calculate(100_000, 920, 736, 0);
Check(first.Columns == 5 && first.First == 0 && first.End == 25, "first viewport plus one-row overscan");
var filename = "Screenshot from 2026-10-04 16-22-08.png";
Check(ThumbnailLayout.TabCaption(filename).EndsWith("16-22-08.png", StringComparison.Ordinal), "long tab label retains distinguishing filename suffix");
Check(DirectoryCatalog.Contains("/home/test", "/home/test/photos") && !DirectoryCatalog.Contains("/home/test", "/home/test2"), "path ancestry respects directory boundaries");
Check(!DirectoryCatalog.Contains("/home/A", "/home/a"), "path ancestry is case sensitive");

var temp = Path.Combine(Path.GetTempPath(), "myoken-browser-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(temp);
    foreach (var name in new[] { "folder10", "folder2", "日本", "A", "a" }) Directory.CreateDirectory(Path.Combine(temp, name));
    Directory.CreateDirectory(Path.Combine(temp, "folder2", "nested"));
    File.WriteAllText(Path.Combine(temp, "not-a-folder.png"), "fixture");
    Directory.CreateSymbolicLink(Path.Combine(temp, "cycle"), temp);
    var entries = await DirectoryCatalog.ReadAsync(temp, CancellationToken.None);
    Check(entries.Length == 6, "directory enumeration is one level only");
    var paths = entries.Select(e => Path.GetFileName(e.Path)).ToArray();
    Check(Array.IndexOf(paths, "folder2") < Array.IndexOf(paths, "folder10"), "folder tree uses natural numeric ordering");
    Check(paths.Contains("A") && paths.Contains("a") && paths.Contains("日本"), "case-distinct and Unicode directories survive enumeration");
    Check(entries.Single(e => Path.GetFileName(e.Path) == "cycle").IsLink, "symbolic link marked for leaf-only tree handling");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await DirectoryCatalog.ReadAsync(temp, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: directory enumeration cancellation"); }
    try { await DirectoryCatalog.ReadAsync(Path.Combine(temp, "missing"), CancellationToken.None); throw new InvalidOperationException("Missing folder ignored."); }
    catch (DirectoryNotFoundException) { Console.WriteLine("PASS: missing folder reported rather than treated as empty"); }
}
finally
{
    var link = Path.Combine(temp, "cycle");
    if (Directory.Exists(link)) Directory.Delete(link);
    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
}
Console.WriteLine("All browser viewport/directory checks passed.");
