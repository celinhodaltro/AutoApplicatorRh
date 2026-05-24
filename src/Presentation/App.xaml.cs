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

        try
        {
            var displayInfo = DeviceDisplay.MainDisplayInfo;
            var density = displayInfo.Density;
            window.Width = displayInfo.Width / density;
            window.Height = displayInfo.Height / density;
            window.X = 0;
            window.Y = 0;
        }
        catch
        {
            window.Width = 1400;
            window.Height = 900;
        }

        return window;
    }
}
