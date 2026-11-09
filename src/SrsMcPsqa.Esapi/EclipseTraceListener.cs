using System;
using System.Diagnostics;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Receives what Eclipse writes to .NET trace output (process-wide) while it is registered, as complete lines
    /// (<see cref="TraceLineBuffer"/>). Eclipse may write from its own threads, so the callback must be thread-safe
    /// (IProgress&lt;T&gt; created on the UI thread is). Use with <c>using</c>: it removes itself on dispose.
    /// </summary>
    internal sealed class EclipseTraceListener : TraceListener
    {
        private readonly Action<string> _onLine;
        private readonly TraceLineBuffer _buffer = new TraceLineBuffer();
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
            System.Collections.Generic.IReadOnlyList<string> lines;
            lock (_lock) lines = _buffer.Write(message);
            Emit(lines);
        }

        public override void WriteLine(string? message)
        {
            System.Collections.Generic.IReadOnlyList<string> lines;
            lock (_lock) lines = _buffer.WriteLine(message);
            Emit(lines);
        }

        private void Emit(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                try { _onLine(line); }
                catch { /* never let progress display break Eclipse's own tracing */ }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Trace.Listeners.Remove(this);
                System.Collections.Generic.IReadOnlyList<string> rest;
                lock (_lock) rest = _buffer.WriteLine(null); // flush a trailing partial line
                Emit(rest);
            }
            base.Dispose(disposing);
        }
    }
}
