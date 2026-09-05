using Avalonia.Controls;
using Avalonia.Layout;
using BGModdingTool.App.ViewModels;

namespace BGModdingTool.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosingAsync;
    }

    /// <summary>Blocks closing while a build has unsaved edits: Save / Discard / Cancel.</summary>
    private async void OnClosingAsync(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel main || !main.Builds.IsDirty) return;
        e.Cancel = true;

        var buildName = main.Builds.Selected?.Name ?? "(build)";
        var choice = await ShowUnsavedDialogAsync(buildName);
        switch (choice)
        {
            case "save":
                main.Builds.SaveIfDirty();
                _closeConfirmed = true;
                Close();
                break;
            case "discard":
                _closeConfirmed = true;
                Close();
                break;
            // "cancel": stay open
        }
    }

    private async Task<string> ShowUnsavedDialogAsync(string buildName)
    {
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 460, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var result = "cancel";
        Button Make(string text, string value, bool accent = false)
        {
            var b = new Button { Content = text, MinWidth = 110 };
            if (accent) b.Classes.Add("accent");
            b.Click += (_, _) => { result = value; dialog.Close(); };
            return b;
        }
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"The build \"{buildName}\" has unsaved changes.\nSave them before closing?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { Make("💾 Save and close", "save", accent: true), Make("Discard and close", "discard"), Make("Cancel", "cancel") },
                },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }
}
