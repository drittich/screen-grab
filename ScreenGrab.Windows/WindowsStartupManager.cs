using System;
using Microsoft.Win32;
using ScreenGrab.Core;

namespace ScreenGrab.Windows;

/// <summary>
/// Windows <see cref="IStartupManager"/>: the per-user "run at startup" registry entry under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Ported from the WinForms
/// <c>StartupManager.cs</c>; the MessageBox on failure is dropped in favour of throwing so the
/// tray/editor layer can surface the error and keep the checkbox state honest.
/// </summary>
public sealed class WindowsStartupManager : IStartupManager
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "ScreenGrab";

	public bool IsEnabled()
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

	public void SetEnabled(bool enabled)
	{
		using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
		if (enabled)
		{
			string exe = Environment.ProcessPath
				?? throw new InvalidOperationException("Could not determine the executable path.");
			key.SetValue(ValueName, $"\"{exe}\"");
		}
		else
		{
			key.DeleteValue(ValueName, throwOnMissingValue: false);
		}
	}
}
