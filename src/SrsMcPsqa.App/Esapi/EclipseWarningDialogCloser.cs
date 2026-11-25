using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Closes the message box Eclipse opens after a dose calculation that only lists warnings (e.g. "Warning: [COUCH 0]
    /// The calibration protocol status is not Commissioning or Approved."). The calculation waits until it's closed, and
    /// the same warnings are already in the progress panel, on the plan's card and in the log.
    /// ESAPI has no documented way to stop the box, so the app watches for standard Windows dialogs opened in its own
    /// process (SetWinEventHook, EVENT_SYSTEM_DIALOGSTART), reads the text, logs it, and presses the button only when
    /// <see cref="EclipseWarningDialog.IsWarningsOnly"/> says so. Every other dialog is left for the user.
    /// Install on the UI thread (the hook's callbacks arrive through its message loop).
    /// </summary>
    public sealed class EclipseWarningDialogCloser : IDisposable
    {
        private readonly Action<string> _log;
        private readonly Dispatcher _dispatcher;
        private readonly WinEventProc _proc; // kept alive: the hook calls it after this method returns
        private IntPtr _hook;

        public EclipseWarningDialogCloser(Action<string> log)
        {
            _log = log;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _proc = OnWinEvent;
            try
            {
                _hook = SetWinEventHook(EVENT_SYSTEM_DIALOGSTART, EVENT_SYSTEM_DIALOGSTART, IntPtr.Zero, _proc,
                    (uint)Process.GetCurrentProcess().Id, 0, WINEVENT_OUTOFCONTEXT);
                if (_hook == IntPtr.Zero) _log("Eclipse warning boxes will not be closed automatically: SetWinEventHook failed (error " + Marshal.GetLastWin32Error() + ").");
            }
            catch (Exception ex) { _log("Eclipse warning boxes will not be closed automatically: " + ex.Message); }
        }

        private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || ClassName(hwnd) != "#32770") return;
            // Give the box a moment to finish setting its text, then look.
            var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, __) => { timer.Stop(); Inspect(hwnd); };
            timer.Start();
        }

        private void Inspect(IntPtr hwnd)
        {
            try
            {
                if (!IsWindow(hwnd)) return;
                var buttons = new List<IntPtr>();
                var statics = new List<string>();
                EnumChildWindows(hwnd, (child, _) =>
                {
                    string cls = ClassName(child);
                    if (cls == "Button" && IsWindowVisible(child)) buttons.Add(child);
                    else if (cls == "Static") { var t = WindowText(child); if (t.Length > 0) statics.Add(t); }
                    return true;
                }, IntPtr.Zero);
                string text = DialogItemText(hwnd, 0xFFFF);
                if (text.Length == 0) text = string.Join(Environment.NewLine, statics);
                string title = WindowText(hwnd);
                string oneLine = text.Replace("\r", "").Replace("\n", " | ");

                if (!EclipseWarningDialog.IsWarningsOnly(text, buttons.Count))
                {
                    _log($"Dialog '{title}' left open for the user ({buttons.Count} button(s)): {oneLine}");
                    return;
                }
                var button = buttons[0];
                int id = GetDlgCtrlID(button);
                PostMessage(hwnd, WM_COMMAND, (IntPtr)(id & 0xFFFF), button); // as if clicked (BN_CLICKED = 0)
                _log($"Closed Eclipse warnings box '{title}' automatically: {oneLine}");
            }
            catch (Exception ex) { _log("Checking an Eclipse dialog failed: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        }

        // ------------------------------------------------------------------ Win32
        private static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        private static string WindowText(IntPtr hwnd)
        {
            int len = GetWindowTextLength(hwnd);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string DialogItemText(IntPtr hwnd, int id)
        {
            var sb = new StringBuilder(32768);
            return GetDlgItemText(hwnd, id, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        private const uint EVENT_SYSTEM_DIALOGSTART = 0x0010, WINEVENT_OUTOFCONTEXT = 0x0000, WM_COMMAND = 0x0111;

        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint idProcess, uint idThread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDlgItemText(IntPtr dlg, int id, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
