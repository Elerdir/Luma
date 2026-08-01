# Luma

[![CI](https://github.com/Elerdir/Luma/actions/workflows/ci.yml/badge.svg)](https://github.com/Elerdir/Luma/actions/workflows/ci.yml)

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
| `Luma.Application` | Use-cases and the `IMediaEngine`, `ISubtitleFinder` and `ISettingsStore<T>` ports. Depends only on Domain. |
| `Luma.Infrastructure` | `LibVlcMediaEngine`, sidecar subtitle discovery, JSON settings. Implements the ports. |
| `Luma.Presentation` | Avalonia UI (MVVM), dependency-injection composition root. |

The **`IMediaEngine`** port is the seam: the whole app is testable against a fake engine, and the VLC backend can later be swapped for FFmpeg without touching the domain.

The domain also owns what is *legal*. `PlaybackSession` throws on an illegal transition,
and `PlaybackStatusExtensions` exposes the matching predicates (`CanPlay`, `CanSeek`, …)
that the UI binds its `IsEnabled` and `CanExecute` to — so a button or shortcut can never
reach the aggregate in a state it would reject. A test cross-checks the two against each
other for every status.

## Features

- Play local files and network streams; playlist with repeat off / one / all
- Seek, volume, mute, playback speed 0.25× – 4×
- Audio and subtitle track selection, plus external subtitle files
- Sidecar subtitles found automatically (`movie.srt`, `movie.en.srt`, `Subs/…`)
- Fullscreen with auto-hiding controls and cursor
- Drag and drop files onto the window (hold <kbd>Shift</kbd> to append)
- Remembers volume, mute, repeat, window size and recent files — and resumes each file
  where you left it

## Tech stack

- .NET 10 · C# latest
- Avalonia 11.3 (cross-platform UI)
- LibVLCSharp 3.10 (media backend)
- Central Package Management (`Directory.Packages.props`)
- xUnit · Shouldly · NSubstitute (tests)

## Platform support

The UI is cross-platform, but the **native LibVLC libraries are only bundled for
Windows** (`VideoLAN.LibVLC.Windows`). On Linux and macOS libvlc has to be installed
system-wide before Luma will start:

```bash
sudo apt install libvlc-dev vlc-plugin-base
```

```bash
brew install --cask vlc
```

## Build & run

```bash
dotnet build
```

```bash
dotnet run --project src/Luma.Presentation
```

```bash
dotnet test
```

Integration tests load the native libraries, so headless CI excludes them:

```bash
dotnet test --filter "Category!=Integration"
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
| `N` / `P` | Next / previous playlist item |
| `R` | Cycle repeat mode |
| `L` | Toggle playlist panel |
| `F` / `F11` | Fullscreen toggle |
| `Esc` | Exit fullscreen |
| `Ctrl`+`O` | Open file |
| Double-click video | Fullscreen toggle |

## Settings

Preferences and window placement are written as one JSON file per settings type under
the user's application data directory:

| Platform | Location |
|---|---|
| Windows | `%APPDATA%\Luma\` |
| Linux / macOS | `~/.config/Luma/` |

Deleting them resets Luma to a fresh install. A missing or corrupt file falls back to
defaults rather than blocking startup.

## Icons

`Assets/luma-icon.svg` is the design source. The raster assets used by the app
(`luma.png` for the window/taskbar icon, `luma.ico` for the executable) are
generated from a vector definition in code — no imaging dependency required:

```bash
dotnet run --project tools/Luma.IconGen
```

## Status

Working player: open files (dialog, drag-and-drop, command line or the recent list),
play/pause/stop, seek, volume and mute, playback speed, audio/subtitle track switching,
external subtitles, playlist navigation with repeat, fullscreen, and resume-where-you-
left-off — all driven through the domain state machine.

Video renders embedded in the main window (verified: no separate VLC output window).
169 tests: 106 over the domain, 48 over the application layer, 12 over the filesystem
and settings adapters, and 3 LibVLC integration smoke tests (`Category=Integration`).

### Next up

- Thumbnail seek preview
- Convert `Assets/luma-icon.svg` to a multi-size `.ico` for the window/taskbar
- Property-based tests (FsCheck) over the playback state machine
- Headless Avalonia tests for `MainViewModel`
- Playlist reordering and `.m3u` save/load
