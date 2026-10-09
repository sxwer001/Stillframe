using Stillframe.Core;
using Stillframe.App.Services;

namespace Stillframe.App;

internal static class OnlineWindowsChecks
{
    public static async Task CheckBingArchiveAsync(string report)
    {
        var folder=Path.GetDirectoryName(Path.GetFullPath(report))!;Directory.CreateDirectory(folder);var lines=new List<string>();
        using var library=new LibraryService(Path.Combine(folder,"bing-archive-check-data"),new WicImageProcessor());await library.InitializeAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var first=await library.Sources.LoadBuiltInAsync("bing-archive",1,"","weekly",timeout.Token);
        var second=await library.Sources.LoadBuiltInAsync("bing-archive",2,"","weekly",timeout.Token);
        if(first.Items.Count!=24||second.Items.Count!=24||first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)).Any())throw new Exception("Archive pages empty or repeated");
        lines.Add("PASS real archive paged metadata and distinct IDs");
        var all=await library.Sources.LoadBuiltInAsync("bing-archive",1,"","archive",timeout.Token);
        if(all.Items.Count<=48||!all.Items.Any(x=>x.Title.StartsWith("2024-")))throw new Exception("Archive contains no older history");
        lines.Add($"PASS full historical selection: {all.Items.Count} entries, includes 2024");
        var older=all.Items.Last();var ready=await library.AcquireAsync(older.Id,null,timeout.Token);
        if(!library.HasOriginal(ready)||ready.Width<=0)throw new Exception("Historic original did not download/decode");
        lines.Add($"PASS old archive original native WIC decode: {ready.Title}, {ready.Dimensions}");
        var home=new HomeImageService(library);var random=await home.RandomAsync("bing",older.Id,null,timeout.Token,excludedIds:all.Items.Where(x=>x.Id!=all.Items[all.Items.Count/2].Id).Select(x=>x.Id).ToHashSet());
        if(random.SourceId!="bing-archive"||!library.HasOriginal(random)||(await home.CachedBingAsync())!=null)throw new Exception("Archive random selection or daily cache isolation failed");
        lines.Add("PASS Bing browsing uses unseen archived original and leaves daily cache untouched");
        lines.Add("No desktop/lockscreen mutation or user library access.");await File.WriteAllLinesAsync(report,lines);
    }
    public static async Task RunAsync(string report)
    {
        var folder=Path.GetDirectoryName(Path.GetFullPath(report))!;Directory.CreateDirectory(folder);
        var lines=new List<string>();
        using var library=new LibraryService(Path.Combine(folder,"api-check-data"),new WicImageProcessor());await library.InitializeAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        foreach(var id in new[]{"bing","wallhaven","commons"})
        {
            try
            {
                var batch=await library.Sources.LoadBuiltInAsync(id,1,"","weekly",timeout.Token);
                if(batch.Items.Count==0)throw new Exception("API returned no usable static image");
                lines.Add($"PASS real {id} API: {batch.Items.Count} usable metadata items, page {batch.Page}/{batch.LastPage}");
                var preview=await library.CachePreviewsAsync(batch.Items.Take(2),timeout.Token);
                if(preview.Ready==0)throw new Exception("No real preview decoded");lines.Add($"PASS real {id} preview download and WIC thumbnail: {preview.Ready}");
                var item=await library.AcquireAsync(batch.Items[0].Id,null,timeout.Token);
                if(item.Width<=0||!library.HasOriginal(item))throw new Exception("Original decode failed");lines.Add($"PASS real {id} original download and WIC validation: {item.Dimensions}");
                var output=Path.Combine(folder,"api-export-"+id+Path.GetExtension(item.OriginalFile));await library.ExportAsync(item,output,timeout.Token);
                var exported=await File.ReadAllBytesAsync(output);var original=await File.ReadAllBytesAsync(library.OriginalPath(item)!);
                if(!exported.SequenceEqual(original))throw new Exception("Export changed bytes");lines.Add($"PASS real {id} original export retains bytes");
                if(id=="wallhaven")
                {
                    var search=await library.Sources.LoadBuiltInAsync(id,1,"landscape","weekly",timeout.Token);
                    if(search.Items.Count==0||search.LastPage<2)throw new Exception("Search did not return pageable results");lines.Add("PASS real Wallhaven keyword search");
                    var second=await library.Sources.LoadBuiltInAsync(id,2,"landscape","weekly",timeout.Token);
                    if(second.Page!=2||second.Items.Count==0||second.Items.Select(x=>x.Id).SequenceEqual(search.Items.Select(x=>x.Id)))throw new Exception("Second page repeated search results");lines.Add("PASS real Wallhaven second-page retrieval");
                }
            }
            catch(Exception ex){lines.Add("FAIL "+id+": "+ex);Environment.ExitCode=1;}
            await File.WriteAllLinesAsync(report,lines);
        }
        try
        {
            using var homeLibrary=new LibraryService(Path.Combine(folder,"home-api-check-data"),new WicImageProcessor());await homeLibrary.InitializeAsync();var home=new HomeImageService(homeLibrary);
            var originalCount=(await homeLibrary.GetItemsAsync()).Count(homeLibrary.HasOriginal);var fanOut=false;
            var today=await home.BingDailyAsync(DateOnly.FromDateTime(DateTime.Now),null,timeout.Token);
            var count=(await homeLibrary.GetItemsAsync()).Count(homeLibrary.HasOriginal);fanOut|=count-originalCount>1;originalCount=count;
            if(today.SourceId!="bing"||!homeLibrary.HasOriginal(today))throw new Exception("Bing daily home original missing");lines.Add("PASS real Bing daily home selection and native original decode");
            var wall=await home.RandomAsync("wallhaven",today.Id,null,timeout.Token);
            count=(await homeLibrary.GetItemsAsync()).Count(homeLibrary.HasOriginal);fanOut|=count-originalCount>1;originalCount=count;
            if(wall.SourceId!="wallhaven"||!homeLibrary.HasOriginal(wall))throw new Exception("Wallhaven random original missing");lines.Add("PASS real Wallhaven random home original selection");
            var commons=await home.RandomAsync("commons",wall.Id,null,timeout.Token);
            count=(await homeLibrary.GetItemsAsync()).Count(homeLibrary.HasOriginal);fanOut|=count-originalCount>1;
            if(commons.SourceId!="commons"||!homeLibrary.HasOriginal(commons))throw new Exception("Commons random original missing");lines.Add("PASS real Commons random home original selection");
            if(fanOut)throw new Exception("Home fetched more than one original per source");lines.Add("PASS home loads one original per source without gallery preview fan-out");
        }
        catch(Exception ex){lines.Add("FAIL home loading: "+ex);Environment.ExitCode=1;}
        lines.Add("INFO lockscreen capability (no mutation): "+PersonalizationService.LockScreenSupported);
        lines.Add("INFO notifications capability (no registration/show): "+PersonalizationService.NotificationsSupported);
        lines.Add("No desktop/lockscreen mutation, notification registration or user library access.");await File.WriteAllLinesAsync(report,lines);
    }
}
