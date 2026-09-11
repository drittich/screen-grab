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
}
