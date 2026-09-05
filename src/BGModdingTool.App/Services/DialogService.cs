using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace BGModdingTool.App.Services;

/// <summary>Thin wrapper over StorageProvider pickers; MainWindow is set at startup.</summary>
public static class DialogService
{
    public static Window? MainWindow { get; set; }

    public static async Task<string?> PickFolderAsync(string title)
    {
        if (MainWindow is null) return null;
        var result = await MainWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>Small modal text prompt; returns null when cancelled.</summary>
    public static async Task<string?> PromptAsync(string title, string label, string initial = "")
    {
        if (MainWindow is null) return null;
        string? result = null;
        var box = new TextBox { Text = initial, MinWidth = 240 };
        var ok = new Button { Content = "OK", IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    box,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await dialog.ShowDialog(MainWindow);
        return result;
    }

    public static async Task<IReadOnlyList<string>> PickFilesAsync(string title, string filterName, params string[] patterns)
    {
        if (MainWindow is null) return [];
        var result = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });
        return result.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
    }
}
