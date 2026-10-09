using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Stillframe.App.Services;
using Stillframe.Core;
using Windows.UI;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private bool fullScreen;
    private Task<bool> pictureReady=Task.FromResult(true);
    private Border? homeCard;
    private Image? detailImage;
    private ToggleMenuFlyoutItem? detailFitMenuItem;
    private Button? detailTargets;
    private bool detailFill=true;
    private double homeTransparency=65;
    private static TransitionCollection ChromeEntrance()=>new(){new EntranceThemeTransition{FromHorizontalOffset=0,FromVerticalOffset=8,IsStaggeringEnabled=false}};
    private string? preparedPicturePath;
    private BitmapImage? preparedPicture;
    private int renderRevision;

    private async Task PreparePictureAsync(WallpaperItem item,int revision)
    {
        var path=library.OriginalPath(item);
        if(!library.HasOriginal(item)||path==null||path==preparedPicturePath&&preparedPicture!=null)return;
        var bitmap=new BitmapImage{DecodePixelWidth=Math.Clamp(AppWindow.Size.Width,1600,4096)};
        // Decode before removing the old view. Keep only one original in memory.
        using var stream=await Windows.Storage.Streams.FileRandomAccessStream.OpenAsync(path,Windows.Storage.FileAccessMode.Read);
        await bitmap.SetSourceAsync(stream);
        if(revision!=renderRevision)return;
        preparedPicture=bitmap;preparedPicturePath=path;
    }

    private void SetChrome(bool overImage)
    {
        var foreground=overImage?Colors.White:immersive.Visibility==Visibility.Visible?Colors.Black:shell.ActualTheme==ElementTheme.Dark?Colors.White:Colors.Black;
        brand.Foreground=new SolidColorBrush(foreground);
        titlebar.Background=new SolidColorBrush(Colors.Transparent);
        var chrome=AppWindow.TitleBar;
        chrome.ButtonBackgroundColor=Colors.Transparent;
        chrome.ButtonInactiveBackgroundColor=Colors.Transparent;
        chrome.ButtonForegroundColor=foreground;
        chrome.ButtonInactiveForegroundColor=overImage?Colors.LightGray:Colors.Gray;
        chrome.ButtonHoverBackgroundColor=Color.FromArgb(80,128,128,128);
    }

    private async Task NavigateAsync(string target)
    {
        if(operation!=null&&!homeLoading)return;
        if(target=="settings")
        {
            synchronizingNavigation=true;
            try{navigation.SelectedItem=navigation.MenuItems.OfType<NavigationViewItem>().First(x=>x.Tag as string==(route=="catalog"?"library":route));}
            finally{synchronizingNavigation=false;}
            await OpenSettingsAsync();return;
        }
        try
        {
            if(homeLoading&&target!=route){CancelHomeRequest();await homeLoadTask;}
            if(target=="discover"&&discoveryPicture==null)
            {await RequestBrowseSourceAsync("discover",smokeReport!=null?"local":discoverySourceId,preserveDrawer:settingsDrawer.IsPaneOpen);return;}
            if(target=="today"&&smokeReport==null&&homeSourceId=="bing"&&checkedBingDate!=DateOnly.FromDateTime(DateTime.Now))
            {await RequestHomeSourceAsync("bing");return;}
            synchronizingNavigation=true;
            try{navigation.SelectedItem=target=="settings"?navigation.SettingsItem:navigation.MenuItems.OfType<NavigationViewItem>().First(x=>x.Tag as string==(target=="catalog"?"library":target));}
            finally{synchronizingNavigation=false;}
            route=target;selected=null;notice.IsOpen=false;
            if(target=="today"&&daily!=null&&!library.HasOriginal(daily)&&daily.OriginalUrl!=null)await RunAsync(LoadDailyOriginalAsync);
            await RenderAsync();
        }
        catch(Exception ex){ReportNavigationFailure(ex);}
    }

    private async Task LoadDailyOriginalAsync(CancellationToken token)
    {
        if(daily==null||library.HasOriginal(daily)||daily.OriginalUrl==null)return;
        await library.AcquireAsync(daily.Id,Transfer(),token);await RefreshAsync();
    }

    private async Task OpenDetailAsync(WallpaperItem item)
    {
        if(operation!=null)return;
        if(!library.HasOriginal(item)&&item.OriginalUrl!=null)
        {
            await RunAsync(async token=>{var ready=await library.AcquireAsync(item.Id,Transfer(),token);await RefreshAsync();selected=ready;notice.IsOpen=false;await RenderAsync();},"正在加载高清原图…");return;
        }
        try{selected=item;notice.IsOpen=false;await RenderAsync();}
        catch(Exception ex){ReportNavigationFailure(ex);}
    }

    private void ReportNavigationFailure(Exception exception)
    {
        // Keep actionable diagnostics instead of allowing an async event to terminate the app.
        try{File.WriteAllText(Path.Combine(library.Root,"navigation-error.txt"),exception.ToString());}catch{}
        if(smokeReport!=null)throw new InvalidOperationException("Navigation regression",exception);
        Notify("页面未能打开，请重试。诊断已记录在数据目录的 navigation-error.txt。",true);
    }

    private Grid Picture(WallpaperItem item,Stretch stretch)
    {
        var surface=new Grid{RequestedTheme=ElementTheme.Light,Background=new SolidColorBrush(Color.FromArgb(255,226,230,232)),ContextFlyout=PictureCommands(item)};
        pictureReady=Task.FromResult(true);
        var path=library.HasOriginal(item)?library.OriginalPath(item):library.ThumbnailPath(item);
        if(path!=null&&File.Exists(path))
        {
            var bitmap=preparedPicturePath==path&&preparedPicture!=null?preparedPicture:new BitmapImage(new Uri(path)){DecodePixelWidth=Math.Clamp(AppWindow.Size.Width,1600,4096)};
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);pictureReady=completion.Task;
            // SetSourceAsync already completed for the shared source. ImageOpened may
            // have fired before this new Image subscribes, so do not wait for it twice.
            if(ReferenceEquals(bitmap,preparedPicture))completion.TrySetResult(true);
            var image=new Image{Stretch=stretch,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
            image.ImageOpened+=(_,_)=>completion.TrySetResult(true);
            image.ImageFailed+=(_,_)=>
            {
                completion.TrySetResult(false);
                surface.Children.Add(new TextBlock{Text="图片无法显示，请重新导入或下载。",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Colors.Black)});
            };
            image.Source=bitmap;
            surface.Children.Add(image);
        }
        else
        {
            surface.Children.Add(new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Spacing=12,Children={new SymbolIcon(Symbol.Pictures),Text("原图待下载",24),Text("点击保存原图或设为壁纸，获取高清图片。")}});
        }
        return surface;
    }

    private static Brush Glass()=>new AcrylicBrush{TintColor=Colors.White,TintOpacity=.86,FallbackColor=Color.FromArgb(255,242,242,242)};
    private Brush HomeGlass()=>new AcrylicBrush{TintColor=Colors.White,TintOpacity=1-homeTransparency/100,TintLuminosityOpacity=.45,FallbackColor=Color.FromArgb(255,242,242,242)};
    private static Border GlassCard(UIElement content)=>new(){Child=content,Background=Glass(),CornerRadius=new CornerRadius(18),Padding=new Thickness(20),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(80,255,255,255)),RequestedTheme=ElementTheme.Light};
    private static FontIcon Glyph(Symbol symbol)=>new(){Glyph=symbol switch
    {
        Symbol.List=>"\uE700",Symbol.Back=>"\uE72B",Symbol.Home=>"\uE80F",Symbol.Find=>"\uE721",Symbol.Pictures=>"\uE8B9",
        Symbol.World=>"\uE909",Symbol.Setting=>"\uE713",Symbol.Cancel=>"\uE711",Symbol.Caption=>"\uE946",Symbol.Refresh=>"\uE72C",
        Symbol.Favorite=>"\uE734",Symbol.FullScreen=>"\uE740",Symbol.Download=>"\uE896",_=>char.ConvertFromUtf32((int)symbol)
    },FontSize=16,FontFamily=new FontFamily("Segoe MDL2 Assets")};
    private static void TransparentChrome(Button button)
    {
        button.Background=new SolidColorBrush(Colors.Transparent);button.BorderBrush=new SolidColorBrush(Colors.Transparent);button.BorderThickness=new Thickness(0);button.Foreground=new SolidColorBrush(Colors.White);button.Padding=new Thickness(0);
        button.Resources["ButtonBackgroundPointerOver"]=new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBackgroundPressed"]=new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBorderBrushPointerOver"]=new SolidColorBrush(Colors.Transparent);button.Resources["ButtonBorderBrushPressed"]=new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonForegroundPointerOver"]=new SolidColorBrush(Colors.White);button.Resources["ButtonForegroundPressed"]=new SolidColorBrush(Colors.White);
    }

    private Button Tool(string label,Symbol icon,Func<Task> action,bool primary=false,bool iconOnly=false)
    {
        var button=Action(label,action,primary);
        button.Content=iconOnly?Glyph(icon):new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={Glyph(icon),Text(label,12.5)}};
        button.MinHeight=36;AutomationProperties.SetName(button,label);ToolTipService.SetToolTip(button,label);return button;
    }

    private Button Menu()
    {
        var button=new Button{Content=Glyph(Symbol.List),Width=36,Height=34,Margin=new Thickness(16,5,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,RequestedTheme=ElementTheme.Light};TransparentChrome(button);
        button.Click+=(_,_)=>ToggleNavigationDrawer();
        AutomationProperties.SetName(button,"打开侧边栏");ToolTipService.SetToolTip(button,"侧边栏 · 图库与设置");return button;
    }

    private string FileSummary(WallpaperItem item)
    {
        var path=library.OriginalPath(item);
        var size="原图待下载";
        if(path!=null&&File.Exists(path))
        {
            var bytes=new FileInfo(path).Length;
            size=bytes>=1048576?$"{bytes/1048576d:0.0} MB":$"{bytes/1024d:0.#} KB";
        }
        return item.SourceName+" · "+item.Dimensions+" · "+size;
    }

    private static void AddSourceLink(StackPanel panel,string? address,string label)
    {
        if(address!=null&&Uri.TryCreate(address,UriKind.Absolute,out var uri)&&uri.Scheme=="https")panel.Children.Add(new HyperlinkButton{Content=label,NavigateUri=uri,Padding=new Thickness(0)});
    }

    private void RenderImmersiveHome(WallpaperItem item)
    {
        var picture=Picture(item,Stretch.UniformToFill);
        // Tapped handles primary activation; right click remains a context action.
        picture.Tapped+=async (_,_)=>await OpenDetailAsync(item);
        immersive.Children.Add(picture);
        // Shade only the edges so the artwork remains clear and controls stay readable.
        var shade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};
        shade.GradientStops.Add(new(){Color=Color.FromArgb(70,0,0,0),Offset=0});shade.GradientStops.Add(new(){Color=Colors.Transparent,Offset=.22});shade.GradientStops.Add(new(){Color=Colors.Transparent,Offset=.65});shade.GradientStops.Add(new(){Color=Color.FromArgb(55,0,0,0),Offset=1});
        immersive.Children.Add(new Border{Background=shade,IsHitTestVisible=false});
        var info=new StackPanel{Spacing=8};
        var title=Text(item.Title,17);title.MaxLines=1;title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;title.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(title);
        info.Children.Add(new Border{Height=2,CornerRadius=new CornerRadius(1),Background=new SolidColorBrush(Color.FromArgb(255,166,112,30))});
        var author=Text(item.Author??"作者未提供",11.5);author.MaxLines=1;author.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(author);
        info.Children.Add(Text("收录于 "+item.AddedAt.ToLocalTime().ToString("yyyy年M月d日"),11));
        var summary=Text(FileSummary(item),11);summary.MaxLines=1;summary.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(summary);
        var card=GlassCard(info);homeCard=card;card.Background=HomeGlass();card.Padding=new Thickness(16,12,16,12);card.CornerRadius=new CornerRadius(12);card.MaxWidth=240;card.Margin=new Thickness(20,12,20,20);card.HorizontalAlignment=HorizontalAlignment.Left;card.VerticalAlignment=VerticalAlignment.Bottom;
        var intro=Action("查看图片介绍",()=>OpenDetailAsync(item));intro.Content=card;intro.Padding=new Thickness(0);intro.BorderThickness=new Thickness(0);intro.Background=new SolidColorBrush(Colors.Transparent);intro.HorizontalContentAlignment=HorizontalAlignment.Stretch;AutomationProperties.SetName(intro,"查看图片介绍");ToolTipService.SetToolTip(intro,item.Title);
        intro.Resources["ButtonBackgroundPointerOver"]=new SolidColorBrush(Colors.Transparent);intro.Resources["ButtonBackgroundPressed"]=new SolidColorBrush(Colors.Transparent);intro.Resources["ButtonBorderBrushPointerOver"]=new SolidColorBrush(Colors.Transparent);
        // Let wheel input over the compact info card bubble to the image browser.
        var holder=new Border{Child=intro,MaxWidth=280,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
        if(animateViewChrome)holder.Transitions=ChromeEntrance();
        immersive.Children.Add(holder);
    }

    private void HomeSizeChanged(object sender,SizeChangedEventArgs e)
    {
        foreach(var holder in immersive.Children.OfType<ScrollViewer>())holder.MaxHeight=Math.Max(120,e.NewSize.Height-90);
        UpdateDrawerWidth();
    }

    private Button TargetOptions()
    {
        // UIElements belong to one visual tree. Persist values, create fresh controls per view.
        var monitors=new ComboBox{Header="目标显示器",ItemsSource=monitorTargets,SelectedItem=monitorTargets.FirstOrDefault(x=>x.Id==monitorId),MinWidth=240};
        var modes=new[]{WallpaperPosition.Fill,WallpaperPosition.Fit,WallpaperPosition.Center,WallpaperPosition.Span};
        var position=new ComboBox{Header="适配方式",ItemsSource=new[]{"填充","适应","居中","跨屏"},SelectedIndex=Array.IndexOf(modes,fit),MinWidth=240};
        monitors.SelectionChanged+=(_,_)=>monitorId=(monitors.SelectedItem as MonitorTarget)?.Id??"";
        position.SelectionChanged+=(_,_)=>{if(position.SelectedIndex>=0)fit=modes[position.SelectedIndex];};
        var options=new StackPanel{Spacing=12,MaxWidth=280,Children={monitors,position,Text("适配方式是 Windows 全局设置，会影响所有屏幕；跨屏统一设置所有显示器。",12)}};
        var button=new Button{Content=Glyph(Symbol.Switch),MinHeight=36,MinWidth=36,Flyout=new Flyout{Content=options}};
        ToolTipService.SetToolTip(button,"显示器与适配");AutomationProperties.SetName(button,"显示器与适配");return button;
    }

    private Button Attribution(WallpaperItem item)
    {
        var panel=new StackPanel{Spacing=10,MaxWidth=320};panel.Children.Add(Text(item.Title,20));panel.Children.Add(Text("作者："+(item.Author??"未提供")));panel.Children.Add(Text(FileSummary(item)));panel.Children.Add(Text("收录时间："+item.AddedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));panel.Children.Add(Text("许可："+(item.LicenseName??"未提供，请核对来源使用条件")));
        AddSourceLink(panel,item.SourcePage,"查看图片来源 ↗");AddSourceLink(panel,item.LicenseUrl,"查看许可条件 ↗");
        var button=new Button{Content=Glyph(Symbol.Caption),MinHeight=36,MinWidth=36,Flyout=new Flyout{Content=panel}};
        ToolTipService.SetToolTip(button,"图片介绍、来源与许可");AutomationProperties.SetName(button,"图片介绍");return button;
    }

    private void RenderImmersiveDetail(WallpaperItem item)
    {
        var layout=new Grid{RequestedTheme=ElementTheme.Light,Background=new SolidColorBrush(Color.FromArgb(255,232,236,239))};
        var picture=Picture(item,detailFill?Stretch.UniformToFill:Stretch.Uniform);detailImage=picture.Children.OfType<Image>().FirstOrDefault();layout.Children.Add(picture);
        var shade=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};shade.GradientStops.Add(new(){Color=Color.FromArgb(95,0,0,0),Offset=0});shade.GradientStops.Add(new(){Color=Colors.Transparent,Offset=.25});layout.Children.Add(new Border{Background=shade,IsHitTestVisible=false});
        var bar=DetailToolbar(item);if(animateViewChrome)bar.Transitions=ChromeEntrance();layout.Children.Add(bar);immersive.Children.Add(layout);
        var back=Tool("返回",Symbol.Back,()=>NavigateAsync(route),iconOnly:true);back.Width=36;back.MinHeight=34;back.Height=34;back.Margin=new Thickness(16,5,0,0);back.HorizontalAlignment=HorizontalAlignment.Left;back.VerticalAlignment=VerticalAlignment.Top;back.RequestedTheme=ElementTheme.Light;TransparentChrome(back);immersive.Children.Add(back);
    }
    private void UpdateDetailFit()
    {
        if(detailImage!=null)detailImage.Stretch=detailFill?Stretch.UniformToFill:Stretch.Uniform;
        if(detailFitMenuItem!=null)detailFitMenuItem.IsChecked=!detailFill;
    }
}
