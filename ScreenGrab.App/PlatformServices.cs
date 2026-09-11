using ScreenGrab.Core;

namespace ScreenGrab.App;

/// <summary>
/// Resolves the per-OS implementations of the Core platform interfaces. The concrete assembly
/// (<c>ScreenGrab.Windows</c> or <c>ScreenGrab.Linux</c>) is linked per target framework via the
/// conditional ProjectReferences in the .csproj, so a compile-time <c>WINDOWS</c> switch picks the
/// matching implementation for whichever build is running.
/// </summary>
internal static class PlatformServices
{
	public static IScreenCapture CreateScreenCapture()
	{
#if WINDOWS
		return new ScreenGrab.Windows.GdiScreenCapture();
#else
		return new ScreenGrab.Linux.SpectacleScreenCapture();
#endif
	}

	public static IClipboardService CreateClipboardService()
	{
#if WINDOWS
		return new ScreenGrab.Windows.WindowsClipboardService();
#else
		return new ScreenGrab.Linux.WlCopyClipboardService();
#endif
	}

	public static IStartupManager CreateStartupManager()
	{
#if WINDOWS
		return new ScreenGrab.Windows.WindowsStartupManager();
#else
		return new ScreenGrab.Linux.LinuxStartupManager();
#endif
	}

	public static IGlobalHotkey CreateGlobalHotkey()
	{
#if WINDOWS
		return new ScreenGrab.Windows.WindowsGlobalHotkey();
#else
		return new ScreenGrab.Linux.LinuxGlobalHotkey();
#endif
	}

	public static ISingleInstanceIpc CreateSingleInstanceIpc()
	{
#if WINDOWS
		return new ScreenGrab.Windows.WindowsSingleInstanceIpc();
#else
		return new ScreenGrab.Linux.LinuxSingleInstanceIpc();
#endif
	}
}
