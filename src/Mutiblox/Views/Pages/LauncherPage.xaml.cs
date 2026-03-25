using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mutiblox.Models;
using Mutiblox.ViewModels;
using Mutiblox.Views.Dialogs;

namespace Mutiblox.Views.Pages;

public partial class LauncherPage : UserControl
{
    public LauncherPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LauncherViewModel vm)
            vm.RefreshAccountsCommand.Execute(null);
    }

    private void AccountItem_Click(object sender, MouseButtonEventArgs e)
    {
        // Only handle if the click wasn't on the CheckBox itself
        if (e.OriginalSource is CheckBox) return;

        if (sender is FrameworkElement { DataContext: RobloxAccount account } &&
            DataContext is LauncherViewModel vm)
        {
            account.IsSelected = !account.IsSelected;
            vm.ToggleAccountSelection(account);
            AccountList.Items.Refresh();
        }
    }

    private void AccountCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: RobloxAccount account } &&
            DataContext is LauncherViewModel vm)
        {
            if (account.IsSelected && !vm.SelectedAccounts.Contains(account))
            {
                vm.SelectedAccounts.Add(account);
            }
            else if (!account.IsSelected && vm.SelectedAccounts.Contains(account))
            {
                vm.SelectedAccounts.Remove(account);
            }
        }
    }

    private async void BrowseServers_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LauncherViewModel vm)
            return;

        await vm.LoadServersCommand.ExecuteAsync(null);

        if (vm.Servers.Count == 0)
            return;

        var dialog = new ServerBrowserDialog(vm.Servers.ToList())
        {
            Owner = Window.GetWindow(this)
        };

        dialog.RefreshRequested += async () =>
        {
            await vm.LoadServersCommand.ExecuteAsync(null);
            dialog.UpdateServers(vm.Servers.ToList());
        };

        if (dialog.ShowDialog() == true && dialog.SelectedServer != null)
        {
            vm.SelectServerCommand.Execute(dialog.SelectedServer);
        }
    }
}
