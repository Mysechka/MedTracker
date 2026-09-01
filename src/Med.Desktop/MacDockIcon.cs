using System.Runtime.InteropServices;

namespace Med.Desktop;

/// <summary>
/// Иконка Dock на macOS берётся из .app бандла, а не из Window.Icon.
/// При <c>dotnet run</c> бандла нет — выставляем NSApplication.applicationIconImage вручную.
/// </summary>
internal static class MacDockIcon
{
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

        try
        {
            nint path = CreateUtf8NSString(pngPath);
            if (path == 0)
            {
                return;
            }

            nint imageClass = objc_getClass("NSImage");
            nint image = objc_msgSend(imageClass, sel_registerName("alloc"));
            image = objc_msgSend_ptr(image, sel_registerName("initWithContentsOfFile:"), path);
            if (image == 0)
            {
                return;
            }

            nint app = objc_msgSend(
                objc_getClass("NSApplication"),
                sel_registerName("sharedApplication"));

            objc_msgSend_void_ptr(
                app,
                sel_registerName("setApplicationIconImage:"),
                image);
        }
        catch
        {
            // AppKit может быть недоступен в нестандартных окружениях — UI не должен падать.
        }
    }

    private static nint CreateUtf8NSString(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return objc_msgSend_ptr(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint sel_registerName(string selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_ptr(nint receiver, nint selector, nint arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_ptr(nint receiver, nint selector, nint arg1);
}
