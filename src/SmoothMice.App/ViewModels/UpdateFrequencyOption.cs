using SmoothMice.Core.Updates;

namespace SmoothMice.App.ViewModels;

public readonly record struct UpdateFrequencyOption(UpdateCheckFrequency Frequency, string Label);
