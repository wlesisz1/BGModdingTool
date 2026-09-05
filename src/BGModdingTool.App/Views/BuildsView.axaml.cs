using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BGModdingTool.App.ViewModels;

namespace BGModdingTool.App.Views;

public partial class BuildsView : UserControl
{
    // Drag-to-reorder for the build entry list: press on a row, drag, release
    // on the row whose position it should take. Plain pointer events (no OS
    // drag-and-drop) so it works the same inside a filtered list.
    private BuildEntryViewModel? _dragged;
    private Point _pressPoint;
    private bool _dragging;

    public BuildsView()
    {
        InitializeComponent();
        var list = this.FindControl<ListBox>("EntriesList");
        if (list is null) return;
        list.AddHandler(PointerPressedEvent, OnEntriesPointerPressed, RoutingStrategies.Tunnel);
        list.AddHandler(PointerMovedEvent, OnEntriesPointerMoved, RoutingStrategies.Tunnel);
        list.AddHandler(PointerReleasedEvent, OnEntriesPointerReleased, RoutingStrategies.Tunnel);
        list.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(list), RoutingStrategies.Tunnel);
    }

    private ListBoxItem? _dropItem;
    private ListBoxItem? _sourceItem;

    private static ListBoxItem? ItemAt(ListBox list, Point point)
    {
        foreach (var visual in list.GetVisualsAt(point))
        {
            var item = visual.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (item?.DataContext is BuildEntryViewModel) return item;
        }
        return null;
    }

    private static BuildEntryViewModel? EntryAt(ListBox list, Point point)
        => ItemAt(list, point)?.DataContext as BuildEntryViewModel;

    /// <summary>Shows where the dragged row will land: a line above (moving up) or below (moving down) the hovered row.</summary>
    private void UpdateDropIndicator(ListBox list, Point pos)
    {
        var item = ItemAt(list, pos);
        if (!ReferenceEquals(item, _dropItem))
        {
            _dropItem?.Classes.Remove("drop-above");
            _dropItem?.Classes.Remove("drop-below");
            _dropItem = null;
        }
        if (item is null || _dragged is null || item.DataContext is not BuildEntryViewModel target
            || ReferenceEquals(target, _dragged)) return;

        _dropItem = item;
        var above = target.Position < _dragged.Position;
        item.Classes.Set("drop-above", above);
        item.Classes.Set("drop-below", !above);
    }

    private void OnEntriesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var list = (ListBox)sender!;
        var point = e.GetCurrentPoint(list);
        var entry = EntryAt(list, point.Position);
        if (entry is null) return;

        if (point.Properties.IsRightButtonPressed)
        {
            // Right-click acts on the row under the cursor, like Explorer.
            list.SelectedItem = entry;
            return;
        }
        if (point.Properties.IsLeftButtonPressed)
        {
            _dragged = entry;
            _pressPoint = point.Position;
            _dragging = false;
        }
    }

    private void OnEntriesPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null) return;
        var list = (ListBox)sender!;
        var pos = e.GetPosition(list);
        if (!_dragging)
        {
            var delta = pos - _pressPoint;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
            _dragging = true;
            list.Cursor = new Cursor(StandardCursorType.DragMove);
            _sourceItem = ItemAt(list, _pressPoint);
            _sourceItem?.Classes.Add("drag-source");
        }
        UpdateDropIndicator(list, pos);
        // Keep the list scrolling when dragging near its edges.
        var scroller = list.FindDescendantOfType<ScrollViewer>();
        if (scroller is null) return;
        if (pos.Y < 24) scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y - 12));
        else if (pos.Y > list.Bounds.Height - 24) scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + 12);
    }

    private void OnEntriesPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var list = (ListBox)sender!;
        if (_dragging && _dragged is not null && DataContext is BuildsViewModel vm)
        {
            var target = EntryAt(list, e.GetPosition(list));
            if (target is not null && !ReferenceEquals(target, _dragged))
                vm.MoveEntryTo(_dragged, target.Position);
            e.Handled = true; // don't let the release re-select the row under the cursor
        }
        EndDrag(list);
    }

    private void EndDrag(ListBox list)
    {
        _dropItem?.Classes.Remove("drop-above");
        _dropItem?.Classes.Remove("drop-below");
        _sourceItem?.Classes.Remove("drag-source");
        _dropItem = null;
        _sourceItem = null;
        _dragged = null;
        _dragging = false;
        list.Cursor = Cursor.Default;
    }
}
