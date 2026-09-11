using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ScreenGrab.Core;

namespace ScreenGrab.Windows;

/// <summary>
/// Windows <see cref="ISingleInstanceIpc"/>: the port of the <c>Program.cs</c> named <c>Mutex</c>, plus a
/// named pipe so a later <c>screengrab --capture</c> can trigger the running tray instance. The mutex
/// grants primary ownership; the pipe carries the "capture" request.
/// </summary>
public sealed class WindowsSingleInstanceIpc : ISingleInstanceIpc
{
	private const string MutexName = "ScreenGrabApplicationMutex";
	private const string PipeName = "ScreenGrabCapturePipe";
	private const string CaptureMessage = "capture";

	private Mutex? _mutex;
	private CancellationTokenSource? _cts;
	private volatile bool _disposed;

	public event Action? CaptureRequested;

	public bool TryAcquire()
	{
		_mutex = new Mutex(true, MutexName, out bool createdNew);
		return createdNew;
	}

	public void StartListening()
	{
		_cts = new CancellationTokenSource();
		_ = Task.Run(() => ListenLoopAsync(_cts.Token));
	}

	private async Task ListenLoopAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
					PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
				await server.WaitForConnectionAsync(token).ConfigureAwait(false);

				using var reader = new StreamReader(server);
				string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
				if (line == CaptureMessage)
					CaptureRequested?.Invoke();
			}
			catch (OperationCanceledException) { break; }
			catch { /* a malformed/aborted connection shouldn't kill the listener */ }
		}
	}

	public void SignalCapture()
	{
		using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
		client.Connect(2000);
		using var writer = new StreamWriter(client) { AutoFlush = true };
		writer.WriteLine(CaptureMessage);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_cts?.Cancel();
		_cts?.Dispose();
		if (_mutex != null)
		{
			try { _mutex.ReleaseMutex(); } catch { /* not held */ }
			_mutex.Dispose();
		}
	}
}
