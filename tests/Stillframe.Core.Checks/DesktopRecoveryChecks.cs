using Stillframe.Core;

internal static class DesktopRecoveryChecks
{
    private sealed class Fake(DesktopSnapshot initial):IDesktopBackend
    {
        public DesktopSnapshot State=initial;
        public int Writes;
        public string? FailOnce;
        public bool FailRollback;
        public bool FailPosition;
        public Action<string>? After;
        public DesktopSnapshot Capture()=>State with{Files=new(State.Files,StringComparer.OrdinalIgnoreCase)};
        public void SetPosition(WallpaperPosition value)
        {Writes++;State=State with{Position=value};After?.Invoke("position");if(FailPosition){FailPosition=false;throw new IOException("position applied then threw");}}
        public void SetWallpaper(string id,string path)
        {
            Writes++;
            if(FailOnce==id){FailOnce=null;throw new IOException("monitor failure");}
            if(FailRollback&&Path.GetFileName(path).StartsWith("old"))throw new IOException("rollback failure");
            State.Files[id]=path;After?.Invoke(id);
        }
    }
    public static async Task RunAsync(string root,Action<bool,string> check)
    {
        var data=Path.Combine(root,"desktop-recovery");Directory.CreateDirectory(data);
        string FileAt(string name){var path=Path.Combine(data,name+".jpg");File.WriteAllText(path,"generated "+name);return path;}
        var oldA=FileAt("old-A");var oldB=FileAt("old-B");var next=FileAt("next");var later=FileAt("later");var user=FileAt("user");
        DesktopSnapshot Initial()=>new(WallpaperPosition.Fit,new(){["A"]=oldA,["B"]=oldB});
        int scenario=0;
        (DesktopRecovery Store,Fake Backend,string Directory) Create()
        {var path=Path.Combine(data,(scenario++).ToString());var fake=new Fake(Initial());return(new(path,fake),fake,path);}
        bool Original(Fake fake)=>fake.State.Position==WallpaperPosition.Fit&&fake.State.Files["A"]==oldA&&fake.State.Files["B"]==oldB;
        var (store,backend,folder)=Create();
        var applied=store.Apply(next,"",WallpaperPosition.Fill);
        check(applied.Success&&store.HasRecovery&&backend.State.Files.Values.All(p=>p==next),"desktop successful all-monitor apply persists recovery");
        var restarted=new DesktopRecovery(folder,backend);check(restarted.HasRecovery&&backend.Writes==3,"desktop restart only loads availability without mutation");
        var restored=restarted.Restore();check(restored.Remaining==0&&Original(backend)&&!restarted.HasRecovery,"desktop explicit restore after restart restores paths and mode");
        (store,backend,folder)=Create();store.Apply(next,"A",WallpaperPosition.Fill);store.Apply(later,"B",WallpaperPosition.Center);store.Restore();
        check(Original(backend),"desktop continuous changes retain the first external baseline");
        (store,backend,folder)=Create();backend.FailOnce="B";applied=store.Apply(next,"",WallpaperPosition.Fill);
        check(!applied.Success&&applied.RolledBack&&Original(backend)&&!store.HasRecovery,"desktop partial monitor failure rolls back images and global mode");
        (store,backend,folder)=Create();backend.FailPosition=true;applied=store.Apply(next,"",WallpaperPosition.Fill);
        check(!applied.Success&&applied.RolledBack&&Original(backend),"desktop position API applies then throws and still rolls back");
        (store,backend,folder)=Create();backend.FailOnce="B";backend.FailRollback=true;applied=store.Apply(next,"",WallpaperPosition.Fill);
        check(!applied.Success&&!applied.RolledBack&&applied.RecoveryRequired,"desktop failed rollback retains durable pending intent");
        int writes=backend.Writes;try{store.Apply(later,"",WallpaperPosition.Center);}catch(AppException){}
        check(backend.Writes==writes,"desktop unresolved pending operation blocks further changes");
        backend.FailRollback=false;new DesktopRecovery(folder,backend).Restore();check(Original(backend)&&!store.HasRecovery,"desktop pending rollback can be explicitly completed after restart");
        (store,backend,folder)=Create();store.Apply(next,"",WallpaperPosition.Fill);backend.State.Files["A"]=user;restored=store.Restore();
        check(backend.State.Files["A"]==user&&backend.State.Files["B"]==oldB&&backend.State.Position==WallpaperPosition.Fill&&restored.Remaining>0&&restored.Conflict,"desktop per-screen user change is preserved and prevents global mode reset");
        (store,backend,folder)=Create();store.Apply(next,"",WallpaperPosition.Fill);backend.State=backend.State with{Position=WallpaperPosition.Tile};writes=backend.Writes;store.Restore();
        check(backend.Writes==writes&&backend.State.Files["A"]==next,"desktop external global mode change prevents stale restore");
        store.Discard();check(!store.HasRecovery&&backend.Writes==writes,"desktop discard deletes only record without system writes");
        (store,backend,folder)=Create();store.Apply(next,"",WallpaperPosition.Fill);backend.State.Files["A"]=user;store.Apply(later,"",WallpaperPosition.Center);store.Restore();
        check(backend.State.Files["A"]==user&&backend.State.Files["B"]==next&&backend.State.Position==WallpaperPosition.Fill,"desktop new explicit apply after user change starts a fresh baseline");
        (store,backend,folder)=Create();store.Apply(next,"",WallpaperPosition.Fill);backend.State.Files.Remove("B");restored=store.Restore();
        check(restored.Remaining>0&&store.HasRecovery&&backend.State.Files["A"]==oldA&&backend.State.Position==WallpaperPosition.Fill,"desktop disconnected monitor remains pending without global side effects");
        backend.State.Files["B"]=next;restored=new DesktopRecovery(folder,backend).Restore();check(restored.Remaining==0&&Original(backend),"desktop reconnected monitor completes recovery");
        (store,backend,folder)=Create();store.Apply(next,"",WallpaperPosition.Fill);File.Delete(oldB);restored=store.Restore();
        check(restored.Remaining>0&&backend.State.Files["B"]==next&&store.HasRecovery,"desktop missing original is skipped and recovery record retained");
        FileAt("old-B");store.Restore();check(Original(backend),"desktop restored source file allows later recovery");
        (store,backend,folder)=Create();backend.State=backend.State with{Status=3};try{store.Apply(next,"",WallpaperPosition.Fill);}catch(AppException){}
        check(backend.Writes==0&&!store.HasRecovery,"desktop configured slideshow is rejected without losing its configuration");
        (store,backend,folder)=Create();try{store.Apply(next,"detached",WallpaperPosition.Fill);}catch(AppException){}
        check(backend.Writes==0,"desktop detached target is rejected before mutation");
        (store,backend,folder)=Create();store.Apply(next,"",WallpaperPosition.Fill);backend.State.Files["new-monitor"]=user;restored=store.Restore();
        check(restored.Remaining>0&&backend.State.Files["new-monitor"]==user&&backend.State.Position==WallpaperPosition.Fill&&store.HasRecovery,"desktop newly connected unmanaged monitor prevents global restore and is reported");
        // Replay durable checkpoints as if the process ended without finally/rollback.
        foreach(var boundary in new[]{"position","A","B"})
        {
            (store,backend,folder)=Create();DesktopSnapshot? checkpoint=null;string? intent=null;
            backend.After=stage=>{if(stage==boundary&&checkpoint==null){checkpoint=backend.Capture();intent=File.ReadAllText(Path.Combine(folder,"desktop-recovery-v1.json"));}};
            store.Apply(next,"",WallpaperPosition.Fill);backend.After=null;backend.State=checkpoint!;
            File.WriteAllText(Path.Combine(folder,"desktop-recovery-v1.json"),intent);
            new DesktopRecovery(folder,backend).Restore();
            check(Original(backend)&&!store.HasRecovery,"desktop durable crash checkpoint recovery after "+boundary);
        }
        (store,backend,folder)=Create();Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"desktop-recovery-v1.json"),"{corrupt");
        try{store.Restore();}catch(AppException){}try{store.Apply(next,"",WallpaperPosition.Fill);}catch(AppException){}
        check(backend.Writes==0&&store.HasRecovery,"desktop corrupt record fails closed and remains available for inspection");
        (store,backend,folder)=Create();Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"desktop-recovery-v1.json"));
        try{store.Apply(next,"",WallpaperPosition.Fill);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        check(backend.Writes==0,"desktop journal write failure prevents all system mutation");
        check(File.Exists(oldA)&&File.Exists(oldB)&&File.Exists(next)&&File.Exists(later)&&File.Exists(user),"desktop apply/recovery never deletes or rewrites source images");
        await Task.CompletedTask;
    }
}
