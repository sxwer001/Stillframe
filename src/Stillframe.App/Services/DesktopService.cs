using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Stillframe.Core;

namespace Stillframe.App.Services;

public sealed record MonitorTarget(string Id,string Name){public override string ToString()=>Name;}

[SupportedOSPlatform("windows")]
public sealed class DesktopService(string root)
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
    public Task<IReadOnlyList<MonitorTarget>> EnumerateAsync()=>OnSta(ReadMonitors);
    private static IReadOnlyList<MonitorTarget> ReadMonitors(IDesktopWallpaper desktop)
    {
        var ids=MonitorIds(desktop);
        return ReadMonitors(ids,ActiveMonitorIds(),id=>{var status=desktop.GetMonitorRECT(id,out var rect);return(status,rect);});
    }
    private static string[] MonitorIds(IDesktopWallpaper desktop)
    {
        desktop.GetMonitorDevicePathCount(out var count);var ids=new string[count];
        for(uint i=0;i<count;i++){desktop.GetMonitorDevicePathAt(i,out var id);ids[i]=id;}return ids;
    }
    internal static IReadOnlyList<MonitorTarget> ReadMonitors(IEnumerable<string> ids,IReadOnlySet<string> activeIds,Func<string,(int Status,Rect Bounds)> readRectangle)
    {
        var targets=new List<MonitorTarget>{new("","所有显示器")};
        foreach(var id in ids)
        {
            // The shell retains detached paths; do not query them as current session targets.
            if(!activeIds.Contains(id))continue;
            var (status,rect)=readRectangle(id);
            if(status==1)continue; // S_FALSE: disconnected after the active-device snapshot.
            if(status<0)throw new COMException($"读取显示器尺寸失败（0x{status:X8}），请重新检测显示器。",status);
            if(status!=0||rect.Right<=rect.Left||rect.Bottom<=rect.Top)throw new AppException("显示器尺寸暂不可用，请重新检测显示器。");
            targets.Add(new(id,$"显示器 {targets.Count} · {rect.Right-rect.Left} × {rect.Bottom-rect.Top}"));
        }
        if(targets.Count==1)throw new AppException("当前会话未检测到可用显示器，请检查屏幕连接后重新检测；未修改桌面。");
        return targets;
    }
    private readonly SemaphoreSlim recoveryGate=new(1,1);
    public bool HasRecovery=>File.Exists(Path.Combine(root,"desktop-recovery-v1.json"));
    private async Task<T> RecoverAsync<T>(Func<DesktopRecovery,T> action)
    {
        await recoveryGate.WaitAsync();
        try{return await OnSta(desktop=>action(new DesktopRecovery(root,new Backend(desktop))));}
        finally{recoveryGate.Release();}
    }
    public Task<DesktopApplyResult> ApplyAsync(string path,string target,WallpaperPosition position)=>RecoverAsync(store=>store.Apply(path,target,position));
    public Task<DesktopSnapshot> ReadSnapshotAsync()=>OnSta(desktop=>new Backend(desktop).Capture());
    public Task<DesktopRestoreResult> RestoreAsync()=>RecoverAsync(store=>store.Restore());
    public Task DiscardRecoveryAsync()=>RecoverAsync(store=>{store.Discard();return true;});
    private sealed class Backend(IDesktopWallpaper desktop):IDesktopBackend
    {
        public DesktopSnapshot Capture()
        {
            var monitors=ReadMonitors(desktop);desktop.GetPosition(out var position);desktop.GetStatus(out var status);desktop.GetBackgroundColor(out var background);
            var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var monitor in monitors.Skip(1)){desktop.GetWallpaper(monitor.Id,out var path);files.Add(monitor.Id,path);}
            return new(position,files,status,background);
        }
        public void SetPosition(WallpaperPosition position)=>desktop.SetPosition(position);
        public void SetWallpaper(string monitor,string path)=>desktop.SetWallpaper(monitor,path);
    }
    [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
    private static HashSet<string> ActiveMonitorIds()
    {
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(uint adapterIndex=0;;adapterIndex++)
        {
            var adapter=new DisplayDevice{Size=(uint)Marshal.SizeOf<DisplayDevice>()};if(!EnumDisplayDevices(null,adapterIndex,ref adapter,0))break;
            if((adapter.Flags&1)==0)continue; // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            for(uint monitorIndex=0;;monitorIndex++)
            {
                var monitor=new DisplayDevice{Size=(uint)Marshal.SizeOf<DisplayDevice>()};if(!EnumDisplayDevices(adapter.Name,monitorIndex,ref monitor,1))break;
                if((monitor.Flags&1)!=0&&!string.IsNullOrEmpty(monitor.Id))ids.Add(monitor.Id); // DISPLAY_DEVICE_ACTIVE; EDD_GET_DEVICE_INTERFACE_NAME
            }
        }
        return ids;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
    }
    [DllImport("user32.dll",EntryPoint="EnumDisplayDevicesW",CharSet=CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device,uint index,ref DisplayDevice display,uint flags);
    [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string? monitor,[MarshalAs(UnmanagedType.LPWStr)]string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]out string wallpaper);
        void GetMonitorDevicePathAt(uint index,[MarshalAs(UnmanagedType.LPWStr)]out string monitor);
        void GetMonitorDevicePathCount(out uint count);
        [PreserveSig]int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor,out Rect rect);
        void SetBackgroundColor(uint color);void GetBackgroundColor(out uint color);
        void SetPosition(WallpaperPosition position);void GetPosition(out WallpaperPosition position);
        void SetSlideshow(IntPtr items);void GetSlideshow(out IntPtr items);
        void SetSlideshowOptions(uint options,uint tick);void GetSlideshowOptions(out uint options,out uint tick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)]string? monitor,uint direction);void GetStatus(out uint state);void Enable([MarshalAs(UnmanagedType.Bool)]bool enable);
    }
}
