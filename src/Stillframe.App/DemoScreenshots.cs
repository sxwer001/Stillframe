namespace Stillframe.App;

public sealed partial class MainWindow
{
    private bool screenshotMode;
    public void BeginScreenshots(string report){smokeReport=report;screenshotMode=true;}

    // Documentation capture uses an isolated gallery and original procedural artwork.
    // It never reads user images, uses online providers, or changes system personalization.
    private async Task ScreenshotsAsync(string report)
    {
        try
        {
            var sample=Path.Combine(library.Root,"山间暮色.bmp");
            WriteShowcase(sample);
            var imported=await library.ImportAsync([sample],CancellationToken.None);
            await RefreshAsync();
            var item=items.First();
            await library.Repository.AddItemAsync(item with{Title="山间暮色",Author="Stillframe · 原创演示图"});
            await RefreshAsync();daily=items.First();homeSourceId="local";
            await NavigateAsync("today");
            var folder=Path.GetDirectoryName(report)!;
            await CaptureAsync(Path.Combine(folder,"home.png"));
            ShowNavigationDrawer();await Task.Delay(350);
            await CaptureAsync(Path.Combine(folder,"sidebar.png"));CloseSettings();
            await OpenDetailAsync(daily);await Task.Delay(200);
            await CaptureAsync(Path.Combine(folder,"detail.png"));
            await File.WriteAllTextAsync(report,"PASS native WinUI home/sidebar/detail captures; isolated generated artwork; no system changes");
        }
        catch(Exception ex){await File.WriteAllTextAsync(report,"FAIL screenshots: "+ex);Environment.ExitCode=1;}
        Close();
    }

    private static void WriteShowcase(string path)
    {
        const int width=1600,height=1000,stride=width*3;
        using var writer=new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42);writer.Write(54+stride*height);writer.Write(0);writer.Write(54);writer.Write(40);writer.Write(width);writer.Write(height);writer.Write((ushort)1);writer.Write((ushort)24);writer.Write(0);writer.Write(stride*height);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
        for(var row=height-1;row>=0;row--)for(var x=0;x<width;x++)
        {
            var u=x/(double)width;var v=row/(double)height;
            double r=40+176*v,g=73+95*v,b=95+27*v;
            var glow=Math.Exp(-((u-.74)*(u-.74)*16+(v-.39)*(v-.39)*20));
            r+=48*glow;g+=28*glow;
            if(Math.Pow((u-.74)*1.6,2)+Math.Pow(v-.36,2)<.0016){r=251;g=224;b=171;}
            for(var layer=0;layer<4;layer++)
            {
                var ridge=.53+layer*.115 + .055*Math.Sin(u*9+layer*2)+.032*Math.Sin(u*23+layer);
                if(v>ridge){r=78-layer*18;g=110-layer*19;b=115-layer*18;}
            }
            if(v>.84){var reflection=Math.Exp(-Math.Pow((u-.73)*6,2));var ripple=Math.Sin(v*320+u*22)*5;r=38+reflection*90+ripple;g=66+reflection*69+ripple;b=76+reflection*42+ripple;}
            writer.Write((byte)Math.Clamp(b,0,255));writer.Write((byte)Math.Clamp(g,0,255));writer.Write((byte)Math.Clamp(r,0,255));
        }
    }
}
