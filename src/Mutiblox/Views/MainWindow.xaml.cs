using System.Windows;
using Mutiblox.ViewModels;

namespace Mutiblox.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
