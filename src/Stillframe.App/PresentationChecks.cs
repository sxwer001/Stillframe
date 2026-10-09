using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Stillframe.Core;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private static void WritePreviewFixture(string path,int variant=0)
    {
        // An owned diagnostic gradient, never a bundled wallpaper or user image.
        const int width=960,height=540,stride=width*3;
        using var writer=new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42);writer.Write(54+stride*height);writer.Write(0);writer.Write(54);writer.Write(40);writer.Write(width);writer.Write(height);writer.Write((ushort)1);writer.Write((ushort)24);writer.Write(0);writer.Write(stride*height);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            writer.Write((byte)(80+130*y/height));writer.Write((byte)(85+80*x/width));writer.Write((byte)(120-80*y/height+variant));
        }
    }

    private async Task CheckPresentationAsync(string report,List<string> lines)
    {
        var folder=Path.GetDirectoryName(report)!;
        var item=items.First();
        await library.Repository.AddItemAsync(item with{Title="拾光_周度精选_超长文件名_0123456789abcdef0123456789abcdef"});await RefreshAsync();
        await NavigateAsync("today");await CaptureAsync(Path.Combine(folder,"ui-today.png"));
        if(homeCard==null||homeCard.ActualWidth>261||homeCard.ActualHeight>145)throw new Exception("Home info card grew beyond compact bounds");
        lines.Add("PASS compact home info card with long title");
        var menu=sidebarToggle;
        var menuPoint=menu.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());var brandPoint=brand.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
        if(Math.Abs(menuPoint.Y+menu.ActualHeight/2-brandPoint.Y-brand.ActualHeight/2)>1||menuPoint.X+menu.ActualWidth>=brandPoint.X)throw new Exception("Menu and brand are not aligned or overlap");
        lines.Add("PASS title bar menu and brand align without overlap");

        if(menu.Flyout!=null)throw new Exception("Hamburger still opens a flyout");
        var oldPane=settingsDrawer.Pane;var photo=immersive.Children.OfType<Grid>().First();var original=photo.Children.OfType<Image>().Single().Source;
        ((IInvokeProvider)new ButtonAutomationPeer(menu).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(300);
        if(!settingsDrawer.IsPaneOpen||drawerBody.Content is not StackPanel navPanel||!navPanel.Children.OfType<ListView>().Any())throw new Exception("Hamburger did not directly open navigation sidebar");
        await CaptureAsync(Path.Combine(folder,"ui-sidebar.png"));
        for(var cycle=0;cycle<5;cycle++)
        {
            CloseSettings();await Task.Delay(25);ToggleNavigationDrawer();await Task.Delay(25);
            if(!settingsDrawer.IsPaneOpen||!ReferenceEquals(oldPane,settingsDrawer.Pane)||!ReferenceEquals(original,photo.Children.OfType<Image>().Single().Source))throw new Exception("Rapid sidebar reversal recreated pane or photo");
        }
        CloseSettings();await Task.Delay(350);
        if(settingsDrawer.IsHitTestVisible||settingsDrawer.Visibility!=Visibility.Visible)throw new Exception("Closed persistent sidebar still intercepts input or was removed");
        lines.Add("PASS real hamburger invocation opens sidebar; five rapid reversals preserve mounted pane and image");

        var context=photo.ContextFlyout as MenuFlyout??throw new Exception("Missing native image ContextFlyout");
        if(!context.Items.OfType<MenuFlyoutItem>().Any(x=>x.Text=="换一张推荐")||context.Items.OfType<MenuFlyoutItem>().Any(x=>x.Text=="我的图库"))throw new Exception("Context menu mixes navigation and picture commands");
        var contextOpened=false;context.Opened+=(_,_)=>contextOpened=true;
        context.ShowAt(photo);await Task.Delay(150);
        if(!contextOpened||selected!=null||settingsDrawer.IsPaneOpen)throw new Exception("Context menu navigated or opened sidebar");context.Hide();await Task.Delay(150);
        lines.Add("PASS native image context menu opens without navigation and contains recommendation/collection/save commands");

        var nextFixture=Path.Combine(library.Root,"preview-next.bmp");WritePreviewFixture(nextFixture,20);
        await library.ImportAsync([nextFixture],CancellationToken.None);await RefreshAsync();var oldDaily=daily!.Id;
        context.ShowAt(photo);await Task.Delay(120);
        var nextCommand=context.Items.OfType<MenuFlyoutItem>().Single(x=>x.Text=="换一张推荐");
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(nextCommand).GetPattern(PatternInterface.Invoke)).Invoke();
        for(var attempt=0;attempt<100&&(daily?.Id==oldDaily||operation!=null);attempt++)await Task.Delay(50);
        context.Hide();
        if(daily?.Id==oldDaily||selected!=null||route!="today"||operation!=null)throw new Exception("Native recommendation command did not switch the wallpaper");
        await CaptureAsync(Path.Combine(folder,"ui-next.png"));
        lines.Add("PASS native context-menu recommendation command changes the daily wallpaper using owned fixtures");

        await Task.WhenAll(NavigateAsync("sources"),NavigateAsync("library"),NavigateAsync("today"));
        if(route!="today"||selected!=null||homeCard==null||gallery!=null)throw new Exception("Stale asynchronous navigation overwrote latest page");
        await OpenDetailAsync(items.First());var beforeFavorite=detailImage!.Source;await FavoriteAsync(selected!);
        if(!ReferenceEquals(beforeFavorite,detailImage!.Source))throw new Exception("Favorite redecoded the unchanged original");
        await NavigateAsync("today");
        lines.Add("PASS overlapping source/library/home navigation honors latest view; favorite reuses decoded original");
        await NavigateAsync("library");gallery!.UpdateLayout();
        if(gallery.ContainerFromIndex(0) is not GridViewItem container||container.ContextFlyout is not MenuFlyout)throw new Exception("Realized gallery item is missing its context commands");
        await NavigateAsync("today");lines.Add("PASS realized gallery item provides native context commands");

        await NavigateAsync("settings");await Task.Delay(300);
        if(!settingsDrawer.IsPaneOpen||route!="today"||homeCard==null)throw new Exception("Drawer replaced immersive home");
        if(transparencySlider==null)throw new Exception("Missing transparency setting");
        transparencySlider.Value=75;
        if(homeCard.Background is not AcrylicBrush material||Math.Abs(material.TintOpacity-.25)>.001)throw new Exception("Transparency preview did not update");
        await CaptureAsync(Path.Combine(folder,"ui-settings.png"));CloseSettings();await Task.Delay(300);await appearanceSaveTask;
        if(settingsDrawer.IsPaneOpen||settingsDrawer.IsHitTestVisible||await library.Repository.GetSettingAsync("homeTransparency")!="75")throw new Exception("Drawer close did not save appearance or release input");
        lines.Add("PASS settings preserves home, previews transparency and persists on close");

        await OpenDetailAsync(items.First());await CaptureAsync(Path.Combine(folder,"ui-detail.png"));
        if(detailImage==null||detailImage.Stretch!=Stretch.UniformToFill||detailImage.ActualWidth<immersive.ActualWidth*.98||detailImage.ActualHeight<immersive.ActualHeight*.98)throw new Exception("Detail does not fill the viewport");
        detailFill=false;UpdateDetailFit();await SaveAppearanceAsync();
        if(detailImage.Stretch!=Stretch.Uniform)throw new Exception("Full image mode did not update");
        await CaptureAsync(Path.Combine(folder,"ui-detail-complete.png"));
        await LoadAppearanceAsync();if(detailFill)throw new Exception("Detail preview preference was not persisted");
        detailFill=true;UpdateDetailFit();await SaveAppearanceAsync();
        lines.Add("PASS detail fills viewport and switches to persistent full-image preview");
        var more=FindVisualButtons(immersive).Single(b=>AutomationProperties.GetName(b)=="更多操作");
        var menuOpened=false;more.Flyout!.Opened+=(_,_)=>menuOpened=true;more.Flyout.ShowAt(more);await Task.Delay(140);
        if(!menuOpened)throw new Exception("More operations menu did not open");more.Flyout.Hide();await Task.Delay(140);
        var targetsOpened=false;detailTargets!.Flyout!.Opened+=(_,_)=>targetsOpened=true;detailTargets.Flyout.ShowAt(more);await Task.Delay(140);
        if(!targetsOpened)throw new Exception("Detached target flyout did not attach to the toolbar owner");detailTargets.Flyout.Hide();await Task.Delay(140);
        lines.Add("PASS minimal toolbar more-menu and monitor flyout open");

        for(var cycle=0;cycle<3;cycle++)
        {
            var previous=selected;await NavigateAsync("settings");await Task.Delay(240);
            if(selected!=previous||route!="today"||!settingsDrawer.IsPaneOpen)throw new Exception("Settings replaced selected detail");
            CloseSettings();await Task.Delay(240);await appearanceSaveTask;
        }
        lines.Add("PASS repeated settings drawer open/close preserves selected detail");
        var before=AppWindow.Size;AppWindow.Resize(new Windows.Graphics.SizeInt32(900,680));await Task.Delay(180);
        await NavigateAsync("today");await CaptureAsync(Path.Combine(folder,"ui-today-small.png"));await NavigateAsync("settings");await Task.Delay(300);
        if(!settingsDrawer.IsPaneOpen||settingsDrawer.Visibility!=Visibility.Visible)throw new Exception($"Settings drawer did not reopen after window resize: open={settingsDrawer.IsPaneOpen}, visibility={settingsDrawer.Visibility}, opening={drawerOpening}, operation={operation!=null}, route={route}, paneWidth={settingsDrawer.OpenPaneLength}, width={settingsDrawer.ActualWidth}");
        await CaptureAsync(Path.Combine(folder,"ui-settings-small.png"));
        CloseSettings();await Task.Delay(260);await appearanceSaveTask;await OpenDetailAsync(items.First());await CaptureAsync(Path.Combine(folder,"ui-detail-small.png"));AppWindow.Resize(before);
        await CheckDrawerNavigationAsync(folder,lines);
        await CheckOnlineSettingsAsync(folder,lines);
        await CheckHomeLoadingAsync(folder,lines);
        await CheckWallpaperBrowserAsync(folder,lines);
        await CheckGalleryViewportAsync(folder,lines);
    }

    private async Task CheckGalleryViewportAsync(string folder,List<string> lines)
    {
        var fixture=items.First(x=>x.SourceId=="local");
        for(var n=0;n<36;n++)await library.Repository.AddItemAsync(fixture with{Id="viewport-check-"+n,Title="布局验证 "+n,OriginalUrl=null});
        await RefreshAsync();var previousSize=AppWindow.Size;
        foreach(var size in new[]{new Windows.Graphics.SizeInt32(1400,1000),new Windows.Graphics.SizeInt32(900,680)})
        {
            AppWindow.Resize(size);await NavigateAsync("library");await Task.Delay(250);pageViewport.UpdateLayout();
            var top=gallery!.TransformToVisual(pageViewport).TransformPoint(new Windows.Foundation.Point()).Y;
            if(Math.Abs(top+gallery.ActualHeight-pageViewport.ActualHeight)>2||gallery.ActualHeight<=0)throw new Exception("Gallery leaves an unused bottom strip after resize");
            var scroller=VisualControls<ScrollViewer>(gallery).First();
            if(scroller.ScrollableHeight<=0)throw new Exception("Gallery lost its own scrolling/virtualization viewport");
            scroller.ChangeView(null,scroller.ScrollableHeight,null,true);await Task.Delay(120);
            if(scroller.VerticalOffset<scroller.ScrollableHeight-2)throw new Exception("Last gallery row cannot be reached");
            await CaptureAsync(Path.Combine(folder,$"ui-library-{size.Width}.png"));
        }
        lines.Add("PASS library fills remaining viewport and reaches final row at large and small window sizes");
        await NavigateAsync("discover");ShowNavigationDrawer();await Task.Delay(250);
        if(discoveryExpander!.BorderBrush is not SolidColorBrush stroke||stroke.Color.R!=stroke.Color.G||stroke.Color.G!=stroke.Color.B)throw new Exception("Selected discovery retains a colored border");
        discoveryExpander.IsExpanded=true;await drawerNavigationTask;await Task.Delay(150);
        if(FindVisualButtons(drawerBody).Any(b=>b.Background is SolidColorBrush brush&&(brush.Color.R!=brush.Color.G||brush.Color.G!=brush.Color.B)))throw new Exception("Discovery source retains a colored selection surface");
        await CaptureAsync(Path.Combine(folder,"ui-sidebar-neutral.png"));lines.Add("PASS selected discovery and source choices retain neutral card appearance");
        CloseSettings();await Task.Delay(250);AppWindow.Resize(previousSize);
        var context=PictureCommands(fixture);
        if(!context.Items.OfType<MenuFlyoutItem>().Any(x=>x.Text=="设为锁屏壁纸"))throw new Exception("Picture context commands lack manual lockscreen action");
        await OpenDetailAsync(fixture);await Task.Delay(150);
        var more=FindVisualButtons(immersive).Single(b=>AutomationProperties.GetName(b)=="更多操作");
        if(more.Flyout is not MenuFlyout menu||!menu.Items.OfType<MenuFlyoutItem>().Any(x=>x.Text=="设为锁屏壁纸"))throw new Exception("Detail more-menu lacks manual lockscreen action");
        lines.Add("PASS manual lockscreen action is available from picture context and detail more-menu; no system mutation");
    }

    private async Task CheckHomeLoadingAsync(string folder,List<string> lines)
    {
        bool fail=false;var candidates=await ConfigureHomeCheckSourcesAsync(()=>fail);
        await NavigateAsync("today");var previous=daily!.Id;var first=RequestHomeSourceAsync("wallhaven");await Task.Delay(80);
        if(daily.Id!=previous||operationStatus.Parent!=immersive||operationStatus.Parent==shell||operationStatus.Visibility!=Visibility.Visible)throw new Exception("Home loading removed previous image or floated above the shell");
        await CaptureAsync(Path.Combine(folder,"ui-home-loading.png"));
        lines.Add("PASS image loading status is inside image viewport and preserves previous original");
        ShowNavigationDrawer();if(!settingsDrawer.IsPaneOpen||!settingsDrawer.IsEnabled)throw new Exception("Image loading blocked source selection sidebar");
        var second=RequestHomeSourceAsync("commons");await Task.WhenAll(first,second);
        if(homeSourceId!="commons"||daily?.SourceId!="commons"||route!="today"||selected!=null||gallery!=null||operationStatus.Visibility!=Visibility.Collapsed)throw new Exception("Latest source did not replace home after original decode");
        await CaptureAsync(Path.Combine(folder,"ui-home-source.png"));
        lines.Add("PASS rapid source switching cancels previous load and displays latest source on home");
        await RequestHomeSourceAsync("bing");if(daily?.Id!=candidates["bing"][1].Id||homeSourceId!="bing")throw new Exception("Home randomized Bing instead of showing daily featured image");
        await FavoriteAsync(daily!);if(daily.Id!=candidates["bing"][1].Id)throw new Exception("Library refresh replaced current home selection");
        lines.Add("PASS Bing featured image and home selection survive favorite/library refresh");
        var beforeCancel=daily.Id;var canceled=RequestHomeSourceAsync("wallhaven");await Task.Delay(80);
        ((IInvokeProvider)new ButtonAutomationPeer(cancel).GetPattern(PatternInterface.Invoke)).Invoke();await canceled;
        if(daily.Id!=beforeCancel||operation!=null||operationStatus.Visibility!=Visibility.Collapsed)throw new Exception("Canceled source load changed current photo or retained progress");
        lines.Add("PASS real cancel command retains current home photo and removes loading status");
        fail=true;await RequestHomeSourceAsync("bing",refresh:true);
        if(daily.Id!=beforeCancel||!notice.IsOpen||notice.Severity!=InfoBarSeverity.Error)throw new Exception("Failed source lost last good photo or failure feedback");fail=false;
        lines.Add("PASS failed home refresh preserves last good original with error feedback");
        await NavigateAsync("library");var gate=new TaskCompletionSource();var work=RunAsync(async token=>await gate.Task.WaitAsync(token),"独立检查：图库操作");
        if(operationStatus.Parent!=page||operationStatus.Parent==shell)throw new Exception("Library progress is not inline in the page");
        await CaptureAsync(Path.Combine(folder,"ui-library-loading.png"));
        ((IInvokeProvider)new ButtonAutomationPeer(cancel).GetPattern(PatternInterface.Invoke)).Invoke();await work;
        if(operationStatus.Visibility!=Visibility.Collapsed||operation!=null)throw new Exception("Inline page cancel did not remove progress");
        lines.Add("PASS non-image operations use cancelable inline page status rather than shell overlay");
    }
    private async Task<Dictionary<string,WallpaperItem[]>> ConfigureHomeCheckSourcesAsync(Func<bool>? shouldFail=null)
    {
        var assets=items.Where(x=>x.SourceId=="local").Take(2).ToArray();if(assets.Length!=2)throw new Exception("Home checks need two owned originals");
        var candidates=new Dictionary<string,WallpaperItem[]>();
        foreach(var id in new[]{"bing","wallhaven","commons"})
        {
            await library.Repository.SaveSourceAsync(BuiltInSources.Create(id));
            candidates[id]=assets.Select((x,index)=>x with{Id="home-check-"+id+"-"+index,SourceId=id,SourceName=id,Title=id+" 独立验证图 "+index}).ToArray();
            foreach(var item in candidates[id])await library.Repository.AddItemAsync(item);
        }
        homeImages=new(library,async (id,search,order,token)=>
        {
            await Task.Delay(600,token);if(shouldFail?.Invoke()==true)throw new AppException("独立检查：图源暂时不可用。");
            return new SourceBatch(candidates[id],1,1,id=="bing"?candidates[id][1].Id:null);
        });
        return candidates;
    }

    private static IEnumerable<T> VisualControls<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
        {
            var child=VisualTreeHelper.GetChild(parent,index);if(child is T match)yield return match;
            foreach(var descendant in VisualControls<T>(child))yield return descendant;
        }
    }
    private async Task CheckOnlineSettingsAsync(string folder,List<string> lines)
    {
        await NavigateAsync("today");ShowNavigationDrawer();await Task.Delay(300);
        if(discoveryExpander==null)throw new Exception("Sidebar discovery foldout missing");
        lines.Add("PASS native sidebar exposes discovery source foldout");
        var navigationCards=VisualControls<Border>(drawerBody).Where(x=>x.CornerRadius.TopLeft==10&&x.Visibility==Visibility.Visible).ToArray();
        if(discoveryExpander.CornerRadius.TopLeft!=10||discoveryExpander.BorderThickness.Left!=1||navigationCards.Count(x=>x.BorderThickness.Left==1)<4)throw new Exception("Rendered sidebar card styles do not agree");
        lines.Add("PASS sidebar navigation and source groups share card radius and border");
        await NavigateAsync("settings");await Task.Delay(300);
        var controls=VisualControls<ComboBox>(drawerBody).Where(x=>!string.IsNullOrEmpty(AutomationProperties.GetName(x))).ToDictionary(x=>AutomationProperties.GetName(x),x=>x);
        if(!controls.TryGetValue("自动更换桌面",out var desktopMode)||!controls.ContainsKey("自动更换锁屏")||!controls.ContainsKey("每日通知"))throw new Exception("Daily automation controls missing");
        if(automationTimer.IsEnabled||automationSettings.Desktop||automationSettings.LockScreen||automationSettings.Notifications)throw new Exception("Smoke must never start automation or default effects");
        desktopMode.SelectedIndex=1;await automationSaveTask;
        controls["每日执行时间"].SelectedIndex=14;await automationSaveTask;
        var saved=await AutomationSettings.LoadAsync(library.Repository);
        if(!saved.Desktop||saved.Hour!=14||saved.Notifications||saved.LockScreen)throw new Exception("Native automation setting did not persist");
        desktopMode.SelectedIndex=0;await automationSaveTask;controls["每日执行时间"].SelectedIndex=9;await automationSaveTask;
        await CaptureAsync(Path.Combine(folder,"ui-automation.png"));CloseSettings();await Task.Delay(300);
        lines.Add("PASS native daily settings persist; smoke timer stays stopped and no system effects run");
        await ConfigureHomeCheckSourcesAsync();
        ShowNavigationDrawer();await Task.Delay(250);
        ((IExpandCollapseProvider)new ExpanderAutomationPeer(discoveryExpander!).GetPattern(PatternInterface.ExpandCollapse)).Expand();await drawerNavigationTask;await Task.Delay(250);
        if(route!="discover"||!settingsDrawer.IsPaneOpen||!discoveryExpander!.IsExpanded)throw new Exception("Expanding discovery did not retain source sidebar");
        await CaptureAsync(Path.Combine(folder,"ui-discovery-sidebar.png"));
        var choice=FindVisualButtons(drawerBody).Single(x=>AutomationProperties.GetName(x)=="发现图源 Wallhaven 本周热门");
        ((IInvokeProvider)new ButtonAutomationPeer(choice).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(60);await homeLoadTask;
        if(route!="discover"||discoveryPicture?.SourceId!="wallhaven"||gallery!=null||settingsDrawer.IsPaneOpen)throw new Exception("Discovery source button did not display a single original");
        ShowNavigationDrawer();await Task.Delay(250);discoveryExpander!.IsExpanded=true;await drawerNavigationTask;await Task.Delay(100);
        choice=FindVisualButtons(drawerBody).Single(x=>AutomationProperties.GetName(x)=="发现图源 Commons 每日精选");
        ((IInvokeProvider)new ButtonAutomationPeer(choice).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(60);await homeLoadTask;
        if(route!="discover"||discoveryPicture?.SourceId!="commons"||gallery!=null)throw new Exception("Commons selection did not stay in discovery");
        await CaptureAsync(Path.Combine(folder,"ui-online-discovery.png"));
        await NavigateAsync("sources");await CaptureAsync(Path.Combine(folder,"ui-source-cards.png"));
        lines.Add("PASS actual discovery foldout and source buttons display one original without gallery or home replacement");
    }

    private async Task CheckDrawerNavigationAsync(string folder,List<string> lines)
    {
        await NavigateAsync("today");
        foreach(var target in new[]{"today","discover","library","sources","settings"})
        {
            ShowNavigationDrawer();await Task.Delay(300);drawerBody.UpdateLayout();
            if(Math.Abs(settingsDrawer.OpenPaneLength-240)>.1)throw new Exception("Navigation sidebar is not the compact fixed width");
            var links=((StackPanel)drawerBody.Content).Children.OfType<ListView>().Single();
            var index=Array.FindIndex((DrawerLink[])links.ItemsSource,x=>x.Route==target);
            var container=links.ContainerFromIndex(index) as ListViewItem??throw new Exception("Navigation item did not realize: "+target);
            var backgroundRoute=route;var backgroundSelection=selected;
            // Invoke the real control's click path, not NavigateAsync or selection setters.
            ((IInvokeProvider)new ListViewItemAutomationPeer(container).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(80);await drawerNavigationTask;await Task.Delay(300);
            if(target=="settings")
            {
                if(!settingsDrawer.IsPaneOpen||!drawerShowsSettings||route!=backgroundRoute||selected!=backgroundSelection||transparencySlider?.XamlRoot==null)throw new Exception("Settings click did not open the actual settings panel");
                if(settingsDrawer.OpenPaneLength<300||settingsDrawer.OpenPaneLength>360)throw new Exception("Settings form width outside bounded range");
                CloseSettings();await Task.Delay(300);
            }
            else if(route!=target||selected!=null||settingsDrawer.IsPaneOpen!=(target=="discover")||immersive.Visibility!=(target is "today" or "discover"?Visibility.Visible:Visibility.Collapsed)||target=="library"&&gallery==null||target=="discover"&&(gallery!=null||discoveryExpander?.IsExpanded!=true))
                throw new Exception("Sidebar click did not navigate to actual content: "+target);
            lines.Add("PASS actual sidebar item invocation navigates: "+target);
        }
        await NavigateAsync("today");ShowNavigationDrawer();await Task.Delay(300);
        await CaptureAsync(Path.Combine(folder,"ui-sidebar.png"));
        var originalSize=AppWindow.Size;AppWindow.Resize(new Windows.Graphics.SizeInt32(900,680));await Task.Delay(300);
        if(settingsDrawer.OpenPaneLength!=240||!settingsDrawer.IsPaneOpen)throw new Exception("Compact navigation width/open state did not survive resize");
        await CaptureAsync(Path.Combine(folder,"ui-sidebar-small.png"));
        CloseSettings();await Task.Delay(300);AppWindow.Resize(originalSize);
        lines.Add("PASS compact navigation sidebar remains 240 DIP at normal and small window sizes");
    }

    private async Task CheckWallpaperBrowserAsync(string folder,List<string> lines)
    {
        await ConfigureHomeCheckSourcesAsync();await RequestHomeSourceAsync("bing");var initialHome=daily!.Id;
        await RequestDiscoverySourceAsync("wallhaven");var first=discoveryPicture!.Id;
        if(immersive.Visibility!=Visibility.Visible||gallery!=null||homeCard==null||daily.Id!=initialHome)throw new Exception("Discovery is not independent immersive browsing");
        await CaptureAsync(Path.Combine(folder,"ui-discovery-large.png"));lines.Add("PASS discovery fills viewport with independent original and compact info card");
        await HandleWallpaperWheelAsync(-120);var second=discoveryPicture!.Id;
        if(second==first||discoveryHistory.Position!=1)throw new Exception("Wheel down did not load next source picture");
        await Task.Delay(480);await HandleWallpaperWheelAsync(120);
        if(discoveryPicture.Id!=first||discoveryHistory.Position!=0)throw new Exception("Wheel up did not restore previous original");
        await Task.Delay(480);await HandleWallpaperWheelAsync(-120);
        if(discoveryPicture.Id!=second||discoveryHistory.Position!=1)throw new Exception("Wheel forward did not reuse browse history");
        lines.Add("PASS discovery wheel down/up/forward changes originals and reuses previous history");
        ShowNavigationDrawer();await Task.Delay(100);await HandleWallpaperWheelAsync(-120);
        if(discoveryPicture.Id!=second)throw new Exception("Sidebar scroll changed background photo");CloseSettings();await Task.Delay(450);
        await OpenDetailAsync(discoveryPicture);await HandleWallpaperWheelAsync(-120);
        if(selected?.Id!=second||discoveryPicture.Id!=second)throw new Exception("Detail wheel changed browser selection");await NavigateAsync("discover");
        lines.Add("PASS sidebar and detail wheel input never switches background wallpaper");
        await NavigateAsync("today");await Task.Delay(480);await HandleWallpaperWheelAsync(-60);
        if(daily.Id!=initialHome)throw new Exception("Partial wheel tick caused a download");await HandleWallpaperWheelAsync(-60);
        if(daily.Id==initialHome||daily.SourceId!="bing")throw new Exception("Home wheel could not browse recent Bing originals");
        var changedHome=daily.Id;await HandleWallpaperWheelAsync(-120);if(daily.Id!=changedHome)throw new Exception("Immediate wheel burst bypassed throttle");
        await Task.Delay(480);await HandleWallpaperWheelAsync(120);if(daily.Id!=initialHome)throw new Exception("Home wheel up did not return to original daily image");
        lines.Add("PASS home wheel accumulates ticks, throttles bursts and returns to Bing daily original");
        await NavigateAsync("discover");if(discoveryPicture.Id!=second||daily.Id!=initialHome)throw new Exception("Switching pages lost independent history");
        var oldSource=discoverySourceId;var change=RequestDiscoverySourceAsync("commons");await Task.Delay(80);await NavigateAsync("library");await change;
        if(route!="library"||discoverySourceId!=oldSource||operation!=null)throw new Exception("Leaving discovery allowed canceled source result to replace route");
        lines.Add("PASS independent page histories survive navigation; leaving discovery cancels pending original");
        catalogSource="wallhaven";apiResultIds=null;await NavigateAsync("catalog");
        if(gallery==null||immersive.Visibility!=Visibility.Collapsed)throw new Exception("Explicit API gallery is no longer reachable");
        await OpenDetailAsync(items.First(x=>x.SourceId=="wallhaven"));await NavigateAsync("catalog");
        if(route!="catalog"||selected!=null||gallery==null)throw new Exception("Online gallery detail could not return to catalog");
        lines.Add("PASS explicit online gallery and detail return remain available independently of discovery");
    }
}
