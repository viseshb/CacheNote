using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CacheNote.Core.ViewModels;

namespace CacheNote_App;

/// <summary>
/// Favorites section: notes that are starred or pinned. Click a row to open it in the
/// Notes editor.
/// </summary>
public sealed partial class FavoritesPage : Page
{
    public FavoritesViewModel Vm { get; }

    public FavoritesPage()
    {
        Vm = App.GetService<FavoritesViewModel>();
        InitializeComponent();

        Loaded += (_, _) => { Vm.Load(); UpdateEmpty(); };
        Vm.Items.CollectionChanged += (_, _) => UpdateEmpty();
    }

    private void UpdateEmpty()
        => EmptyHint.Visibility = Vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is long id)
            Frame.Navigate(typeof(MainPage), id);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
