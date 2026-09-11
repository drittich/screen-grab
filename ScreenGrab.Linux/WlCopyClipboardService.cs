using System;
using System.Diagnostics;
using System.Text;
using ScreenGrab.Core;
using SkiaSharp;

namespace ScreenGrab.Linux;

/// <summary>
/// Linux/KDE <see cref="IClipboardService"/> for Wayland: Wayland clipboards don't reliably accept
/// images from generic toolkits, so images and text are handed to <c>wl-copy</c> (shipped with
/// Plasma/Wayland as <c>wl-clipboard</c>), with an <c>xclip</c> X11 fallback. Images are PNG-encoded
/// via Skia and streamed to <c>wl-copy --type image/png</c>; text is streamed as UTF-8.
/// </summary>
public sealed class WlCopyClipboardService : IClipboardService
{
	public void SetImage(SKBitmap image)
	{
		ArgumentNullException.ThrowIfNull(image);
		using SKData png = image.Encode(SKEncodedImageFormat.Png, 100)
			?? throw new InvalidOperationException("Failed to PNG-encode the image for the clipboard.");
		byte[] bytes = png.ToArray();

		if (TryPipe("wl-copy", new[] { "--type", "image/png" }, bytes)) return;
		if (TryPipe("xclip", new[] { "-selection", "clipboard", "-t", "image/png" }, bytes)) return;

		throw new InvalidOperationException(
			"Could not copy the image to the clipboard. Install `wl-clipboard` (provides wl-copy) " +
			"on Wayland, or `xclip` on X11.");
	}

	public void SetText(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);

		if (TryPipe("wl-copy", Array.Empty<string>(), bytes)) return;
		if (TryPipe("xclip", new[] { "-selection", "clipboard" }, bytes)) return;

		throw new InvalidOperationException(
			"Could not copy text to the clipboard. Install `wl-clipboard` (wl-copy) or `xclip`.");
	}

	/// <summary>
	/// Starts <paramref name="exe"/>, writes <paramref name="stdin"/> to it, and returns true if it
	/// launched and exited cleanly. A missing tool (launch failure) returns false so the next
	/// fallback is tried; a non-zero exit throws.
	/// </summary>
	private static bool TryPipe(string exe, string[] args, byte[] stdin)
	{
		var psi = new ProcessStartInfo(exe)
		{
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardError = true,
		};
		foreach (string a in args) psi.ArgumentList.Add(a);

		Process process;
		try
		{
			process = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} did not start.");
		}
		catch
		{
			return false; // tool not installed / not on PATH — let the caller try a fallback
		}

		using (process)
		{
			using (var s = process.StandardInput.BaseStream)
				s.Write(stdin, 0, stdin.Length);

			// wl-copy forks to serve the selection, so the foreground process returns promptly.
			if (!process.WaitForExit(15_000))
			{
				try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
				throw new InvalidOperationException($"{exe} timed out while setting the clipboard.");
			}

			if (process.ExitCode != 0)
			{
				string err = process.StandardError.ReadToEnd();
				throw new InvalidOperationException($"{exe} exited with code {process.ExitCode}. {err}".Trim());
			}
			return true;
		}
	}
}
