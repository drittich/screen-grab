using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScreenGrab.Core;

namespace ScreenGrab.Linux;

/// <summary>
/// Linux/KDE <see cref="ISingleInstanceIpc"/>: a Unix-domain socket in <c>$XDG_RUNTIME_DIR</c>. Binding
/// the socket is what claims primary ownership; a later <c>screengrab --capture</c> connects to it and
/// sends "capture", which is how a KDE custom shortcut reaches the running tray instance.
/// </summary>
public sealed class LinuxSingleInstanceIpc : ISingleInstanceIpc
{
	private const string CaptureMessage = "capture";

	private readonly string _socketPath;
	private Socket? _listener;
	private CancellationTokenSource? _cts;
	private volatile bool _disposed;

	public event Action? CaptureRequested;

	public LinuxSingleInstanceIpc()
	{
		string dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } xdg
			? xdg
			: Path.GetTempPath();
		_socketPath = Path.Combine(dir, "screengrab.sock");
	}

	public bool TryAcquire()
	{
		// If an existing socket accepts a connection, another instance is live — we're secondary.
		if (File.Exists(_socketPath) && IsLive())
			return false;

		// Otherwise the socket file is stale (or absent); remove and claim it.
		try { File.Delete(_socketPath); } catch { /* fine if it wasn't there */ }

		var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
			listener.Listen(4);
		}
		catch (SocketException)
		{
			// Lost a race to bind: another instance grabbed it first.
			listener.Dispose();
			return false;
		}
		_listener = listener;
		return true;
	}

	private bool IsLive()
	{
		try
		{
			using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			probe.Connect(new UnixDomainSocketEndPoint(_socketPath));
			return true;
		}
		catch
		{
			return false; // nobody listening → stale socket file
		}
	}

	public void StartListening()
	{
		if (_listener == null) throw new InvalidOperationException("Not the primary instance.");
		_cts = new CancellationTokenSource();
		_ = Task.Run(() => AcceptLoopAsync(_cts.Token));
	}

	private async Task AcceptLoopAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested && _listener != null)
		{
			Socket conn;
			try { conn = await _listener.AcceptAsync(token).ConfigureAwait(false); }
			catch (OperationCanceledException) { break; }
			catch (ObjectDisposedException) { break; }
			catch { continue; }

			using (conn)
			{
				try
				{
					var buffer = new byte[64];
					int read = await conn.ReceiveAsync(buffer, token).ConfigureAwait(false);
					string msg = Encoding.UTF8.GetString(buffer, 0, read).Trim();
					if (msg == CaptureMessage)
						CaptureRequested?.Invoke();
				}
				catch { /* ignore a bad/aborted connection */ }
			}
		}
	}

	public void SignalCapture()
	{
		using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		client.Connect(new UnixDomainSocketEndPoint(_socketPath));
		client.Send(Encoding.UTF8.GetBytes(CaptureMessage));
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_cts?.Cancel();
		_cts?.Dispose();
		_listener?.Dispose();
		if (_listener != null)
		{
			try { File.Delete(_socketPath); } catch { /* best effort */ }
		}
	}
}
