using System;
using System.IO;
using ScreenGrab.Core;

namespace ScreenGrab.Linux;

/// <summary>
/// Linux/KDE <see cref="IStartupManager"/>: writes or removes a freedesktop autostart entry at
/// <c>~/.config/autostart/screengrab.desktop</c> with <c>Exec=screengrab</c> (no <c>--capture</c>),
/// mirroring the Windows registry toggle. KDE Plasma reads this directory at login.
/// </summary>
public sealed class LinuxStartupManager : IStartupManager
{
	private static string DesktopFilePath
	{
		get
		{
			string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
				? xdg
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
			return Path.Combine(configHome, "autostart", "screengrab.desktop");
		}
	}

	public bool IsEnabled() => File.Exists(DesktopFilePath);

	public void SetEnabled(bool enabled)
	{
		string path = DesktopFilePath;
		if (enabled)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			// Exec resolves the installed `screengrab` binary on PATH; fall back to the current
			// executable path if it is known (e.g. running from a publish dir).
			string exec = Environment.ProcessPath is { Length: > 0 } p && !p.EndsWith("dotnet", StringComparison.Ordinal)
				? p
				: "screengrab";

			File.WriteAllText(path,
				"[Desktop Entry]\n" +
				"Type=Application\n" +
				"Name=ScreenGrab\n" +
				$"Exec={exec}\n" +
				"Terminal=false\n" +
				"X-GNOME-Autostart-enabled=true\n");
		}
		else if (File.Exists(path))
		{
			File.Delete(path);
		}
	}
}
