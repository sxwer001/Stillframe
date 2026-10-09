using Stillframe.Core;

internal static class HomeImageChecks
{
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        using var library=new LibraryService(Path.Combine(root,"home-selection"),new TestImages());await library.InitializeAsync();
        var files=new[]{Path.Combine(root,"home-a.jpg"),Path.Combine(root,"home-b.jpg")};
        await File.WriteAllBytesAsync(files[0],[0xff,0xd8,1]);await File.WriteAllBytesAsync(files[1],[0xff,0xd8,2]);await library.ImportAsync(files,CancellationToken.None);
        var originals=(await library.GetItemsAsync()).ToArray();
        var candidates=new Dictionary<string,WallpaperItem[]>();
        foreach(var id in new[]{"bing","wallhaven","commons"})
        {
            await library.Repository.SaveSourceAsync(BuiltInSources.Create(id));
            candidates[id]=originals.Select((x,n)=>x with{Id=id+"-"+n,SourceId=id,SourceName=id}).ToArray();foreach(var item in candidates[id])await library.Repository.AddItemAsync(item);
        }
        int requests=0;bool fail=false;
        Task<SourceBatch> Fetch(string id,string query,string order,CancellationToken token)
        {requests++;token.ThrowIfCancellationRequested();if(fail)throw new AppException("offline fixture");return Task.FromResult(new SourceBatch(candidates[id],1,1,id=="bing"?candidates[id][1].Id:null));}
        var home=new HomeImageService(library,Fetch);var day=new DateOnly(2026,10,9);
        var today=await home.BingDailyAsync(day,null,CancellationToken.None);
        check(today.Id==candidates["bing"][1].Id,"home uses Bing featured daily image, not random history");
        var restarted=new HomeImageService(library,Fetch);await restarted.BingDailyAsync(day,null,CancellationToken.None);
        check(requests==1,"Bing original and daily metadata cache survive service restart");
        await home.BingDailyAsync(day.AddDays(1),null,CancellationToken.None);check(requests==2,"Bing daily refreshes after local date rollover");
        var random=await home.RandomAsync("wallhaven",candidates["wallhaven"][0].Id,null,CancellationToken.None);
        check(random.SourceId=="wallhaven"&&random.Id==candidates["wallhaven"][1].Id,"home source switch selects one image and avoids previous image");
        var bingNext=await home.RandomAsync("bing",today.Id,null,CancellationToken.None);
        check(bingNext.SourceId=="bing"&&bingNext.Id!=today.Id&&(await home.CachedBingAsync())?.Id==today.Id,"random Bing browsing changes original without overwriting daily cache");
        try{await home.RandomAsync("wallhaven",null,null,CancellationToken.None,excludedIds:candidates["wallhaven"].Select(x=>x.Id).ToHashSet());throw new Exception("Exhausted history repeated an image");}
        catch(AppException){check(true,"fully viewed source never silently repeats an already seen wallpaper");}
        var before=requests;await home.RandomAsync("local",originals[0].Id,null,CancellationToken.None);check(requests==before,"local home source switch does not request public APIs");
        fail=true;try{await home.BingDailyAsync(day.AddDays(2),null,CancellationToken.None);throw new Exception("Failed Bing fetch accepted");}catch(AppException){}
        check((await home.CachedBingAsync())?.Id==today.Id&&await library.Repository.GetSettingAsync("home.bing.date")==day.AddDays(1).ToString("yyyy-MM-dd"),"failed Bing update preserves last successful daily original and date");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{await home.BingDailyAsync(day.AddDays(1),null,cancellation.Token);throw new Exception("Canceled cached selection accepted");}catch(OperationCanceledException){check(true,"canceled home selection cannot commit cached image");}
    }
}
