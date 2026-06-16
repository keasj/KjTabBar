using System;
using System.Threading;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.Services;

namespace KjTabBar.Models
{
    internal sealed class MemoryMaintenanceService
    {
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(60);
        private readonly IExplorerService _explorerService;
        private DateTime _lastMaintenanceUtc = DateTime.MinValue;
        private int _gcMaintenanceRunning;

        public MemoryMaintenanceService(IExplorerService explorerService)
        {
            _explorerService = explorerService;
        }

        public void PerformIfDue()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (_lastMaintenanceUtc != DateTime.MinValue)
            {
                if ((nowUtc - _lastMaintenanceUtc) < MaintenanceInterval)
                {
                    return;
                }
            }

            _lastMaintenanceUtc = nowUtc;

            try
            {
                _explorerService.ReleaseCachedComObjects();
                _ = ComThreadService.Instance.InvokeAsync(() =>
                {
                    _explorerService.ReleaseCachedComObjects();
                });

                System.Runtime.InteropServices.Marshal.CleanupUnusedObjectsInCurrentContext();
                StartBackgroundGarbageCollection();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MemoryMaintenanceService", "Failed to perform periodic memory maintenance.", ex);
            }
        }

        private void StartBackgroundGarbageCollection()
        {
            if (Interlocked.Exchange(ref _gcMaintenanceRunning, 1) != 0)
            {
                return;
            }

            _ = Task.Run((Action)delegate
            {
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                finally
                {
                    Interlocked.Exchange(ref _gcMaintenanceRunning, 0);
                }
            });
        }
    }
}
