using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Arcanist.Tray;

/// <summary>Base for the app's few dialogs: small, centered, with the Arcanist icon.</summary>
internal abstract class DialogWindow : Window
{
    protected DialogWindow(string title)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Arcanist.Tray/Assets/arcanist.ico")));
    }

    protected static TextBlock Paragraph(string text, IBrush? foreground = null) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = foreground };

    protected static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.AddRange(buttons);
        return panel;
    }

    protected static Button Button(string text, bool primary = false)
    {
        var button = new Button { Content = text, IsDefault = primary, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary)
            button.Classes.Add("accent");
        return button;
    }
}

/// <summary>Explains the service mode and collects the music folders the service account may read.</summary>
internal sealed class ServiceWindow : DialogWindow
{
    private readonly ObservableCollection<string> _folders = [];
    private readonly TaskCompletionSource<IReadOnlyList<string>?> _result = new();

    public ServiceWindow() : base(Strings.Current.ServiceTitle)
    {
        var text = Strings.Current;
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(music))
            _folders.Add(music);

        var list = new ListBox { ItemsSource = _folders, Height = 140 };
        var add = Button(text.AddFolder);
        add.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = true });
            foreach (var path in picked.Select(folder => folder.TryGetLocalPath()).OfType<string>())
            {
                if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _folders.Add(path);
            }
        };
        var remove = Button(text.Remove);
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is string folder)
                _folders.Remove(folder);
        };

        var install = Button(text.Install, primary: true);
        install.Click += (_, _) =>
        {
            _result.TrySetResult(_folders.ToList());
            Close();
        };
        var cancel = Button(text.Cancel);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        Closed += (_, _) => _result.TrySetResult(null);

        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        folderButtons.Children.AddRange([add, remove]);

        var content = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        content.Children.AddRange(
        [
            Paragraph(text.ServiceIntro),
            Paragraph(text.ServiceAccount),
            Paragraph(text.ServiceNetworkWarning, Brushes.DarkOrange),
            new TextBlock { Text = text.ServiceFolders, FontWeight = FontWeight.SemiBold },
            list,
            folderButtons,
            Buttons(cancel, install),
        ]);
        Content = content;
    }

    /// <summary>The folders to grant access to, or null if the user cancelled.</summary>
    public Task<IReadOnlyList<string>?> AskAsync()
    {
        Show();
        Activate();
        return _result.Task;
    }
}

/// <summary>A message with one or two buttons.</summary>
internal sealed class MessageWindow : DialogWindow
{
    private readonly TaskCompletionSource<bool> _result = new();

    private MessageWindow(string title, string message, string confirm, string? cancel) : base(title)
    {
        var ok = Button(confirm, primary: true);
        ok.Click += (_, _) =>
        {
            _result.TrySetResult(true);
            Close();
        };
        Closed += (_, _) => _result.TrySetResult(false);

        Button[] buttons = [ok];
        if (cancel is not null)
        {
            var no = Button(cancel);
            no.IsCancel = true;
            no.Click += (_, _) => Close();
            buttons = [no, ok];
        }

        var content = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        content.Children.AddRange([Paragraph(message), Buttons(buttons)]);
        Content = content;
    }

    /// <summary>True if the user chose <paramref name="confirm"/>.</summary>
    public static Task<bool> ShowAsync(string title, string message, string confirm, string? cancel = null)
    {
        var window = new MessageWindow(title, message, confirm, cancel);
        window.Show();
        window.Activate();
        return window._result.Task;
    }
}
