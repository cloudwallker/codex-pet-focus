namespace CodexPetFocus.Native;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(PixelRect other) =>
        Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;
}

public enum PetFindStatus
{
    NotRun,
    Found,
    StateFileUnavailable,
    OverlayClosed,
    StateBoundsMissing,
    StateMascotBoundsMissing,
    ProcessNotFound,
    WindowNotFound,
    Ambiguous,
    UnsupportedVersion,
    UnsupportedDpi,
    NativeError
}

public sealed record PetTarget(
    nint TopLevelHwnd,
    nint ContentHwnd,
    PixelRect SpriteBounds,
    PixelRect WindowBounds,
    string Version);

public sealed record PetSelection(PetTarget? Target, PetFindStatus Status, string Reason);

public sealed record WindowCandidate(
    nint Hwnd,
    uint ProcessId,
    bool Visible,
    string ClassName,
    long ExStyle,
    PixelRect Bounds);

public sealed record OverlayState(bool IsOpen, PixelPoint? Position, PixelRect? MascotOffset, int? MascotWidth);

public static class SupportedCodexVersion
{
    public static bool IsSupported(string version) =>
        string.Equals(version, "26.908.4834.0", StringComparison.Ordinal);
}

public sealed class OverlayStateException(string message) : Exception(message);
