using System.Windows;
using LiquidGlass.ViewModels;

namespace LiquidGlass.Views;

/// <summary>Janela de personalização. Todas as mudanças são aplicadas na dock em tempo real.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }
}
