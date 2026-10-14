using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>
/// Fenêtre Win32 minimale. Une seule procédure de fenêtre native, qui route vers l'instance
/// gérée correspondante. Toutes les fenêtres vivent sur le thread UI (boucle de messages unique).
/// </summary>
internal abstract unsafe class NativeWindow
{
    private static readonly Dictionary<nint, NativeWindow> s_windows = [];
    private static readonly HashSet<string> s_registered = [];
    private static NativeWindow? s_creating;

    public nint Handle { get; private set; }

    protected void CreateHandle(string className, uint exStyle, uint style, uint classStyle = 0)
    {
        var instance = Kernel32.GetModuleHandleW(null);
        if (s_registered.Add(className))
        {
            fixed (char* name = className)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    style = classStyle,
                    lpfnWndProc = &WndProcThunk,
                    hInstance = instance,
                    hCursor = User32.LoadCursorW(0, User32.IDC_ARROW),
                    lpszClassName = name,
                };
                if (User32.RegisterClassExW(&wc) == 0)
                    throw new InvalidOperationException($"RegisterClassEx({className}) : erreur {Marshal.GetLastPInvokeError()}");
            }
        }

        s_creating = this;
        try
        {
            Handle = User32.CreateWindowExW(exStyle, className, null, style, 0, 0, 0, 0, 0, 0, instance, 0);
        }
        finally
        {
            s_creating = null;
        }
        if (Handle == 0) throw new InvalidOperationException($"CreateWindowEx({className}) a échoué");
    }

    public void DestroyHandle()
    {
        if (Handle != 0) User32.DestroyWindow(Handle);
    }

    protected virtual nint WndProc(uint msg, nint wParam, nint lParam)
        => User32.DefWindowProcW(Handle, msg, wParam, lParam);

    [UnmanagedCallersOnly]
    private static nint WndProcThunk(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!s_windows.TryGetValue(hwnd, out var window))
        {
            if (s_creating is null) return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
            window = s_creating;
            window.Handle = hwnd;
            s_windows[hwnd] = window;
        }

        try
        {
            if (msg == Msg.WM_NCDESTROY)
            {
                s_windows.Remove(hwnd);
                window.Handle = 0;
                return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            return window.WndProc(msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Une exception ne doit jamais traverser la frontière native.
            Log.Error($"WndProc 0x{msg:X4}", ex);
            return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    protected static int LoWord(nint value) => (short)((long)value & 0xFFFF);
    protected static int HiWord(nint value) => (short)(((long)value >> 16) & 0xFFFF);
}
