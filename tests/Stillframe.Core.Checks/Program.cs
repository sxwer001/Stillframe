using Stillframe.Core;
using Stillframe.Core.Network;
using System.Net;
using System.Security.Cryptography;

var root=Path.Combine(Path.GetTempPath(),"shijing-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var data=Path.Combine(root,"app");var source=Path.Combine(root,"source.jpg");var other=Path.Combine(root,"other.jpg");
await File.WriteAllBytesAsync(source,[0xff,0xd8,0xff,1,2,3]);await File.WriteAllBytesAsync(other,[0xff,0xd8,0xff,4,5,6]);
int count=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
async Task Reject(Func<Task> run,string name){try{await run();throw new Exception("FAIL no rejection: "+name);}catch(AppException){Check(true,name);}}
try
{
    using(var service=new LibraryService(data,new TestImages()))
    {
        await service.InitializeAsync();
        Check(await service.GetDailyAsync(new(2026,10,8))==null,"empty daily recommendation");
        var result=await service.ImportAsync([source,source,other],CancellationToken.None);
        Check(result==(2,1,0),"content hash import deduplication");
        Check(File.Exists(source)&&File.Exists(other),"source files preserved");
        var items=await service.GetItemsAsync();Check(items.Count==2&&items.All(service.HasOriginal),"verified assets persisted");
        var daily=await service.GetDailyAsync(new(2026,10,8));var repeated=await service.GetDailyAsync(new(2026,10,8));
        Check(daily?.Id==repeated?.Id,"same-day recommendation stable");
        var next=await service.GetDailyAsync(new(2026,10,8),true);Check(next?.Id!=daily?.Id,"explicit daily replacement");
        await service.Repository.SetFavoriteAsync(items[0].Id,true);Check((await service.GetItemsAsync()).First(x=>x.Id==items[0].Id).Favorite,"favorite persisted");
        var target=Path.Combine(root,"export.jpg");await service.ExportAsync(items[0],target,CancellationToken.None);
        Check(SHA256.HashData(await File.ReadAllBytesAsync(target)).SequenceEqual(Convert.FromHexString(items[0].Sha256!)),"export retains original bytes");
        await Reject(()=>service.ExportAsync(items[0],service.OriginalPath(items[0])!,CancellationToken.None),"export cannot overwrite managed original");
        var corrupt=Path.Combine(root,"invalid.jpg");await File.WriteAllTextAsync(corrupt,"not an image");
        var bad=await service.ImportAsync([corrupt],CancellationToken.None);Check(bad==(0,0,1),"bad image not committed");
        Check(!Directory.EnumerateFiles(Path.Combine(data,"staging")).Any(),"staging files cleaned");
        using var cts=new CancellationTokenSource();cts.Cancel();
        try{await service.ImportAsync([source],cts.Token);throw new Exception("cancellation not respected");}catch(OperationCanceledException){Check(true,"import cancellation");}
        var local=(await service.Repository.GetSourcesAsync()).Single();await service.Repository.SaveSourceAsync(local with{Enabled=false});
        Check(await service.GetDailyAsync(new(2026,10,9))==null,"disabled source excluded from daily");
        Check((await service.GetItemsAsync()).Count==2,"disabling source retains originals");
        await service.Repository.SaveSourceAsync(local with{Enabled=true});
        var orphan=Path.Combine(data,"staging","old.part");await File.WriteAllTextAsync(orphan,"interrupted");
        await service.InitializeAsync();Check(!File.Exists(orphan),"startup interrupted-transfer cleanup");
    }
    using(var reopened=new LibraryService(data,new TestImages()))
    {
        await reopened.InitializeAsync();Check((await reopened.GetItemsAsync()).Count==2,"restart retains library");
        Check((await reopened.GetDailyAsync(new(2026,10,8)))!=null,"restart retains daily selection");
        Check(await reopened.Sources.AddAsync("测试直链","direct","https://example.org/photo.jpg",CancellationToken.None)==1,"direct source adds metadata without downloading");
        var remoteSource=(await reopened.Repository.GetSourcesAsync()).Single(x=>x.Kind=="direct");
        await reopened.Sources.RefreshAsync(remoteSource,CancellationToken.None);
        Check((await reopened.GetItemsAsync()).Count==3,"source refresh stable identity deduplicates");
        var remoteItem=(await reopened.GetItemsAsync()).Single(x=>x.SourceId==remoteSource.Id);
        await reopened.Repository.SetFavoriteAsync(remoteItem.Id,true);
        await reopened.Repository.AddItemAsync(remoteItem with{Title="已更新",LicenseName="CC BY 4.0",LicenseUrl="https://creativecommons.org/licenses/by/4.0/"});
        var updated=(await reopened.GetItemsAsync()).Single(x=>x.Id==remoteItem.Id);
        Check(updated.Title=="已更新"&&updated.Favorite&&updated.LicenseName=="CC BY 4.0","metadata refresh preserves favorite and stores license");
    }
    foreach(var ip in new[]{"127.0.0.1","10.0.0.1","192.168.1.2","172.16.0.1","169.254.169.254","100.64.1.2","::1","fc00::1","::ffff:127.0.0.1"})Check(!SafeHttp.IsPublicAddress(IPAddress.Parse(ip)),"private network rejection "+ip);
    Check(SafeHttp.IsPublicAddress(IPAddress.Parse("8.8.8.8")),"public address allowed");
    foreach(var uri in new[]{"http://example.org/a.jpg","https://localhost/a.jpg","https://user:password@example.org/a.jpg","https://example.org:8443/a.jpg"})
    {try{SafeHttp.ValidateUri(uri);throw new Exception("unsafe URI allowed");}catch(AppException){Check(true,"unsafe URI rejection");}}
    Check(LibraryService.CleanTitle("a\n\rb")=="ab","title strips control characters");
    await OnlineFeatureChecks.RunAsync(root,Check);
    await HomeImageChecks.RunAsync(root,Check);
    Console.WriteLine($"RESULT: {count} checks passed. Fake decoder/effect handlers isolate service tests; real WIC and system effects require separate Windows checks.");
}
finally
{
    // Only remove the exact unique directory created for these checks.
    var full=Path.GetFullPath(root);var temporary=Path.GetFullPath(Path.GetTempPath());
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if(full.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("shijing-checks-"))Directory.Delete(full,true);
}
sealed class TestImages : IImageProcessor
{
    public async Task<ImageInfo> ValidateAsync(string path,CancellationToken token)
    {var bytes=await File.ReadAllBytesAsync(path,token);if(bytes.Length<3||bytes[0]!=0xff||bytes[1]!=0xd8)throw new AppException("invalid test image");return new(3840,2160,".jpg");}
    public Task CreateThumbnailAsync(string original,string destination,CancellationToken token)=>File.WriteAllBytesAsync(destination,[1,2,3],token);
}
