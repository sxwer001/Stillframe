namespace Stillframe.Core;

// Home selection is separate from daily automation and multi-image gallery browsing.
public sealed class HomeImageService
{
    private readonly LibraryService library;
    private readonly Func<string,string,string,CancellationToken,Task<SourceBatch>> fetch;
    private readonly Func<string,string,string,int,CancellationToken,Task<SourceBatch>> fetchPage;
    public HomeImageService(LibraryService library,Func<string,string,string,CancellationToken,Task<SourceBatch>>? fetch=null)
    {this.library=library;this.fetch=fetch??((id,query,order,token)=>library.Sources.LoadBuiltInAsync(id=="bing"&&order=="archive"?"bing-archive":id,1,query,order,token));fetchPage=(id,query,order,page,token)=>library.Sources.LoadBuiltInAsync(id=="bing"&&order=="archive"?"bing-archive":id,page,query,order,token);}

    public async Task<WallpaperItem?> CachedBingAsync()
    {
        var id=await library.Repository.GetSettingAsync("home.bing.id");
        return (await library.GetItemsAsync()).FirstOrDefault(x=>x.Id==id&&x.SourceId=="bing"&&library.HasOriginal(x));
    }
    public async Task<WallpaperItem> BingDailyAsync(DateOnly date,IProgress<TransferProgress>? progress,CancellationToken token,bool refresh=false)
    {
        token.ThrowIfCancellationRequested();
        var day=date.ToString("yyyy-MM-dd");var cached=await CachedBingAsync();
        if(!refresh&&cached!=null&&await library.Repository.GetSettingAsync("home.bing.date")==day)return cached;
        var batch=await fetch("bing","","weekly",token);token.ThrowIfCancellationRequested();
        var item=batch.Items.FirstOrDefault(x=>x.Id==batch.FeaturedItemId)??throw new AppException("Bing当前每日图暂不可获取，请稍后重试。");
        var ready=await library.AcquireAsync(item.Id,progress,token);token.ThrowIfCancellationRequested();
        await library.Repository.SetSettingAsync("home.bing.id",ready.Id);await library.Repository.SetSettingAsync("home.bing.date",day);
        return ready;
    }
    public async Task<WallpaperItem> RandomAsync(string id,string? previousId,IProgress<TransferProgress>? progress,CancellationToken token,string query="",string order="weekly",IReadOnlySet<string>? excludedIds=null)
    {
        IReadOnlyList<WallpaperItem> candidates;
        if(id is "bing" or "bing-archive" or "wallhaven" or "commons")
        {
            if(id is "bing" or "bing-archive")order="archive";
            var batch=await fetch(id,query,order,token);candidates=batch.Items;
            var seen=excludedIds??new HashSet<string>();
            var unseen=candidates.Where(x=>x.Id!=previousId&&!seen.Contains(x.Id)).ToArray();
            for(var page=batch.Page+1;unseen.Length==0&&page<=batch.LastPage;page++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400),token);
                batch=await fetchPage(id,query,order,page,token);candidates=batch.Items;
                unseen=candidates.Where(x=>x.Id!=previousId&&!seen.Contains(x.Id)).ToArray();
            }
            candidates=unseen;
        }
        else candidates=(await library.GetItemsAsync()).Where(x=>(id=="all"||x.SourceId==id)&&(library.HasOriginal(x)||x.OriginalUrl!=null)&&(excludedIds==null||!excludedIds.Contains(x.Id))).ToArray();
        token.ThrowIfCancellationRequested();
        var choices=candidates.Where(x=>x.Id!=previousId).ToArray();
        if(choices.Length==0)throw new AppException(excludedIds?.Count>0?"这个图源的图片已浏览完，请切换图源后再试。":"这个图源暂无可用图片，请先导入或同步。");
        // Acquire exactly one original; do not download an entire page of previews.
        return await library.AcquireAsync(choices[Random.Shared.Next(choices.Length)].Id,progress,token);
    }
}
