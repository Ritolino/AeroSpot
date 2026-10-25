using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using AeroSpot.Core.Native;

namespace AeroSpot.Core.Preview
{
    [ComImport]
    [Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref Win32.RECT prc);
        void SetRect(ref Win32.RECT prc);
        void DoPreview();
        void Unload();
        void SetFocus();
        void QueryFocus(out IntPtr phwnd);
        [PreserveSig]
        int TranslateAccelerator(ref Win32.MSG pmsg);
    }

    [ComImport]
    [Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [ComImport]
    [Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithItem
    {
        void Initialize([MarshalAs(UnmanagedType.IUnknown)] object psi, uint grfMode);
    }

    [ComImport]
    [Guid("fc480a25-2ba8-11cf-a229-00aa003d7352")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IObjectWithSite
    {
        void SetSite([MarshalAs(UnmanagedType.IUnknown)] object? pUnkSite);
        void GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvSite);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PreviewHandlerFrameInfo
    {
        public IntPtr haccel;
        public uint cAccelEntries;
    }

    [ComImport]
    [Guid("fec87aaf-35f9-447a-adb7-20234491401a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandlerFrame
    {
        void GetWindowContext(ref PreviewHandlerFrameInfo pinfo);
        [PreserveSig]
        int TranslateAccelerator(ref Win32.MSG pmsg);
    }

    public static class PreviewHandlerNativeMethods
    {
        public const uint STGM_READ = 0x00000000;
        public const uint STGM_SHARE_DENY_NONE = 0x00000040;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        public static readonly Guid IidShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateStreamOnFileEx(
            string pszFile,
            uint grfMode,
            uint dwAttributes,
            bool fCreate,
            IntPtr pstmTemplate,
            out IStream ppstm);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SHCreateItemFromParsingName(
            string pszPath,
            IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
    }
}
