# Portable core scope

Read ../../linux/AGENTS.md before changes. Target netstandard2.0 to remain consumable by Windows .NET Framework 4.8. Do not reference Avalonia, WPF, SkiaSharp, Win32, DirectX, CUDA or desktop services. Keep path identity separate from display sorting. Windows does not reference this project yet; do not change that without a separately reviewed migration and Windows regression tests.
