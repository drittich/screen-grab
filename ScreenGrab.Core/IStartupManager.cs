namespace ScreenGrab.Core;

/// <summary>
/// Toggles whether ScreenGrab launches at login. Implemented per-OS: an HKCU <c>...\Run</c> registry
/// value on Windows (the port of <c>StartupManager.cs</c>), a <c>~/.config/autostart/screengrab.desktop</c>
/// entry on Linux/KDE. Drives the tray menu's "Run at startup" checkbox.
/// </summary>
public interface IStartupManager
{
	/// <summary>Returns true if a startup entry for this app currently exists.</summary>
	bool IsEnabled();

	/// <summary>Adds (<paramref name="enabled"/> = true) or removes the startup entry.
	/// Throws on failure so the caller can surface the error and keep the checkbox honest.</summary>
	void SetEnabled(bool enabled);
}
