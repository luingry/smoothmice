using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.App;

/// <summary>Read-only picker for visible, executable-backed top-level windows.</summary>
public partial class RunningWindowPickerDialog : Window
{
    public RunningWindowPickerDialog(IReadOnlyList<RunningApplicationWindow> windows)
    {
        InitializeComponent();
        DataContext = windows;
        EmptyState.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WindowsList.Visibility = windows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (windows.Count > 0)
            WindowsList.SelectedIndex = 0;
    }

    public RunningApplicationWindow? SelectedWindow { get; private set; }

    private void WindowsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedWindow = WindowsList.SelectedItem as RunningApplicationWindow;
        AddButton.IsEnabled = SelectedWindow is not null;
        SelectionHint.Text = SelectedWindow is null
            ? "Select a window to continue."
            : $"Executable: {SelectedWindow.ExecutableName}";
    }

    private void AddProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedWindow is null)
            return;

        DialogResult = true;
    }
}
