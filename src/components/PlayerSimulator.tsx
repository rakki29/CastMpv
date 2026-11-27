import React, { useEffect, useRef, useState } from 'react';
import {
  Camera,
  Cast,
  FolderOpen,
  Link2,
  Maximize2,
  Minimize2,
  Pause,
  Pin,
  Play,
  RotateCcw,
  RotateCw,
  Settings,
  Square,
  Subtitles,
  Volume2,
  VolumeX,
} from 'lucide-react';

interface SimSettings {
  deviceName: string;
  dlnaPort: number;
  autoFullscreenOnCast: boolean;
  autoMinimizeOnStop: boolean;
  autoStartWindows: boolean;
  hardwareAcceleration: boolean;
  subtitleFontSize: number;
  subtitleColor: string;
}

function formatClock(sec: number): string {
  const s = Math.max(0, Math.floor(sec));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const r = s % 60;
  if (h > 0) {
    return `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`;
  }
  return `${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`;
}

export function PlayerSimulator() {
  const [windowVisible, setWindowVisible] = useState(true);
  const [trayMenuOpen, setTrayMenuOpen] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [isPinned, setIsPinned] = useState(false);
  const [isPlaying, setIsPlaying] = useState(false);
  const [isPaused, setIsPaused] = useState(false);
  const [mediaTitle, setMediaTitle] = useState<string>('');
  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(324);
  const [volume, setVolume] = useState(85);
  const [muted, setMuted] = useState(false);
  const [speed, setSpeed] = useState(1.0);
  const [aspect, setAspect] = useState<'Original' | '16:9' | '4:3' | 'Stretch'>('Original');
  const [subDelay, setSubDelay] = useState(0);
  const [audioTrack, setAudioTrack] = useState<'EN AAC 5.1' | 'JP FLAC 2.0'>('EN AAC 5.1');
  const [osdText, setOsdText] = useState<string | null>(null);
  const [hoverTime, setHoverTime] = useState<{ x: number; time: number } | null>(null);
  const [controlsVisible, setControlsVisible] = useState(true);

  const [openUrlModal, setOpenUrlModal] = useState(false);
  const [urlInput, setUrlInput] = useState('https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8');
  const [uaInput, setUaInput] = useState('Mozilla/5.0 (Windows NT 10.0; Win64; x64)');
  const [refInput, setRefInput] = useState('');

  const [settingsModal, setSettingsModal] = useState(false);
  const [simSettings, setSimSettings] = useState<SimSettings>({
    deviceName: 'CastMPV Receiver',
    dlnaPort: 8200,
    autoFullscreenOnCast: true,
    autoMinimizeOnStop: true,
    autoStartWindows: true,
    hardwareAcceleration: true,
    subtitleFontSize: 48,
    subtitleColor: '#FFFFFF',
  });

  const osdTimerRef = useRef<number | null>(null);
  const hideTimerRef = useRef<number | null>(null);

  const showOsd = (msg: string) => {
    setOsdText(msg);
    if (osdTimerRef.current) window.clearTimeout(osdTimerRef.current);
    osdTimerRef.current = window.setTimeout(() => setOsdText(null), 2000);
  };

  useEffect(() => {
    if (!isPlaying || isPaused) return;
    const id = window.setInterval(() => {
      setPosition((prev) => {
        const next = prev + speed;
        if (next >= duration) {
          setIsPlaying(false);
          setIsPaused(false);
          if (simSettings.autoMinimizeOnStop) {
            setIsFullscreen(false);
            setWindowVisible(false);
          }
          return 0;
        }
        return next;
      });
    }, 1000);
    return () => window.clearInterval(id);
  }, [isPlaying, isPaused, speed, duration, simSettings.autoMinimizeOnStop]);

  const handleMouseMoveInPlayer = () => {
    setControlsVisible(true);
    if (hideTimerRef.current) window.clearTimeout(hideTimerRef.current);
    if (isFullscreen) {
      hideTimerRef.current = window.setTimeout(() => setControlsVisible(false), 2400);
    }
  };

  const triggerDlnaCast = () => {
    setTrayMenuOpen(false);
    setWindowVisible(true);
    setIsPlaying(true);
    setIsPaused(false);
    setMediaTitle('DLNA Cast: Big Buck Bunny (1080p HLS)');
    setDuration(596);
    setPosition(14);
    if (simSettings.autoFullscreenOnCast) {
      setIsFullscreen(true);
    }
    showOsd(`Cast received on "${simSettings.deviceName}"`);
  };

  const handleStop = () => {
    setIsPlaying(false);
    setIsPaused(false);
    setPosition(0);
    setMediaTitle('');
    if (simSettings.autoMinimizeOnStop && isFullscreen) {
      setIsFullscreen(false);
    }
    showOsd('Playback stopped');
  };

  return (
    <div className="space-y-6">
      {/* Interactive Action Strip */}
      <div className="flex flex-wrap items-center justify-between gap-4 border border-slate-800 bg-[#13161E] px-5 py-3.5 rounded-lg">
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-300">
          <span className="font-medium text-slate-100">Interactive Desktop Behavior</span>
          <span aria-hidden="true" className="text-slate-600">·</span>
          <span className="text-slate-400">
            Tray state: {windowVisible ? 'Window Open' : 'Minimized to System Tray'}
          </span>
          <span aria-hidden="true" className="text-slate-600">·</span>
          <span className="text-slate-400 font-mono text-xs tabular-nums">
            DLNA: {simSettings.deviceName} (:{simSettings.dlnaPort})
          </span>
        </div>

        <div className="flex items-center gap-2.5 shrink-0">
          <button
            type="button"
            onClick={triggerDlnaCast}
            className="inline-flex items-center gap-2 px-3.5 py-1.5 text-xs font-semibold rounded-md bg-sky-500 text-slate-950 hover:bg-sky-400 transition-colors whitespace-nowrap shrink-0 cursor-pointer"
          >
            <Cast className="w-3.5 h-3.5" />
            Simulate Mobile DLNA Cast
          </button>
          <button
            type="button"
            onClick={() => setWindowVisible((v) => !v)}
            className="px-3 py-1.5 text-xs font-medium rounded-md border border-slate-700 bg-slate-800/70 text-slate-200 hover:bg-slate-800 transition-colors whitespace-nowrap shrink-0 cursor-pointer"
          >
            {windowVisible ? 'Minimize to Tray' : 'Restore from Tray'}
          </button>
        </div>
      </div>

      {/* Player Frame or Minimized Tray View */}
      {windowVisible ? (
        <div
          onMouseMove={handleMouseMoveInPlayer}
          className={`relative border border-slate-800 bg-[#0E0F12] overflow-hidden select-none transition-all ${
            isFullscreen ? 'rounded-none ring-2 ring-sky-500/40' : 'rounded-lg'
          }`}
        >
          {/* Top Bar (Auto-hides in fullscreen) */}
          <div
            className={`flex items-center justify-between gap-4 px-4 h-10 bg-[#181A20] border-b border-slate-800/90 transition-opacity duration-200 ${
              isFullscreen && !controlsVisible ? 'opacity-0 pointer-events-none' : 'opacity-100'
            }`}
          >
            <div className="flex items-center gap-3 min-w-0">
              <span className="text-sm font-bold text-sky-400 whitespace-nowrap shrink-0">CastMPV</span>
              <span className="text-xs text-slate-400 truncate">
                {isPlaying ? mediaTitle : `DLNA Ready (${simSettings.deviceName})`}
              </span>
            </div>

            <div className="flex items-center gap-1 shrink-0">
              <button
                type="button"
                onClick={() => {
                  setIsPlaying(true);
                  setIsPaused(false);
                  setMediaTitle('Sample_Local_4K.mkv');
                  setDuration(420);
                  setPosition(0);
                  showOsd('Loaded Sample_Local_4K.mkv');
                }}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded whitespace-nowrap shrink-0 cursor-pointer"
              >
                <FolderOpen className="w-3.5 h-3.5 text-slate-400" />
                Open File
              </button>
              <button
                type="button"
                onClick={() => setOpenUrlModal(true)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded whitespace-nowrap shrink-0 cursor-pointer"
              >
                <Link2 className="w-3.5 h-3.5 text-slate-400" />
                Open URL
              </button>
              <button
                type="button"
                onClick={() => {
                  setIsPinned((p) => !p);
                  showOsd(!isPinned ? 'Always on Top: ON' : 'Always on Top: OFF');
                }}
                className={`inline-flex items-center gap-1 px-2 py-1 text-xs rounded whitespace-nowrap shrink-0 cursor-pointer ${
                  isPinned ? 'text-sky-400 bg-slate-800' : 'text-slate-300 hover:bg-slate-800'
                }`}
              >
                <Pin className="w-3.5 h-3.5" />
                Pin
              </button>
              <button
                type="button"
                onClick={() => setSettingsModal(true)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded whitespace-nowrap shrink-0 cursor-pointer"
              >
                <Settings className="w-3.5 h-3.5 text-slate-400" />
                Settings
              </button>
            </div>
          </div>

          {/* Video Viewport */}
          <div
            onDoubleClick={() => setIsFullscreen((f) => !f)}
            className="relative h-[400px] bg-[#0B0C0F] flex items-center justify-center overflow-hidden"
          >
            {osdText && (
              <div className="absolute top-4 left-4 z-20 px-3 py-1.5 bg-slate-900/90 border border-slate-700 text-xs font-semibold text-slate-100 rounded">
                {osdText}
              </div>
            )}

            {isPlaying ? (
              <div
                className={`relative flex flex-col items-center justify-center transition-all duration-200 bg-gradient-to-br from-slate-900 via-[#121826] to-slate-950 border border-slate-800/70 ${
                  aspect === 'Stretch'
                    ? 'w-full h-full'
                    : aspect === '4:3'
                    ? 'w-[480px] h-[360px]'
                    : 'w-[640px] h-[360px]'
                }`}
              >
                <div className="text-xs font-mono text-sky-400 mb-2">
                  libmpv · {simSettings.hardwareAcceleration ? 'hwdec=auto-safe (D3D11VA)' : 'hwdec=no (Software)'} · {aspect}
                </div>
                <div className="text-lg font-semibold text-slate-100 px-6 text-center">{mediaTitle}</div>
                <div className="text-xs text-slate-400 mt-2 font-mono tabular-nums">
                  {audioTrack} · Speed {speed}x · Sub Delay {subDelay >= 0 ? `+${subDelay.toFixed(1)}s` : `${subDelay.toFixed(1)}s`}
                </div>

                {/* Live Styled Subtitle Preview */}
                <div
                  className="absolute bottom-7 px-3 py-1 bg-black/60 rounded font-medium text-center"
                  style={{
                    color: simSettings.subtitleColor,
                    fontSize: `${Math.max(12, Math.min(24, simSettings.subtitleFontSize / 3))}px`,
                  }}
                >
                  [Subtitles active · {simSettings.subtitleFontSize}px · {audioTrack}]
                </div>
              </div>
            ) : (
              <div className="text-center max-w-md px-6">
                <p className="text-base font-medium text-slate-300">CastMPV Receiver Ready</p>
                <p className="text-xs text-slate-500 mt-2 leading-relaxed">
                  Click Simulate Mobile DLNA Cast above, open a stream URL, or inspect the complete C# source code in the Source Files tab.
                </p>
              </div>
            )}
          </div>

          {/* Bottom PotPlayer Control Bar */}
          <div
            className={`bg-[#181A20] border-t border-slate-800/90 px-3 pt-2 pb-2.5 transition-opacity duration-200 ${
              isFullscreen && !controlsVisible ? 'opacity-0 pointer-events-none' : 'opacity-100'
            }`}
          >
            {/* Seek Bar with Hover Time Preview */}
            <div
              className="relative h-5 flex items-center cursor-pointer group"
              onMouseMove={(e) => {
                const rect = e.currentTarget.getBoundingClientRect();
                const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                setHoverTime({ x: e.clientX - rect.left, time: ratio * duration });
              }}
              onMouseLeave={() => setHoverTime(null)}
              onClick={(e) => {
                const rect = e.currentTarget.getBoundingClientRect();
                const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                setPosition(ratio * duration);
              }}
            >
              {hoverTime && (
                <div
                  className="absolute -top-6 -translate-x-1/2 px-1.5 py-0.5 bg-slate-950 border border-slate-700 text-[11px] font-mono tabular-nums text-slate-100 rounded pointer-events-none"
                  style={{ left: `${hoverTime.x}px` }}
                >
                  {formatClock(hoverTime.time)}
                </div>
              )}
              <div className="w-full h-1.5 group-hover:h-2 bg-slate-700 rounded-full overflow-hidden transition-all">
                <div
                  className="h-full bg-sky-400"
                  style={{ width: `${duration > 0 ? Math.min(100, (position / duration) * 100) : 0}%` }}
                />
              </div>
            </div>

            {/* Buttons Row */}
            <div className="flex flex-wrap items-center justify-between gap-2 mt-1.5">
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => {
                    if (!isPlaying) {
                      setIsPlaying(true);
                      setIsPaused(false);
                      setMediaTitle('Demo Stream (1080p)');
                    } else {
                      setIsPaused((p) => !p);
                    }
                  }}
                  className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-slate-100 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  {isPlaying && !isPaused ? <Pause className="w-3.5 h-3.5" /> : <Play className="w-3.5 h-3.5" />}
                  {isPlaying && !isPaused ? 'Pause' : 'Play'}
                </button>
                <button
                  type="button"
                  onClick={handleStop}
                  className="inline-flex items-center gap-1 px-2 py-1.5 text-xs text-slate-300 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  <Square className="w-3.5 h-3.5" />
                  Stop
                </button>
                <button
                  type="button"
                  onClick={() => setPosition((p) => Math.max(0, p - 10))}
                  className="inline-flex items-center gap-1 px-2 py-1.5 text-xs text-slate-300 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  <RotateCcw className="w-3.5 h-3.5" />
                  10s
                </button>
                <button
                  type="button"
                  onClick={() => setPosition((p) => Math.min(duration, p + 10))}
                  className="inline-flex items-center gap-1 px-2 py-1.5 text-xs text-slate-300 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  <RotateCw className="w-3.5 h-3.5" />
                  10s
                </button>
                <span className="ml-2 text-xs font-mono tabular-nums text-slate-400">
                  {formatClock(position)} / {formatClock(duration)}
                </span>
              </div>

              <div className="flex items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => setMuted((m) => !m)}
                  className="p-1.5 text-slate-300 hover:bg-slate-800 rounded cursor-pointer"
                  title="Mute"
                >
                  {muted ? <VolumeX className="w-4 h-4 text-rose-400" /> : <Volume2 className="w-4 h-4" />}
                </button>
                <input
                  type="range"
                  min={0}
                  max={100}
                  value={muted ? 0 : volume}
                  onChange={(e) => {
                    setMuted(false);
                    setVolume(Number(e.target.value));
                  }}
                  className="w-20 accent-sky-400 cursor-pointer"
                  aria-label="Volume"
                />
                <button
                  type="button"
                  onClick={() => {
                    const steps = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];
                    const next = steps[(steps.indexOf(speed) + 1) % steps.length];
                    setSpeed(next);
                    showOsd(`Speed: ${next}x`);
                  }}
                  className="px-2 py-1 text-xs font-mono tabular-nums text-slate-200 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  {speed}x
                </button>
                <button
                  type="button"
                  onClick={() => {
                    const modes: Array<'Original' | '16:9' | '4:3' | 'Stretch'> = ['Original', '16:9', '4:3', 'Stretch'];
                    const next = modes[(modes.indexOf(aspect) + 1) % modes.length];
                    setAspect(next);
                    showOsd(`Aspect: ${next}`);
                  }}
                  className="px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  {aspect}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    const next = Math.round((subDelay + 0.5) * 10) / 10;
                    setSubDelay(next > 2 ? -1 : next);
                    showOsd(`Subtitle Delay: ${next > 2 ? '-1.0' : next.toFixed(1)}s`);
                  }}
                  className="inline-flex items-center gap-1 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  <Subtitles className="w-3.5 h-3.5" />
                  Sub
                </button>
                <button
                  type="button"
                  onClick={() => {
                    const next = audioTrack === 'EN AAC 5.1' ? 'JP FLAC 2.0' : 'EN AAC 5.1';
                    setAudioTrack(next);
                    showOsd(`Audio Track: ${next}`);
                  }}
                  className="px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  Audio
                </button>
                <button
                  type="button"
                  onClick={() => showOsd(`Screenshot saved: CastMPV_${Date.now()}.png`)}
                  className="inline-flex items-center gap-1 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  <Camera className="w-3.5 h-3.5" />
                  Shot
                </button>
                <button
                  type="button"
                  onClick={() => setIsFullscreen((f) => !f)}
                  className="inline-flex items-center gap-1 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 rounded cursor-pointer whitespace-nowrap shrink-0"
                >
                  {isFullscreen ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
                  {isFullscreen ? 'Exit' : 'Full'}
                </button>
              </div>
            </div>
          </div>
        </div>
      ) : (
        <div className="border border-slate-800 bg-[#13161E] rounded-lg p-8 text-center">
          <p className="text-sm font-medium text-slate-200">CastMPV is running silently in the Windows System Tray</p>
          <p className="text-xs text-slate-400 mt-1.5">
            Send a DLNA cast command or click the System Tray icon below to wake the player.
          </p>
        </div>
      )}

      {/* Simulated Windows Taskbar System Tray */}
      <div className="relative flex items-center justify-end border border-slate-800 bg-[#111319] px-4 py-2 rounded-lg">
        <span className="text-xs text-slate-500 mr-auto">Windows Notification Area (Right-click or click tray icon)</span>
        <button
          type="button"
          onClick={() => setTrayMenuOpen((o) => !o)}
          className="inline-flex items-center gap-2 px-3 py-1 rounded bg-slate-800/80 hover:bg-slate-800 text-xs text-slate-200 cursor-pointer"
        >
          <span className="inline-flex items-center justify-center w-4 h-4 rounded-sm bg-sky-500 text-slate-950 font-bold text-[10px]">
            C
          </span>
          <span>CastMPV Tray</span>
        </button>

        {trayMenuOpen && (
          <div className="absolute right-4 bottom-12 z-30 w-64 border border-slate-700 bg-[#1D2028] rounded-md shadow-xl py-1.5 text-xs text-slate-200">
            <button
              type="button"
              onClick={() => {
                setWindowVisible(true);
                setTrayMenuOpen(false);
              }}
              className="w-full text-left px-3.5 py-2 font-semibold hover:bg-slate-700/60 cursor-pointer"
            >
              Open CastMPV
            </button>
            <button
              type="button"
              onClick={() => {
                setWindowVisible(true);
                setOpenUrlModal(true);
                setTrayMenuOpen(false);
              }}
              className="w-full text-left px-3.5 py-2 hover:bg-slate-700/60 cursor-pointer"
            >
              Open Stream / URL...
            </button>
            <div className="my-1 border-t border-slate-700/80" />
            <div className="px-3.5 py-1.5 text-slate-400">
              DLNA: {simSettings.deviceName} (Port {simSettings.dlnaPort})
            </div>
            <button
              type="button"
              onClick={() =>
                setSimSettings((s) => ({ ...s, autoStartWindows: !s.autoStartWindows }))
              }
              className="w-full text-left px-3.5 py-2 hover:bg-slate-700/60 flex items-center justify-between cursor-pointer"
            >
              <span>Start with Windows</span>
              <span className="font-mono text-sky-400">{simSettings.autoStartWindows ? 'ON' : 'OFF'}</span>
            </button>
            <button
              type="button"
              onClick={() => {
                setWindowVisible(true);
                setSettingsModal(true);
                setTrayMenuOpen(false);
              }}
              className="w-full text-left px-3.5 py-2 hover:bg-slate-700/60 cursor-pointer"
            >
              Settings...
            </button>
            <div className="my-1 border-t border-slate-700/80" />
            <button
              type="button"
              onClick={() => {
                setWindowVisible(false);
                setIsPlaying(false);
                setTrayMenuOpen(false);
              }}
              className="w-full text-left px-3.5 py-2 text-rose-400 hover:bg-slate-700/60 cursor-pointer"
            >
              Exit CastMPV
            </button>
          </div>
        )}
      </div>

      {/* Open URL Modal */}
      {openUrlModal && (
        <div className="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-4">
          <div className="w-full max-w-lg border border-slate-700 bg-[#181A20] rounded-lg p-5 space-y-4">
            <h3 className="text-base font-semibold text-slate-100">Open Stream / URL</h3>
            <div className="space-y-3 text-xs">
              <label className="block space-y-1">
                <span className="text-slate-300">Video or Stream URL (HTTP, M3U8, MPD, RTMP, IPTV)</span>
                <input
                  type="text"
                  value={urlInput}
                  onChange={(e) => setUrlInput(e.target.value)}
                  className="w-full px-3 py-2 bg-[#22252E] border border-slate-700 rounded text-slate-100 font-mono"
                />
              </label>
              <label className="block space-y-1">
                <span className="text-slate-400">User-Agent (optional)</span>
                <input
                  type="text"
                  value={uaInput}
                  onChange={(e) => setUaInput(e.target.value)}
                  className="w-full px-3 py-2 bg-[#22252E] border border-slate-700 rounded text-slate-100 font-mono"
                />
              </label>
              <label className="block space-y-1">
                <span className="text-slate-400">Referer Header (optional)</span>
                <input
                  type="text"
                  value={refInput}
                  onChange={(e) => setRefInput(e.target.value)}
                  placeholder="https://example.com/"
                  className="w-full px-3 py-2 bg-[#22252E] border border-slate-700 rounded text-slate-100 font-mono"
                />
              </label>
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setOpenUrlModal(false)}
                className="px-4 py-2 text-xs rounded border border-slate-700 bg-slate-800 text-slate-200 hover:bg-slate-700 cursor-pointer"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => {
                  setOpenUrlModal(false);
                  setIsPlaying(true);
                  setIsPaused(false);
                  setMediaTitle(urlInput.split('/').pop() || 'Network Stream');
                  setPosition(0);
                  setDuration(480);
                  showOsd('Stream loaded via libmpv');
                }}
                className="px-4 py-2 text-xs font-semibold rounded bg-sky-500 text-slate-950 hover:bg-sky-400 cursor-pointer"
              >
                Play Stream
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Settings Modal */}
      {settingsModal && (
        <div className="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-4">
          <div className="w-full max-w-md border border-slate-700 bg-[#181A20] rounded-lg p-5 space-y-4">
            <h3 className="text-base font-semibold text-slate-100">CastMPV Settings</h3>
            <div className="space-y-3 text-xs text-slate-200">
              <label className="block space-y-1">
                <span className="text-slate-400">DLNA Receiver Name</span>
                <input
                  type="text"
                  value={simSettings.deviceName}
                  onChange={(e) => setSimSettings({ ...simSettings, deviceName: e.target.value })}
                  className="w-full px-3 py-1.5 bg-[#22252E] border border-slate-700 rounded"
                />
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={simSettings.autoFullscreenOnCast}
                  onChange={(e) => setSimSettings({ ...simSettings, autoFullscreenOnCast: e.target.checked })}
                />
                <span>Auto-Fullscreen when video is cast</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={simSettings.autoMinimizeOnStop}
                  onChange={(e) => setSimSettings({ ...simSettings, autoMinimizeOnStop: e.target.checked })}
                />
                <span>Minimize to System Tray when playback stops/ends</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={simSettings.autoStartWindows}
                  onChange={(e) => setSimSettings({ ...simSettings, autoStartWindows: e.target.checked })}
                />
                <span>Start CastMPV automatically with Windows</span>
              </label>
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={simSettings.hardwareAcceleration}
                  onChange={(e) => setSimSettings({ ...simSettings, hardwareAcceleration: e.target.checked })}
                />
                <span>Enable Hardware Decoding (auto-safe fallback)</span>
              </label>
              <div className="grid grid-cols-2 gap-3 pt-1">
                <label className="block space-y-1">
                  <span className="text-slate-400">Subtitle Size</span>
                  <input
                    type="number"
                    min={16}
                    max={120}
                    value={simSettings.subtitleFontSize}
                    onChange={(e) => setSimSettings({ ...simSettings, subtitleFontSize: Number(e.target.value) })}
                    className="w-full px-2.5 py-1.5 bg-[#22252E] border border-slate-700 rounded font-mono tabular-nums"
                  />
                </label>
                <label className="block space-y-1">
                  <span className="text-slate-400">Subtitle Color</span>
                  <select
                    value={simSettings.subtitleColor}
                    onChange={(e) => setSimSettings({ ...simSettings, subtitleColor: e.target.value })}
                    className="w-full px-2.5 py-1.5 bg-[#22252E] border border-slate-700 rounded"
                  >
                    <option value="#FFFFFF">White</option>
                    <option value="#FACC15">Yellow</option>
                    <option value="#38BDF8">Sky</option>
                    <option value="#4ADE80">Green</option>
                  </select>
                </label>
              </div>
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setSettingsModal(false)}
                className="px-4 py-2 text-xs font-semibold rounded bg-sky-500 text-slate-950 hover:bg-sky-400 cursor-pointer"
              >
                Save Settings
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
