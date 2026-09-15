using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CodexPetFocus.Native;

namespace CodexPetFocus.App;

public partial class BannerWindow : Window
{
    private const int ExStyleIndex = -20;
    private const long ExTransparent = 0x20;
    private const long ExToolWindow = 0x80;
    private const long ExNoActivate = 0x08000000;
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;
    private static readonly nint TopMost = new(-1);
    private nint hwnd;

    public BannerWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public void UpdateText(string value) => BannerText.Text = value;

    public void ShowNear(PixelRect spriteBounds, nint petWindow)
    {
        var monitorRect = new NativeRect(spriteBounds.Left, spriteBounds.Top, spriteBounds.Right, spriteBounds.Bottom);
        var monitor = NativeMethods.MonitorFromRect(in monitorRect, 2);
        var information = MonitorInformation.Create();
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref information))
        {
            HideBanner();
            return;
        }

        if (!IsVisible)
            Show();
        UpdateLayout();

        var dpi = NativeMethods.GetDpiForWindow(petWindow);
        var workArea = information.WorkArea.ToPixelRect();
        if (dpi == 0 || workArea.Width <= 0 || workArea.Height <= 0)
        {
            HideBanner();
            return;
        }

        var width = Math.Clamp((int)Math.Ceiling(ActualWidth * dpi / 96d), 1, workArea.Width);
        var height = Math.Clamp((int)Math.Ceiling(ActualHeight * dpi / 96d), 1, workArea.Height);
        var location = BannerPlacement.Calculate(spriteBounds, workArea, width, height);

        NativeMethods.SetWindowPos(hwnd, TopMost, location.X, location.Y, width, height, NoActivate | ShowWindow);
    }

    public void HideBanner()
    {
        if (IsVisible)
            Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, ExStyleIndex).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, ExStyleIndex, new nint(style | ExTransparent | ExToolWindow | ExNoActivate));
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromRect(in NativeRect rect, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation information);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern nint SetWindowLongPtr(nint window, int index, nint value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        internal readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInformation
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;

        internal static MonitorInformation Create() => new()
        {
            Size = Marshal.SizeOf<MonitorInformation>()
        };
    }
}
