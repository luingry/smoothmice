namespace SmoothMice.Core.Diagnostics;

/// <summary>Read-only reports decisions; Suppress also drops the pulses identified as inertia.</summary>
public enum FreeSpinDetectionMode { ReadOnly, Suppress }
public enum FreeSpinVerdict { Pass, InertiaCandidate, Abstain }

public sealed class FreeSpinDetectionDecision
{
    public FreeSpinVerdict Verdict { get; set; }
    /// <summary>The policy identified the pulse as inertia, independently of the mode.</summary>
    public bool Eligible { get; set; }
    /// <summary>The pulse was actually swallowed (Suppress mode).</summary>
    public bool ShouldSuppress { get; set; }
    public string Reason { get; set; } = "Detector unavailable; event passed.";
}
