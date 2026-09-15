namespace CodexPetFocus.Native;
public sealed record PetImageCandidate(string Name, PixelRect Bounds, bool IsOffscreen);
public static class PetImageSelector
{
    public static PixelRect? Select(IEnumerable<PetImageCandidate> images, PixelRect window, PixelPoint expectedPosition)
    {
        var matches = images.Where(i => !i.IsOffscreen &&
            (i.Name.EndsWith(" \u5ba0\u7269", StringComparison.Ordinal) || i.Name.EndsWith(" pet", StringComparison.OrdinalIgnoreCase)) &&
            Math.Abs((long)i.Bounds.Left - expectedPosition.X) <= 1 &&
            Math.Abs((long)i.Bounds.Top - expectedPosition.Y) <= 1 &&
            i.Bounds.Width >= 16 && i.Bounds.Height >= 16 &&
            i.Bounds.Width <= 1024 && i.Bounds.Height <= 1024 && window.Contains(i.Bounds)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0].Bounds : null;
    }
}
