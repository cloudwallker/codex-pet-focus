using System.Windows.Automation;
namespace CodexPetFocus.Native;
internal static class PetAccessibility
{
    internal static PixelRect? FindImage(nint hwnd, PixelRect window, PixelPoint expectedPosition)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var elements = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image));
        if (elements.Count > 32) return null;
        var candidates = new List<PetImageCandidate>();
        foreach (AutomationElement element in elements)
        {
            var current = element.Current;
            var rect = current.BoundingRectangle;
            if (rect.IsEmpty || !double.IsFinite(rect.Left) || !double.IsFinite(rect.Top) ||
                !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom)) continue;
            candidates.Add(new(current.Name,
                new((int)Math.Floor(rect.Left), (int)Math.Floor(rect.Top),
                    (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom)), current.IsOffscreen));
        }
        return PetImageSelector.Select(candidates, window, expectedPosition);
    }
}
