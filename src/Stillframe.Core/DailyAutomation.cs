using System.Text.Json;
using Stillframe.Core.Storage;

namespace Stillframe.Core;

public sealed record AutomationSettings(bool Desktop=false,bool LockScreen=false,bool Notifications=false,int Hour=9,string SourceId="all")
{
    public static async Task<AutomationSettings> LoadAsync(LibraryRepository repository)
    {
        var json=await repository.GetSettingAsync("automation");if(json==null)return new();
        try
        {
            var settings=JsonSerializer.Deserialize<AutomationSettings>(json)??new();
            return settings with{Hour=Math.Clamp(settings.Hour,0,23),SourceId=settings.SourceId is "all" or "local" or "bing" or "wallhaven" or "commons"?settings.SourceId:"all"};
        }
        catch(JsonException){throw new AppException("自动任务配置无法读取，请在设置中重新保存。");}
    }
    public Task SaveAsync(LibraryRepository repository)=>repository.SetSettingAsync("automation",JsonSerializer.Serialize(this));
}
public sealed record AutomationOutcome(string Kind,bool Success,string? Error);
public sealed class DailyAutomation(LibraryRepository repository)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,DateTimeOffset> retryAt=[];
    public async Task<IReadOnlyList<AutomationOutcome>> TickAsync(DateTimeOffset now,Func<CancellationToken,Task<WallpaperItem?>> select,Func<string,WallpaperItem,CancellationToken,Task> perform,CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token))return [];
        try
        {
            var config=await AutomationSettings.LoadAsync(repository);if(now.Hour<config.Hour)return [];
            var today=DateOnly.FromDateTime(now.DateTime).ToString("yyyy-MM-dd");var due=new List<string>();
            foreach(var (kind,enabled) in new[]{("desktop",config.Desktop),("lockscreen",config.LockScreen),("notification",config.Notifications)})
                if(enabled&&await repository.GetSettingAsync("automation.last."+kind)!=today&&(!retryAt.TryGetValue(kind,out var retry)||now>=retry))due.Add(kind);
            if(due.Count==0)return [];
            WallpaperItem? item;
            try{item=await select(token)??throw new AppException("没有可用壁纸，请先导入或同步图源。");}
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch(Exception ex){foreach(var kind in due)retryAt[kind]=now.AddMinutes(15);return due.Select(kind=>new AutomationOutcome(kind,false,ex.Message)).ToArray();}
            var results=new List<AutomationOutcome>();
            foreach(var kind in due)
            {
                token.ThrowIfCancellationRequested();
                // A user may turn off an action while selection/download awaits.
                config=await AutomationSettings.LoadAsync(repository);
                if(!(kind=="desktop"?config.Desktop:kind=="lockscreen"?config.LockScreen:config.Notifications))continue;
                try{await perform(kind,item,token);await repository.SetSettingAsync("automation.last."+kind,today);retryAt.Remove(kind);results.Add(new(kind,true,null));}
                catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
                catch(Exception ex){retryAt[kind]=now.AddMinutes(15);results.Add(new(kind,false,ex.Message));}
            }
            return results;
        }
        finally{gate.Release();}
    }
}
