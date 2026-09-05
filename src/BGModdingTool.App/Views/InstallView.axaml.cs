using System.Collections.Specialized;
using Avalonia.Controls;
using BGModdingTool.App.ViewModels;

namespace BGModdingTool.App.Views;

public partial class InstallView : UserControl
{
    private InstallViewModel? _attachedVm;

    public InstallView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (ReferenceEquals(_attachedVm, DataContext)) return;
            if (_attachedVm is not null) _attachedVm.LogLines.CollectionChanged -= OnLogChanged;
            _attachedVm = DataContext as InstallViewModel;
            if (_attachedVm is not null) _attachedVm.LogLines.CollectionChanged += OnLogChanged;
        };
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.ItemCount > 0)
            LogList.ScrollIntoView(LogList.ItemCount - 1);
    }
}
