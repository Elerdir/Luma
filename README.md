# Luma

A clean, cross-platform video player inspired by MPC-HC. Built with **.NET 10**, **Avalonia**, and **LibVLCSharp**, following Clean Architecture and SOLID principles, with a fully tested domain core.

> *luma* — the luminance (brightness) component of a video signal (the **Y** in YCbCr).

## Architecture

Dependencies point inward. The domain knows nothing about VLC or Avalonia.

```
Luma.Presentation  (Avalonia MVVM)  ─┐
Luma.Infrastructure (LibVLC adapter) ─┼─► Luma.Application (use-cases, ports) ─► Luma.Domain (pure)
```

| Project | Responsibility |
|---|---|
| `Luma.Domain` | Playback state machine, playlist, value objects. Pure C#, no dependencies. |
| `Luma.Application` | Use-cases and the `IMediaEngine` port. Depends only on Domain. |
| `Luma.Infrastructure` | `LibVlcMediaEngine` — implements `IMediaEngine`, maps VLC events to domain. |
| `Luma.Presentation` | Avalonia UI (MVVM), dependency-injection composition root. |

The **`IMediaEngine`** port is the seam: the whole app is testable against a fake engine, and the VLC backend can later be swapped for FFmpeg without touching the domain.

## Tech stack

- .NET 10 · C# latest
- Avalonia 11.3 (cross-platform UI)
- LibVLCSharp 3.10 (media backend)
- Central Package Management (`Directory.Packages.props`)
- xUnit · Shouldly · NSubstitute (tests)

## Build & run

```bash
dotnet build
dotnet run --project src/Luma.Presentation
dotnet test
```

Open a file directly (file association / CLI):

```bash
dotnet run --project src/Luma.Presentation -- "C:\videos\clip.mkv"
```

## Keyboard shortcuts

| Key | Action |
|---|---|
| `Space` / `K` | Play / pause |
| `←` / `→` | Seek ∓5 s |
| `Shift`+`←` / `→` | Seek ∓30 s |
| `↑` / `↓` | Volume ±5 |
| `M` | Mute toggle |
| `S` | Stop |
| `F` / `F11` | Fullscreen toggle |
| `Esc` | Exit fullscreen |
| `Ctrl`+`O` | Open file |

## Status

Working player: open a file (or several as a playlist), play/pause/stop, seek,
volume, audio/subtitle track switching, and auto-advance on end — driven entirely
through the domain state machine. Domain and application layers are covered by 80
unit tests; the LibVLC adapter has 3 integration smoke tests (`Category=Integration`).

Video renders embedded in the main window (verified: no separate VLC output
window), and files can be opened from the command line. Keyboard shortcuts,
fullscreen, and audio/subtitle track selection are wired.

### Next up

- Thumbnail seek preview
- Playlist panel UI
- Convert `Assets/luma-icon.svg` to a multi-size `.ico` for the window/taskbar
- Property-based tests (FsCheck) over the playback state machine
- Headless Avalonia tests for `MainViewModel`
