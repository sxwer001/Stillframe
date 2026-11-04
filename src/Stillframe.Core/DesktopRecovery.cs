using System.Text.Json;

namespace Stillframe.Core;

public enum WallpaperPosition { Center=0, Tile=1, Stretch=2, Fit=3, Fill=4, Span=5 }
public sealed record DesktopSnapshot(WallpaperPosition Position, Dictionary<string,string> Files, uint Status=1, uint Background=0);
public interface IDesktopBackend
{
    DesktopSnapshot Capture();
    void SetPosition(WallpaperPosition position);
    void SetWallpaper(string monitor, string path);
}
public sealed record DesktopApplyResult(int Succeeded, bool Success, bool RolledBack, bool RecoveryRequired);
public sealed record DesktopRestoreResult(int Restored, int Remaining, bool Conflict);

// Invoked serially on the backend's STA. No system effects on construction or startup.
public sealed class DesktopRecovery(string root, IDesktopBackend backend)
{
    private sealed record Journal(int Version, DesktopSnapshot Original, DesktopSnapshot Expected, DesktopSnapshot? Before);
    private string Store => Path.Combine(Path.GetFullPath(root), "desktop-recovery-v1.json");
    public bool HasRecovery => File.Exists(Store);

    public DesktopApplyResult Apply(string path, string target, WallpaperPosition position)
    {
        if(!Enum.IsDefined(position) || !File.Exists(path))throw new AppException("原图不存在或适配方式无效，未修改桌面。");
        var prior=Read();
        if(prior?.Before!=null)throw new AppException("上次桌面操作未完成，请先在设置中恢复或放弃恢复记录；未修改桌面。");
        var before=backend.Capture(); Validate(before);
        if(before.Status!=1 || before.Files.Count==0 || before.Files.Values.Any(p=>!File.Exists(p)))
            throw new AppException("当前为幻灯片、禁用背景或旧原图不可用，无法保证恢复；未修改桌面。");
        if(target.Length>0&&!before.Files.ContainsKey(target))throw new AppException("目标显示器已断开，未修改桌面。");
        path=Path.GetFullPath(path);
        var targets=target.Length==0||position==WallpaperPosition.Span?before.Files.Keys.ToArray():[target];
        var planned=new DesktopSnapshot(position,new(before.Files,StringComparer.OrdinalIgnoreCase),before.Status,before.Background);
        foreach(var id in targets)planned.Files[id]=path;
        bool continues=prior!=null&&before.Position==prior.Expected.Position&&before.Status==prior.Expected.Status&&before.Background==prior.Expected.Background
            &&before.Files.All(p=>!prior.Expected.Files.TryGetValue(p.Key,out var expected)||Same(p.Value,expected));
        var original=continues?Merge(prior!.Original,before):before;
        if(original.Files.Any(p=>before.Files.ContainsKey(p.Key)&&!File.Exists(p.Value)))
            throw new AppException("恢复记录中的旧原图已不存在，请先处理或放弃恢复记录；未修改桌面。");
        var expected=continues?Merge(planned,prior!.Expected):planned;
        var intent=new Journal(1,original,expected,before);
        Write(intent); // must be durable before the first global or per-monitor mutation
        try
        {
            if(!Equal(backend.Capture(),before))throw new AppException("桌面已被其他操作更改。");
            backend.SetPosition(position);
            foreach(var id in targets)backend.SetWallpaper(id,path);
            if(!Equal(backend.Capture(),planned))throw new AppException("桌面设置读回不一致。");
            Write(intent with { Before=null });
            return new(targets.Length,true,false,true);
        }
        catch
        {
            // Even an API that throws may already have changed the system. Compare before undoing.
            var rolledBack=false;
            try
            {
                RestoreTo(intent,before);
                rolledBack=Equal(backend.Capture(),before);
                if(rolledBack){if(prior==null)Discard();else Write(prior);}
            }
            catch { /* durable intent remains available after restart */ }
            return new(0,false,rolledBack,HasRecovery);
        }
    }

