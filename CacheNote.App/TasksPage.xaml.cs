using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CacheNote.Core.ViewModels;

namespace CacheNote_App;

/// <summary>
/// Tasks section: create to-dos (title, optional due date/time, priority), see them
/// listed (open first, soonest due first), complete via the checkbox, or delete.
/// </summary>
public sealed partial class TasksPage : Page
{
    public TasksViewModel Vm { get; }

    public TasksPage()
    {
        Vm = App.GetService<TasksViewModel>();
        InitializeComponent();

        Loaded += (_, _) => { Vm.Load(); UpdateEmpty(); };
        Vm.Tasks.CollectionChanged += (_, _) => UpdateEmpty();
    }

    private void UpdateEmpty()
        => EmptyHint.Visibility = Vm.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
            return;

        DateTime? dueUtc = null;
        if (DueDate.Date is DateTimeOffset d)
        {
            var time = DueTime.SelectedTime ?? new TimeSpan(9, 0, 0);
            var localWhen = DateTime.SpecifyKind(d.Date + time, DateTimeKind.Local);
            dueUtc = localWhen.ToUniversalTime();
        }

        var priority = ((PriorityCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Medium").ToLowerInvariant();
        Vm.Create(TitleBox.Text.Trim(), dueUtc, priority);

        TitleBox.Text = "";
        DueDate.Date = null;
        DueTime.SelectedTime = null;
        PriorityCombo.SelectedIndex = 1;
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is long id)
            Vm.SetCompleted(id, cb.IsChecked == true);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is long id)
            Vm.Delete(id);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }
}
