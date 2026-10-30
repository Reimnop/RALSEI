using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Swag.Avalonia.ViewModels;

namespace Swag.Avalonia.Views;

public partial class LogsView : UserControl
{
    private LogsViewModel? viewModel;

    public LogsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (viewModel is not null)
            viewModel.EntryAdded -= OnEntryAdded;

        viewModel = DataContext as LogsViewModel;

        if (viewModel is not null)
            viewModel.EntryAdded += OnEntryAdded;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Shown at the end, which is the newest
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnEntryAdded(object? sender, EventArgs e)
    {
        // Follows the end of the log, unless somebody scrolled up to read something
        var nearEnd = LogScroll.Offset.Y + LogScroll.Viewport.Height >= LogScroll.Extent.Height - 48;
        if (nearEnd)
            Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }
}
