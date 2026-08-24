using System.Windows;
using System.Windows.Controls;

namespace SshAgentGui;

internal sealed class PackLeftPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty IsFillProperty = DependencyProperty.RegisterAttached(
        "IsFill",
        typeof(bool),
        typeof(PackLeftPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static void SetIsFill(DependencyObject element, bool value) => element.SetValue(IsFillProperty, value);

    public static bool GetIsFill(DependencyObject element) => (bool)element.GetValue(IsFillProperty);

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        var used = 0d;
        var height = 0d;
        UIElement? fill = null;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;
            if (GetIsFill(child))
            {
                fill = child;
                continue;
            }

            child.Measure(availableSize);
            used += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        if (fill is not null)
        {
            var remain = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - used);
            fill.Measure(new System.Windows.Size(remain, availableSize.Height));
            used += fill.DesiredSize.Width;
            height = Math.Max(height, fill.DesiredSize.Height);
        }

        return new System.Windows.Size(used, height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var others = 0d;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed || GetIsFill(child))
                continue;
            others += child.DesiredSize.Width;
        }

        var x = 0d;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;
            var width = GetIsFill(child)
                ? Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - others))
                : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
