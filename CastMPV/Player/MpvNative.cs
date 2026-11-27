using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CastMPV.Player
{
    internal enum MpvFormat
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
        NodeArray = 7,
        NodeMap = 8,
        ByteArray = 9
    }

    internal enum MpvEventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        ClientMessage = 16,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        QueueOverflow = 24,
        Hook = 25
    }

    internal enum MpvEndFileReason
    {
        Eof = 0,
        Stop = 2,
        Quit = 3,
        Error = 4,
        Redirect = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEvent
    {
        public MpvEventId EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventProperty
    {
        public IntPtr Name;
        public MpvFormat Format;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MpvEventEndFile
    {
        public MpvEndFileReason Reason;
        public int Error;
        public long PlaylistEntryId;
        public long PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }

    internal static class MpvNative
    {
        private const string DllName = "libmpv-2.dll";
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
        private const int LC_NUMERIC = 4;

        private static readonly object _initLock = new object();
        private static volatile bool _dllLoaded;
        private static string _loadedDllDirectory;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string NewDirectory);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr setlocale(int category, string locale);

        public static void IsolateProcessEnvironment()
        {
            // Clear OpenSSL/TLS environment variables inherited from system tools
            // (Git, Strawberry Perl, OpenVPN, Python, etc.) before loading libmpv-2.dll.
            string[] varsToClear =
            {
                "OPENSSL_CONF",
                "OPENSSL_MODULES",
                "OPENSSL_ENGINES",
                "OPENSSL_FORCE_FIPS_MODE",
                "OPENSSL_WIN32_UTF8",
                "RANDFILE",
                "SSL_CERT_FILE",
                "SSL_CERT_DIR",
                "CURL_CA_BUNDLE"
            };

            foreach (string varName in varsToClear)
            {
                try { Environment.SetEnvironmentVariable(varName, null); } catch { }
            }

            // libmpv requires CRT LC_NUMERIC to be "C"
            try { setlocale(LC_NUMERIC, "C"); } catch { }
        }

        private static void IsolateDllDirectory(string dllDir)
        {
            if (string.IsNullOrWhiteSpace(dllDir)) return;

            try
            {
                // Restrict DLL search strictly to app dir, added dir, and System32
                // so conflicting system PATH DLLs (e.g., third-party libcrypto/libssl) are never loaded.
                if (SetDefaultDllDirectories(
                    LOAD_LIBRARY_SEARCH_APPLICATION_DIR |
                    LOAD_LIBRARY_SEARCH_USER_DIRS |
                    LOAD_LIBRARY_SEARCH_SYSTEM32))
                {
                    AddDllDirectory(dllDir);
                    return;
                }
            }
            catch
            {
                // Fallback on unpatched Windows 7
            }

            try { SetDllDirectory(dllDir); } catch { }
        }

        public static bool EnsureLibraryLoaded(out string errorMessage)
        {
            lock (_initLock)
            {
                if (_dllLoaded)
                {
                    errorMessage = null;
                    return true;
                }

                IsolateProcessEnvironment();

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string archDir = Environment.Is64BitProcess ? "x64" : "x86";
                string[] candidates =
                {
                    Path.Combine(baseDir, DllName),
                    Path.Combine(baseDir, archDir, DllName),
                    Path.Combine(baseDir, "native", archDir, DllName)
                };

                foreach (string path in candidates)
                {
                    if (!File.Exists(path)) continue;

                    string dir = Path.GetDirectoryName(path);
                    IsolateDllDirectory(dir);

                    IntPtr h = LoadLibraryEx(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                    if (h == IntPtr.Zero)
                        h = LoadLibrary(path);

                    if (h != IntPtr.Zero)
                    {
                        _dllLoaded = true;
                        _loadedDllDirectory = dir;
                        errorMessage = null;
                        return true;
                    }
                }

                // Fallback to System32 / isolated search
                IntPtr sysHandle = LoadLibrary(DllName);
                if (sysHandle != IntPtr.Zero)
                {
                    _dllLoaded = true;
                    errorMessage = null;
                    return true;
                }

                errorMessage =
                    $"Could not load {DllName} ({archDir}).\n\n" +
                    $"Please place the {archDir} version of {DllName} in:\n{baseDir}";
                return false;
            }
        }

        /// <summary>
        /// Safely isolates environment/DLL paths, calls mpv_create(), applies options,
        /// and calls mpv_initialize() on a dedicated MTA thread to avoid WinForms STA COM
        /// and OpenSSL/CRT thread-locking conflicts on Windows.
        /// </summary>
        public static bool CreateAndInitializeOnMtaThread(
            Action<IntPtr> configureOptions,
            out IntPtr ctx,
            out string errorMessage)
        {
            IntPtr createdCtx = IntPtr.Zero;
            string threadError = null;

            var initThread = new Thread(() =>
            {
                lock (_initLock)
                {
                    if (!EnsureLibraryLoaded(out string loadErr))
                    {
                        threadError = loadErr;
                        return;
                    }

                    // Re-ensure clean CRT locale & env on the dedicated MTA thread
                    IsolateProcessEnvironment();
                    if (!string.IsNullOrEmpty(_loadedDllDirectory))
                        IsolateDllDirectory(_loadedDllDirectory);

                    createdCtx = mpv_create();
                    if (createdCtx == IntPtr.Zero)
                    {
                        threadError = "mpv_create() returned null.";
                        return;
                    }

                    try
                    {
                        configureOptions?.Invoke(createdCtx);
                        int res = mpv_initialize(createdCtx);
                        if (res < 0)
                        {
                            threadError = "mpv_initialize failed: " + GetErrorString(res);
                            mpv_terminate_destroy(createdCtx);
                            createdCtx = IntPtr.Zero;
                        }
                    }
                    catch (Exception ex)
                    {
                        threadError = "libmpv initialization exception: " + ex.Message;
                        if (createdCtx != IntPtr.Zero)
                        {
                            try { mpv_terminate_destroy(createdCtx); } catch { }
                            createdCtx = IntPtr.Zero;
                        }
                    }
                }
            })
            {
                IsBackground = true,
                Name = "CastMPV-MpvInit-MTA"
            };

            initThread.SetApartmentState(ApartmentState.MTA);
            initThread.Start();
            initThread.Join();

            ctx = createdCtx;
            errorMessage = threadError;
            return ctx != IntPtr.Zero && string.IsNullOrEmpty(errorMessage);
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_wakeup(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_error_string(int error);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option(IntPtr ctx, byte[] name, MpvFormat format, ref long data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr ctx, IntPtr[] args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command_async(IntPtr ctx, ulong replyUserdata, IntPtr[] args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command_string(IntPtr ctx, byte[] args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(IntPtr ctx, byte[] name, MpvFormat format, ref double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(IntPtr ctx, byte[] name, MpvFormat format, ref long data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property(IntPtr ctx, byte[] name, MpvFormat format, ref int data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, byte[] name, MpvFormat format, out double data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, byte[] name, MpvFormat format, out long data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, byte[] name, MpvFormat format, out int data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata, byte[] name, MpvFormat format);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        public static byte[] ToUtf8(string value)
        {
            if (value == null) return new byte[] { 0 };
            int count = Encoding.UTF8.GetByteCount(value);
            byte[] buf = new byte[count + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, buf, 0);
            buf[count] = 0;
            return buf;
        }

        public static string FromUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            if (len == 0) return string.Empty;
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        public static string GetErrorString(int err)
        {
            return FromUtf8(mpv_error_string(err)) ?? $"mpv error {err}";
        }
    }
}
