using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class MouseHookInjectionFilterTests
{
    [Fact]
    public void Physical_input_is_processed() =>
        Assert.False(MouseHookService.ShouldIgnoreInjected(0, IntPtr.Zero));

    [Fact]
    public void Own_untagged_injection_is_ignored() =>
        Assert.True(MouseHookService.ShouldIgnoreInjected(NativeMethods.LlmhfInjected, IntPtr.Zero));

    [Fact]
    public void Other_tools_injection_is_ignored() =>
        Assert.True(MouseHookService.ShouldIgnoreInjected(NativeMethods.LlmhfInjected, new IntPtr(0x1234)));

    // What the hook actually receives: Windows truncates dwExtraInfo to 32 bits.
    [Fact]
    public void Winput_lan_tag_truncated_by_windows_is_processed() =>
        Assert.False(MouseHookService.ShouldIgnoreInjected(NativeMethods.LlmhfInjected, new IntPtr(0x4E505554)));

    [Fact]
    public void Winput_lan_remote_input_is_processed() =>
        Assert.False(MouseHookService.ShouldIgnoreInjected(
            NativeMethods.LlmhfInjected, new IntPtr(MouseHookService.WinputLanInputTag)));
}
