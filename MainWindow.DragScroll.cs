using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace McProfileStudio;

public partial class MainWindow
{
    private ScrollViewer? mouseDragScroll;
    private Point mouseDragOrigin;
    private double mouseDragHorizontalOrigin;
    private double mouseDragVerticalOrigin;
    private bool mouseDragScrollActive;

    private void EnableGlobalMouseDragScrolling()
    {
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(DragScroll_MouseDown), true);
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(DragScroll_MouseMove), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(DragScroll_MouseUp), true);
    }

    private void DragScroll_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source || IsInteractiveDragScrollSource(source)) return;
        var scroll = FindScrollableParent(source); if (scroll == null) return;
        mouseDragScroll = scroll; mouseDragOrigin = e.GetPosition(this); mouseDragHorizontalOrigin = scroll.HorizontalOffset; mouseDragVerticalOrigin = scroll.VerticalOffset; mouseDragScrollActive = false;
    }

    private void DragScroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (mouseDragScroll == null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this); var delta = point - mouseDragOrigin;
        if (!mouseDragScrollActive)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            mouseDragScrollActive = true; Mouse.Capture(mouseDragScroll, CaptureMode.SubTree); mouseDragScroll.Cursor = Cursors.ScrollAll;
        }
        if (mouseDragScroll.ScrollableWidth > 0) mouseDragScroll.ScrollToHorizontalOffset(Math.Clamp(mouseDragHorizontalOrigin - delta.X, 0, mouseDragScroll.ScrollableWidth));
        if (mouseDragScroll.ScrollableHeight > 0) mouseDragScroll.ScrollToVerticalOffset(Math.Clamp(mouseDragVerticalOrigin - delta.Y, 0, mouseDragScroll.ScrollableHeight));
        e.Handled = true;
    }

    private void DragScroll_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || mouseDragScroll == null) return;
        var wasActive = mouseDragScrollActive; mouseDragScroll.Cursor = null;
        if (Mouse.Captured == mouseDragScroll) Mouse.Capture(null);
        mouseDragScroll = null; mouseDragScrollActive = false;
        if (wasActive) e.Handled = true;
    }

    private ScrollViewer? FindScrollableParent(DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = DragScrollParent(current))
        {
            if (current is ScrollViewer scroll && (scroll.ScrollableWidth > 0 || scroll.ScrollableHeight > 0)) return scroll;
        }
        return null;
    }

    private bool IsInteractiveDragScrollSource(DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = DragScrollParent(current))
        {
            if (current is ScrollBar or Thumb or TextBoxBase or PasswordBox or ComboBox or ButtonBase or CheckBox) return true;
            // Resource-pack lists own their left-drag gesture for ordering. Keep walking
            // past the internal ScrollViewer so it cannot capture the gesture first.
            if (current is ListBox list && (ReferenceEquals(list, PackList) || ReferenceEquals(list, disabledPackList) || ReferenceEquals(list, enabledPackList))) return true;
        }
        return false;
    }

    private static DependencyObject? DragScrollParent(DependencyObject current) => current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
}
