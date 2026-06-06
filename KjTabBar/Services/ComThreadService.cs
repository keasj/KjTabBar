using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    public sealed class ComThreadService : IDisposable
    {
        private static readonly Lazy<ComThreadService> _instance = new Lazy<ComThreadService>(() => new ComThreadService());
        public static ComThreadService Instance => _instance.Value;
        public static bool IsCreated { get { return _instance.IsValueCreated; } }

        private readonly Thread _thread;
        private readonly BlockingCollection<Action> _queue;
        private int _disposed;

        private ComThreadService()
        {
            _queue = new BlockingCollection<Action>();
            _thread = new Thread(RunLoop);
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Name = "KjTabBar_STA_Worker";
            _thread.Start();
        }

        private void RunLoop()
        {
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ComThreadService", "Unhandled action failed on COM worker thread.", ex);
                }
            }
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            if (!TryAdd(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }))
            {
                tcs.SetCanceled();
            }
            return tcs.Task;
        }

        public Task InvokeAsync(Action action)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            if (!TryAdd(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                tcs.SetCanceled();
            }
            return tcs.Task;
        }

        private bool TryAdd(Action action)
        {
            if (_queue.IsAddingCompleted)
            {
                return false;
            }

            try
            {
                _queue.Add(action);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _queue.CompleteAdding();
            }
        }
    }
}
