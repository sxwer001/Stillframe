using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Stillframe.App;

/// <summary>Native controls wrap at their measured width instead of clipping in narrow windows.</summary>
public sealed class WrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double x=0,y=0,line=0,max=0;
        foreach(var child in Children)
        {
            child.Measure(new Size(availableSize.Width,double.PositiveInfinity));var size=child.DesiredSize;
            if(x>0&&x+size.Width>availableSize.Width){max=Math.Max(max,x-12);y+=line+12;x=0;line=0;}
            x+=size.Width+12;line=Math.Max(line,size.Height);
        }
        return new Size(Math.Min(availableSize.Width,Math.Max(max,Math.Max(0,x-12))),y+line);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x=0,y=0,line=0;
        foreach(var child in Children)
        {
            var size=child.DesiredSize;
            if(x>0&&x+size.Width>finalSize.Width){y+=line+12;x=0;line=0;}
            child.Arrange(new Rect(x,y,Math.Min(size.Width,finalSize.Width),size.Height));x+=size.Width+12;line=Math.Max(line,size.Height);
        }
        return finalSize;
    }
}
