using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;

namespace NovaLauncher.App.Views;

// Search-only viewport virtualization. Cards, templates and commands remain unchanged.
public sealed class SearchWrapPanel : VirtualizingPanel, IScrollInfo
{
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Tab || (Keyboard.Modifiers & ~ModifierKeys.Shift) != 0) return;
        var owner = ItemsControl.GetItemsOwner(this);
        if (e.OriginalSource is not Button button || button.Name != "LaunchButton") return;
        int index = owner.Items.IndexOf(button.DataContext);
        int next = index + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        if (index < 0 || next < 0 || next >= owner.Items.Count) return;
        double top = next / _columns * _cellHeight;
        if (top < VerticalOffset) SetVerticalOffset(top);
        else if (top + _cellHeight > VerticalOffset + ViewportHeight) SetVerticalOffset(top + _cellHeight - ViewportHeight);
        UpdateLayout();
        if (owner.ItemContainerGenerator.ContainerFromIndex(next) is DependencyObject container && FindLaunchButton(container) is { } target)
        {
            target.Focus();
            e.Handled = true;
        }
    }

    private static Button? FindLaunchButton(DependencyObject parent)
    {
        if (parent is Button { Name: "LaunchButton" } button) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindLaunchButton(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private int _columns = 1;
    private double _cellWidth, _cellHeight;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth { get; private set; }
    public double ExtentHeight { get; private set; }
    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }
    public double HorizontalOffset => 0;
    public double VerticalOffset { get; private set; }
    public ScrollViewer ScrollOwner { get; set; } = null!;

    protected override Size MeasureOverride(Size available)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return new Size();
        var margin = (Thickness)FindResource("ApplicationCardMargin");
        _cellWidth = (double)FindResource("ApplicationCardWidth") + margin.Left + margin.Right;
        _cellHeight = (double)FindResource("ApplicationCardHeight") + margin.Top + margin.Bottom;
        ViewportWidth = double.IsInfinity(available.Width) ? _cellWidth : available.Width;
        ViewportHeight = double.IsInfinity(available.Height) ? _cellHeight : available.Height;
        _columns = Math.Max(1, (int)(ViewportWidth / _cellWidth));
        ExtentWidth = ViewportWidth;
        ExtentHeight = Math.Ceiling(owner.Items.Count / (double)_columns) * _cellHeight;
        VerticalOffset = Math.Clamp(VerticalOffset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        ScrollOwner?.InvalidateScrollInfo();
        int first = (int)(VerticalOffset / _cellHeight) * _columns;
        int last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((VerticalOffset + ViewportHeight) / _cellHeight)) * _columns - 1);
        _ = InternalChildren; // Initializes the panel generator on first measure.
        var generator = ItemContainerGenerator;
        // Recycle outside the viewport before asking for new containers.
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index >= first && index <= last) continue;
            ((IRecyclingItemContainerGenerator)generator).Recycle(new GeneratorPosition(i, 0), 1);
            RemoveInternalChildRange(i, 1);
        }
        if (last >= first)
        {
            var position = generator.GeneratorPositionFromIndex(first);
            int childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
            {
                for (int i = first; i <= last; i++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out bool newlyRealized);
                    if (newlyRealized || !InternalChildren.Contains(child))
                    {
                        if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(_cellWidth, _cellHeight));
                }
            }
        }
        return new Size(ViewportWidth, ViewportHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect(index % _columns * _cellWidth,
                index / _columns * _cellHeight - VerticalOffset, _cellWidth, _cellHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove
            or System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        else if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
        VerticalOffset = 0;
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index) => SetVerticalOffset(index / _columns * _cellHeight);
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is not UIElement element || !IsAncestorOf(element)) return Rect.Empty;
        var bounds = element.TransformToAncestor(this).TransformBounds(rectangle);
        if (bounds.Top < 0) SetVerticalOffset(VerticalOffset + bounds.Top);
        else if (bounds.Bottom > ViewportHeight) SetVerticalOffset(VerticalOffset + bounds.Bottom - ViewportHeight);
        return rectangle;
    }
    public void SetVerticalOffset(double offset)
    {
        double next = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (next == VerticalOffset) return;
        VerticalOffset = next;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }
    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => SetVerticalOffset(VerticalOffset - _cellHeight);
    public void LineDown() => SetVerticalOffset(VerticalOffset + _cellHeight);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - _cellHeight * 3);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + _cellHeight * 3);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageLeft() { }
    public void PageRight() { }
}
