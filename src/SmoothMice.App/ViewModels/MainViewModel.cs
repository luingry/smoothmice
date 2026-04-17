using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Scrolling;

namespace SmoothMice.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ProfileManager _manager;
    private readonly Action _persist;

    private bool _autoStartOnLogin;
    private bool _enabled;
    private ScrollProfile? _selected;

    public static IReadOnlyList<string> AccelPresetNames { get; } =
        ["Linear", "Smooth", "Exponential"];

    public MainViewModel(ProfileManager manager, Action persist)
    {
        _manager = manager;
        _persist = persist;
        ProfileNames = new ObservableCollection<string>();

        ResetAllCommand    = new RelayCommand(_ => ResetAll());
        AddProfileCommand  = new RelayCommand(_ => AddProfile());
        RemoveProfileCommand = new RelayCommand(
            _ => RemoveProfile(), _ => SelectedProfile is { IsGlobal: false });

        ReloadFromManager();
    }

    public ObservableCollection<string> ProfileNames { get; }

    public bool AutoStartOnLogin
    {
        get => _autoStartOnLogin;
        set => Set(ref _autoStartOnLogin, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public ScrollProfile? SelectedProfile
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            RaiseAllProfileProperties();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsGlobalProfile => SelectedProfile?.IsGlobal ?? false;

    public string SelectedDisplayName
    {
        get => SelectedProfile?.DisplayName ?? "";
        set { if (SelectedProfile is not null) SelectedProfile.DisplayName = value; Raise(); }
    }

    // ── Animation ─────────────────────────────────────────────────────────

    public double StepSizePx
    {
        get => SelectedProfile?.Settings.StepSizePx ?? 0;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.StepSizePx = value; Raise(); }
    }

    public int AnimationTimeMs
    {
        get => SelectedProfile?.Settings.AnimationTimeMs ?? 0;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.AnimationTimeMs = value; Raise(); }
    }

    public double TailToHeadRatio
    {
        get => SelectedProfile?.Settings.TailToHeadRatio ?? 0;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.TailToHeadRatio = value; Raise(); }
    }

    public bool AnimationEasing
    {
        get => SelectedProfile?.Settings.AnimationEasing ?? false;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.AnimationEasing = value; Raise(); }
    }

    // ── Acceleration ──────────────────────────────────────────────────────

    public int AccelerationDeltaMs
    {
        get => SelectedProfile?.Settings.AccelerationDeltaMs ?? 400;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.AccelerationDeltaMs = value; Raise(); }
    }

    public int AccelerationCurvePreset
    {
        get => SelectedProfile?.Settings.AccelerationCurvePreset ?? 1;
        set
        {
            if (SelectedProfile is null) return;
            SelectedProfile.Settings.AccelerationCurvePreset = value;
            ApplyPreset(value);
            Raise();
        }
    }

    public double AccelerationExponent
    {
        get => SelectedProfile?.Settings.AccelerationExponent ?? 1.3;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.AccelerationExponent = value; Raise(); }
    }

    public double AccelerationMaxX
    {
        get => SelectedProfile?.Settings.AccelerationMaxX ?? 3.5;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.AccelerationMaxX = value; Raise(); }
    }

    // ── Behaviour ─────────────────────────────────────────────────────────

    public bool EnableForAllAppsByDefault
    {
        get => SelectedProfile?.Settings.EnableForAllAppsByDefault ?? false;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.EnableForAllAppsByDefault = value; Raise(); }
    }

    public bool HorizontalSmoothness
    {
        get => SelectedProfile?.Settings.HorizontalSmoothness ?? false;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.HorizontalSmoothness = value; Raise(); }
    }

    public bool ReverseWheelDirection
    {
        get => SelectedProfile?.Settings.ReverseWheelDirection ?? false;
        set { if (SelectedProfile is not null) SelectedProfile.Settings.ReverseWheelDirection = value; Raise(); }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    public ICommand ResetAllCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand RemoveProfileCommand { get; }

    // ── Lifecycle ────────────────────────────────────────────────────────

    public void ReloadFromManager()
    {
        var snap = _manager.Snapshot;
        AutoStartOnLogin = snap.AutoStartOnLogin;
        Enabled = snap.Enabled;

        ProfileNames.Clear();
        foreach (var p in snap.Profiles
                     .OrderByDescending(x => x.IsGlobal)
                     .ThenBy(x => x.DisplayName))
            ProfileNames.Add(p.DisplayName);

        var selected = snap.Profiles.FirstOrDefault(p => p.Id == snap.SelectedProfileId)
                       ?? snap.Profiles.First(p => p.IsGlobal);
        SelectedProfile = selected.Clone();
    }

    public void Save()
    {
        if (SelectedProfile is null) return;
        _manager.UpsertEditedProfile(SelectedProfile);
        _manager.UpdateShell(AutoStartOnLogin, Enabled, SelectedProfile.Id);
    }

    public void SwitchProfile(string displayName)
    {
        if (SelectedProfile is null) return;
        _manager.UpsertEditedProfile(SelectedProfile);

        var snap  = _manager.Snapshot;
        var match = snap.Profiles.FirstOrDefault(p => p.DisplayName == displayName);
        if (match is null) return;

        _manager.UpdateShell(AutoStartOnLogin, Enabled, match.Id);
        SelectedProfile = match.Clone();
        _persist();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void ApplyPreset(int preset)
    {
        if (SelectedProfile is null) return;
        var (exp, max) = ScrollMath.PresetValues(preset);
        SelectedProfile.Settings.AccelerationExponent = exp;
        SelectedProfile.Settings.AccelerationMaxX     = max;
        Raise(nameof(AccelerationExponent));
        Raise(nameof(AccelerationMaxX));
    }

    private void RaiseAllProfileProperties()
    {
        Raise(nameof(StepSizePx));
        Raise(nameof(AnimationTimeMs));
        Raise(nameof(TailToHeadRatio));
        Raise(nameof(AnimationEasing));
        Raise(nameof(AccelerationDeltaMs));
        Raise(nameof(AccelerationCurvePreset));
        Raise(nameof(AccelerationExponent));
        Raise(nameof(AccelerationMaxX));
        Raise(nameof(EnableForAllAppsByDefault));
        Raise(nameof(HorizontalSmoothness));
        Raise(nameof(ReverseWheelDirection));
        Raise(nameof(SelectedDisplayName));
        Raise(nameof(IsGlobalProfile));
    }

    private void ResetAll()
    {
        if (MessageBox.Show("Reset all settings to defaults?", "SmoothMice",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _manager.ResetAllToDefaults();
        ReloadFromManager();
        _persist();
    }

    private void AddProfile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            Title  = "Pick an application executable",
        };
        if (dlg.ShowDialog() != true) return;

        var exe  = Path.GetFileName(dlg.FileName);
        var name = Path.GetFileNameWithoutExtension(dlg.FileName);
        if (!_manager.TryAddAppProfile(exe, string.IsNullOrWhiteSpace(name) ? exe : name))
        {
            MessageBox.Show("A profile for this executable already exists.", "SmoothMice");
            return;
        }
        ReloadFromManager();
        _persist();
    }

    private void RemoveProfile()
    {
        if (SelectedProfile is null || SelectedProfile.IsGlobal) return;

        if (MessageBox.Show($"Remove profile '{SelectedProfile.DisplayName}'?", "SmoothMice",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _ = _manager.TryRemoveProfile(SelectedProfile.Id);
        ReloadFromManager();
        _persist();
    }
}
