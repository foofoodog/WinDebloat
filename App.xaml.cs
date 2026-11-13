using System.Windows;
using WinDebloat.Services;

namespace WinDebloat;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Initialize();
    }
}
