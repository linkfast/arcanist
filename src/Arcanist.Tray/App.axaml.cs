using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Arcanist.Tray;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var controller = new TrayController(desktop);
            TrayIcon.SetIcons(this, [controller.Icon]);
            desktop.Exit += (_, _) => controller.Dispose();
            _ = controller.StartAsync(updateService: desktop.Args?.Contains(Program.UpdateServiceSwitch) == true);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
