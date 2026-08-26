using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SshAgentGui;

internal static class CopyFeedback
{
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(1200);
    private static readonly ConditionalWeakTable<FrameworkElement, DispatcherTimer> Timers = new();

    public static bool TryCopy(string? text, FrameworkElement? mark)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            return false;
        }

        if (mark is not null)
            Flash(mark);
        return true;
    }

    public static FrameworkElement? FindMark(FrameworkElement clicked)
    {
        if (clicked.Parent is not System.Windows.Controls.Panel panel)
            return null;
        foreach (System.Windows.UIElement child in panel.Children)
        {
            if (child is FrameworkElement mark
                && !ReferenceEquals(mark, clicked)
                && Equals(mark.Tag, "CopyMark"))
                return mark;
        }

        return null;
    }

    public static void Flash(FrameworkElement mark)
    {
        Hide(mark);
        mark.Visibility = Visibility.Visible;
        var timer = new DispatcherTimer { Interval = FlashDuration };
        timer.Tick += (_, _) => Hide(mark);
        mark.Unloaded += OnUnloaded;
        Timers.Add(mark, timer);
        timer.Start();
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement mark)
            Hide(mark);
    }

    private static void Hide(FrameworkElement mark)
    {
        mark.Unloaded -= OnUnloaded;
        if (Timers.TryGetValue(mark, out var timer))
        {
            timer.Stop();
            Timers.Remove(mark);
        }

        mark.Visibility = Visibility.Collapsed;
    }
}
