using System.Windows;
using CTRL.Desktop.Settings;

namespace CTRL.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopShell _shell = new();
    private bool _isDisposed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        TxtStatus.Text = _shell.IsListening ? "● Listening" : "● Stopped";
        TxtPort.Text = "Port: " + (_shell.LocalPort?.ToString() ?? "—");
        TxtClients.Text = "Connected: " + _shell.ClientCount;
        TxtPairingCode.Text = _shell.PairingCode ?? "—";
        BtnStart.IsEnabled = !_shell.IsRunning;
        BtnStop.IsEnabled = _shell.IsRunning;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        _shell.Start();
        RefreshStatus();
        Log("Server started.");
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _shell.Stop();
        RefreshStatus();
        Log("Server stopped.");
    }

    private void OnGenerateCode(object sender, RoutedEventArgs e)
    {
        _shell.GeneratePairingCode();
        RefreshStatus();
        Log("Pairing code regenerated.");
    }

    private void Log(string message)
    {
        TxtLog.Text += message + "\n";
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isDisposed)
        {
            _shell.Dispose();
            _isDisposed = true;
        }
    }
}