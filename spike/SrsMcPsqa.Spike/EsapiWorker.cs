using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EsapiApplication = VMS.TPS.Common.Model.API.Application;

namespace SrsMcPsqa.Spike
{
    /// <summary>
    /// Owns the ESAPI <see cref="EsapiApplication"/> and runs every ESAPI call on one dedicated STA thread.
    /// The WPF UI thread never touches ESAPI objects; it awaits <see cref="RunAsync{T}"/>.
    /// The Application is created by the first queued work item, so the UI can show before ESAPI starts.
    /// </summary>
    internal sealed class EsapiWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Thread _thread;
        private readonly Action<string> _log;
        private EsapiApplication? _app;
        private Exception? _initError;
        private int _pending;

        public EsapiWorker(Action<string> log)
        {
            _log = log;
            _thread = new Thread(ThreadMain) { IsBackground = true, Name = "ESAPI" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        /// <summary>True while a work item is queued or running.</summary>
        public bool IsBusy => Volatile.Read(ref _pending) > 0;

        /// <summary>Queue <paramref name="work"/> on the ESAPI thread. Exceptions are rethrown to the awaiting caller.</summary>
        public Task<T> RunAsync<T>(Func<EsapiApplication, T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pending);
            _queue.Add(() =>
            {
                try
                {
                    EnsureApplication();
                    tcs.SetResult(work(_app!));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            });
            return tcs.Task;
        }

        private void EnsureApplication()
        {
            if (_app != null) return;
            if (_initError != null)
                throw new InvalidOperationException("ESAPI Application.CreateApplication() failed earlier in this session. Restart the app.", _initError);

            _log("[INFO] Creating ESAPI Application on dedicated STA thread (Application.CreateApplication())...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _app = EsapiApplication.CreateApplication();
            }
            catch (Exception ex)
            {
                _initError = ex;
                _log("[FAIL] Application.CreateApplication() threw:\n" + ex);
                throw;
            }
            _log($"[PASS] ESAPI Application created in {sw.Elapsed.TotalSeconds:F1} s.");
        }

        private void ThreadMain()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
                item();

            // Shutdown, on the same thread that created the Application.
            if (_app != null)
            {
                try { _app.ClosePatient(); } catch (Exception ex) { _log("[WARN] ClosePatient on exit: " + ex.Message); }
                try { _app.Dispose(); } catch (Exception ex) { _log("[WARN] Application.Dispose on exit: " + ex.Message); }
                _log("[INFO] ESAPI Application disposed.");
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            // A dose calculation cannot be interrupted; give it time to finish.
            if (!_thread.Join(TimeSpan.FromMinutes(5)))
                _log("[WARN] ESAPI thread did not stop within 5 minutes.");
        }
    }
}
