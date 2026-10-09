using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace Stillframe.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--bing-archive-checks")
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            try{OnlineWindowsChecks.CheckBingArchiveAsync(args[1]).GetAwaiter().GetResult();}
            catch(Exception ex){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);File.WriteAllText(args[1],"FAIL Bing archive checks: "+ex);Environment.ExitCode=1;}
            return;
        }
        if(args.Length==2&&args[0]=="--api-checks")
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            try{OnlineWindowsChecks.RunAsync(args[1]).GetAwaiter().GetResult();}
            catch(Exception ex){File.WriteAllText(args[1],"FAIL API checks: "+ex);Environment.ExitCode=1;}
            return;
        }
        if(args.Length==2&&args[0]=="--diagnostics")
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            try{WindowsChecks.RunAsync(args[1]).GetAwaiter().GetResult();Environment.ExitCode=0;}
            catch(Exception ex){File.WriteAllText(args[1],"FAIL: "+ex.ToString());Environment.ExitCode=1;}
            return;
        }
        using var instance=new Mutex(true,"Local\\Shijing.Wallpaper.v01",out var first);
        if(!first){MessageBox(IntPtr.Zero,"拾景已在运行，请切换到现有窗口。","拾景",0);return;}
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initialization=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _=new StillframeApplication(args);
        });
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int MessageBox(IntPtr window,string text,string caption,uint type);
}
public sealed partial class StillframeApplication : Application
{
    private MainWindow? window;
    private readonly string[] arguments;
    public StillframeApplication(string[] args)
    {
        arguments=args;
        UnhandledException+=(_,e)=>
        {
            var diagnostic=arguments.Length==2&&arguments[0] is "--smoke" or "--screenshots"?arguments[1]:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Shijing","startup-error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(diagnostic)!);File.WriteAllText(diagnostic,e.Exception.ToString());
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var smoke=arguments.Length==2&&arguments[0] is "--smoke" or "--screenshots";
        window=new(smoke?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(arguments[1]))!,"ui-smoke-data"):null);window.Activate();
        if(arguments.Length==2&&arguments[0]=="--smoke")window.BeginSmoke(arguments[1]);
        if(arguments.Length==2&&arguments[0]=="--screenshots")window.BeginScreenshots(arguments[1]);
    }
}
