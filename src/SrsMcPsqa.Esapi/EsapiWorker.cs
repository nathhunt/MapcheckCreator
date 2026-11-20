using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EsapiApplication = VMS.TPS.Common.Model.API.Application;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Owns the ESAPI Application and runs every ESAPI call on one dedicated STA thread (ESAPI is single-threaded).
    /// The Application is created by the first work item, so the UI can appear before ESAPI has started.
    /// Confirmed on Citrix (Phase 0): CreateApplication on this thread takes ~2 s.
    /// </summary>
    public sealed class EsapiWorker : IDisposable
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

        public bool IsBusy => Volatile.Read(ref _pending) > 0;

        /// <summary>
        /// Run <paramref name="work"/> on the ESAPI thread. Exceptions reach the caller wrapped in an
        /// <see cref="EsapiOperationException"/> that names the operation.
        /// </summary>
        public Task<T> RunAsync<T>(string operation, Func<EsapiApplication, T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pending);
            _queue.Add(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    EnsureApplication();
                    var result = work(_app!);
                    _log($"{operation}: done in {sw.Elapsed.TotalSeconds:0.00} s.");
                    tcs.SetResult(result);
                }
                catch (OperationCanceledException oce)
                {
                    tcs.SetCanceled();
                    _log($"{operation}: cancelled after {sw.Elapsed.TotalSeconds:0.00} s ({oce.Message}).");
                }
                catch (EsapiOperationException ex)
                {
                    _log($"{operation} failed after {sw.Elapsed.TotalSeconds:0.00} s: {ex}");
                    tcs.SetException(ex);
                }
                catch (Exception ex)
                {
                    _log($"{operation} failed after {sw.Elapsed.TotalSeconds:0.00} s: {ex}");
                    tcs.SetException(new EsapiOperationException(operation, ex));
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
                throw new EsapiOperationException("connect to Eclipse", _initError);
            _log("Creating ESAPI Application...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _app = EsapiApplication.CreateApplication();
            }
            catch (Exception ex)
            {
                _initError = ex;
                _log("Application.CreateApplication() failed: " + ex);
                throw new EsapiOperationException("connect to Eclipse (Application.CreateApplication)", ex);
            }
            _log($"ESAPI Application created in {sw.Elapsed.TotalSeconds:F1} s for user {_app.CurrentUser?.Id}.");
        }

        private void ThreadMain()
        {
            foreach (var item in _queue.GetConsumingEnumerable()) item();
            if (_app == null) return;
            try { _app.ClosePatient(); } catch (Exception ex) { _log("ClosePatient on exit: " + ex.Message); }
            try { _app.Dispose(); } catch (Exception ex) { _log("Application.Dispose on exit: " + ex.Message); }
            _log("ESAPI Application disposed.");
        }

        /// <summary>Stops the thread after queued work finishes (a dose calculation can't be interrupted).</summary>
        public void Dispose()
        {
            _queue.CompleteAdding();
            if (!_thread.Join(TimeSpan.FromMinutes(5))) _log("ESAPI thread did not stop within 5 minutes.");
        }
    }

    /// <summary>An ESAPI call failed; the message names the operation and includes the ESAPI message.</summary>
    public sealed class EsapiOperationException : Exception
    {
        public EsapiOperationException(string operation, Exception inner)
            : base($"Eclipse: could not {operation}. {inner.Message}", inner)
        {
            Operation = operation;
        }

        public string Operation { get; }
    }
}
