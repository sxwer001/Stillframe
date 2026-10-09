using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stillframe.Core;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private HomeImageService homeImages=null!;
    private string homeSourceId="bing",homeSearch="",homeOrder="weekly";
    private DateOnly? checkedBingDate;
    private int homeRequestRevision;
    private CancellationTokenSource? homeRequestCancellation;
    private Task homeLoadTask=Task.CompletedTask;
    private bool homeLoading;
    private readonly Border operationStatus=new(){Visibility=Visibility.Collapsed,CornerRadius=new CornerRadius(8),Padding=new Thickness(12,8,12,8),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(190,255,255,255)),RequestedTheme=ElementTheme.Light};
    private readonly TextBlock operationLabel=new(){Text="正在加载图片",FontSize=12,Foreground=new SolidColorBrush(Colors.Black),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};

    private void InitializeOperationStatus()
    {
        progress.Height=2;progress.MinHeight=2;progress.Margin=new Thickness(0,6,0,0);
        cancel.Content=new FontIcon{Glyph="\uE711",FontSize=12};cancel.Width=28;cancel.Height=28;cancel.MinHeight=28;cancel.Padding=new Thickness(0);cancel.Background=new SolidColorBrush(Colors.Transparent);cancel.BorderThickness=new Thickness(0);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cancel,"取消当前操作");ToolTipService.SetToolTip(cancel,"取消当前操作");
        var row=new Grid{ColumnSpacing=8};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(operationLabel);Grid.SetColumn(cancel,1);row.Children.Add(cancel);
        operationStatus.Child=new StackPanel{Children={row,progress}};
    }
    private void AttachOperationStatus()
    {
        if(operationStatus.Parent is Panel owner)owner.Children.Remove(operationStatus);
        if(operation==null)return;
        if(immersive.Visibility==Visibility.Visible)
        {
            var hasPicture=selected!=null||BrowsePicture(route)!=null;
            operationStatus.Width=240;operationStatus.HorizontalAlignment=hasPicture?HorizontalAlignment.Right:HorizontalAlignment.Center;
            operationStatus.VerticalAlignment=hasPicture?VerticalAlignment.Bottom:VerticalAlignment.Center;
            operationStatus.Margin=new Thickness(20);immersive.Children.Add(operationStatus);
        }
        else
        {
            operationStatus.Width=double.NaN;operationStatus.HorizontalAlignment=HorizontalAlignment.Stretch;operationStatus.VerticalAlignment=VerticalAlignment.Top;operationStatus.Margin=new Thickness(0);
            page.Children.Insert(Math.Min(2,page.Children.Count),operationStatus);
        }
    }
    private Task RequestHomeSourceAsync(string id,string query="",string order="weekly",bool refresh=false)
        =>RequestBrowseSourceAsync("today",id,query,order,refresh);
    private Task RequestDiscoverySourceAsync(string id,string query="",string order="weekly")
        =>RequestBrowseSourceAsync("discover",id,query,order);
    private Task RequestBrowseSourceAsync(string target,string id,string query="",string order="weekly",bool refresh=false,bool random=false,bool resetHistory=true,bool preserveDrawer=false)
    {
        if(operation!=null&&!homeLoading){Notify("请先完成或取消当前操作，再切换图源。",true);return Task.CompletedTask;}
        var revision=++homeRequestRevision;homeRequestCancellation?.Cancel();
        var request=CancellationTokenSource.CreateLinkedTokenSource(appLifetime.Token);homeRequestCancellation=request;
        var previous=homeLoadTask;
        homeLoadTask=LoadHomeSourceAsync(previous,request,revision,target,id,query,order,refresh,random,resetHistory,preserveDrawer);return homeLoadTask;
    }
    private async Task LoadHomeSourceAsync(Task previous,CancellationTokenSource request,int revision,string target,string id,string search,string order,bool refresh,bool random,bool resetHistory,bool preserveDrawer)
    {
        try
        {
            await previous;if(revision!=homeRequestRevision||request.IsCancellationRequested)return;
            if(!preserveDrawer)CloseSettings();route=target;selected=null;
            synchronizingNavigation=true;try{navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().First(x=>x.Tag as string==target);}finally{synchronizingNavigation=false;}
            await RenderAsync();homeLoading=true;
            var name=id switch{"bing"=>target=="today"&&!random?"Bing 每日图":"Bing 历史图库","bing-archive"=>"Bing 历史图库","wallhaven"=>"Wallhaven","commons"=>"Commons","local"=>"本地图库",_=>"图源"};
            await RunAsync(async token=>
            {
                if(BrowsePicture(target)==null)await RenderAsync();
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,request.Token);linked.CancelAfter(TimeSpan.FromMinutes(3));
                var date=DateOnly.FromDateTime(DateTime.Now);
                if((id is "bing" or "bing-archive" or "wallhaven" or "commons")&&(id!="bing"||refresh||random||target=="discover"||await library.Repository.GetSettingAsync("home.bing.date")!=date.ToString("yyyy-MM-dd")))
                {
                    if(lastApiRequest.TryGetValue(id,out var last))
                    {var wait=TimeSpan.FromSeconds(2)-(DateTimeOffset.UtcNow-last);if(wait>TimeSpan.Zero)await Task.Delay(wait,linked.Token);}
                    lastApiRequest[id]=DateTimeOffset.UtcNow;
                }
                var alreadyShown=resetHistory?new HashSet<string>(StringComparer.Ordinal):HistoryFor(target).Ids.ToHashSet(StringComparer.Ordinal);
                var ready=id=="bing"&&target=="today"&&!random?await homeImages.BingDailyAsync(date,Transfer(),linked.Token,refresh):await homeImages.RandomAsync(id,BrowsePicture(target)?.Id,Transfer(),linked.Token,search,order,alreadyShown);
                linked.Token.ThrowIfCancellationRequested();if(revision!=homeRequestRevision)return;
                // Decode the new original while the previous image remains mounted.
                await PreparePictureAsync(ready,renderRevision);linked.Token.ThrowIfCancellationRequested();if(revision!=homeRequestRevision)return;
                if(target=="today"){daily=ready;homeSourceId=id;homeSearch=search;homeOrder=order;if(id=="bing")checkedBingDate=date;}
                else{discoveryPicture=ready;discoverySourceId=id;discoverySearch=search;discoveryOrder=order;discoverySelectionKey=$"{id}|{order}|{search}";}
                RecordBrowsePicture(target,ready,resetHistory);
                await RefreshAsync();notice.IsOpen=false;await RenderAsync();
            },"正在加载 "+name+"…");
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(revision==homeRequestRevision)ReportNavigationFailure(ex);}
        finally
        {
            if(revision==homeRequestRevision)
            {
                homeLoading=false;homeRequestCancellation=null;
                if(BrowsePicture(route)==null&&route is "today" or "discover"&&!appLifetime.IsCancellationRequested)await RenderAsync();
            }
            request.Dispose();
        }
    }
    private void CancelHomeRequest()
    {++homeRequestRevision;homeRequestCancellation?.Cancel();homeRequestCancellation=null;homeLoading=false;}
}
