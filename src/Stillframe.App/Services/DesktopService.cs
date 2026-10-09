using System.Runtime.InteropServices;
using Stillframe.Core;

namespace Stillframe.App.Services;

public enum WallpaperPosition{Center=0,Tile=1,Stretch=2,Fit=3,Fill=4,Span=5}
public sealed record MonitorTarget(string Id,string Name){public override string ToString()=>Name;}
public sealed record DesktopSnapshot(WallpaperPosition Position,Dictionary<string,string> Files);
public sealed record DesktopApplyResult(DesktopSnapshot Before,int Succeeded,List<string> Failed);

public sealed class DesktopService
{
    // One dedicated STA per invocation; COM instances never cross threads.
    private static Task<T> OnSta<T>(Func<IDesktopWallpaper,T> action)
    {
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            IDesktopWallpaper? desktop=null;
            try{desktop=(IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!)!;completion.SetResult(action(desktop));}
            catch(Exception ex){completion.SetException(ex);}
            finally{if(desktop!=null)Marshal.FinalReleaseComObject(desktop);}
        }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }
    public Task<IReadOnlyList<MonitorTarget>> EnumerateAsync()=>OnSta<IReadOnlyList<MonitorTarget>>(desktop=>
    {
        var targets=new List<MonitorTarget>{new("","所有显示器")};desktop.GetMonitorDevicePathCount(out var count);
        for(uint i=0;i<count;i++){desktop.GetMonitorDevicePathAt(i,out var id);desktop.GetMonitorRECT(id,out var rect);targets.Add(new(id,$"显示器 {i+1} · {rect.Right-rect.Left} × {rect.Bottom-rect.Top}"));}return targets;
    });
    public Task<DesktopApplyResult> ApplyAsync(string path,string target,WallpaperPosition position)=>OnSta(desktop=>
    {
        if(!File.Exists(path))throw new AppException("原图不存在，未修改桌面。");
        desktop.GetPosition(out var previousPosition);desktop.GetMonitorDevicePathCount(out var count);
        var old=new Dictionary<string,string>();for(uint i=0;i<count;i++){desktop.GetMonitorDevicePathAt(i,out var id);desktop.GetWallpaper(id,out var file);old[id]=file;}
        if(target.Length>0&&!old.ContainsKey(target))throw new AppException("目标显示器已断开，未修改桌面。");
        var snapshot=new DesktopSnapshot(previousPosition,old);var failures=new List<string>();int success=0;
        var targets=target.Length==0||position==WallpaperPosition.Span?old.Keys.ToArray():[target];
        desktop.SetPosition(position);
        foreach(var id in targets){try{desktop.SetWallpaper(id,Path.GetFullPath(path));success++;}catch(COMException){failures.Add(id);}}
        return new DesktopApplyResult(snapshot,success,failures);
    });
    public Task<int> RestoreAsync(DesktopSnapshot snapshot)=>OnSta(desktop=>
    {
        desktop.SetPosition(snapshot.Position);int failed=0;
        foreach(var pair in snapshot.Files){try{if(pair.Value.Length>0&&!File.Exists(pair.Value)){failed++;continue;}desktop.SetWallpaper(pair.Key,pair.Value);}catch(COMException){failed++;}}
        return failed;
    });
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string? monitor,[MarshalAs(UnmanagedType.LPWStr)]string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]out string wallpaper);
        void GetMonitorDevicePathAt(uint index,[MarshalAs(UnmanagedType.LPWStr)]out string monitor);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor,out Rect rect);
        void SetBackgroundColor(uint color);void GetBackgroundColor(out uint color);
        void SetPosition(WallpaperPosition position);void GetPosition(out WallpaperPosition position);
    }
}
