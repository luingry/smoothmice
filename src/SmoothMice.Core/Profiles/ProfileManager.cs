using SmoothMice.Core.Config;
using SmoothMice.Core.Updates;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Core.Profiles;

public sealed class ProfileManager
{
    private readonly object _lock = new();
    private AppSettings _settings;

    public ProfileManager(AppSettings initial)
    {
        _settings = initial.Clone();
        EnsureGlobalProfile();
    }

    public event EventHandler? SettingsChanged;

    /// <summary>Global, fail-open preference used before resolving a smoothing profile.</summary>
    public bool DoNotActivateInGames
    {
        get
        {
            lock (_lock)
                return _settings.DoNotActivateInGames;
        }
    }

    /// <summary>Global visual preference shared by every application window.</summary>
    public bool DarkMode
    {
        get
        {
            lock (_lock)
                return _settings.DarkMode;
        }
    }

    public AppSettings Snapshot
    {
        get
        {
            lock (_lock)
                return _settings.Clone();
        }
    }

    public void ReplaceAll(AppSettings next)
    {
        lock (_lock)
        {
            _settings = next.Clone();
            EnsureGlobalProfile();
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateShell(
        bool? autoStart = null,
        string? selectedProfileId = null,
        UpdateCheckFrequency? updateCheckFrequency = null)
    {
        lock (_lock)
        {
            if (autoStart is not null) _settings.AutoStartOnLogin = autoStart.Value;
            if (selectedProfileId is not null) _settings.SelectedProfileId = selectedProfileId;
            if (updateCheckFrequency is not null) _settings.UpdateCheckFrequency = updateCheckFrequency.Value;
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLastSuccessfulUpdateCheckUtc(DateTimeOffset utc)
    {
        lock (_lock)
            _settings.LastUpdateCheckUtc = utc;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFreeSpinInertiaSuppressionEnabled(bool enabled)
    {
        lock (_lock)
            _settings.FreeSpinInertiaSuppressionEnabled = enabled;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDoNotActivateInGames(bool enabled)
    {
        lock (_lock)
            _settings.DoNotActivateInGames = enabled;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDarkMode(bool enabled)
    {
        lock (_lock)
            _settings.DarkMode = enabled;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFreeSpinCalibrationTarget(FreeSpinCalibrationPhase phase, int target)
    {
        lock (_lock)
        {
            target = AppSettings.ClampCalibrationTarget(target);
            switch (phase)
            {
                case FreeSpinCalibrationPhase.Lift: _settings.FreeSpinLiftTarget = target; break;
                case FreeSpinCalibrationPhase.Landing: _settings.FreeSpinLandingTarget = target; break;
                case FreeSpinCalibrationPhase.Reposition: _settings.FreeSpinRepositionTarget = target; break;
                case FreeSpinCalibrationPhase.LegitimateScroll: _settings.FreeSpinLegitimateScrollTarget = target; break;
                default: throw new ArgumentOutOfRangeException(nameof(phase));
            }
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ProfileResolution ResolveForExecutable(string? exeName, string? parentExeName = null)
    {
        lock (_lock)
        {
            var global = _settings.Profiles.FirstOrDefault(p => p.IsGlobal)
                         ?? throw new InvalidOperationException("Global profile missing.");

            if (string.IsNullOrWhiteSpace(exeName))
                return new ProfileResolution(global.Settings.Clone(), global.Settings.Enabled);

            var specific = FindAppProfile(exeName);
            if (specific is null && !string.IsNullOrWhiteSpace(parentExeName))
                specific = FindAppProfile(parentExeName);

            if (specific is not null)
                return new ProfileResolution(specific.Settings.Clone(), specific.Settings.Enabled);

            return new ProfileResolution(
                global.Settings.Clone(),
                InterceptForSmoothing: global.Settings.Enabled);
        }
    }

    private ScrollProfile? FindAppProfile(string? exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return null;
        return _settings.Profiles.FirstOrDefault(p =>
            !p.IsGlobal &&
            p.ExecutableName is not null &&
            p.ExecutableName.Equals(exeName, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryAddAppProfile(string executableName, string displayName)
    {
        executableName = executableName.Trim();
        if (string.IsNullOrWhiteSpace(executableName))
            return false;

        lock (_lock)
        {
            if (_settings.Profiles.Any(p =>
                    p.ExecutableName is not null &&
                    p.ExecutableName.Equals(executableName, StringComparison.OrdinalIgnoreCase)))
                return false;

            var id = Guid.NewGuid().ToString("N");
            var global = _settings.Profiles.First(p => p.IsGlobal);
            var clone = global.Settings.Clone();
            _settings.Profiles.Add(new ScrollProfile
            {
                Id = id,
                DisplayName = displayName,
                ExecutableName = executableName,
                IsGlobal = false,
                Settings = clone,
            });
            _settings.SelectedProfileId = id;
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryRemoveProfile(string profileId)
    {
        lock (_lock)
        {
            var p = _settings.Profiles.FirstOrDefault(x => x.Id == profileId);
            if (p is null || p.IsGlobal)
                return false;
            _settings.Profiles.Remove(p);
            if (_settings.SelectedProfileId == profileId)
                _settings.SelectedProfileId = DefaultSettings.GlobalProfileId;
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void UpsertEditedProfile(ScrollProfile edited)
    {
        lock (_lock)
        {
            var idx = _settings.Profiles.FindIndex(p => p.Id == edited.Id);
            if (idx < 0) return;
            _settings.Profiles[idx] = edited.Clone();
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAllToDefaults()
    {
        ReplaceAll(DefaultSettings.CreateAppSettings());
    }

    private void EnsureGlobalProfile()
    {
        if (_settings.Profiles.Count == 0 ||
            _settings.Profiles.All(p => !p.IsGlobal))
        {
            _settings.Profiles.Insert(0, new ScrollProfile
            {
                Id = DefaultSettings.GlobalProfileId,
                DisplayName = DefaultSettings.GlobalProfileName,
                ExecutableName = null,
                IsGlobal = true,
                Settings = DefaultSettings.CreateGlobalProfileSettings(),
            });
        }

        if (_settings.Profiles.All(p => p.Id != _settings.SelectedProfileId))
            _settings.SelectedProfileId = DefaultSettings.GlobalProfileId;
    }
}
