# Hublot

**Fluent popups for Windows** — a lightweight “visual PowerToys”: small Windows 11-style cards for your
wireless devices, the music you're playing, USB drives, displays and battery. Every module can be turned
on or off.

*Version française : [LISEZMOI.md](LISEZMOI.md).*

<p>
  <img src="docs/screenshots/now-playing.png" width="420" alt="Now playing card">
  <img src="docs/screenshots/tray-hover.png" width="360" alt="Tray icon hover card">
</p>

## Modules

| Module | What it shows |
|---|---|
| **Wireless devices** | Name, state and battery of Bluetooth devices, Logitech receivers (Unifying, Bolt, Lightspeed) and Xbox controllers when they connect or disconnect; low battery, charging and charge-level alerts. |
| **Now playing** | Cover, title and artist on every new track, with previous / play-pause / next, a clickable progress bar, colors taken from the album art and a keyboard shortcut to keep it on screen. Drag the player to any corner or screen. By default only the major music platforms (Spotify, Deezer, Apple Music, YouTube Music…) trigger it. |
| **USB devices** | Name and state of USB devices; free and used space for drives. |
| **Displays** | Real name, connection (HDMI, DisplayPort, VGA, USB-C…) and resolution of a display when it is plugged in or unplugged. |
| **Audio jack** | Headphones, earphones or a microphone plugged into or unplugged from the jack (when the audio driver reports it). |
| **Power** | PC name and battery level when a laptop is plugged in or unplugged, charge-level and low-battery alerts. |

<p>
  <img src="docs/screenshots/wireless-device.png" width="400" alt="Wireless device card">
  <img src="docs/screenshots/usb-storage.png" width="400" alt="USB drive card">
  <img src="docs/screenshots/display.png" width="400" alt="Display card">
  <img src="docs/screenshots/power.png" width="400" alt="Power card">
  <img src="docs/screenshots/audio-jack.png" width="400" alt="Audio jack card">
</p>

Right-click any popup to open its settings. Hovering the tray icon shows every connected device and what's playing in a single card. Popups follow
the Windows light / dark mode and accent color, with Acrylic or Mica materials on Windows 11.

## Album art colors

The music player can take its colors from the album art, Material You–style: a subtle tint for the card,
and a matching accent for the play button and the progress bar, changing with every track. *Album art
colors* is on by default; turned off, or with a black-and-white cover, the player keeps the Windows
accent color.

<p>
  <img src="docs/screenshots/album-colors-1.png" width="400" alt="Player tinted by a teal album cover">
  <img src="docs/screenshots/album-colors-2.png" width="400" alt="Player tinted by a pink album cover">
  <img src="docs/screenshots/album-colors-3.png" width="400" alt="Player tinted by a red album cover">
  <img src="docs/screenshots/album-colors-4.png" width="400" alt="Player tinted by a blue album cover">
  <img src="docs/screenshots/album-colors-5.png" width="400" alt="Player tinted by an orange album cover">
</p>

## Everything is configurable

Each module has its own settings page. Here is the *Now playing* page, with its defaults: which apps
trigger the player, when it appears, the always-on player shortcut (*Ctrl + Num 0*), progress bar,
album art colors and per-app rules.

<p><img src="docs/screenshots/now-playing-settings.png" width="600" alt="Now playing settings page"></p>

## Install

1. Download **`Hublot-Setup.exe`** from the [latest release](../../releases/latest).
2. Run it and click *Install* — per-user install, no administrator rights.

Windows SmartScreen may warn you because the installer isn't signed yet: click *More info* › *Run anyway*.
Hublot then checks for new versions on GitHub once a day (can be turned off in *Settings › About*).
Uninstall from *Windows Settings › Apps › Installed apps* (you can keep your settings for a later reinstall).

Requires Windows 10 version 2004 or later; Acrylic and Mica need Windows 11 22H2 or later.
The interface is in English or French (Windows language, or chosen in *Settings › General*).

## Lightweight by design

- A single background process with no polling: Hublot waits for Windows notifications and only draws
  during animations — about 3 to 5 MB of memory when idle.
- Native Win32 rendering (GDI+ and the Windows compositor), no web engine.
- No data collected. The only network access is the daily update check against this repository's releases.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
.\build.ps1                 # dist\Hublot-Setup.exe
.\build-store.ps1           # dist\Hublot-<version>.msix (Microsoft Store package, needs the Windows SDK)
dotnet build src\Hublot     # background service (Win32, trimmed)
dotnet build src\Hublot.Core  # settings window and installer (WPF)
```

All interface texts, in French and English, live in [`src/Hublot/Common/Strings.cs`](src/Hublot/Common/Strings.cs).
Architecture notes (in French): [CLAUDE.md](CLAUDE.md).

**Release:** set the same `<Version>` in both `.csproj` files, then push a matching tag (`git tag v3.1.0 && git push --tags`):
GitHub Actions builds `Hublot-Setup.exe` and attaches it to the release.

## License

[MIT](LICENSE)
