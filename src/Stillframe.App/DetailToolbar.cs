using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stillframe.Core;
using Windows.UI;

namespace Stillframe.App;

public sealed partial class MainWindow
{
    private Button BareTool(string label,Symbol icon,Func<Task> handler)
    {
        var button=Tool(label,icon,handler,iconOnly:true);button.Width=36;button.Height=36;TransparentChrome(button);return button;
    }

    private Border DetailToolbar(WallpaperItem item)
    {
        var grid=new Grid{ColumnSpacing=16};
        foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})grid.ColumnDefinitions.Add(new(){Width=width});
        var badge=new Border{CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(35,255,255,255)),Padding=new Thickness(10,6,10,6),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=daily?.Id==item.Id?"今日":"图片",FontSize=13,Foreground=new SolidColorBrush(Colors.White)}};grid.Children.Add(badge);
        var title=new TextBlock{Text=item.Title,FontSize=15,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(Colors.White),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};ToolTipService.SetToolTip(title,item.Title);Grid.SetColumn(title,1);grid.Children.Add(title);
        var copyright=Attribution(item);copyright.Content=new TextBlock{Text="©",FontSize=18,Foreground=new SolidColorBrush(Colors.White)};copyright.Width=36;copyright.Height=36;TransparentChrome(copyright);Grid.SetColumn(copyright,2);grid.Children.Add(copyright);

        var favorite=BareTool(item.Favorite?"取消收藏":"收藏",Symbol.Favorite,()=>FavoriteAsync(item));favorite.Content=new TextBlock{Text=item.Favorite?"♥":"♡",FontSize=22,Foreground=new SolidColorBrush(Colors.White)};
        var fullScreenButton=BareTool("切换全屏",Symbol.FullScreen,()=>{fullScreen=!fullScreen;AppWindow.SetPresenter(fullScreen?Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen:Microsoft.UI.Windowing.AppWindowPresenterKind.Default);return Task.CompletedTask;});
        var apply=BareTool("设为壁纸",Symbol.Setting,()=>ApplyAsync(item));apply.Content=new FontIcon{Glyph="\uE7F4",FontSize=16,FontFamily=new FontFamily("Segoe MDL2 Assets")};
        var save=BareTool("保存原图",Symbol.Download,()=>ExportAsync(item));
        var more=BareTool("更多操作",Symbol.List,()=>Task.CompletedTask);more.Content=new FontIcon{Glyph="\uE712",FontSize=16,FontFamily=new FontFamily("Segoe MDL2 Assets")};
        var menu=new MenuFlyout();var targets=TargetOptions();detailTargets=targets;
        var lockScreen=new MenuFlyoutItem{Text="设为锁屏壁纸",Icon=Glyph(Symbol.ProtectedDocument),IsEnabled=Services.PersonalizationService.LockScreenSupported};lockScreen.Click+=async (_,_)=>await ApplyLockScreenAsync(item);menu.Items.Add(lockScreen);
        var display=new MenuFlyoutItem{Text="显示器与适配",Icon=Glyph(Symbol.Switch)};display.Click+=(_,_)=>targets.Flyout!.ShowAt(more);menu.Items.Add(display);
        var complete=new ToggleMenuFlyoutItem{Text="完整显示图片",IsChecked=!detailFill};detailFitMenuItem=complete;complete.Click+=async (_,_)=>{detailFill=!complete.IsChecked;UpdateDetailFit();await SaveAppearanceAsync();};menu.Items.Add(complete);
        var source=new MenuFlyoutItem{Text="图片介绍与来源",Icon=Glyph(Symbol.Caption)};source.Click+=(_,_)=>copyright.Flyout!.ShowAt(more);menu.Items.Add(source);
        var settings=new MenuFlyoutItem{Text="设置",Icon=Glyph(Symbol.Setting)};settings.Click+=async (_,_)=>await NavigateAsync("settings");menu.Items.Add(settings);more.Flyout=menu;
        var commands=new StackPanel{Orientation=Orientation.Horizontal,Spacing=18,VerticalAlignment=VerticalAlignment.Center,Children={favorite,fullScreenButton,apply,save,more}};Grid.SetColumn(commands,3);grid.Children.Add(commands);
        grid.SizeChanged+=(_,e)=>{var small=e.NewSize.Width<650;badge.Visibility=small?Visibility.Collapsed:Visibility.Visible;grid.ColumnSpacing=small?8:16;commands.Spacing=small?10:18;};
        return new Border{Child=grid,VerticalAlignment=VerticalAlignment.Bottom,Padding=new Thickness(16,8,16,8),MinHeight=54,Background=new AcrylicBrush{TintColor=Color.FromArgb(255,50,70,87),TintOpacity=.36,TintLuminosityOpacity=.25,FallbackColor=Color.FromArgb(255,50,70,87)},BorderThickness=new Thickness(0,1,0,0),BorderBrush=new SolidColorBrush(Color.FromArgb(30,255,255,255))};
    }
}
