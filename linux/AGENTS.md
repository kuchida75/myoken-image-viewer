# Myoken Ubuntu/GNOME development context

Read `linux/README.md` before Linux work. Continue this project independently from Windows.

## Non-negotiable boundaries

- Work on `linux/ubuntu-gnome` or a Linux feature branch in a separate checkout.
- Windows baseline is v0.2.167 at ea785b3770383278f1a3bf67762d35014e8bda26. Preserve WPF and every existing Windows source/build/version file.
- Do not run Windows build/version-reservation scripts for Linux tasks.
- Do not change the default branch, merge, retag releases or publish binaries as part of ordinary Linux development.
- No Windows profile reads/writes or case-insensitive global path comparison on Linux.
- Core targets netstandard2.0 because Windows targets .NET Framework 4.8. Do not switch the shared library to netstandard2.1/net10.0 while claiming Windows compatibility.
- Core must not reference Avalonia, WPF, SkiaSharp, Win32, DirectX, CUDA or desktop services. Platform/session I/O and image decoding remain outside it.

## Current implementation and workflow

Linux UI: src/Myoken.Linux. Portable policies/DTOs: src/Myoken.Core. Independent regression runner: tests/Myoken.Linux.Tests.

Use `bash linux/check.sh`, then `bash linux/run.sh`. Keep status honest: source authored, compiled, headless launched and GNOME validated are separate states. Run real GNOME acceptance before calling the first milestone complete. The source starts with paged thumbnails and an active-tab-only bounded preview, not final large-folder performance.

Each change should state files affected, exact checks run, known limitations and the next concrete acceptance step. Port functionality incrementally; do not attempt a wholesale WPF rewrite or change Windows behaviour to simplify Linux.
