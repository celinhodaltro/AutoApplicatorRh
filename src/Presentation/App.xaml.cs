namespace AutoApplicator.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "AutoApplicator" };
        window.Width = 1400;
        window.Height = 900;
        return window;
    }
}
