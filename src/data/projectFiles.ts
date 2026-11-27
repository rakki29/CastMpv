import buildYml from '../../.github/workflows/build.yml?raw';
import csproj from '../../CastMPV/CastMPV.csproj?raw';
import appManifest from '../../CastMPV/app.manifest?raw';
import readmeMd from '../../CastMPV/README.md?raw';
import programCs from '../../CastMPV/Program.cs?raw';
import appSettingsCs from '../../CastMPV/Core/AppSettings.cs?raw';
import startupManagerCs from '../../CastMPV/Core/StartupManager.cs?raw';
import iconFactoryCs from '../../CastMPV/Core/IconFactory.cs?raw';
import mpvNativeCs from '../../CastMPV/Player/MpvNative.cs?raw';
import mpvPlayerCs from '../../CastMPV/Player/MpvPlayer.cs?raw';
import dlnaServerCs from '../../CastMPV/DLNA/DlnaServer.cs?raw';
import themeHelperCs from '../../CastMPV/UI/ThemeHelper.cs?raw';
import darkControlsCs from '../../CastMPV/UI/DarkControls.cs?raw';
import mainFormCs from '../../CastMPV/UI/MainForm.cs?raw';
import openUrlFormCs from '../../CastMPV/UI/OpenUrlForm.cs?raw';
import settingsFormCs from '../../CastMPV/UI/SettingsForm.cs?raw';

export interface ProjectFileEntry {
  id: string;
  path: string;
  category: 'Build & CI' | 'Entry & Core' | 'libmpv Engine' | 'DLNA Renderer' | 'PotPlayer UI';
  language: 'csharp' | 'xml' | 'yaml' | 'markdown';
  summary: string;
  content: string;
}

export const PROJECT_FILES: ProjectFileEntry[] = [
  {
    id: 'csproj',
    path: 'CastMPV/CastMPV.csproj',
    category: 'Build & CI',
    language: 'xml',
    summary: '.NET Framework 4.8 WinExe project supporting x86 and x64 with zero NuGet overhead.',
    content: csproj,
  },
  {
    id: 'workflow',
    path: '.github/workflows/build.yml',
    category: 'Build & CI',
    language: 'yaml',
    summary: 'GitHub Actions matrix workflow downloading libmpv-2.dll (x86/x64) and packaging Windows releases.',
    content: buildYml,
  },
  {
    id: 'manifest',
    path: 'CastMPV/app.manifest',
    category: 'Build & CI',
    language: 'xml',
    summary: 'PerMonitorV2 DPI awareness and Windows 7/8.1/10/11 compatibility manifest.',
    content: appManifest,
  },
  {
    id: 'readme',
    path: 'CastMPV/README.md',
    category: 'Build & CI',
    language: 'markdown',
    summary: 'Build instructions, architecture overview, and usage reference.',
    content: readmeMd,
  },
  {
    id: 'program',
    path: 'CastMPV/Program.cs',
    category: 'Entry & Core',
    language: 'csharp',
    summary: 'Single-instance Mutex, Named Pipe IPC, System Tray NotifyIcon, and DLNA/player lifecycle.',
    content: programCs,
  },
  {
    id: 'settings',
    path: 'CastMPV/Core/AppSettings.cs',
    category: 'Entry & Core',
    language: 'csharp',
    summary: 'Persistent JSON configuration stored in %AppData%\\CastMPV\\settings.json.',
    content: appSettingsCs,
  },
  {
    id: 'startup',
    path: 'CastMPV/Core/StartupManager.cs',
    category: 'Entry & Core',
    language: 'csharp',
    summary: 'Windows HKCU Run registry auto-start toggle launching CastMPV silently with --background.',
    content: startupManagerCs,
  },
  {
    id: 'icon',
    path: 'CastMPV/Core/IconFactory.cs',
    category: 'Entry & Core',
    language: 'csharp',
    summary: 'Programmatic GDI+ vector app and System Tray icon generator.',
    content: iconFactoryCs,
  },
  {
    id: 'mpv-native',
    path: 'CastMPV/Player/MpvNative.cs',
    category: 'libmpv Engine',
    language: 'csharp',
    summary: 'P/Invoke bindings for libmpv-2.dll with architecture-aware DLL loading and UTF-8 marshaling.',
    content: mpvNativeCs,
  },
  {
    id: 'mpv-player',
    path: 'CastMPV/Player/MpvPlayer.cs',
    category: 'libmpv Engine',
    language: 'csharp',
    summary: 'Thread-safe libmpv controller: hwdec auto-safe fallback, User-Agent/Referer, tracks, subtitles, screenshots.',
    content: mpvPlayerCs,
  },
  {
    id: 'dlna',
    path: 'CastMPV/DLNA/DlnaServer.cs',
    category: 'DLNA Renderer',
    language: 'csharp',
    summary: 'Zero-admin TcpListener UPnP HTTP/1.1 + SSDP multicast server for AVTransport, RenderingControl, ConnectionManager.',
    content: dlnaServerCs,
  },
  {
    id: 'mainform',
    path: 'CastMPV/UI/MainForm.cs',
    category: 'PotPlayer UI',
    language: 'csharp',
    summary: 'PotPlayer-style dark player window, auto-hiding fullscreen controls, shortcuts, drag-and-drop, and cast wake-up.',
    content: mainFormCs,
  },
  {
    id: 'controls',
    path: 'CastMPV/UI/DarkControls.cs',
    category: 'PotPlayer UI',
    language: 'csharp',
    summary: 'Owner-drawn SeekTrackBar with hover time preview, VolumeSlider, DarkFlatButton, and dark menus.',
    content: darkControlsCs,
  },
  {
    id: 'openurl',
    path: 'CastMPV/UI/OpenUrlForm.cs',
    category: 'PotPlayer UI',
    language: 'csharp',
    summary: 'Stream URL dialog supporting HTTP/M3U8/MPD/RTMP/IPTV with optional User-Agent and Referer headers.',
    content: openUrlFormCs,
  },
  {
    id: 'settingsform',
    path: 'CastMPV/UI/SettingsForm.cs',
    category: 'PotPlayer UI',
    language: 'csharp',
    summary: 'Settings dialog for DLNA name/port, auto-fullscreen, Windows startup, hwdec, and subtitle defaults.',
    content: settingsFormCs,
  },
  {
    id: 'theme',
    path: 'CastMPV/UI/ThemeHelper.cs',
    category: 'PotPlayer UI',
    language: 'csharp',
    summary: 'Windows 10/11 DWM immersive dark title bar helper and shared dark color palette.',
    content: themeHelperCs,
  },
];
