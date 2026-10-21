using System;
using System.Threading;
using System.Threading.Tasks;

namespace KjTabBar.Services
{
    internal sealed class FileOperationTracker
    {
        internal static readonly FileOperationTracker Shared = new FileOperationTracker();
        private readonly object _gate = new object();
        private int _active;
        private bool _stopping;
        private TaskCompletionSource<bool> _idle;

        internal IDisposable TryBegin()
        {
            lock (_gate)
            {
                if (_stopping) return null;
                if (_active++ == 0) _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return new Lease(this);
            }
        }

        internal async Task<bool> RunAsync(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            IDisposable lease = TryBegin();
            if (lease == null) return false;
            try
            {
                // Release the lease on the worker, even if the UI dispatcher is closing.
                await Task.Run(() =>
                {
                    using (lease) action();
                }).ConfigureAwait(false);
                return true;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        internal Task StopAndWaitAsync()
        {
            lock (_gate)
            {
                _stopping = true;
                return _active == 0 ? Task.CompletedTask : _idle.Task;
            }
        }

        private void Complete()
        {
            TaskCompletionSource<bool> idle = null;
            lock (_gate)
            {
                if (--_active == 0) idle = _idle;
            }
            if (idle != null) idle.TrySetResult(true);
        }

        private sealed class Lease : IDisposable
        {
            private FileOperationTracker _owner;
            internal Lease(FileOperationTracker owner) { _owner = owner; }
            public void Dispose()
            {
                FileOperationTracker owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null) owner.Complete();
            }
        }
    }
}
