using Microsoft.Win32;

namespace ScreenGrab;

/// <summary>
/// Manages the per-user "run at startup" registry entry
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// </summary>
static class StartupManager
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "ScreenGrab";

	/// <summary>Returns true if a startup entry for this app exists.</summary>
	public static bool IsEnabled()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
			return key?.GetValue(ValueName) is string;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Adds or removes the startup entry. Returns true on success; shows a message box on failure.
	/// </summary>
	public static bool SetEnabled(bool enabled)
	{
		try
		{
			using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
			if (enabled)
			{
				key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
			}
			else
			{
				key.DeleteValue(ValueName, throwOnMissingValue: false);
			}
			return true;
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Could not update the startup setting:\n{ex.Message}",
				"ScreenGrab",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
			return false;
		}
	}
}
