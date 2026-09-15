using CodexPetFocus.Native;

namespace CodexPetFocus.App;

internal static class BannerPlacement
{
    private const int Gap = 10;

    internal static PixelPoint Calculate(PixelRect sprite, PixelRect workArea, int bannerWidth, int bannerHeight)
    {
        var minimumX = workArea.Left;
        var maximumX = Math.Max(minimumX, workArea.Right - bannerWidth);
        var centeredX = sprite.Left + ((sprite.Width - bannerWidth) / 2);
        var x = Math.Clamp(centeredX, minimumX, maximumX);

        var preferredY = sprite.Top - bannerHeight - Gap;
        var fallbackY = sprite.Bottom + Gap;
        var maximumY = Math.Max(workArea.Top, workArea.Bottom - bannerHeight);
        var y = preferredY >= workArea.Top
            ? preferredY
            : Math.Min(fallbackY, maximumY);
        y = Math.Clamp(y, workArea.Top, maximumY);
        return new PixelPoint(x, y);
    }
}
