using System.Windows;
using System.Windows.Controls;
using Mutiblox.ViewModels;
using Mutiblox.Views.Dialogs;

namespace Mutiblox.Views.Pages;

public partial class AccountListPage : UserControl
{
    public AccountListPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountListViewModel vm)
            vm.LoadAccountsCommand.Execute(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is AccountListViewModel vm)
        {
            vm.BrowserLoginRequested += OnBrowserLoginRequested;
        }

        if (e.OldValue is AccountListViewModel oldVm)
        {
            oldVm.BrowserLoginRequested -= OnBrowserLoginRequested;
        }
    }

    private Task<string?> OnBrowserLoginRequested()
    {
        var dialog = new BrowserLoginDialog
        {
            Owner = Window.GetWindow(this)
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.CapturedCookie : null);
    }
}
