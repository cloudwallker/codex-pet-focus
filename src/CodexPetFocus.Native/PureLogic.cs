using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexPetFocus.Native;

public static partial class CodexPackagePath
{
    [GeneratedRegex(@"(?:^|\\)WindowsApps\\OpenAI\.Codex_([0-9]+(?:\.[0-9]+){3})_[^\\]+\\app\\ChatGPT\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex PackageRegex();

    public static bool TryGetVersion(string? path, out string? version)
    {
        var match = path is null ? Match.Empty : PackageRegex().Match(path.Replace('/', '\\'));
        version = match.Success ? match.Groups[1].Value : null;
        return match.Success;
    }
}

public static class OverlayStateParser
{
    public static OverlayState Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new OverlayStateException("桌宠状态根节点必须是对象");
            var isOpen = root.TryGetProperty("electron-avatar-overlay-open", out var open) && open.ValueKind == JsonValueKind.True;
            PixelPoint? position = null;
            PixelRect? mascotOffset = null;
            if (root.TryGetProperty("electron-avatar-overlay-bounds", out var bounds))
            {
                if (bounds.ValueKind != JsonValueKind.Object || !TryInt(bounds, "x", out var x) || !TryInt(bounds, "y", out var y))
                    throw new OverlayStateException("桌宠 bounds 形状无效");
                position = new PixelPoint(x, y);
                if (bounds.TryGetProperty("mascot", out var mascot))
                {
                    if (!TryInt(mascot, "left", out var left) || !TryInt(mascot, "top", out var top) ||
                        !TryInt(mascot, "width", out var width) || !TryInt(mascot, "height", out var height) || width <= 0 || height <= 0)
                        throw new OverlayStateException("桌宠 mascot 形状无效");
                    mascotOffset = new PixelRect(left, top, left + width, top + height);
                }
            }
            int? mascotWidth = null;
            if (root.TryGetProperty("electron-persisted-atom-state", out var atoms) && atoms.ValueKind == JsonValueKind.Object &&
                atoms.TryGetProperty("avatar-overlay-mascot-width-px", out var widthElement))
            {
                if (!widthElement.TryGetInt32(out var width) || width is < 80 or > 224)
                    throw new OverlayStateException("桌宠宽度状态无效");
                mascotWidth = width;
            }
            return new OverlayState(isOpen, position, mascotOffset, mascotWidth);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw new OverlayStateException($"桌宠状态 JSON 无效（{error.GetType().Name}）");
        }
    }

    private static bool TryInt(JsonElement value, string name, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.TryGetInt32(out result);
    }
}

public static class PetCandidateSelector
{
    private const long WsExTopmost = 0x8;
    private const long WsExLayered = 0x80000;

    public static PetSelection Select(IEnumerable<WindowCandidate> windows, IReadOnlyCollection<uint> processIds, PixelPoint position)
    {
        var matches = windows.Where(window => processIds.Contains(window.ProcessId) && window.Visible &&
            window.ClassName == "Chrome_WidgetWin_1" &&
            (window.ExStyle & (WsExTopmost | WsExLayered)) == (WsExTopmost | WsExLayered) &&
            window.Bounds.Left <= position.X && position.X < window.Bounds.Right &&
            window.Bounds.Top <= position.Y && position.Y < window.Bounds.Bottom).ToArray();
        var empty = new PixelRect(position.X, position.Y, position.X, position.Y);
        return matches.Length switch
        {
            1 => new PetSelection(new PetTarget(matches[0].Hwnd, 0, empty, matches[0].Bounds, ""), PetFindStatus.Found, "unique-compatible-window"),
            0 => new PetSelection(null, PetFindStatus.WindowNotFound, "no-compatible-window"),
            _ => new PetSelection(null, PetFindStatus.Ambiguous, $"ambiguous-compatible-windows:{matches.Length}")
        };
    }
}