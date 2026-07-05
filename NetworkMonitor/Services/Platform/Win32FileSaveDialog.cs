using System.Runtime.InteropServices;

namespace NetworkMonitor.Services.Platform
{
    public static class Win32FileSaveDialog
    {
        private const uint SigdnFileSysPath = 0x80058000;
        private const int ErrorCancelled = unchecked((int)0x800704C7);

        public static string? PickSavePath(IntPtr ownerHandle, string suggestedFileName, string filterDescription, string extension, string? title = null)
        {
            string? resultPath = null;
            IFileDialog dialog = (IFileDialog)new FileSaveDialogComClass();

            try
            {
                ComdlgFilterSpec[] filters = new ComdlgFilterSpec[]
                {
                    new ComdlgFilterSpec
                    {
                        FriendlyName = filterDescription,
                        Pattern = "*" + extension
                    }
                };
                dialog.SetFileTypes((uint)filters.Length, filters);
                dialog.SetDefaultExtension(extension.TrimStart('.'));
                dialog.SetFileName(suggestedFileName);

                if (!string.IsNullOrEmpty(title))
                {
                    dialog.SetTitle(title);
                }

                int showResult = dialog.Show(ownerHandle);

                if (showResult != ErrorCancelled)
                {
                    Marshal.ThrowExceptionForHR(showResult);
                    dialog.GetResult(out IShellItem item);
                    item.GetDisplayName(SigdnFileSysPath, out IntPtr pathPointer);
                    resultPath = Marshal.PtrToStringUni(pathPointer);
                    Marshal.FreeCoTaskMem(pathPointer);
                    Marshal.ReleaseComObject(item);
                }

            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }

            return resultPath;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ComdlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string FriendlyName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Pattern;
    }

    [ComImport]
    [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    internal class FileSaveDialogComClass
    {
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray)] ComdlgFilterSpec[] filterSpec);

        void SetFileTypeIndex(uint fileType);

        void GetFileTypeIndex(out uint fileType);

        void Advise(IntPtr eventsHandler, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IntPtr shellItem);

        void SetFolder(IntPtr shellItem);

        void GetFolder(out IntPtr shellItem);

        void GetCurrentSelection(out IntPtr shellItem);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem shellItem);

        void AddPlace(IntPtr shellItem, int placement);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);

        void Close([MarshalAs(UnmanagedType.Error)] int result);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr instance);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint nameKind, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }
}
