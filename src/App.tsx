import React, { useMemo, useState } from 'react';
import { Check, Copy, Download, FileCode2, Search } from 'lucide-react';
import { PROJECT_FILES, ProjectFileEntry } from './data/projectFiles';
import { buildZipBlob } from './utils/zipBuilder';
import { PlayerSimulator } from './components/PlayerSimulator';

type WorkspaceTab = 'source' | 'preview' | 'architecture';

export default function App() {
  const [activeTab, setActiveTab] = useState<WorkspaceTab>('source');
  const [selectedFileId, setSelectedFileId] = useState<string>(PROJECT_FILES[0].id);
  const [searchQuery, setSearchQuery] = useState('');
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const filteredFiles = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return PROJECT_FILES;
    return PROJECT_FILES.filter(
      (f) => f.path.toLowerCase().includes(q) || f.summary.toLowerCase().includes(q)
    );
  }, [searchQuery]);

  const activeFile: ProjectFileEntry =
    PROJECT_FILES.find((f) => f.id === selectedFileId) ?? PROJECT_FILES[0];

  const categories = useMemo(() => {
    const order: ProjectFileEntry['category'][] = [
      'Build & CI',
      'Entry & Core',
      'DLNA Renderer',
      'libmpv Engine',
      'PotPlayer UI',
    ];
    return order
      .map((cat) => ({
        name: cat,
        items: filteredFiles.filter((f) => f.category === cat),
      }))
      .filter((g) => g.items.length > 0);
  }, [filteredFiles]);

  const handleCopy = async (file: ProjectFileEntry) => {
    try {
      await navigator.clipboard.writeText(file.content);
      setCopiedId(file.id);
      window.setTimeout(() => setCopiedId(null), 1600);
    } catch {
      // Clipboard blocked in restricted iframe
    }
  };

  const handleDownloadZip = () => {
    const blob = buildZipBlob(
      PROJECT_FILES.map((f) => ({
        path: f.path,
        content: f.content,
      }))
    );
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'CastMPV-Windows-Source.zip';
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  };

  const lines = useMemo(() => activeFile.content.split('\n'), [activeFile]);

  return (
    <div className="min-h-screen bg-[#0B0D12] text-slate-100 flex flex-col">
      {/* Three-Zone Top Bar */}
      <header className="flex items-center justify-between gap-8 px-6 h-14 border-b border-slate-800/90 bg-[#0F1219] shrink-0">
        <a
          href="#top"
          onClick={(e) => {
            e.preventDefault();
            setActiveTab('source');
          }}
          className="text-lg font-bold tracking-tight text-slate-100 whitespace-nowrap shrink-0"
        >
          CastMPV
        </a>

        <nav className="flex items-center gap-6 text-sm font-medium">
          <button
            type="button"
            onClick={() => setActiveTab('source')}
            className={`py-1 transition-colors whitespace-nowrap shrink-0 cursor-pointer border-b-2 ${
              activeTab === 'source'
                ? 'text-sky-400 border-sky-400'
                : 'text-slate-400 border-transparent hover:text-slate-100'
            }`}
          >
            Source Files
          </button>
          <button
            type="button"
            onClick={() => setActiveTab('preview')}
            className={`py-1 transition-colors whitespace-nowrap shrink-0 cursor-pointer border-b-2 ${
              activeTab === 'preview'
                ? 'text-sky-400 border-sky-400'
                : 'text-slate-400 border-transparent hover:text-slate-100'
            }`}
          >
            Player Preview
          </button>
          <button
            type="button"
            onClick={() => setActiveTab('architecture')}
            className={`py-1 transition-colors whitespace-nowrap shrink-0 cursor-pointer border-b-2 ${
              activeTab === 'architecture'
                ? 'text-sky-400 border-sky-400'
                : 'text-slate-400 border-transparent hover:text-slate-100'
            }`}
          >
            Architecture &amp; CI
          </button>
        </nav>

        <div className="shrink-0">
          <button
            type="button"
            onClick={handleDownloadZip}
            className="inline-flex items-center gap-2 px-4 py-2 text-xs font-semibold rounded-md bg-sky-500 text-slate-950 hover:bg-sky-400 transition-colors whitespace-nowrap shrink-0 cursor-pointer"
          >
            <Download className="w-3.5 h-3.5" />
            Download Source (.zip)
          </button>
        </div>
      </header>

      {/* Main Content */}
      {activeTab === 'source' && (
        <div className="flex-1 flex min-h-0 max-w-[1440px] w-full mx-auto">
          {/* File Explorer Sidebar */}
          <aside className="w-72 border-r border-slate-800/90 bg-[#0F1219]/60 flex flex-col shrink-0">
            <div className="p-3 border-b border-slate-800/80">
              <div className="relative">
                <Search className="w-3.5 h-3.5 text-slate-500 absolute left-2.5 top-1/2 -translate-y-1/2" />
                <input
                  type="text"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder="Filter files..."
                  className="w-full pl-8 pr-3 py-1.5 text-xs bg-[#161A24] border border-slate-800 rounded text-slate-200 placeholder:text-slate-500 focus:outline-none focus:border-sky-500"
                />
              </div>
            </div>

            <div className="flex-1 overflow-y-auto p-3 space-y-5">
              {categories.length === 0 ? (
                <p className="text-xs text-slate-500 px-2 py-4">No matching project files.</p>
              ) : (
                categories.map((group) => (
                  <div key={group.name} className="space-y-1">
                    <div className="px-2 text-[11px] font-semibold text-slate-500">
                      {group.name}
                    </div>
                    {group.items.map((file) => {
                      const isSelected = file.id === activeFile.id;
                      const shortName = file.path.split('/').pop() ?? file.path;
                      return (
                        <button
                          key={file.id}
                          type="button"
                          onClick={() => setSelectedFileId(file.id)}
                          className={`w-full flex items-center gap-2 px-2.5 py-1.5 text-left text-xs rounded transition-colors cursor-pointer ${
                            isSelected
                              ? 'bg-sky-500/15 text-sky-300 font-medium'
                              : 'text-slate-300 hover:bg-slate-800/70'
                          }`}
                        >
                          <FileCode2 className="w-3.5 h-3.5 shrink-0 text-slate-500" />
                          <span className="truncate font-mono">{shortName}</span>
                        </button>
                      );
                    })}
                  </div>
                ))
              )}
            </div>
          </aside>

          {/* Code Viewer */}
          <main className="flex-1 min-w-0 flex flex-col bg-[#0B0D12]">
            <div className="flex items-center justify-between gap-4 px-6 py-3.5 border-b border-slate-800/90 bg-[#11141D]">
              <div className="min-w-0">
                <div className="text-sm font-mono font-medium text-slate-100 truncate">
                  {activeFile.path}
                </div>
                <div className="flex items-center gap-2 text-xs text-slate-400 mt-0.5">
                  <span>{activeFile.category}</span>
                  <span aria-hidden="true">·</span>
                  <span className="tabular-nums">{lines.length} lines</span>
                  <span aria-hidden="true">·</span>
                  <span className="truncate">{activeFile.summary}</span>
                </div>
              </div>

              <button
                type="button"
                onClick={() => handleCopy(activeFile)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded border border-slate-700 bg-slate-800/80 text-slate-200 hover:bg-slate-700 transition-colors whitespace-nowrap shrink-0 cursor-pointer"
              >
                {copiedId === activeFile.id ? (
                  <>
                    <Check className="w-3.5 h-3.5 text-emerald-400" />
                    Copied
                  </>
                ) : (
                  <>
                    <Copy className="w-3.5 h-3.5" />
                    Copy File
                  </>
                )}
              </button>
            </div>

            <div className="flex-1 overflow-auto p-5">
              <table className="w-full border-collapse text-xs font-mono leading-relaxed">
                <tbody>
                  {lines.map((line, idx) => (
                    <tr key={idx} className="hover:bg-slate-900/50">
                      <td className="w-12 pr-4 text-right text-slate-600 select-none tabular-nums align-top">
                        {idx + 1}
                      </td>
                      <td className="text-slate-200 whitespace-pre">{line || ' '}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </main>
        </div>
      )}

      {activeTab === 'preview' && (
        <main className="flex-1 max-w-[1200px] w-full mx-auto px-6 py-8">
          <div className="mb-6">
            <h1 className="text-2xl font-bold text-slate-100">
              PotPlayer-Style Desktop UI &amp; Zero-Touch DLNA Flow
            </h1>
            <p className="text-sm text-slate-400 mt-1">
              Test the System Tray menu, Open URL headers dialog, Settings panel, and automatic fullscreen cast wake-up.
            </p>
          </div>
          <PlayerSimulator />
        </main>
      )}

      {activeTab === 'architecture' && (
        <main className="flex-1 max-w-[1160px] w-full mx-auto px-6 py-8 space-y-8">
          <div>
            <h1 className="text-2xl font-bold text-slate-100">
              CastMPV Technical Architecture &amp; Package Footprint
            </h1>
            <p className="text-sm text-slate-400 mt-1.5 max-w-3xl">
              Built on .NET Framework 4.8 WinForms with zero NuGet dependencies and direct P/Invoke bindings to libmpv-2.dll, keeping the entire portable Windows package within 35–45 MB for both 32-bit (x86) and 64-bit (x64) systems.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="border border-slate-800 bg-[#12151E] rounded-lg p-4">
              <div className="text-xs text-slate-400">Target Runtime</div>
              <div className="text-lg font-semibold text-slate-100 mt-1">.NET 4.8 WinExe</div>
              <div className="text-xs text-slate-500 mt-1">Preinstalled on Windows 10/11</div>
            </div>
            <div className="border border-slate-800 bg-[#12151E] rounded-lg p-4">
              <div className="text-xs text-slate-400">Architectures</div>
              <div className="text-lg font-semibold text-slate-100 mt-1">x86 &amp; x64</div>
              <div className="text-xs text-slate-500 mt-1">Matrix CI build + native DLL</div>
            </div>
            <div className="border border-slate-800 bg-[#12151E] rounded-lg p-4">
              <div className="text-xs text-slate-400">DLNA Transport</div>
              <div className="text-lg font-semibold text-slate-100 mt-1">Zero-Admin TCP</div>
              <div className="text-xs text-slate-500 mt-1">SSDP 239.255.255.250:1900</div>
            </div>
            <div className="border border-slate-800 bg-[#12151E] rounded-lg p-4">
              <div className="text-xs text-slate-400">Release Footprint</div>
              <div className="text-lg font-semibold text-slate-100 mt-1 tabular-nums">~36–44 MB</div>
              <div className="text-xs text-slate-500 mt-1">CastMPV.exe (~90 KB) + libmpv</div>
            </div>
          </div>

          <div className="border border-slate-800 bg-[#12151E] rounded-lg overflow-hidden">
            <div className="px-5 py-3.5 border-b border-slate-800 text-sm font-semibold text-slate-100">
              Module Breakdown
            </div>
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-slate-800 text-slate-400">
                  <th className="py-2.5 px-5 font-medium">Component</th>
                  <th className="py-2.5 px-5 font-medium">File</th>
                  <th className="py-2.5 px-5 font-medium">Responsibilities</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/80 text-slate-300">
                <tr>
                  <td className="py-3 px-5 font-medium text-slate-100">Tray &amp; Single-Instance</td>
                  <td className="py-3 px-5 font-mono text-sky-400">CastMPV/Program.cs</td>
                  <td className="py-3 px-5">Global Mutex, NamedPipe IPC, System Tray NotifyIcon, silent --background boot</td>
                </tr>
                <tr>
                  <td className="py-3 px-5 font-medium text-slate-100">DLNA Media Renderer</td>
                  <td className="py-3 px-5 font-mono text-sky-400">CastMPV/DLNA/DlnaServer.cs</td>
                  <td className="py-3 px-5">SSDP multicast discovery, AVTransport/RenderingControl/ConnectionManager SOAP &amp; DIDL-Lite</td>
                </tr>
                <tr>
                  <td className="py-3 px-5 font-medium text-slate-100">Playback Engine</td>
                  <td className="py-3 px-5 font-mono text-sky-400">CastMPV/Player/MpvPlayer.cs</td>
                  <td className="py-3 px-5">libmpv-2.dll wid rendering, hwdec=auto-safe, UA/Referer headers, tracks, subs, screenshots</td>
                </tr>
                <tr>
                  <td className="py-3 px-5 font-medium text-slate-100">PotPlayer Dark UI</td>
                  <td className="py-3 px-5 font-mono text-sky-400">CastMPV/UI/MainForm.cs</td>
                  <td className="py-3 px-5">Auto-hiding fullscreen overlay, hover seek preview, shortcuts, drag-and-drop, cast wake-up</td>
                </tr>
                <tr>
                  <td className="py-3 px-5 font-medium text-slate-100">CI/CD Release Pipeline</td>
                  <td className="py-3 px-5 font-mono text-sky-400">.github/workflows/build.yml</td>
                  <td className="py-3 px-5">Fetches latest shinchiro libmpv-2.dll (x64/x86), compiles Release binaries, packages zips</td>
                </tr>
              </tbody>
            </table>
          </div>
        </main>
      )}
    </div>
  );
}
