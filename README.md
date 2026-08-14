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
- Opening one file loads its folder, in episode order — <kbd>Page Down</kbd> is the next
  episode. Switchable off in the right-click menu
- Remembers volume, mute, repeat and window size — and resumes each file where you left
  it

## Tech stack

- .NET 10 · C# latest
- Avalonia 11.3 (cross-platform UI)
- LibVLCSharp 3.10 (media backend)
- Central Package Management (`Directory.Packages.props`)
- xUnit · Shouldly · NSubstitute (tests)

## Platform support

| Platform | Native libvlc |
|---|---|
| Windows x64 | Bundled (`VideoLAN.LibVLC.Windows`) — the MSI is self-contained |
| macOS arm64 | Bundled into the `.app` by the release workflow, taken from the official VLC release |
| Linux | **Not bundled** — install libvlc system-wide |

There is deliberately no `VideoLAN.LibVLC.Mac` package reference: it ships a single
**x64** `libvlc.dylib` and no plugin directory, so it can neither load on Apple silicon
nor decode anything. The release workflow pulls libvlc and its plugins out of the
official VLC arm64 disk image instead.

Running from source on Linux or macOS needs libvlc installed:

```bash
sudo apt install libvlc-dev vlc-plugin-base
```

```bash
brew install --cask vlc
```

## Releases

`.github/workflows/release.yml` builds both installers:

| Platform | Output |
|---|---|
| Windows x64 | `Luma-<version>-x64.msi` |
| macOS arm64 | `Luma-<version>-arm64.dmg` |

Publishing a GitHub release builds both, attaches them to it, and uploads them to
UpdateHub. Running the workflow by hand (**Actions → Release → Run workflow**) builds
both and leaves them as workflow artifacts, so the packaging can be exercised without
cutting a release.

The UpdateHub upload is skipped unless these are configured, so the workflow is useful
before the update server exists:

| Secret | |
|---|---|
| `UPDATEHUB_URL` | e.g. `https://updates.example.com` |
| `UPDATEHUB_CI_TOKEN` | CI or personal access token |

Repo variable `APP_SLUG` overrides the default slug (`luma`). Uploads land as **drafts**
— publish them in the UpdateHub admin UI.

The macOS bundle is **ad-hoc signed**. Without any signature Gatekeeper refuses to run
it at all; with it, users get the usual "unidentified developer" prompt they can
override via right-click → Open. Proper notarisation needs a paid Apple Developer
account and is not wired up.

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

Open a file directly from the command line:

```bash
dotnet run --project src/Luma.Presentation -- "C:\videos\clip.mkv"
```

The MSI registers Luma as a handler for the media types it plays, so it appears under
**Open with** and can be picked in **Settings › Default apps**. It does not take any
association for itself — on Windows 10 and later an installer cannot make itself the
default anyway, and doing it without asking would be rude where it can.

### Windows helper scripts

`run.bat` builds and starts the app straight from source, for quick testing:

```bat
run.bat "D:\video\clip.mkv"
```

`instalator.bat` produces an MSI in `dist\`:

```bat
instalator.bat
```

It publishes a **self-contained** `win-x64` build first, so the installed app needs no
.NET runtime on the target machine — which is why the MSI is around 110 MB. Pass a
version to override the one in `Directory.Build.props`: `instalator.bat 1.2.0`.

### macOS build script

`installer/macos/build-dmg.sh` is the counterpart, and produces a disk image in `dist/`:

```bash
./installer/macos/build-dmg.sh
```

It publishes `osx-arm64` self-contained, pulls libvlc and its plugins out of the
official VLC disk image, builds the `.icns`, assembles and ad-hoc signs `Luma.app`,
and packages it with a symlink to `/Applications`. Pass a version to override
`Directory.Build.props`: `./installer/macos/build-dmg.sh 1.2.0`.

The release workflow runs this same script, so the bundle can be changed and tried on
a Mac without pushing a commit to find out.

The script installs the WiX CLI if it is missing, pinned to **5.x**. From v6 onwards WiX
is gated behind the Open Source Maintenance Fee EULA; accepting that is a licensing
decision for whoever ships the app, so the build script does not do it on your behalf.
WiX 5 is MS-RL and needs no agreement.

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
| `PgDn` / `PgUp` | Next / previous file in the folder |
| `R` | Cycle repeat mode |
| `L` | Toggle playlist panel |
| `F` / `F11` | Fullscreen toggle |
| `Esc` | Exit fullscreen |
| `Ctrl`+`O` | Open file |
| Double-click video | Fullscreen toggle |

## Updates

Luma can check an [UpdateHub](https://github.com/Elerdir/updatehub) server for new
releases. It is **off until configured** — a fresh install never contacts anything.

On first run Luma writes `UpdateOptions.json` next to its other settings. Point it at
your server:

```json
{
  "ServerUrl": "https://updates.example.com",
  "AppSlug": "luma",
  "Channel": "stable",
  "Enabled": true
}
```

On the next start, if the server offers a newer release for this platform, a banner
appears above the status line. Installing downloads the artifact, verifies it against
the SHA-256 the server reported, launches it and closes Luma so it can be replaced.

What arrives is executed — as an MSI asking for administrator rights on Windows — so an
update is refused outright unless all of this holds:

| Rule | Why |
|---|---|
| `ServerUrl` is `https` (or `http` on this machine) | plain HTTP over a network can be spoken for by anyone on it |
| The download comes from that same server | UpdateHub serves artifacts from its own origin; anywhere else means tampering or misconfiguration |
| The release has a SHA-256, and the file matches it | UpdateHub computes the hash itself on upload, so a missing one is not normal |

A failed check is silent by design — an unreachable, moved or misconfigured server must
not interrupt someone watching a film — and so is a release that fails these rules.
A hash mismatch at install time is reported in the banner and the download is discarded.

Register the app in UpdateHub with a slug matching `AppSlug`, then let the release
workflow upload builds to it — see the [Releases](#releases) section above.

## Settings

Preferences, window placement and update options are written as one JSON file per
settings type under the user's application data directory:

| Platform | Location |
|---|---|
| Windows | `%APPDATA%\Luma\` |
| Linux / macOS | `~/.config/Luma/` |

Deleting them resets Luma to a fresh install. A missing or corrupt file falls back to
defaults rather than blocking startup.

`PlayerPreferences.json` also remembers where you left each file, so reopening it picks
up from there. That list is deliberately a working set, not a history: the newest 50
positions are kept, and anything untouched for 90 days is dropped.

`InterfaceOptions.json` holds the choices made from the right-click menu:

| Setting | Default | Meaning |
|---|---|---|
| `Language` | `""` | Culture name (`cs`, `en`); empty follows the operating system |
| `LoadWholeFolder` | `true` | Whether opening one file also loads the rest of its folder |

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
