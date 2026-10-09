using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Stillframe.Core.Network;
using Stillframe.Core.Storage;

namespace Stillframe.Core;

public sealed class SourceService(LibraryRepository repository,SafeHttp http)
{
    private string? archiveJson;
    private DateTimeOffset archiveUpdated;
    private readonly SemaphoreSlim archiveReader=new(1,1);
    private readonly HashSet<string> storedArchiveItems=new(StringComparer.Ordinal);
    private async Task<string> ReadArchiveAsync(string address,CancellationToken token)
    {
        await archiveReader.WaitAsync(token);
        try
        {
            if(archiveJson!=null&&DateTimeOffset.UtcNow-archiveUpdated<TimeSpan.FromHours(6))return archiveJson;
            var json=await http.ReadTextAsync(address,token);
            // Validate before replacing the last usable metadata snapshot.
            BuiltInSources.Parse(BuiltInSources.Create("bing-archive"),json);
            archiveJson=json;archiveUpdated=DateTimeOffset.UtcNow;return json;
        }
        finally{archiveReader.Release();}
    }
    public async Task<int> AddAsync(string name,string kind,string address,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>60)throw new AppException("图源名称不能为空且不能超过60字。");
        SafeHttp.ValidateUri(address);
        var source=new SourceDefinition(Guid.NewGuid().ToString("N"),name.Trim(),kind,address,true);
        var items=await ResolveAsync(source,token);token.ThrowIfCancellationRequested();
        await repository.SaveSourceAsync(source);foreach(var item in items)await repository.AddItemAsync(item);return items.Count;
    }
    public async Task<int> RefreshAsync(SourceDefinition source,CancellationToken token)
    {var items=await ResolveAsync(source,token);foreach(var item in items)await repository.AddItemAsync(item);return items.Count;}
    public async Task<int> LoadBingAsync(CancellationToken token)
        =>(await LoadBuiltInAsync("bing",1,"","weekly",token)).Items.Count;
    public async Task<SourceBatch> LoadBuiltInAsync(string id,int page,string query,string order,CancellationToken token)
    {
        var source=BuiltInSources.Create(id,page,query,order);
        var existing=(await repository.GetSourcesAsync()).FirstOrDefault(x=>x.Id==id);
        if(existing!=null)source=source with{Enabled=existing.Enabled};
        var json=id=="bing-archive"?await ReadArchiveAsync(source.Address,token):await http.ReadTextAsync(source.Address,token);var batch=BuiltInSources.Parse(source,json,page,order);
        token.ThrowIfCancellationRequested();await repository.SaveSourceAsync(source);
        foreach(var item in batch.Items)
        {
            token.ThrowIfCancellationRequested();if(id=="bing-archive"&&storedArchiveItems.Contains(item.Id))continue;
            await repository.AddItemAsync(item);if(id=="bing-archive")storedArchiveItems.Add(item.Id);
        }
        return batch;
    }
    private async Task<IReadOnlyList<WallpaperItem>> ResolveAsync(SourceDefinition source,CancellationToken token)
    {
        if(source.Kind=="direct")return[new(StableId(source.Id,"direct"),source.Name,source.Id,source.Name,source.Address,null,null,null,0,0,null,null,false,DateTimeOffset.UtcNow)];
        var json=await http.ReadTextAsync(source.Address,token);using var document=JsonDocument.Parse(json,new(){MaxDepth=16});var root=document.RootElement;
        var result=new List<WallpaperItem>();
        if(source.Kind is "bing" or "wallhaven" or "commons")return BuiltInSources.Parse(source,json).Items;
        else if(source.Kind=="manifest")
        {
            if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new AppException("仅支持schemaVersion为1的JSON清单。");
            var entries=root.GetProperty("items");if(entries.GetArrayLength()>5000)throw new AppException("清单不能超过5000项。");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries.EnumerateArray())
            {
                var remoteId=entry.GetProperty("id").GetString();if(string.IsNullOrWhiteSpace(remoteId)||!ids.Add(remoteId))throw new AppException("清单中的id不能为空或重复。");
                var url=entry.GetProperty("originalUrl").GetString()!;SafeHttp.ValidateUri(url);
                string? Optional(string key)=>entry.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
                // Claimed sizes are deliberately not shown until an original has been decoded.
                result.Add(new(StableId(source.Id,remoteId),LibraryService.CleanTitle(Optional("title")??remoteId),source.Id,source.Name,url,null,null,null,0,0,Optional("author"),Optional("sourcePage"),false,DateTimeOffset.UtcNow,Optional("license"),Optional("licenseUrl")));
            }
        }
        else throw new AppException("不支持此图源类型。");
        if(result.Count==0)throw new AppException("图源没有提供图片。");return result;
    }
    private static string StableId(string source,string remote)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source+"\n"+remote))).ToLowerInvariant();
}
