using System;
using Avalonia.Controls;
using Swag.Avalonia.ViewModels;

namespace Swag.Avalonia.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is LibraryViewModel viewModel)
        {
            // Covers are loaded for what is on the list, not for what is not shown
            foreach (var item in viewModel.Recents)
                item.LoadCover();

            viewModel.Recents.CollectionChanged += (_, _) =>
            {
                foreach (var item in viewModel.Recents)
                    item.LoadCover();
            };
        }
    }
}