    public DesktopRestoreResult Restore()
    {
        var journal=Read();if(journal==null)return new(0,0,false);
        var result=RestoreTo(journal,journal.Original);
        var current=backend.Capture();
        if(Equal(current,journal.Original))Discard(); // disconnected or conflicting monitors keep the journal
        return result;
    }
    private DesktopRestoreResult RestoreTo(Journal journal, DesktopSnapshot destination)
    {
        int restored=0;bool conflict=false;
        foreach(var pair in destination.Files)
        {
            var current=backend.Capture();
            if(!current.Files.TryGetValue(pair.Key,out var path))continue;
            if(Same(path,pair.Value))continue;
            if(!GlobalOwned(current,journal)||!File.Exists(pair.Value)||!OwnedPath(pair.Key,path,journal))
            {conflict=true;continue;}
            try{backend.SetWallpaper(pair.Key,pair.Value);restored++;}
            catch { /* retain record; retry only on explicit restore */ }
        }
        var after=backend.Capture();
        bool all=after.Files.Count==destination.Files.Count&&destination.Files.All(p=>after.Files.TryGetValue(p.Key,out var value)&&Same(value,p.Value));
        if(all&&after.Position!=destination.Position&&GlobalOwned(after,journal))
        {
            try{backend.SetPosition(destination.Position);}catch { }
        }
        after=backend.Capture();
        int remaining=destination.Files.Count(p=>!after.Files.TryGetValue(p.Key,out var value)||!Same(value,p.Value));
        int unknown=after.Files.Keys.Count(id=>!destination.Files.ContainsKey(id));
        remaining+=unknown;conflict|=unknown>0;
        if(after.Position!=destination.Position||after.Status!=destination.Status||after.Background!=destination.Background)remaining++;
        return new(restored,remaining,conflict);
    }
    private static bool GlobalOwned(DesktopSnapshot current,Journal journal) => current.Status==journal.Expected.Status&&current.Background==journal.Expected.Background
        &&(current.Position==journal.Expected.Position||current.Position==journal.Before?.Position);
    private static bool OwnedPath(string id,string path,Journal journal) =>
        journal.Expected.Files.TryGetValue(id,out var expected)&&Same(path,expected)
        ||journal.Before!=null&&journal.Before.Files.TryGetValue(id,out var before)&&Same(path,before);
    private static bool Same(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static bool Equal(DesktopSnapshot a,DesktopSnapshot b)=>a.Position==b.Position&&a.Status==b.Status&&a.Background==b.Background
        &&a.Files.Count==b.Files.Count&&b.Files.All(p=>a.Files.TryGetValue(p.Key,out var value)&&Same(value,p.Value));
    // First snapshot wins for existing monitors; add newly connected monitors without losing detached ones.
    private static DesktopSnapshot Merge(DesktopSnapshot first,DesktopSnapshot extra)
    {
        var files=new Dictionary<string,string>(first.Files,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in extra.Files)files.TryAdd(pair.Key,pair.Value);
        return first with { Files=files };
    }
    public void Discard()
    {
        CheckStore();File.Delete(Store); // only our journal, never image files or settings
    }
    private static void Validate(DesktopSnapshot value)
    {
        if(value==null||!Enum.IsDefined(value.Position)||value.Files==null||value.Files.Count is 0 or >64||value.Files.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Key.Length>2048||string.IsNullOrEmpty(p.Value)||p.Value.Length>32767||p.Value.Contains('\0')||!Path.IsPathFullyQualified(p.Value)))
            throw new AppException("桌面恢复记录无效，已保留；未修改桌面。");
    }
    private void CheckStore()
    {
        foreach(var path in new[]{Path.GetFullPath(root),Store})
            if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new AppException("桌面恢复记录包含链接，未修改桌面。");
    }
    private Journal? Read()
    {
        CheckStore();if(!File.Exists(Store))return null;
        try
        {
            if(new FileInfo(Store).Length>512*1024)throw new JsonException();
            var journal=JsonSerializer.Deserialize<Journal>(File.ReadAllText(Store));
            if(journal is not { Version:1 })throw new JsonException();
            Validate(journal.Original);Validate(journal.Expected);if(journal.Before!=null)Validate(journal.Before);
            return journal;
        }
        catch(JsonException){throw new AppException("桌面恢复记录无法识别，已保留；未修改桌面。");}
    }
    private void Write(Journal journal)
    {
        CheckStore();Directory.CreateDirectory(root);
        var temp=Store+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {JsonSerializer.Serialize(output,journal);output.Flush(true);}
            File.Move(temp,Store,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
