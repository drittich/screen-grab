using System;
using System.Runtime.InteropServices;
using System.Threading;
using ScreenGrab.Core;

namespace ScreenGrab.Windows;

/// <summary>
/// Windows <see cref="IGlobalHotkey"/>: the port of the <c>Form1</c> user32 hotkey code. Since the
/// Avalonia app has no <c>WndProc</c> to hook, this runs a private message-only window on its own
/// thread, registers Ctrl-Alt-F12 and Ctrl-Alt-PrintScreen against it, and pumps its messages,
/// invoking the callback on each <c>WM_HOTKEY</c>.
/// </summary>
public sealed class WindowsGlobalHotkey : IGlobalHotkey
{
	private const int WM_HOTKEY = 0x0312;
	private const int WM_CLOSE = 0x0010;
	private const uint MOD_ALT = 0x0001;
	private const uint MOD_CONTROL = 0x0002;
	private const uint VK_F12 = 0x7B;
	private const uint VK_SNAPSHOT = 0x2C; // PrintScreen
	private const int HOTKEY_F12 = 100;
	private const int HOTKEY_PRTSC = 101;
	private static readonly IntPtr HWND_MESSAGE = new(-3);

	private Thread? _thread;
	private Action? _onTriggered;
	private IntPtr _hwnd;
	private WndProc? _wndProc; // kept alive for the window's lifetime
	private volatile bool _disposed;

	public void Register(Action onTriggered)
	{
		if (_thread != null) throw new InvalidOperationException("Hotkey already registered.");
		_onTriggered = onTriggered ?? throw new ArgumentNullException(nameof(onTriggered));

		using var ready = new ManualResetEventSlim(false);
		_thread = new Thread(() => MessageLoop(ready)) { IsBackground = true, Name = "ScreenGrab hotkey" };
		_thread.Start();
		ready.Wait();
	}

	private void MessageLoop(ManualResetEventSlim ready)
	{
		// A message-only window (HWND_MESSAGE parent) receives WM_HOTKEY without ever being shown.
		string className = "ScreenGrabHotkey_" + Environment.ProcessId;
		_wndProc = WindowProc;
		var wc = new WNDCLASS
		{
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
			hInstance = GetModuleHandle(null),
			lpszClassName = className,
		};
		RegisterClass(ref wc);

		_hwnd = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

		RegisterHotKey(_hwnd, HOTKEY_F12, MOD_CONTROL | MOD_ALT, VK_F12);
		RegisterHotKey(_hwnd, HOTKEY_PRTSC, MOD_CONTROL | MOD_ALT, VK_SNAPSHOT);

		ready.Set();

		while (!_disposed && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
		{
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
		}
	}

	private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WM_HOTKEY && !_disposed)
		{
			try { _onTriggered?.Invoke(); }
			catch { /* never let a callback failure kill the message loop */ }
			return IntPtr.Zero;
		}
		return DefWindowProc(hWnd, msg, wParam, lParam);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		if (_hwnd != IntPtr.Zero)
		{
			UnregisterHotKey(_hwnd, HOTKEY_F12);
			UnregisterHotKey(_hwnd, HOTKEY_PRTSC);
			// Wake the message loop so it re-checks _disposed and exits.
			PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
		}
		_thread?.Join(1000);
	}

	private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	private struct MSG
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int pt_x;
		public int pt_y;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WNDCLASS
	{
		public uint style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string? lpszMenuName;
		public string lpszClassName;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
		uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
		IntPtr hInstance, IntPtr lpParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
