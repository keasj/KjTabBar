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

        private readonly Thread _thread;
        private readonly BlockingCollection<Action> _queue;

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
            if (_queue.IsAddingCompleted)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            _queue.Add(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        public Task InvokeAsync(Action action)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            if (_queue.IsAddingCompleted)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            _queue.Add(() =>
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
            });
            return tcs.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
