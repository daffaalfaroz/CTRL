using System.Windows;
using CTRL.Desktop;

namespace CTRL.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 1 && e.Args[0] == "--integration")
        {
            var port = e.Args.Length >= 2 && int.TryParse(e.Args[1], out var p) ? p : 0;
            Program.RunIntegrationServerAsync(port).Wait();
            Current.Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}