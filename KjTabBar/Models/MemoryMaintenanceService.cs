using System;
using KjTabBar.Helpers;
using KjTabBar.Services;

namespace KjTabBar.Models
{
    internal sealed class MemoryMaintenanceService
    {
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(3);
        private readonly IExplorerService _explorerService;
        private DateTime _lastMaintenanceUtc = DateTime.MinValue;

        public MemoryMaintenanceService(IExplorerService explorerService)
        {
            _explorerService = explorerService;
        }

        internal static TimeSpan GetMaintenanceInterval()
        {
            return MaintenanceInterval;
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
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MemoryMaintenanceService", "Failed to perform periodic memory maintenance.", ex);
            }
        }

    }
}
