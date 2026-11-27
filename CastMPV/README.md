# CastMPV

Lightweight Windows desktop media player and zero-touch UPnP/DLNA Media Renderer powered by `libmpv` (.NET Framework 4.8, compatible with Windows 32-bit x86 and 64-bit x64).

## Features

- **System Tray Background Service & Windows Auto-Startup**: Runs silently in the notification area with minimal idle memory and a tray context menu (`Open CastMPV`, `Open Stream / URL`, `DLNA Status`, `Start with Windows`, `Settings`, `Exit`).
- **Zero-Touch DLNA / UPnP Media Renderer**: Discoverable on LAN/Wi-Fi as `"CastMPV Receiver"` (customizable). Automatically wakes up, optionally enters fullscreen, and starts playing cast streams immediately with full remote Play, Pause, Seek, Stop, Volume, and Mute control.
- **Universal `libmpv` Engine**: Plays MP4, MKV, AVI, TS, MOV, WebM, HTTP/HTTPS, M3U8/HLS, MPD/DASH, RTMP, and IPTV streams with custom `User-Agent` and `Referer` header support and `hwdec=auto-safe` hardware acceleration (automatic software fallback on older 32-bit PCs).
- **PotPlayer-Style Controls**: Auto-hiding fullscreen overlay, hover seek-bar time preview, playback speed (`0.5x`–`2.0x`), aspect ratio switcher (`Original`, `16:9`, `4:3`, `Stretch`), external subtitles (`SRT`, `ASS`, `VTT`) with delay/font/color settings, audio track switcher, and PNG screenshots.

## Building Locally (Windows)

1. Download `libmpv-2.dll` (matching `x64` or `x86`) from [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake/releases).
2. Place `libmpv-2.dll` in `CastMPV/native/x64/libmpv-2.dll` (or `native/x86/libmpv-2.dll`).
3. Build with .NET SDK or MSBuild:

```powershell
dotnet build CastMPV/CastMPV.csproj -c Release -p:Platform=x64 -o dist/CastMPV-win-x64
```

## Automated GitHub Actions Release

Push to `main` (or tag `v1.0.0`) to trigger `.github/workflows/build.yml`, which automatically downloads `libmpv-2.dll` for both `x64` and `x86`, builds `CastMPV.exe`, verifies the ~35–45 MB package size, and uploads `CastMPV-win-x64.zip` and `CastMPV-win-x86.zip`.
