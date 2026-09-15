using CodexPetFocus.Native;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{name}: expected {expected}, actual {actual}");
}

var goodPath = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.4834.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
Equal(true, CodexPackagePath.TryGetVersion(goodPath, out var version), "strict packaged path");
Equal("26.908.4834.0", version, "version extraction");
Equal(false, CodexPackagePath.TryGetVersion(@"C:\Temp\ChatGPT.exe", out _), "reject unrelated executable");
Equal(true, SupportedCodexVersion.IsSupported("26.908.4834.0"), "checked package version");
Equal(false, SupportedCodexVersion.IsSupported("26.999.0.0"), "reject unknown version");

var state = OverlayStateParser.Parse("""
    {"secret":"must-not-escape","electron-avatar-overlay-open":true,
     "electron-avatar-overlay-bounds":{"x":-245,"y":150,
       "mascot":{"left":244,"top":207,"width":112,"height":121}}}
    """);
Equal(true, state.IsOpen, "open flag");
Equal(new PixelPoint(-245, 150), state.Position, "negative coordinates");
Equal(new PixelRect(244, 207, 356, 328), state.MascotOffset, "mascot offset");

try
{
    OverlayStateParser.Parse("{\"private\":\"DO_NOT_ECHO\",");
    throw new Exception("malformed JSON was accepted");
}
catch (OverlayStateException error)
{
    Equal(false, error.Message.Contains("DO_NOT_ECHO", StringComparison.Ordinal), "malformed content redaction");
}
foreach (var malformedShape in new[] { "[]", "null", "{\"electron-avatar-overlay-bounds\":{\"x\":\"bad\",\"y\":1}}" })
{
    try { OverlayStateParser.Parse(malformedShape); throw new Exception("malformed shape was accepted"); }
    catch (OverlayStateException) { }
}

var candidates = new[]
{
    new WindowCandidate((nint)1, 44, true, "Chrome_WidgetWin_1", 0x2800A8, new PixelRect(-552, 0, 0, 1080)),
    new WindowCandidate((nint)2, 44, false, "Chrome_WidgetWin_1", 0x2800A8, new PixelRect(-245, 150, -109, 312)),
};
var selection = PetCandidateSelector.Select(candidates, new HashSet<uint> { 44 }, new PixelPoint(-245, 150));
Equal((nint)1, selection.Target!.TopLevelHwnd, "unique overlay hwnd");
Equal(PetFindStatus.Found, selection.Status, "found status");

var ambiguous = PetCandidateSelector.Select(
    new[] { candidates[0], candidates[0] with { Hwnd = (nint)3 } },
    new HashSet<uint> { 44 }, new PixelPoint(-245, 150));
Equal(PetFindStatus.Ambiguous, ambiguous.Status, "ambiguous status");
Equal<PetTarget?>(null, ambiguous.Target, "ambiguous has no target");

var pet = new PetImageCandidate("Tomori pet", new PixelRect(-245,150,-115,291), false);
var overlay = new PixelRect(-552,0,0,1080);
Equal<PixelRect?>(pet.Bounds, PetImageSelector.Select(new[]{pet}, overlay, new PixelPoint(-245,150)), "unique visible UIA image");
Equal<PixelRect?>(null, PetImageSelector.Select(new[]{pet,pet}, overlay, new PixelPoint(-245,150)), "ambiguous images rejected");
Equal<PixelRect?>(null, PetImageSelector.Select(new[]{pet with { IsOffscreen=true }}, overlay, new PixelPoint(-245,150)), "offscreen image rejected");
Equal<PixelRect?>(null, PetImageSelector.Select(new[]{pet with { Bounds=new PixelRect(-600,150,-115,291) }}, overlay, new PixelPoint(-245,150)), "outside overlay rejected");
Equal<PixelRect?>(null, PetImageSelector.Select(new[]{pet with { Bounds=new PixelRect(-245,150,-245,291) }}, overlay, new PixelPoint(-245,150)), "empty image rejected");
Equal<PixelRect?>(null, PetImageSelector.Select(new[]{pet}, overlay, new PixelPoint(-200,150)), "stale state rejected");
Equal<PixelRect?>(pet.Bounds, PetImageSelector.Select(new[]{pet with { Name="Tomori \u5ba0\u7269" }}, overlay, new PixelPoint(-245,150)), "Chinese pet name");
Console.WriteLine("Native pure logic tests passed: 22 assertions");
