using System.Security.Cryptography;
using Stillframe.Core.Storage;
using Stillframe.Core.Network;

namespace Stillframe.Core;

public sealed class LibraryService : IDisposable
{
    public string Root {get;}
    public LibraryRepository Repository {get;}
    private readonly IImageProcessor images;
    private readonly SafeHttp http;
    private readonly SemaphoreSlim gate=new(1,1);
    public LibraryService(string root,IImageProcessor images)
    {Root=root;this.images=images;Repository=new(root);http=new();Sources=new(Repository,http);}
    public async Task InitializeAsync()
    {
        foreach(var name in new[]{"originals","thumbnails","staging"})Directory.CreateDirectory(Path.Combine(Root,name));
        await Repository.InitializeAsync();
        // Exclusive application instance owns staging; originals are never automatically removed.
        foreach(var path in Directory.EnumerateFiles(Path.Combine(Root,"staging"),"*.part"))try{File.Delete(path);}catch(IOException){}
    }
    public string? OriginalPath(WallpaperItem item)=>item.OriginalFile==null?null:Path.Combine(Root,"originals",Path.GetFileName(item.OriginalFile));
    public string? ThumbnailPath(WallpaperItem item)=>item.ThumbnailFile==null?null:Path.Combine(Root,"thumbnails",Path.GetFileName(item.ThumbnailFile));
    public bool HasOriginal(WallpaperItem item)=>OriginalPath(item) is string path&&File.Exists(path);
    public async Task<(int Imported,int Duplicate,int Failed)> ImportAsync(IEnumerable<string> files,CancellationToken token)
    {
        await gate.WaitAsync(token);int imported=0,duplicate=0,failed=0;
        try
        {
            foreach(var file in files.Take(1000))
            {
                token.ThrowIfCancellationRequested();var part=Path.Combine(Root,"staging",Guid.NewGuid()+".part");
                try
                {
                    var length=new FileInfo(file).Length;if(length==0||length>50L*1024*1024)throw new AppException("图片为空或超过50MB。");
                    await using(var input=File.OpenRead(file))await using(var output=File.Create(part))await input.CopyToAsync(output,token);
                    var (name,thumb,hash,info)=await CommitAsync(part,token);
                    var existing=(await Repository.GetItemsAsync()).FirstOrDefault(x=>x.Sha256==hash);
                    if(existing!=null){duplicate++;continue;}
                    var item=new WallpaperItem(Guid.NewGuid().ToString("N"),CleanTitle(Path.GetFileNameWithoutExtension(file)),"local","本地图库",null,name,thumb,hash,info.Width,info.Height,null,null,false,DateTimeOffset.UtcNow);
                    await Repository.AddItemAsync(item);imported++;
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or AppException or ArgumentException){failed++;}
                finally {if(File.Exists(part))File.Delete(part);}
            }
            return(imported,duplicate,failed);
        }
        finally{gate.Release();}
    }
    public async Task<WallpaperItem> AcquireAsync(string id,IProgress<TransferProgress>? progress,CancellationToken token)
    {
        await gate.WaitAsync(token);
        var part=Path.Combine(Root,"staging",Guid.NewGuid()+".part");
        try
        {
            var item=(await Repository.GetItemsAsync()).FirstOrDefault(i=>i.Id==id)??throw new AppException("图片已不存在。");
            if(HasOriginal(item))return item;
            if(item.OriginalUrl==null)throw new AppException("原图已丢失，请重新导入。");
            await http.DownloadAsync(item.OriginalUrl,part,progress,token);progress?.Report(new(0,null,"正在校验原图"));
            var (name,thumb,hash,info)=await CommitAsync(part,token);
            // Once commit starts, persist it even if a late UI cancellation arrives.
            await Repository.UpdateAssetAsync(item.Id,name,thumb,hash,info);
            return item with{OriginalFile=name,ThumbnailFile=thumb,Sha256=hash,Width=info.Width,Height=info.Height};
        }
        finally {if(File.Exists(part))File.Delete(part);gate.Release();}
    }
    private async Task<(string,string,string,ImageInfo)> CommitAsync(string part,CancellationToken token)
    {
        var info=await images.ValidateAsync(part,token);
        await using var input=File.OpenRead(part);var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token)).ToLowerInvariant();input.Close();
        var name=hash+info.Extension;var original=Path.Combine(Root,"originals",name);var thumb=hash+".jpg";
        var thumbnail=Path.Combine(Root,"thumbnails",thumb);
        if(!File.Exists(thumbnail))await images.CreateThumbnailAsync(part,thumbnail,token);
        token.ThrowIfCancellationRequested();if(!File.Exists(original))File.Move(part,original);return(name,thumb,hash,info);
    }
    public async Task ExportAsync(WallpaperItem item,string destination,CancellationToken token)
    {
        var original=OriginalPath(item);if(original==null||!File.Exists(original))throw new AppException("原图尚未保存。");
        var target=Path.GetFullPath(destination);var originalFull=Path.GetFullPath(original);
        if(string.Equals(target,originalFull,StringComparison.OrdinalIgnoreCase))throw new AppException("请选择应用原图目录以外的保存位置。");
        if(Path.GetDirectoryName(target)!.StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new AppException("请将副本保存到应用数据目录之外。");
        var part=target+"."+Guid.NewGuid().ToString("N")+".part";
        try
        {await using(var input=File.OpenRead(original))await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))await input.CopyToAsync(output,token);token.ThrowIfCancellationRequested();File.Move(part,target,true);}
        finally {if(File.Exists(part))File.Delete(part);}
    }
    public async Task<WallpaperItem?> GetDailyAsync(DateOnly date,bool replace=false,string sourceId="all")
    {
        var items=(await Repository.GetItemsAsync(true)).Where(x=>sourceId=="all"||x.SourceId==sourceId).ToArray();if(items.Length==0)return null;
        var selected=await Repository.GetDailyAsync(date);
        var current=items.FirstOrDefault(x=>x.Id==selected&&(HasOriginal(x)||x.OriginalUrl!=null));
        if(!replace&&current!=null)return current;
        var candidates=items.Where(x=>(HasOriginal(x)||x.OriginalUrl!=null)&&(!replace||x.Id!=selected)).ToArray();
        if(candidates.Length==0)return current;
        var chosen=candidates[Random.Shared.Next(candidates.Length)];await Repository.SaveDailyAsync(date,chosen.Id,replace||selected!=null);
        var stored=await Repository.GetDailyAsync(date);return items.FirstOrDefault(x=>x.Id==stored);
    }
    public Task<IReadOnlyList<WallpaperItem>> GetItemsAsync()=>Repository.GetItemsAsync();
    public async Task<(int Ready,int Failed)> CachePreviewsAsync(IEnumerable<WallpaperItem> items,CancellationToken token)
    {
        int ready=0,failed=0;
        await Parallel.ForEachAsync(items.Take(24),new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=token},async (item,ct)=>
        {
            if(HasOriginal(item)||ThumbnailPath(item) is string cached&&File.Exists(cached)){Interlocked.Increment(ref ready);return;}
            if(item.PreviewUrl==null)return;
            var part=Path.Combine(Root,"staging",Guid.NewGuid()+".part");var output=Path.Combine(Root,"staging",Guid.NewGuid()+".part");
            try
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await http.DownloadAsync(item.PreviewUrl,part,null,deadline.Token,3L*1024*1024);
                await images.ValidateAsync(part,deadline.Token);await images.CreateThumbnailAsync(part,output,deadline.Token);ct.ThrowIfCancellationRequested();
                var name="preview-"+item.Id+".jpg";File.Move(output,Path.Combine(Root,"thumbnails",name),true);await Repository.UpdatePreviewAsync(item.Id,name);Interlocked.Increment(ref ready);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception){Interlocked.Increment(ref failed);}
            finally{if(File.Exists(part))File.Delete(part);if(File.Exists(output))File.Delete(output);}
        });return(ready,failed);
    }
    public SourceService Sources{get;}
    public static string CleanTitle(string value)=>new string(value.Where(c=>!char.IsControl(c)).Take(120).ToArray()).Trim() is string clean&&clean.Length>0?clean:"未命名图片";
    public void Dispose(){http.Dispose();gate.Dispose();}
}
