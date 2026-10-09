using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stillframe.Core.Network;

namespace Stillframe.Core;

public sealed record SourceBatch(IReadOnlyList<WallpaperItem> Items,int Page,int LastPage,string? FeaturedItemId=null);
public static class BuiltInSources
{
    public static SourceDefinition Create(string id,int page=1,string query="",string order="weekly",DateOnly? date=null)
    {
        if(page<1||page>100||query.Length>100)throw new AppException("页码或搜索词超出范围。");
        return id switch
        {
            "bing"=>new(id,"Bing 每日图（实验）",id,$"https://www.bing.com/HPImageArchive.aspx?format=js&idx={(Math.Min(page,2)-1)*8}&n=8&mkt=zh-CN",true),
            "bing-archive"=>new(id,"Bing 历史图库（社区存档）",id,"https://bing.npanuhin.me/CN-zh.min.json",true),
            "wallhaven"=>new(id,"Wallhaven",id,"https://wallhaven.cc/api/v1/search?purity=100&categories=111&atleast=1920x1080&order=desc&sorting="+(query.Length>0?"relevance":order=="latest"?"date_added":"toplist")+"&topRange=1w&page="+page+"&q="+Uri.EscapeDataString(query),true),
            "commons"=>Commons(page,date??DateOnly.FromDateTime(DateTime.Now)),
            _=>throw new AppException("未知内置图源。")
        };
    }
    private static SourceDefinition Commons(int page,DateOnly date)
    {
        var dates=Enumerable.Range(0,7).Select(offset=>"Template:Potd/"+date.AddDays(-((Math.Min(page,12)-1)*7+offset)).ToString("yyyy-MM-dd"));
        return new("commons","Wikimedia Commons 每日精选","commons","https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&generator=images&gimlimit=50&titles="+Uri.EscapeDataString(string.Join("|",dates))+"&prop=imageinfo&iiprop=url%7Csize%7Cmime%7Cextmetadata&iiurlwidth=600",true);
    }
    public static SourceBatch Parse(SourceDefinition source,string json,int page=1,string order="weekly")
    {
        using var document=JsonDocument.Parse(json,new(){MaxDepth=32});var root=document.RootElement;
        var result=new List<WallpaperItem>();string? featured=null;var last=source.Kind=="bing"?2:source.Kind=="commons"?12:page;
        if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("error",out _))throw new AppException("图源API返回错误，请稍后重试。");
        if(source.Kind=="bing-archive")
        {
            if(root.ValueKind!=JsonValueKind.Array||root.GetArrayLength()>10000)throw new AppException("Bing历史存档格式无效或超过允许规模。");
            var cutoff=DateOnly.FromDateTime(DateTime.Now).AddDays(-14);
            foreach(var entry in root.EnumerateArray().OrderByDescending(x=>S(x,"date")))
            {
                if(!DateOnly.TryParseExact(S(entry,"date"),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date)||date>cutoff)continue;
                var original=S(entry,"url");if(original==null)continue;
                var uri=SafeHttp.ValidateUri(original);if(uri.Host!="bing.npanuhin.me")continue;
                var label=S(entry,"caption")??S(entry,"title")??"Bing历史壁纸";
                // Old Bing URLs can return a placeholder; use the archive's original bytes.
                result.Add(Item(source,date.ToString("yyyy-MM-dd"),date.ToString("yyyy-MM-dd")+" · "+label,original,null,S(entry,"copyright"),S(entry,"bing_url")??original,"仅限个人壁纸使用", "https://bing.npanuhin.me"));
            }
            if(order!="archive")
            {
                last=Math.Max(1,(result.Count+23)/24);result=result.Skip((page-1)*24).Take(24).ToList();
            }
            else last=1;
        }
        else if(source.Kind=="bing")
        {
            var position=0;
            foreach(var entry in root.GetProperty("images").EnumerateArray().Take(8))
            {
                var current=position++==0;
                if(entry.TryGetProperty("wp",out var wp)&&wp.ValueKind==JsonValueKind.False)continue;
                var baseUrl=S(entry,"urlbase");var original=baseUrl!=null?"https://www.bing.com"+baseUrl+"_UHD.jpg":"https://www.bing.com"+S(entry,"url");
                var preview=baseUrl!=null?"https://www.bing.com"+baseUrl+"_640x360.jpg":original;
                result.Add(Item(source,"https://www.bing.com"+S(entry,"url"),S(entry,"title")??S(entry,"copyright")??"Bing每日图",original,preview,S(entry,"copyright"),S(entry,"copyrightlink"),"按来源使用条件","https://www.bing.com"));
                if(current&&page==1)featured=result[^1].Id;
            }
        }
        else if(source.Kind=="wallhaven")
        {
            if(root.TryGetProperty("meta",out var meta)&&meta.TryGetProperty("last_page",out var lastPage))last=Math.Clamp(lastPage.GetInt32(),page,100);
            foreach(var entry in root.GetProperty("data").EnumerateArray().Take(24))
            {
                if(S(entry,"purity")!="sfw")continue;
                if(entry.TryGetProperty("file_size",out var size)&&size.GetInt64()>50L*1024*1024)continue;
                var id=S(entry,"id")??throw new AppException("Wallhaven返回的图片缺少标识。");
                var preview=entry.TryGetProperty("thumbs",out var thumbs)?S(thumbs,"large"):null;
                // Search results do not identify the photographer. Do not mislabel the uploader.
                result.Add(Item(source,id,"Wallhaven · "+(S(entry,"category")??"壁纸")+" #"+id,S(entry,"path")!,preview,null,S(entry,"url"),null,null));
            }
        }
        else if(source.Kind=="commons")
        {
            if(root.TryGetProperty("query",out var query)&&query.TryGetProperty("pages",out var pages))foreach(var entry in pages.EnumerateArray())
            {
                if(!entry.TryGetProperty("imageinfo",out var details)||details.GetArrayLength()==0)continue;var image=details[0];
                if(S(image,"mime") is not ("image/jpeg" or "image/png")||image.GetProperty("size").GetInt64()>50L*1024*1024)continue;
                var metadata=image.TryGetProperty("extmetadata",out var ext)?ext:default;
                string? M(string name)=>metadata.ValueKind==JsonValueKind.Object&&metadata.TryGetProperty(name,out var value)?Plain(S(value,"value")):null;
                result.Add(Item(source,entry.GetProperty("pageid").ToString(),M("ImageDescription")??S(entry,"title")??"Commons每日精选",S(image,"url")!,S(image,"thumburl"),M("Artist"),S(image,"descriptionurl"),M("LicenseShortName"),M("LicenseUrl")));
            }
        }
        else throw new AppException("不支持此API类型。");
        return new(result,page,last,featured);
    }
    private static WallpaperItem Item(SourceDefinition source,string remote,string title,string original,string? preview,string? author,string? page,string? license,string? licenseUrl)
    {
        SafeHttp.ValidateUri(original);if(preview!=null)SafeHttp.ValidateUri(preview);
        var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Id+"\n"+remote))).ToLowerInvariant();
        return new(id,LibraryService.CleanTitle(title),source.Id,source.Name,original,null,null,null,0,0,author,page,false,DateTimeOffset.UtcNow,license,licenseUrl,preview);
    }
    private static string? S(JsonElement entry,string key)=>entry.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    private static string? Plain(string? html)=>html==null?null:LibraryService.CleanTitle(WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>","",RegexOptions.None,TimeSpan.FromSeconds(1))));
}
