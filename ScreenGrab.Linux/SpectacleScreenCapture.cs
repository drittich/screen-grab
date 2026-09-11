using System;
using System.Diagnostics;
using System.IO;
using ScreenGrab.Core;
using SkiaSharp;

namespace ScreenGrab.Linux;

/// <summary>
/// Linux/KDE <see cref="IScreenCapture"/>: shell out to Spectacle to grab the current monitor to a
/// temp PNG, then decode it with SkiaSharp. Spectacle is guaranteed on KDE and works on Wayland,
/// so it matches today's "capture the current screen, then run our own selection overlay" flow.
/// </summary>
public sealed class SpectacleScreenCapture : IScreenCapture
{
	// Spectacle flags: -b background (no GUI), -n no on-screen notification, -m current monitor,
	// -o output file. Applied as separate tokens in RunSpectacle below.

	public SKBitmap CaptureFullScreen()
	{
		string tmp = Path.Combine(Path.GetTempPath(), $"screengrab-{Guid.NewGuid():N}.png");
		try
		{
			RunSpectacle(tmp);

			if (!File.Exists(tmp) || new FileInfo(tmp).Length == 0)
				throw new InvalidOperationException("Spectacle produced no capture file.");

			SKBitmap? bitmap = SKBitmap.Decode(tmp);
			if (bitmap is null)
				throw new InvalidOperationException("Could not decode the Spectacle capture PNG.");
			return bitmap;
		}
		finally
		{
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
		}
	}

	private static void RunSpectacle(string outputPath)
	{
		var psi = new ProcessStartInfo("spectacle")
		{
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
		};
		// -bnm as separate tokens, then the output path as its own argument (handles spaces).
		psi.ArgumentList.Add("-b");
		psi.ArgumentList.Add("-n");
		psi.ArgumentList.Add("-m");
		psi.ArgumentList.Add("-o");
		psi.ArgumentList.Add(outputPath);

		Process process;
		try
		{
			process = Process.Start(psi)
				?? throw new InvalidOperationException("Failed to start Spectacle.");
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				"Spectacle is required for capture on Linux/KDE but could not be launched. " +
				"Install it (e.g. `sudo dnf install spectacle`).", ex);
		}

		// Spectacle -b returns once the file is written; give it a generous ceiling.
		if (!process.WaitForExit(30_000))
		{
			try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
			throw new InvalidOperationException("Spectacle capture timed out.");
		}

		if (process.ExitCode != 0)
		{
			string err = process.StandardError.ReadToEnd();
			throw new InvalidOperationException($"Spectacle exited with code {process.ExitCode}. {err}".Trim());
		}
	}
}
