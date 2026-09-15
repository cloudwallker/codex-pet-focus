using CodexPetFocus.App;
using CodexPetFocus.Native;
static void Equal(PixelPoint expected,PixelPoint actual,string name){if(expected!=actual)throw new Exception($"{name}: {actual}, expected {expected}");}
var work=new PixelRect(-1920,0,0,1040);
Equal(new(-340,104),BannerPlacement.Calculate(new(-245,150,-115,291),work,320,36),"negative monitor centered above pet");
Equal(new(-320,104),BannerPlacement.Calculate(new(-80,150,0,291),work,320,36),"right edge remains on same monitor");
Equal(new(-1920,104),BannerPlacement.Calculate(new(-1920,150,-1790,291),work,320,36),"left edge remains on same monitor");
Equal(new(-340,156),BannerPlacement.Calculate(new(-245,5,-115,146),work,320,36),"top edge places banner below pet");
Equal(new(-340,1004),BannerPlacement.Calculate(new(-245,1080,-115,1221),work,320,36),"stale offscreen position clamps to workarea");
Console.WriteLine("Banner placement tests passed: 5 cases");
