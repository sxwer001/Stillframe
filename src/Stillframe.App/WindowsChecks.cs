using Stillframe.App.Services;
using Stillframe.Core;
using Microsoft.Data.Sqlite;

namespace Stillframe.App;

internal static class WindowsChecks
{
    public static async Task RunAsync(string report)
    {
        var temporary=Path.Combine(Path.GetTempPath(),"shijing-windows-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);var lines=new List<string>();
        try
        {
            var input=Path.Combine(temporary,"original.bmp");
            // Own 4x2 uncompressed 24-bit BMP fixture; no remote or user image.
            WriteBmp(input);
            var processor=new WicImageProcessor();var info=await processor.ValidateAsync(input,CancellationToken.None);
            if(info.Width!=4||info.Height!=2||info.Extension!=".bmp")throw new Exception("WIC dimensions mismatch");lines.Add("PASS real WIC original validation: 4x2 BMP");
            var thumbnail=Path.Combine(temporary,"thumb.jpg");await processor.CreateThumbnailAsync(input,thumbnail,CancellationToken.None);
            var thumbInfo=await processor.ValidateAsync(thumbnail,CancellationToken.None);if(thumbInfo.Width!=4||thumbInfo.Height!=2)throw new Exception("thumbnail dimensions mismatch");lines.Add("PASS real JPEG thumbnail decode");
            var invalid=Path.Combine(temporary,"bad.jpg");await File.WriteAllTextAsync(invalid,"not an image");
            try{await processor.ValidateAsync(invalid,CancellationToken.None);throw new Exception("bad image accepted");}catch(AppException){lines.Add("PASS invalid image rejection");}
            using(var library=new LibraryService(Path.Combine(temporary,"data"),processor))
            {
                await library.InitializeAsync();var result=await library.ImportAsync([input,input,invalid],CancellationToken.None);
                if(result!=(1,1,1))throw new Exception("real import result mismatch");lines.Add("PASS real import + dedup + corrupt rejection");
                var item=(await library.GetItemsAsync()).Single();var exported=Path.Combine(temporary,"export.bmp");await library.ExportAsync(item,exported,CancellationToken.None);
                var exportedBytes=await File.ReadAllBytesAsync(exported);var originalBytes=await File.ReadAllBytesAsync(input);
                if(!exportedBytes.SequenceEqual(originalBytes))throw new Exception("export modified original");lines.Add("PASS real original export byte equality");
            }
            var monitors=await new DesktopService().EnumerateAsync();if(monitors.Count<2)throw new Exception("no connected monitor enumerated");lines.Add($"PASS desktop COM enumeration: {monitors.Count-1} monitor(s), no wallpaper mutation");
            await File.WriteAllLinesAsync(report,lines);
        }
        finally
        {
            SqliteConnection.ClearAllPools();var full=Path.GetFullPath(temporary);
            if(full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("shijing-windows-"))Directory.Delete(full,true);
        }
    }
    public static void WriteBmp(string path)
    {
        using var writer=new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4d42);writer.Write(78);writer.Write(0);writer.Write(54);writer.Write(40);writer.Write(4);writer.Write(2);writer.Write((ushort)1);writer.Write((ushort)24);writer.Write(0);writer.Write(24);writer.Write(2835);writer.Write(2835);writer.Write(0);writer.Write(0);
        for(var i=0;i<8;i++){writer.Write((byte)90);writer.Write((byte)130);writer.Write((byte)180);}
    }
}
