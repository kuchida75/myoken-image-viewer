using Myoken.Core;
using Myoken.Linux;

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

var comparer = NaturalNameComparer.Instance;
var files = new[] { "image10.png", "image02.png", "image2.png", "image1.png" };
Array.Sort(files, comparer);
Check(files.SequenceEqual(new[] { "image1.png", "image2.png", "image02.png", "image10.png" }), "natural numeric ordering");
Check(comparer.Compare("n999999999999999999999999", "n1000000000000000000000000") < 0, "numeric runs do not overflow");
Check(comparer.Compare("A.png", "a.png") != 0, "case-distinct filenames retain a deterministic tie-break");
Check(comparer.Compare("日本2.png", "日本10.png") < 0, "Unicode filenames with numeric runs");
Check(comparer.Compare(null, "a") < 0 && comparer.Compare("a", null) > 0 && comparer.Compare(null, null) == 0, "null ordering");
var sample = new[] { "a", "A", "a0", "a00", "a2", "a02", "a10", "日本2", "日本10", "z" };
foreach (var a in sample)
foreach (var b in sample)
    Check(Math.Sign(comparer.Compare(a, b)) == -Math.Sign(comparer.Compare(b, a)), "comparison antisymmetry: " + a + "/" + b);
foreach (var a in sample)
foreach (var b in sample)
foreach (var c in sample)
    if (comparer.Compare(a, b) <= 0 && comparer.Compare(b, c) <= 0 && comparer.Compare(a, c) > 0)
        throw new InvalidOperationException("Comparison transitivity failed.");
Console.WriteLine("PASS: comparison transitivity");

var temp = Path.Combine(Path.GetTempPath(), "myoken-linux-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(temp);
    var upper = Path.Combine(temp, "A.png");
    var lower = Path.Combine(temp, "a.png");
    using (var store = new SessionStore(temp))
    {
        Check(store.CanWrite, "first session writer acquires lock");
        store.Save(new SessionState { FolderPath = temp, ImagePaths = new() { upper, lower }, ActiveImagePath = lower });
        var restored = store.Load();
        Check(restored.FolderPath == temp && restored.ImagePaths.SequenceEqual(new[] { upper, lower }) && restored.ActiveImagePath == lower, "session round trip preserves folder, tab order, case and active tab");
        using var second = new SessionStore(temp);
        Check(!second.CanWrite, "second session writer is read-only");
    }
    File.WriteAllText(Path.Combine(temp, "session.json"), "{broken");
    using (var corrupt = new SessionStore(temp))
    {
        Check(corrupt.Load().ImagePaths.Count == 0 && !corrupt.CanWrite, "corrupt session disables writes");
        corrupt.Save(new SessionState());
        Check(File.ReadAllText(Path.Combine(temp, "session.json")) == "{broken", "corrupt original preserved");
    }
    File.WriteAllText(Path.Combine(temp, "session.json"), "{\"SchemaVersion\":99}");
    using (var newer = new SessionStore(temp))
    {
        newer.Load();
        Check(!newer.CanWrite, "newer session schema is not overwritten");
    }
}
finally { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
Console.WriteLine("All independent Linux core/session checks passed.");
