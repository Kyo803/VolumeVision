# V^2 (VolumeVision)

A custom Windows volume bar to replace the stock one â€” a liquid-glass pill (designed in Figma) with system volume, Spotify control, and full theming.

## What it does

- **System volume OSD** â€” a bottom-center pill appears on volume keys instead of the Windows flyout (the native bar is suppressed for intercepted keys)
- **Spotify frame** â€” auto-switches to a media view: previous / play-pause / next, song **title on hover**, live **album art** in the center, green **progress ring**, and a separate **per-app Spotify volume** bar (no Spotify API key needed)
- **Sliders you can drag** â€” click-drag works even outside the bar (mouse capture), hover holds the pill open
- **4 docks** â€” bottom / top / left / right from Settings; side docks turn the pill 90Â° (speaker stays upright, chevrons become up/down, drags go vertical)
- **Theming** â€” 7 colors, glass opacity, gloss intensity, size, live preview pane, Photoshop-style color picker, persists to `%AppData%\VolumeOSD\settings.json`
- **Wallpapers** â€” static image or animated GIF behind the glass (Settings â†’ Wallpaper), plus a built-in **wallpaper studio**: crop/pan/zoom locked to the pill shape, brightness/contrast/saturation/B&W, Ken Burns animation preview, one-click PNG snapshot or animated-GIF export straight onto the pill
- **Background citizen** â€” starts with Windows, tray icon (show/settings/autostart/exit), EcoQoS + slow polling while hidden, ~1â€“2 MB trimmed working set idle

## Install

**Option A â€” installer (recommended):** download `V^2-Setup-1.1.0.exe` from
[Releases](https://github.com/Kyo803/VolumeVision/releases), run it, done.
No admin needed â€” one file installs the app, Start Menu entries, uninstaller,
and login autostart (on by default, toggle anytime from the tray icon).

**Option B â€” portable:** download `V^2.exe` (self-contained, ~236 MB, no .NET needed) and run it.

## Use

| Input | Action |
|---|---|
| Volume keys | System volume + pill |
| `Alt+X` / `Alt+S` | Summon overlay |
| `Alt+Z` / `Alt+C` (`Alt+Q` fallback) | Previous / next track |
| `Alt+Shift+Z` / `Alt+Shift+C` | System / Spotify frame |
| `Alt+Shift+A` / `Alt+Shift+D` | Nudge active slider âˆ“5% |
| `Ctrl+Shift+V` | Summon Â· `Ctrl+Shift+S` settings Â· `Ctrl+Shift+Plus/Minus` resize |
| Hover pill | Holds it open Â· double-click empty area opens settings |

## Build from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows 10/11 x64.

```powershell
dotnet build VolumeOSD.csproj -c Release
dotnet run --project VolumeOSD.csproj -c Release
```

Self-contained single exe:

```powershell
dotnet publish VolumeOSD.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishReadyToRun=true -o dist/win-x64
```

Installer (needs [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
iscc installer.iss   # -> dist/V^2-Setup-1.1.0.exe
```

## How it works

- **WPF (.NET 8)** overlay: borderless topmost pill, acrylic-style translucency
- **Volume keys** are swallowed with a low-level keyboard hook and applied via CoreAudio (NAudio), so Explorer never shows its OSD
- **Spotify** via Windows SMTC (transport/title/progress/art) + CoreAudio sessions (per-app volume) â€” detects `Spotify.exe` automatically
- Single instance, crash-logged to `%Temp%\V^2.log`

## Privacy

Everything is local. No accounts, no network calls, no telemetry. The app reads the currently playing track (title/art) only to render the pill, and writes settings + a debug log on your own machine.

## Uninstall

In-app (portable or installed): tray icon â†’ **Uninstallâ€¦** (or Settings â†’ Uninstall) â€”
confirms, then removes the login entry, settings, shortcuts and the exe itself.
Installer version: also removable from Settings â†’ Apps (same cleanup).
