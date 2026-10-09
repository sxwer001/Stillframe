using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Stillframe.App.Services;
using Windows.UI;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    // Keep the native SplitView mounted, following WinUI Gallery. IsPaneOpen alone
    // drives its transition; Visibility changes would cut that animation short.
    // The Inline content is only the dismiss surface, so the sibling photo never shifts.
    private readonly SplitView settingsDrawer=new(){PanePlacement=SplitViewPanePlacement.Left,DisplayMode=SplitViewDisplayMode.Inline,IsPaneOpen=false,IsHitTestVisible=false,RequestedTheme=ElementTheme.Light};
    private readonly ScrollViewer drawerBody=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly Border drawerDismiss=new(){Background=new SolidColorBrush(Colors.Transparent)};
    private readonly TextBlock drawerTitle=new(){Text="拾景",FontSize=12,Margin=new Thickness(72,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    private int drawerRevision;
    private bool drawerShowsSettings;
    private Task drawerNavigationTask=Task.CompletedTask;
    private Expander? discoveryExpander;
    private sealed record DrawerLink(string Label,string Route,string IconGlyph,string Description)
    {
        public Visibility CardVisibility=>Route=="discover"?Visibility.Collapsed:Visibility.Visible;
        public Visibility DiscoveryVisibility=>Route=="discover"?Visibility.Visible:Visibility.Collapsed;
    }
    private readonly SemaphoreSlim appearanceWriter=new(1,1);
    private Task appearanceSaveTask=Task.CompletedTask;
    private bool drawerOpening;
    private Slider? transparencySlider;

    private void InitializeSettingsDrawer()
    {
        drawerDismiss.Tapped+=(_,e)=>{CloseSettings();e.Handled=true;};
        settingsDrawer.Content=drawerDismiss;settingsDrawer.PaneBackground=new AcrylicBrush{TintColor=Colors.White,TintOpacity=.86,FallbackColor=Color.FromArgb(255,242,244,245)};
        drawerBody.ContentTransitions=ChromeEntrance();
        var pane=new Grid();pane.RowDefinitions.Add(new(){Height=new GridLength(44)});pane.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid();header.Children.Add(drawerTitle);var toggle=Menu();toggle.Foreground=new SolidColorBrush(Colors.Black);
        foreach(var key in new[]{"ButtonForegroundPointerOver","ButtonForegroundPressed"})toggle.Resources[key]=toggle.Foreground;
        AutomationProperties.SetName(toggle,"关闭侧边栏");header.Children.Add(toggle);pane.Children.Add(header);
        Grid.SetRow(drawerBody,1);pane.Children.Add(drawerBody);settingsDrawer.Pane=pane;
        settingsDrawer.PaneClosing+=(_,_)=>appearanceSaveTask=SaveAppearanceAsync();
        settingsDrawer.PaneClosed+=(_,_)=>{if(!settingsDrawer.IsPaneOpen)settingsDrawer.IsHitTestVisible=false;};
        Grid.SetRowSpan(settingsDrawer,2);shell.Children.Add(settingsDrawer);
        shell.SizeChanged+=(_,_)=>UpdateDrawerWidth();
    }

    private void ShowDrawer()
    {
        UpdateDrawerWidth();settingsDrawer.IsHitTestVisible=true;
        drawerDismiss.Background=new SolidColorBrush(Color.FromArgb(16,0,0,0));settingsDrawer.IsPaneOpen=true;
    }

    private void ToggleNavigationDrawer()
    {
        if(operation!=null&&!homeLoading)return;
        if(settingsDrawer.IsPaneOpen){CloseSettings();return;}
        ShowNavigationDrawer();
    }

    private void ShowNavigationDrawer()
    {
        ++drawerRevision;drawerShowsSettings=false;drawerTitle.Text="拾景";
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(12,18,12,20)};
        panel.Children.Add(new TextBlock{Text="浏览",FontSize=18,Margin=new Thickness(2,0,0,4)});
        discoveryExpander=null;
        var entries=new[]{new DrawerLink("今日推荐","today","\uE80F","每天发现一张好图"),new DrawerLink("发现壁纸","discover","\uE721","展开选源 · 滚轮切图"),new DrawerLink("我的图库","library","\uE8B9","收藏与已下载原图"),new DrawerLink("图源管理","sources","\uE909","公共图源与自定义导入"),new DrawerLink("设置","settings","\uE713","外观与每日执行计划")};
        // Data items are command targets; realized UI containers are only presentation.
        // Explicit ListViewItem content can be returned as ClickedItem, so never cast it.
        var links=new ListView{IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single,ItemsSource=entries,SelectedItem=entries.First(x=>x.Route==(route=="catalog"?"library":route))};
        links.ItemTemplate=(DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid><Border Visibility="{Binding CardVisibility}" Padding="14,10" CornerRadius="10" BorderThickness="1" BorderBrush="#1E000000" Background="#AAFFFFFF"><Grid ColumnSpacing="10"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><FontIcon Glyph="{Binding IconGlyph}" FontFamily="Segoe MDL2 Assets" FontSize="16" VerticalAlignment="Center"/><StackPanel Grid.Column="1" Spacing="3"><TextBlock Text="{Binding Label}" FontSize="14"/><TextBlock Text="{Binding Description}" FontSize="11.5" Foreground="#FF666666" TextTrimming="CharacterEllipsis"/></StackPanel></Grid></Border><Expander Visibility="{Binding DiscoveryVisibility}" HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" MinHeight="64" CornerRadius="10" BorderThickness="1" BorderBrush="#1E000000" Background="#AAFFFFFF"><Expander.Header><Grid ColumnSpacing="10" VerticalAlignment="Center"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><FontIcon Glyph="{Binding IconGlyph}" FontFamily="Segoe MDL2 Assets" FontSize="16" VerticalAlignment="Center"/><StackPanel Grid.Column="1" Spacing="3" VerticalAlignment="Center"><TextBlock Text="{Binding Label}" FontSize="14"/><TextBlock Text="{Binding Description}" FontSize="11.5" Foreground="#FF666666" TextTrimming="CharacterEllipsis"/></StackPanel></Grid></Expander.Header></Expander></Grid></DataTemplate>
            """);
        links.ItemContainerStyle=(Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem"><Setter Property="Padding" Value="0"/><Setter Property="Margin" Value="0,0,0,8"/><Setter Property="MinHeight" Value="44"/><Setter Property="CornerRadius" Value="10"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Background" Value="Transparent"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/></Style>
            """);
        foreach(var state in new[]{"PointerOver","Pressed","Selected","SelectedPointerOver","SelectedPressed"})links.Resources["ListViewItemBackground"+state]=new SolidColorBrush(Colors.Transparent);
        links.Resources["ListViewItemCornerRadius"]=new CornerRadius(10);
        void PaintCard(ListViewItem container,bool hover=false)
        {
            if(container.Content is DrawerLink{Route:"discover"}&&VisualControls<Expander>(container.ContentTemplateRoot).FirstOrDefault() is Expander foldout)
            {
                foldout.Background=SidebarCardBackground();foldout.Resources["ExpanderHeaderBackground"]=hover?new SolidColorBrush(Color.FromArgb(210,255,255,255)):SidebarCardBackground();foldout.Resources["ExpanderHeaderBorderBrush"]=SidebarCardStroke();foldout.BorderBrush=SidebarCardStroke();return;
            }
            var card=VisualControls<Border>(container.ContentTemplateRoot).FirstOrDefault(x=>x.CornerRadius.TopLeft==10);if(card==null)return;
            card.Background=hover?new SolidColorBrush(Color.FromArgb(210,255,255,255)):SidebarCardBackground();
            card.BorderBrush=SidebarCardStroke();
        }
        links.ContainerContentChanging+=(_,e)=>
        {
            if(e.InRecycleQueue||e.Item is not DrawerLink link)return;
            AutomationProperties.SetName(e.ItemContainer,link.Label);
            e.RegisterUpdateCallback(1,(_,args)=>
            {
                PaintCard((ListViewItem)args.ItemContainer);
                if(link.Route=="discover"&&VisualControls<Expander>(args.ItemContainer.ContentTemplateRoot).FirstOrDefault() is Expander expander&&expander.Tag==null)
                {
                    expander.Tag="discover";discoveryExpander=expander;
                    expander.Resources["ExpanderHeaderBackground"]=SidebarCardBackground();expander.Resources["ExpanderHeaderBackgroundPointerOver"]=new SolidColorBrush(Color.FromArgb(210,255,255,255));expander.Resources["ExpanderHeaderBorderBrush"]=SidebarCardStroke();
                    expander.Content=DiscoverySourceChoices();AutomationProperties.SetName(expander,"发现壁纸图源");
                    expander.Expanding+=(_,_)=>{links.SelectedItem=link;drawerNavigationTask=NavigateAsync("discover");};
                }
            });
        };
        links.SelectionChanged+=(_,_)=>{foreach(var entry in entries)if(links.ContainerFromItem(entry) is ListViewItem container)PaintCard(container);};
        links.PointerMoved+=(_,e)=>{foreach(var entry in entries)if(links.ContainerFromItem(entry) is ListViewItem container){var point=e.GetCurrentPoint(container).Position;PaintCard(container,point.X>=0&&point.Y>=0&&point.X<=container.ActualWidth&&point.Y<=container.ActualHeight);}};
        links.PointerExited+=(_,_)=>{foreach(var entry in entries)if(links.ContainerFromItem(entry) is ListViewItem container)PaintCard(container);};
        links.ItemClick+=(_,e)=>
        {
            if(e.ClickedItem is DrawerLink link)drawerNavigationTask=ActivateDrawerLinkAsync(link.Route);
        };
        panel.Children.Add(links);panel.Children.Add(new TextBlock{Text="图片上滚轮下滑切下一张，上滑返回。\n右键图片，显示快捷操作。",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,4,2,0),Foreground=new SolidColorBrush(Colors.DimGray)});
        drawerBody.Content=panel;ShowDrawer();
    }

    private async Task ActivateDrawerLinkAsync(string target)
    {
        try
        {
            if(target=="settings"){await OpenSettingsAsync();return;}
            if(target=="discover"&&discoveryExpander!=null)
            {
                discoveryExpander.IsExpanded=!discoveryExpander.IsExpanded;
                if(discoveryExpander.IsExpanded)await drawerNavigationTask;
                return;
            }
            CloseSettings();await NavigateAsync(target);
        }
        catch(Exception ex){ReportNavigationFailure(ex);}
    }

    private void UpdateDrawerWidth()
    {
        if(shell.ActualWidth>0)settingsDrawer.OpenPaneLength=Math.Min(shell.ActualWidth,drawerShowsSettings?Math.Clamp(shell.ActualWidth*.30,300,360):240);
    }

    private async Task LoadAppearanceAsync()
    {
        var transparency=await library.Repository.GetSettingAsync("homeTransparency");
        if(double.TryParse(transparency,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))homeTransparency=Math.Clamp(value,45,80);
        detailFill=await library.Repository.GetSettingAsync("detailFill")!="false";
    }

    private async Task SaveAppearanceAsync()
    {
        await appearanceWriter.WaitAsync();
        try
        {
            await library.Repository.SetSettingAsync("homeTransparency",homeTransparency.ToString(CultureInfo.InvariantCulture));
            await library.Repository.SetSettingAsync("detailFill",detailFill?"true":"false");
        }
        catch(Exception ex){Notify("外观设置未能保存（"+ex.GetType().Name+"），请检查数据目录权限。",true);}
        finally{appearanceWriter.Release();}
    }

    private async Task OpenSettingsAsync()
    {
        if(operation!=null&&!homeLoading)return;
        var revision=++drawerRevision;
        drawerOpening=true;
        try
        {
            drawerShowsSettings=true;drawerTitle.Text="设置";ShowDrawer();
            var panel=await BuildSettingsPanelAsync();
            if(revision==drawerRevision&&settingsDrawer.IsPaneOpen)drawerBody.Content=panel;
        }
        catch(Exception ex){ReportNavigationFailure(ex);}
        finally{drawerOpening=false;}
    }

    private void CloseSettings()
    {
        ++drawerRevision;
        if(!settingsDrawer.IsPaneOpen)return;
        drawerDismiss.Background=new SolidColorBrush(Colors.Transparent);
        settingsDrawer.IsPaneOpen=false;
    }

    private static SolidColorBrush SidebarCardBackground()=>new(Color.FromArgb(170,255,255,255));
    private static SolidColorBrush SidebarCardStroke()=>new(Color.FromArgb(30,0,0,0));
    private static Expander DrawerCardExpander(string title,string description,UIElement content)
    {
        var heading=new StackPanel{Spacing=4,Margin=new Thickness(0,10,0,10),Children={Text(title,14)}};
        if(description.Length>0){var subtitle=Text(description,11.5);subtitle.Foreground=new SolidColorBrush(Colors.DimGray);subtitle.MaxLines=2;subtitle.TextTrimming=TextTrimming.CharacterEllipsis;ToolTipService.SetToolTip(subtitle,description);heading.Children.Add(subtitle);}
        var card=new Expander{Header=heading,Content=content,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=SidebarCardStroke(),Background=SidebarCardBackground()};
        AutomationProperties.SetName(card,title);
        card.Resources["ExpanderHeaderBackground"]=SidebarCardBackground();card.Resources["ExpanderHeaderBackgroundPointerOver"]=new SolidColorBrush(Color.FromArgb(210,255,255,255));
        card.Resources["ExpanderHeaderBorderBrush"]=SidebarCardStroke();
        return card;
    }
    private static FrameworkElement SettingCard(string title,string description,UIElement content)
    {
        // One card surface: foldout controls become the card instead of nesting a second frame.
        if(content is Expander foldout)
        {
            var foldoutBody=(UIElement)foldout.Content;foldout.Content=null;
            return DrawerCardExpander(title,description,foldoutBody);
        }
        var subtitle=Text(description,11.5);subtitle.Foreground=new SolidColorBrush(Colors.DimGray);
        UIElement body;
        if(content is ComboBox choice)
        {
            subtitle.MaxLines=2;subtitle.TextTrimming=TextTrimming.CharacterEllipsis;ToolTipService.SetToolTip(subtitle,description);
            var row=new Grid{ColumnSpacing=12};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.Children.Add(new StackPanel{Spacing=4,VerticalAlignment=VerticalAlignment.Center,Children={Text(title,14),subtitle}});
            choice.Width=100;choice.FontSize=13;choice.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(choice,1);row.Children.Add(choice);body=row;
        }
        else body=new StackPanel{Spacing=10,Children={Text(title,14),subtitle,content}};
        return new Border{MinHeight=68,Padding=new Thickness(14),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=SidebarCardStroke(),Background=SidebarCardBackground(),Child=body};
    }

    private async Task<StackPanel> BuildSettingsPanelAsync()
    {
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(12,18,12,24)};
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        header.Children.Add(Text("设置",26));var back=Tool("返回导航",Symbol.Back,()=>{ShowNavigationDrawer();return Task.CompletedTask;},iconOnly:true);Grid.SetColumn(back,1);header.Children.Add(back);panel.Children.Add(header);
        panel.Children.Add(Text("外观、图库与桌面，按你的习惯调整。",13));
        AddAutomationCards(panel);

        var theme=new ComboBox{FontSize=13,ItemsSource=new[]{"跟随系统","浅色","深色"},SelectedIndex=shell.RequestedTheme switch{ElementTheme.Light=>1,ElementTheme.Dark=>2,_=>0},HorizontalAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetName(theme,"应用主题");
        theme.SelectionChanged+=async (_,_)=>
        {
            shell.RequestedTheme=theme.SelectedIndex switch{1=>ElementTheme.Light,2=>ElementTheme.Dark,_=>ElementTheme.Default};
            try{await library.Repository.SetSettingAsync("theme",theme.SelectedIndex switch{1=>"light",2=>"dark",_=>"system"});}
            catch(Exception ex){Notify("主题未能保存（"+ex.GetType().Name+"）。",true);}
        };
        panel.Children.Add(SettingCard("应用主题","用于图库与管理页面；图片预览保留清晰的浮层控件。",theme));

        var transparencyLabel=Text($"透明度 {homeTransparency:0}%",12);
        transparencySlider=new Slider{Minimum=45,Maximum=80,StepFrequency=5,Value=homeTransparency};AutomationProperties.SetName(transparencySlider,"首页信息卡透明度");
        transparencySlider.ValueChanged+=(_,e)=>{homeTransparency=e.NewValue;transparencyLabel.Text=$"透明度 {homeTransparency:0}%";if(homeCard!=null)homeCard.Background=HomeGlass();};
        panel.Children.Add(SettingCard("首页信息卡","小尺寸信息卡，透明度越高，背景越明显。",new StackPanel{Spacing=4,Children={transparencyLabel,transparencySlider}}));

        var preview=new ToggleSwitch{Header="铺满详情视窗",IsOn=detailFill};preview.Toggled+=(_,_)=>{detailFill=preview.IsOn;UpdateDetailFit();};
        panel.Children.Add(SettingCard("图片预览","铺满会裁切边缘；关闭后完整显示，也可在图片底部随时切换。",preview));

        var imports=new StackPanel{Spacing=10,Children={Text($"已收录 {items.Count} 张图片",12),Row(Action("导入图片",async ()=>{await ImportFilesAsync();},true),Action("导入文件夹",ImportFolderAsync)),Action("打开数据目录",()=>{Process.Start(new ProcessStartInfo{FileName="explorer.exe",ArgumentList={library.Root},UseShellExecute=false});return Task.CompletedTask;})}};
        panel.Children.Add(SettingCard("本地图库","原图导入后可离线使用，保留你的源文件。",new Expander{Header="导入与存储",HorizontalAlignment=HorizontalAlignment.Stretch,Content=imports}));

        var restore=Action("恢复本次更换前的桌面",()=>RunAsync(async _=>{if(snapshot==null)return;var failed=await desktop.RestoreAsync(snapshot);Notify(failed==0?"已尝试恢复原桌面。":"部分屏幕无法恢复，请检查旧文件或屏幕连接。",failed>0);}));restore.IsEnabled=snapshot!=null;
        var desktopControls=new StackPanel{Spacing=10,Children={TargetOptions(),Action("重新检测显示器",()=>RunAsync(async _=>{await LoadMonitorsAsync();drawerBody.Content=await BuildSettingsPanelAsync();Notify("显示器列表已更新。");})),restore}};
        panel.Children.Add(SettingCard("桌面壁纸","选择显示器与适配方式；恢复保留本次会话的更换前状态。",new Expander{Header="显示器与恢复",HorizontalAlignment=HorizontalAlignment.Stretch,Content=desktopControls}));

        var sources=await library.Repository.GetSourcesAsync();
        panel.Children.Add(SettingCard("图源管理",$"已有 {sources.Count} 个图源 · 本地图片、HTTPS直链与JSON清单。",Action("管理图源",async ()=>{CloseSettings();await NavigateAsync("sources");})));
        panel.Children.Add(Action("保存外观设置",async ()=>{await SaveAppearanceAsync();if(!(notice.IsOpen&&notice.Severity==InfoBarSeverity.Error))Notify("外观设置已保存。");},true));
        panel.Children.Add(Text("关闭面板时自动保存外观。系统关闭透明效果时，信息卡使用清晰的浅色背景。",12));
        return panel;
    }
}
