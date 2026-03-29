using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Orbital;

/// <summary>
/// HwndHost that creates a native child window, then reparents
/// an external application's main window into it.
/// </summary>
public class NativeAppHost : HwndHost
{
    private IntPtr _hwndHost;
    private IntPtr _embeddedHwnd;
    private Process? _process;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const int GWL_STYLE = -16;
    private const int SW_SHOW = 5;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwndHost = CreateWindowEx(0, "static", "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, (int)Width, (int)Height,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, _hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseEmbedded();
        DestroyWindow(hwnd.Handle);
    }

    /// <summary>
    /// Launch a process and embed its main window into this host.
    /// </summary>
    public async Task<bool> EmbedProcessAsync(string exePath, string arguments = "")
    {
        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
            });

            if (_process is null) return false;

            // Wait for the process to create its main window
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                _process.Refresh();
                if (_process.MainWindowHandle != IntPtr.Zero)
                    break;
            }

            if (_process.MainWindowHandle == IntPtr.Zero)
                return false;

            _embeddedHwnd = _process.MainWindowHandle;

            // Reparent into our host window
            SetParent(_embeddedHwnd, _hwndHost);

            // Remove title bar / borders, make it a child window
            int style = GetWindowLong(_embeddedHwnd, GWL_STYLE);
            style = (int)(WS_CHILD | WS_VISIBLE);
            SetWindowLong(_embeddedHwnd, GWL_STYLE, style);

            ResizeEmbedded();
            ShowWindow(_embeddedHwnd, SW_SHOW);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Embed an already-running window handle.
    /// </summary>
    public bool EmbedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        _embeddedHwnd = hwnd;

        SetParent(_embeddedHwnd, _hwndHost);
        int style = (int)(WS_CHILD | WS_VISIBLE);
        SetWindowLong(_embeddedHwnd, GWL_STYLE, style);
        ResizeEmbedded();
        ShowWindow(_embeddedHwnd, SW_SHOW);
        return true;
    }

    public void ResizeEmbedded()
    {
        if (_embeddedHwnd != IntPtr.Zero && _hwndHost != IntPtr.Zero)
        {
            int w = Math.Max(1, (int)ActualWidth);
            int h = Math.Max(1, (int)ActualHeight);
            MoveWindow(_embeddedHwnd, 0, 0, w, h, true);
        }
    }

    private void ReleaseEmbedded()
    {
        if (_embeddedHwnd != IntPtr.Zero)
        {
            // Reparent back to desktop so it doesn't get destroyed
            SetParent(_embeddedHwnd, IntPtr.Zero);
            _embeddedHwnd = IntPtr.Zero;
        }
        try { _process?.Kill(); } catch { }
        _process = null;
    }

    protected override void OnRenderSizeChanged(System.Windows.SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ResizeEmbedded();
    }
}
