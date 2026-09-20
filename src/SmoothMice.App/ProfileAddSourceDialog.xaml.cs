using System.Windows;

namespace SmoothMice.App;

public enum ProfileAddSource
{
    ExecutablePath,
    RunningWindow,
}

/// <summary>First step of adding a profile; it deliberately does not mutate settings.</summary>
public partial class ProfileAddSourceDialog : Window
{
    public ProfileAddSourceDialog()
    {
        InitializeComponent();
    }

    public ProfileAddSource? SelectedSource { get; private set; }

    private void SelectExecutable_OnClick(object sender, RoutedEventArgs e) => Select(ProfileAddSource.ExecutablePath);
    private void SelectRunningWindow_OnClick(object sender, RoutedEventArgs e) => Select(ProfileAddSource.RunningWindow);

    private void Select(ProfileAddSource source)
    {
        SelectedSource = source;
        DialogResult = true;
    }
}
