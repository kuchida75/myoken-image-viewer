using Myoken.Linux;

static void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
static bool Near(double a, double b) => Math.Abs(a - b) < 1e-7;
var cases = 0;
foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
foreach (var size in new[] { (6000, 4000), (4000, 6000), (12000, 1000), (96, 64) })
foreach (var vp in new[] { (1000.0, 800.0), (1500.0, 900.0), (300.0, 1200.0) })
{
    var v = new ImageViewport();
    v.SetViewport(vp.Item1, vp.Item2, scaling); v.SetSource(size.Item1, size.Item2);
    if (!v.IsFit || v.DrawWidth > v.Width + 1e-7 || v.DrawHeight > v.Height + 1e-7 || v.Zoom > 1)
        throw new InvalidOperationException("Fit bounds failed.");
    v.ZoomAt(1, v.Width / 2, v.Height / 2);
    if (!Near(v.Scale * scaling, 1)) throw new InvalidOperationException("Actual-size pixel ratio failed.");
    v.PanBy(1e9, 1e9);
    if (v.DrawWidth > v.Width && !Near(v.X, 0)) throw new InvalidOperationException("Left bound failed.");
    if (v.DrawHeight > v.Height && !Near(v.Y, 0)) throw new InvalidOperationException("Top bound failed.");
    v.PanBy(-1e9, -1e9);
    if (v.DrawWidth > v.Width && !Near(v.X, v.Width - v.DrawWidth)) throw new InvalidOperationException("Right bound failed.");
    if (v.DrawHeight > v.Height && !Near(v.Y, v.Height - v.DrawHeight)) throw new InvalidOperationException("Bottom bound failed.");
    cases++;
}
Check(cases == 60, $"Fit, actual-size pixel mapping and pan bounds across {cases} size/scale combinations");
var view = new ImageViewport(); view.SetViewport(1000, 800, 1); view.SetSource(6000, 4000);
view.ZoomAt(1, 500, 400);
var sx = (321 - view.X) / view.Scale; var sy = (245 - view.Y) / view.Scale;
view.ZoomAt(2, 321, 245);
Check(Near((321 - view.X) / view.Scale, sx) && Near((245 - view.Y) / view.Scale, sy), "pointer anchoring away from image bounds");
var cx = (view.Width / 2 - view.X) / view.Scale; var cy = (view.Height / 2 - view.Y) / view.Scale;
view.SetViewport(1200, 900, 1.5);
Check(Near(view.Zoom, 2) && Near((view.Width / 2 - view.X) / view.Scale, cx) && Near((view.Height / 2 - view.Y) / view.Scale, cy), "resize and render-scale change preserve source centre and zoom");
var oldX = view.X; var oldY = view.Y;
view.SetViewport(0, 0, 1);
Check(Near(view.X, oldX) && Near(view.Y, oldY) && Near(view.Width, 1200), "zero-size detach preserves per-tab geometry");
view.SetSource(6000, 4000);
Check(Near(view.Zoom, 2) && Near(view.X, oldX), "reloading same source dimensions retains view");
view.ZoomAt(double.NaN, 0, 0); view.PanBy(double.PositiveInfinity, 0);
Check(Near(view.X, oldX) && Near(view.Zoom, 2), "non-finite input does not corrupt geometry");
view.ZoomAt(1e9, 500, 400); Check(Near(view.Zoom, 16), "maximum zoom is bounded");
view.ZoomAt(-1, 500, 400); Check(view.Zoom > 0, "minimum zoom stays positive");
view.Fit(); Check(view.IsFit && !view.CanPan, "Fit resets pan and manual mode");
view.SetSource(96, 64); Check(Near(view.Zoom, 1), "Fit does not upscale small source images");
Check(ViewerDecodePolicy.CanDecodeFull(8000, 8000) && !ViewerDecodePolicy.CanDecodeFull(8001, 8000), "64-million-pixel full-resolution boundary");
Check(!ViewerDecodePolicy.CanDecodeFull(int.MaxValue, int.MaxValue), "pixel arithmetic does not overflow Int32");
ViewerDecodePolicy.Validate(12000, 10000, false);
try { ViewerDecodePolicy.Validate(12000, 10001, false); throw new Exception("Input guard failed."); }
catch (InvalidDataException) { Console.WriteLine("PASS: 120-million-pixel input guard"); }
try { ViewerDecodePolicy.Validate(8001, 8000, true); throw new Exception("Full guard failed."); }
catch (InvalidDataException) { Console.WriteLine("PASS: full-resolution size guard"); }
Console.WriteLine("All viewer geometry and decode-policy checks passed.");

var preloadTargets = PreviewPreloadPolicy.Targets(new[] { "a", "b", "c", "d", "e" }, "c");
Check(preloadTargets.SequenceEqual(new[] { "d", "b", "e" }), "preload policy selects next, previous and second-next");
Check(PreviewPreloadPolicy.Targets(new[] { "a", "b" }, "b").SequenceEqual(new[] { "a" }), "preload policy does not wrap at list edge");
{ var gate = new PriorityAsyncGate();
{
    using var held = await gate.EnterAsync(ViewerDecodePriority.Foreground);
    var background = gate.EnterAsync(ViewerDecodePriority.Background).AsTask();
    var foreground = gate.EnterAsync(ViewerDecodePriority.Foreground).AsTask();
    held.Dispose();
    var first = await Task.WhenAny(background, foreground);
    Check(ReferenceEquals(first, foreground), "foreground viewer decode is dequeued before pending preload");
    using var fg = await foreground;
    fg.Dispose();
    using var bg = await background;
}
Console.WriteLine("All viewer preload-policy and decode-priority checks passed.");
