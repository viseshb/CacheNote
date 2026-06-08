using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CacheNote.Core.ViewModels;

namespace CacheNote_App;

/// <summary>
/// Reminders section: create time-based reminders (with optional repeat), see them
/// listed soonest-first, and complete or delete them. Firing + toasts are handled by
/// the app-level reminder scheduler.
/// </summary>
public sealed partial class RemindersPage : Page
{
    public RemindersViewModel Vm { get; }

    public RemindersPage()
    {
        Vm = App.GetService<RemindersViewModel>();
        InitializeComponent();

        // Default to ~5 minutes from now.
        var soon = DateTime.Now.AddMinutes(5);
        DatePick.Date = soon.Date;
        TimePick.SelectedTime = soon.TimeOfDay;

        Loaded += (_, _) => { Vm.Load(); UpdateEmpty(); };
        Vm.Reminders.CollectionChanged += (_, _) => UpdateEmpty();
    }

    private void UpdateEmpty()
        => EmptyHint.Visibility = Vm.Reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var date = DatePick.Date?.Date ?? DateTime.Now.Date;
        var time = TimePick.SelectedTime ?? DateTime.Now.AddMinutes(5).TimeOfDay;
        var localWhen = DateTime.SpecifyKind(date + time, DateTimeKind.Local);
        var utc = localWhen.ToUniversalTime();

        var repeat = ((RepeatCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Once").ToLowerInvariant();
        var message = string.IsNullOrWhiteSpace(MessageBox.Text) ? null : MessageBox.Text.Trim();

        Vm.Create(message, utc, repeat);
        MessageBox.Text = "";
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is long id)
            Vm.Complete(id);
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
