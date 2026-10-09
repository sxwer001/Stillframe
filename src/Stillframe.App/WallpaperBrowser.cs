using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stillframe.Core;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private WallpaperItem? discoveryPicture;
    private string discoverySourceId="wallhaven",discoverySearch="",discoveryOrder="weekly";
    // Store IDs, not decoded bitmaps. The two pages retain independent session histories.
    private sealed class BrowseHistory
    {
        public readonly List<string> Ids=[];
        public int Position=-1;
        public void Record(string id,bool reset)
        {
            if(reset){Ids.Clear();Position=-1;}
            if(Position>=0&&Ids[Position]==id)return;
            if(Position+1<Ids.Count)Ids.RemoveRange(Position+1,Ids.Count-Position-1);
            Ids.Add(id);Position=Ids.Count-1;
        }
    }
    private readonly BrowseHistory homeHistory=new(),discoveryHistory=new();
    private bool wheelBusy;
    private int wheelDelta;
    private DateTimeOffset lastWheelInput,lastWheelChange;
    private WallpaperItem? BrowsePicture(string target)=>target=="today"?daily:target=="discover"?discoveryPicture:null;
    private BrowseHistory HistoryFor(string target)=>target=="discover"?discoveryHistory:homeHistory;
    private void RecordBrowsePicture(string target,WallpaperItem item,bool reset)=>HistoryFor(target).Record(item.Id,reset);

    private void RenderDiscovery()
    {
        if(discoveryPicture!=null){RenderImmersiveHome(discoveryPicture);return;}
        immersive.Children.Add(new Border{Background=new SolidColorBrush(Colors.LightGray)});
        if(homeLoading)return;
        immersive.Children.Add(new StackPanel{Spacing=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Children={Text("发现壁纸",24),Text("展开侧栏的发现壁纸，选择图源。",13),Action("加载一张壁纸",()=>RequestDiscoverySourceAsync(discoverySourceId),true)}});
    }

    private async Task HandleWallpaperWheelAsync(int delta)
    {
        if(delta==0||settingsDrawer.IsPaneOpen||selected!=null||route is not ("today" or "discover")||operation!=null||wheelBusy){wheelDelta=0;return;}
        var now=DateTimeOffset.UtcNow;
        if(now-lastWheelInput>TimeSpan.FromMilliseconds(400)||Math.Sign(delta)!=Math.Sign(wheelDelta))wheelDelta=0;
        lastWheelInput=now;wheelDelta+=delta;
        if(Math.Abs(wheelDelta)<120||now-lastWheelChange<TimeSpan.FromMilliseconds(450))return;
        var direction=wheelDelta<0?1:-1;wheelDelta=0;wheelBusy=true;lastWheelChange=now;
        try{await StepWallpaperAsync(direction);}
        catch(Exception ex){ReportNavigationFailure(ex);}
        finally{wheelBusy=false;wheelDelta=0;lastWheelChange=DateTimeOffset.UtcNow;}
    }
    private async Task StepWallpaperAsync(int direction)
    {
        if(operation!=null||selected!=null||route is not ("today" or "discover"))return;
        var target=route;var history=HistoryFor(target);var current=BrowsePicture(target);
        if(current==null)return;
        if(history.Position<0||history.Ids[history.Position]!=current.Id)history.Record(current.Id,true);
        var next=history.Position+direction;
        if(next>=0&&next<history.Ids.Count)
        {
            var item=items.FirstOrDefault(x=>x.Id==history.Ids[next]);if(item==null||!library.HasOriginal(item))return;
            var revision=renderRevision;await PreparePictureAsync(item,revision);
            if(route!=target||renderRevision!=revision||selected!=null)return;
            history.Position=next;if(target=="today")daily=item;else discoveryPicture=item;
            notice.IsOpen=false;await RenderAsync();return;
        }
        if(direction<0)return;
        if(target=="discover")await RequestBrowseSourceAsync(target,discoverySourceId,discoverySearch,discoveryOrder,random:true,resetHistory:false);
        else await RequestBrowseSourceAsync(target,homeSourceId,homeSearch,homeOrder,random:true,resetHistory:false);
    }
}
