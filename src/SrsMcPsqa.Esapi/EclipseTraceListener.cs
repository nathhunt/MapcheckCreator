using System;
using System.Diagnostics;
using System.Text;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Receives what Eclipse writes to .NET trace output (process-wide) while it is registered, one complete line at a time.
    /// Eclipse may write from its own threads, so the callback must be thread-safe (IProgress&lt;T&gt; created on the UI thread is).
    /// Use with <c>using</c>: it removes itself from <see cref="Trace.Listeners"/> on dispose.
    /// </summary>
    internal sealed class EclipseTraceListener : TraceListener
    {
        private readonly Action<string> _onLine;
        private readonly StringBuilder _partial = new StringBuilder();
        private readonly object _lock = new object();

        private EclipseTraceListener(Action<string> onLine) : base("SrsMcPsqa") { _onLine = onLine; }

        public static EclipseTraceListener Attach(Action<string> onLine)
        {
            var l = new EclipseTraceListener(onLine);
            Trace.Listeners.Add(l);
            return l;
        }

        public override void Write(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (_lock) _partial.Append(message);
        }

        public override void WriteLine(string? message)
        {
            string line;
            lock (_lock)
            {
                _partial.Append(message);
                line = _partial.ToString();
                _partial.Clear();
            }
            if (string.IsNullOrWhiteSpace(line)) return;
            try { _onLine(line); }
            catch { /* never let progress display break Eclipse's own tracing */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Trace.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }
}
