using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace JustReadTheInstructions
{
    internal static class Wine
    {
        private const int WaitForExit = 1;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern IntPtr GetProcessHeap();
        [DllImport("kernel32.dll")] private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr memory);
        [DllImport("kernel32.dll", EntryPoint = "wine_get_unix_file_name", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr UnixFileName(string windowsPath);
        [DllImport("kernel32.dll", EntryPoint = "wine_get_dos_file_name", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DosFileName(byte[] unixPath);
        [DllImport("ntdll.dll", EntryPoint = "__wine_unix_spawnvp")]
        private static extern int UnixSpawn(IntPtr[] argv, int wait);

        public static readonly bool IsRunning = Environment.OSVersion.Platform == PlatformID.Win32NT
            && GetProcAddress(GetModuleHandleW("ntdll.dll"), "wine_get_version") != IntPtr.Zero;

        public static string UnixPath(string windowsPath)
        {
            var path = UnixFileName(windowsPath);
            if (path == IntPtr.Zero) throw new InvalidOperationException($"No Linux path for {windowsPath}");
            try { return ReadUtf8(path); }
            finally { HeapFree(GetProcessHeap(), 0, path); }
        }

        public static string WindowsPath(string unixPath)
        {
            var path = DosFileName(NullTerminatedUtf8(unixPath));
            if (path == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(path); }
            finally { HeapFree(GetProcessHeap(), 0, path); }
        }

        public static int Run(IList<string> argv)
        {
            var pointers = new IntPtr[argv.Count + 1];
            try
            {
                for (int i = 0; i < argv.Count; i++)
                {
                    var bytes = NullTerminatedUtf8(argv[i]);
                    pointers[i] = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, pointers[i], bytes.Length);
                }
                return UnixSpawn(pointers, WaitForExit);
            }
            finally
            {
                foreach (var pointer in pointers)
                    if (pointer != IntPtr.Zero) Marshal.FreeHGlobal(pointer);
            }
        }

        private static byte[] NullTerminatedUtf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

        private static string ReadUtf8(IntPtr text)
        {
            int length = 0;
            while (Marshal.ReadByte(text, length) != 0) length++;
            var bytes = new byte[length];
            Marshal.Copy(text, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
