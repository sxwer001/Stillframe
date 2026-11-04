using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Stillframe.App.Services;
using Stillframe.Core;

[SupportedOSPlatform("windows")]
internal static class MonitorChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int failure=unchecked((int)0x80004005);
        var bounds=new DesktopService.Rect{Right=2560,Bottom=1600};
        var calls=new List<string>();
        var connected=DesktopService.ReadMonitors(["active","detached"],new HashSet<string>{"active"},id=>{calls.Add(id);return id=="active"?(0,bounds):(failure,new DesktopService.Rect());});
        check(connected.Count==2&&connected[1].Id=="active"&&calls.SequenceEqual(["active"]),"inactive shell monitor is excluded before its E_FAIL rectangle query");
        var raced=DesktopService.ReadMonitors(["active","detached"],new HashSet<string>{"active","detached"},id=>id=="active"?(0,bounds):(1,new DesktopService.Rect()));
        check(raced.Count==2&&raced[1].Name.Contains("2560 × 1600"),"S_FALSE disconnect never produces a zero-size monitor");
        try{DesktopService.ReadMonitors(["active"],new HashSet<string>{"active"},_ =>(failure,new DesktopService.Rect()));throw new Exception("Active monitor failure was hidden");}
        catch(COMException ex){check(ex.HResult==failure,"unexpected rectangle failure for active monitor is reported");}
        try{DesktopService.ReadMonitors(["active"],new HashSet<string>{"active"},_ =>(0,new DesktopService.Rect()));throw new Exception("Zero-size monitor accepted");}
        catch(AppException){check(true,"zero-size successful rectangle is rejected");}
        try{DesktopService.ReadMonitors(["detached"],new HashSet<string>(),_ =>throw new Exception("Inactive monitor queried"));throw new Exception("Missing display session accepted");}
        catch(AppException){check(true,"no active monitor fails without fabricated all-displays target");}
        try{DesktopService.ReadMonitors(["detached"],new HashSet<string>{"detached"},_ =>(1,new DesktopService.Rect()));throw new Exception("Disconnected-only result accepted");}
        catch(AppException){check(true,"all monitors disconnected during read fails safely");}
    }
}
