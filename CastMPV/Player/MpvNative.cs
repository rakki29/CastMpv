using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

        public static bool EnsureLibraryLoaded(out string errorMessage)
        {
            // Prevent external system OpenSSL env vars (e.g., from Git/Perl/OpenVPN)
            // from interfering with internal TLS/crypto inside libmpv-2.dll.
            try
            {
                Environment.SetEnvironmentVariable("OPENSSL_CONF", null);
                Environment.SetEnvironmentVariable("OPENSSL_MODULES", null);
                Environment.SetEnvironmentVariable("OPENSSL_ENGINES", null);
            }
            catch
            {
                // Ignore if restricted
            }

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
                if (File.Exists(path))
                {
                    IntPtr h = LoadLibraryEx(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                    if (h == IntPtr.Zero)
                        h = LoadLibrary(path);

                    if (h != IntPtr.Zero)
                    {
                        errorMessage = null;
                        return true;
                    }
                }
            }

            // Try system PATH fallback
            IntPtr sysHandle = LoadLibrary(DllName);
            if (sysHandle != IntPtr.Zero)
            {
                errorMessage = null;
                return true;
            }

            errorMessage =
                $"Could not load {DllName} ({archDir}).\n\n" +
                $"Please place the {archDir} version of {DllName} in:\n{baseDir}";
            return false;
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
