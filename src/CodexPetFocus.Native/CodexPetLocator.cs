using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexPetFocus.Native;

public sealed class CodexPetLocator
{
    private readonly string _statePath;
    public PetFindStatus LastStatus { get; private set; } = PetFindStatus.NotRun;
    public string LastReason { get; private set; } = "not-run";

    public CodexPetLocator(string? statePath = null) =>
        _statePath = statePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                               ".codex", ".codex-global-state.json");

    public PetTarget? FindPet()
    {
        OverlayState state;
        try { state = OverlayStateParser.Parse(File.ReadAllText(_statePath)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverlayStateException)
        { return Fail(PetFindStatus.StateFileUnavailable, $"state-unavailable:{error.GetType().Name}"); }
        if (!state.IsOpen) return Fail(PetFindStatus.OverlayClosed, "overlay-closed");
        if (state.Position is null) return Fail(PetFindStatus.StateBoundsMissing, "state-bounds-missing");

        try
        {
            var paths = EnumerateCodexProcesses();
            if (paths.Count == 0) return Fail(PetFindStatus.ProcessNotFound, "codex-process-not-found");
            var versions = paths.Values.Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Length != 1 || !SupportedCodexVersion.IsSupported(versions.FirstOrDefault() ?? ""))
                return Fail(PetFindStatus.UnsupportedVersion, "unsupported-or-mixed-codex-version");
            var selection = PetCandidateSelector.Select(EnumerateTopWindows(), paths.Keys, state.Position.Value);
            LastStatus = selection.Status;
            LastReason = selection.Reason;
            if (selection.Target is null) return null;
            var window = selection.Target.WindowBounds;
            var sprite = PetAccessibility.FindImage(selection.Target.TopLevelHwnd, window, state.Position.Value);
            if (sprite is null)
                return Fail(PetFindStatus.StateMascotBoundsMissing, "accessible-pet-image-missing-or-ambiguous");
            var target = selection.Target with
            {
                ContentHwnd = FindUniqueContentWindow(selection.Target.TopLevelHwnd),
                SpriteBounds = sprite.Value,
                Version = paths[GetProcessId(selection.Target.TopLevelHwnd)]
            };
            return target;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or System.Windows.Automation.ElementNotAvailableException or COMException)
        { return Fail(PetFindStatus.NativeError, $"native-error:{error.GetType().Name}"); }
    }

    private PetTarget? Fail(PetFindStatus status, string reason)
    { LastStatus = status; LastReason = reason; return null; }

    private static Dictionary<uint, string> EnumerateCodexProcesses()
    {
        var result = new Dictionary<uint, string>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                var path = QueryProcessPath((uint)process.Id);
                if (CodexPackagePath.TryGetVersion(path, out var version) && version is not null)
                    result[(uint)process.Id] = version;
            }
        }
        return result;
    }

    private static string? QueryProcessPath(uint processId)
    {
        var handle = NativeMethods.OpenProcess(0x1000, false, processId);
        if (handle == 0) return null;
        try
        {
            var size = 32768u;
            var buffer = new StringBuilder((int)size);
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { NativeMethods.CloseHandle(handle); }
    }

    private static List<WindowCandidate> EnumerateTopWindows()
    {
        var result = new List<WindowCandidate>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.GetWindowRect(hwnd, out var rect))
                result.Add(new WindowCandidate(hwnd, GetProcessId(hwnd), NativeMethods.IsWindowVisible(hwnd),
                    GetClassName(hwnd), NativeMethods.GetWindowLongPtr(hwnd, -20), rect.ToPixelRect()));
            return true;
        }, 0);
        return result;
    }

    private static nint FindUniqueContentWindow(nint parent)
    {
        var matches = new List<nint>();
        NativeMethods.EnumChildWindows(parent, (hwnd, _) =>
        { if (NativeMethods.IsWindowVisible(hwnd) && GetClassName(hwnd) == "Chrome_RenderWidgetHostHWND") matches.Add(hwnd); return true; }, 0);
        return matches.Count == 1 ? matches[0] : 0;
    }

    private static uint GetProcessId(nint hwnd)
    { NativeMethods.GetWindowThreadProcessId(hwnd, out var id); return id; }

    private static string GetClassName(nint hwnd)
    { var value = new StringBuilder(256); NativeMethods.GetClassName(hwnd, value, value.Capacity); return value.ToString(); }

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(nint hwnd, nint parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        { internal int Left, Top, Right, Bottom; internal readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom); }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder value, int capacity);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    }
}
