using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Stillframe.Core;
using Stillframe.App.Services;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private AutomationSettings automationSettings=new();
    private DailyAutomation? automation;
    private readonly DispatcherTimer automationTimer=new(){Interval=TimeSpan.FromMinutes(1)};
    private readonly CancellationTokenSource appLifetime=new();
    private readonly SemaphoreSlim automationWriter=new(1,1);
    private readonly PersonalizationService personalization=new();
    private bool automationBusy;
    private Task automationSaveTask=Task.CompletedTask;
    private async Task LoadAutomationSettingsAsync()=>automationSettings=await AutomationSettings.LoadAsync(library.Repository);
    private void StartAutomation()
    {
        automation=new(library.Repository);
        personalization.NotificationClicked+=()=>DispatcherQueue.TryEnqueue(async ()=>{Activate();await NavigateAsync("today");});
        automationTimer.Tick+=async (_,_)=>await TickAutomationAsync();automationTimer.Start();
        _=TickAutomationAsync();
    }
    private void StopAutomation(){automationTimer.Stop();appLifetime.Cancel();personalization.Dispose();}
    private async Task TickAutomationAsync()
    {
        if(automationBusy||operation!=null||appLifetime.IsCancellationRequested||smokeReport!=null)return;
        if(!automationSettings.Desktop&&!automationSettings.LockScreen&&!automationSettings.Notifications)return;
        automationBusy=true;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(appLifetime.Token);timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await RunAsync(async cancelToken=>
            {
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token,cancelToken);
                var outcomes=await automation!.TickAsync(DateTimeOffset.Now,async token=>
                {
                    if(automationSettings.SourceId is "bing" or "wallhaven" or "commons")await library.Sources.LoadBuiltInAsync(automationSettings.SourceId,1,"","weekly",token);
                    await RefreshAsync();return await library.GetDailyAsync(DateOnly.FromDateTime(DateTime.Now),sourceId:automationSettings.SourceId);
                },async (kind,item,token)=>
                {
                    if(kind=="notification"){personalization.ShowDaily(item);return;}
                    var ready=await library.AcquireAsync(item.Id,null,token);token.ThrowIfCancellationRequested();
                    if(kind=="desktop")
                    {
                        var result=await desktop.ApplyAsync(library.OriginalPath(ready)!,monitorId,fit);snapshot=result.Before;
                        if(result.Failed.Count>0)throw new AppException("部分显示器自动更换失败，可在设置中恢复。");
                    }
                    else await personalization.SetLockScreenAsync(library.OriginalPath(ready)!,library.Root,token);
                },linked.Token);
                if(outcomes.Count>0)
                {
                    await RefreshAsync();if(route=="today"&&selected==null)await RenderAsync();
                    var failures=outcomes.Where(x=>!x.Success).ToArray();
                    if(failures.Length>0)Notify("自动任务未完成："+failures[0].Error+" 15分钟后重试。",true);
                }
            });
        }
        finally{automationBusy=false;}
    }
    private async Task SaveAutomationSettingsAsync()
    {
        await automationWriter.WaitAsync();
        try{await automationSettings.SaveAsync(library.Repository);}
        catch(Exception ex){Notify("自动任务设置未保存（"+ex.GetType().Name+"）。",true);}
        finally{automationWriter.Release();}
    }
    private void AddAutomationCards(StackPanel panel)
    {
        ComboBox Mode(string name,bool enabled,bool supported,Action<bool> update)
        {
            var control=new ComboBox{ItemsSource=new[]{"关闭","每天"},SelectedIndex=enabled?1:0,FontSize=13,HorizontalAlignment=HorizontalAlignment.Stretch,IsEnabled=supported};AutomationProperties.SetName(control,name);
            control.SelectionChanged+=(_,_)=>{update(control.SelectedIndex==1);automationSaveTask=SaveAutomationSettingsAsync();};return control;
        }
        panel.Children.Add(SettingCard("自动更换桌面","周期：每天 · 今日推荐",Mode("自动更换桌面",automationSettings.Desktop,true,value=>automationSettings=automationSettings with{Desktop=value})));
        panel.Children.Add(SettingCard("自动更换锁屏",PersonalizationService.LockScreenSupported?"周期：每天 · 今日推荐":"当前Windows不支持更换锁屏。",Mode("自动更换锁屏",automationSettings.LockScreen,PersonalizationService.LockScreenSupported,value=>automationSettings=automationSettings with{LockScreen=value})));
        var notifications=Mode("每日通知",automationSettings.Notifications,PersonalizationService.NotificationsSupported,value=>automationSettings=automationSettings with{Notifications=value});
        panel.Children.Add(SettingCard("每日通知","通知推送新壁纸",notifications));
        var hour=new ComboBox{ItemsSource=Enumerable.Range(0,24).Select(h=>$"{h:00}:00").ToArray(),SelectedIndex=automationSettings.Hour};AutomationProperties.SetName(hour,"每日执行时间");
        hour.SelectionChanged+=(_,_)=>{automationSettings=automationSettings with{Hour=hour.SelectedIndex};automationSaveTask=SaveAutomationSettingsAsync();};
        var ids=new[]{"all","local","bing","wallhaven","commons"};var source=new ComboBox{ItemsSource=new[]{"全部启用的图源","仅本地图库","Bing每日图","Wallhaven","Commons每日精选"},SelectedIndex=Array.IndexOf(ids,automationSettings.SourceId)};AutomationProperties.SetName(source,"每日推荐图源");
        source.SelectionChanged+=async (_,_)=>{if(source.SelectedIndex<0)return;automationSettings=automationSettings with{SourceId=ids[source.SelectedIndex]};automationSaveTask=SaveAutomationSettingsAsync();await automationSaveTask;await RefreshAsync();if(route=="today"&&selected==null)await NavigateAsync("today");};
        panel.Children.Add(SettingCard("每日执行计划","到点后每分钟检查，错过时在本日下一次运行补一次；关闭软件后停止。默认全部关闭，不会主动更改桌面。",new StackPanel{Spacing=10,Children={Text("执行时间",12),hour,Text("推荐图源",12),source}}));
    }
}
