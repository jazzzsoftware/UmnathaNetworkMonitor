using System;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Services.Platform
{
    public static class OpenFileDialog
    {
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnNoChangeDir = 0x00000008;
        private const int FileBufferLength = 1024;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public IntPtr lpstrInitialDir;
            public IntPtr lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public IntPtr lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        public static string? Show(IntPtr owner, string title, string filterLabel, string extension)
        {
            string? chosenPath = null;
            IntPtr filterBuffer = IntPtr.Zero;
            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr titleBuffer = IntPtr.Zero;

            try
            {
                string filter = $"{filterLabel}\0*.{extension}\0All Files\0*.*\0\0";
                filterBuffer = Marshal.StringToHGlobalUni(filter);
                titleBuffer = Marshal.StringToHGlobalUni(title);
                fileBuffer = Marshal.AllocHGlobal(FileBufferLength * 2);

                for (int index = 0; index < FileBufferLength; index++)
                {
                    Marshal.WriteInt16(fileBuffer, index * 2, 0);
                }

                OpenFileName ofn = new()
                {
                    lStructSize = Marshal.SizeOf<OpenFileName>(),
                    hwndOwner = owner,
                    lpstrFilter = filterBuffer,
                    nFilterIndex = 1,
                    lpstrFile = fileBuffer,
                    nMaxFile = FileBufferLength,
                    lpstrTitle = titleBuffer,
                    Flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir
                };

                bool picked = GetOpenFileNameW(ref ofn);

                if (picked)
                {
                    chosenPath = Marshal.PtrToStringUni(fileBuffer);
                }

            }
            finally
            {

                if (filterBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(filterBuffer);
                }

                if (fileBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(fileBuffer);
                }

                if (titleBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(titleBuffer);
                }

            }

            return chosenPath;
        }
    }
}
