using System.Runtime.InteropServices;

namespace Med.Desktop;

/// <summary>
/// Иконка Dock на macOS берётся из .app бандла, а не из Window.Icon.
/// При <c>dotnet run</c> бандла нет — выставляем NSApplication.applicationIconImage вручную.
/// </summary>
internal static class MacDockIcon
{
    private const double DockIconSide = 512;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint MsgSendRetPtrDelegate(nint receiver, nint selector);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint MsgSendPtrArgDelegate(nint receiver, nint selector, nint arg1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MsgSendVoidPtrDelegate(nint receiver, nint selector, nint arg1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MsgSendVoidNsSizeDelegate(nint receiver, nint selector, NSSize arg1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MsgSendVoidDelegate(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint sel_registerName(string selector);

    private static readonly nint LibObjC;
    private static readonly nint MsgSendPtr;
    private static readonly MsgSendRetPtrDelegate? MsgSendRetPtr;
    private static readonly MsgSendPtrArgDelegate? MsgSendPtrArg;
    private static readonly MsgSendVoidPtrDelegate? MsgSendVoidPtr;
    private static readonly MsgSendVoidNsSizeDelegate? MsgSendVoidNsSize;
    private static readonly MsgSendVoidDelegate? MsgSendVoid;

    static MacDockIcon()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                if (NativeLibrary.TryLoad("/usr/lib/libobjc.A.dylib", out LibObjC) &&
                    NativeLibrary.TryGetExport(LibObjC, "objc_msgSend", out MsgSendPtr))
                {
                    MsgSendRetPtr = Marshal.GetDelegateForFunctionPointer<MsgSendRetPtrDelegate>(MsgSendPtr);
                    MsgSendPtrArg = Marshal.GetDelegateForFunctionPointer<MsgSendPtrArgDelegate>(MsgSendPtr);
                    MsgSendVoidPtr = Marshal.GetDelegateForFunctionPointer<MsgSendVoidPtrDelegate>(MsgSendPtr);
                    MsgSendVoidNsSize = Marshal.GetDelegateForFunctionPointer<MsgSendVoidNsSizeDelegate>(MsgSendPtr);
                    MsgSendVoid = Marshal.GetDelegateForFunctionPointer<MsgSendVoidDelegate>(MsgSendPtr);
                }
            }
            catch
            {
                // Игнорируем ошибки привязки к нативной библиотеке
            }
        }
    }

    public static void TrySetFromPng(string pngPath)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!File.Exists(pngPath))
        {
            return;
        }

        if (MsgSendRetPtr is null || MsgSendPtrArg is null || MsgSendVoidPtr is null ||
            MsgSendVoidNsSize is null || MsgSendVoid is null)
        {
            return;
        }

        try
        {
            nint path = CreateUtf8NSString(pngPath);
            if (path == 0)
            {
                return;
            }

            nint imageClass = objc_getClass("NSImage");
            nint image = MsgSendRetPtr(imageClass, sel_registerName("alloc"));
            image = MsgSendPtrArg(image, sel_registerName("initWithContentsOfFile:"), path);
            if (image == 0)
            {
                return;
            }

            try
            {
                // Иконка в формате macOS HIG (824x824 squircle внутри 1024x1024 с прозрачными полями и тенью)
                var size = new NSSize { Width = DockIconSide, Height = DockIconSide };
                MsgSendVoidNsSize(image, sel_registerName("setSize:"), size);

                nint app = MsgSendRetPtr(
                    objc_getClass("NSApplication"),
                    sel_registerName("sharedApplication"));

                MsgSendVoidPtr(
                    app,
                    sel_registerName("setApplicationIconImage:"),
                    image);
            }
            finally
            {
                // Освобождаем аллоцированный NSImage для предотвращения утечки нативной памяти
                MsgSendVoid(image, sel_registerName("release"));
            }
        }
        catch
        {
            // AppKit может быть недоступен в нестандартных окружениях — UI не должен падать.
        }
    }

    private static nint CreateUtf8NSString(string value)
    {
        if (MsgSendPtrArg is null)
        {
            return 0;
        }

        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return MsgSendPtrArg(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize
    {
        public double Width;
        public double Height;
    }
}
