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
            System.Diagnostics.Stopwatch timer = AppLogger.StartDiagnosticTiming();
            AppLogger.LogDiagnosticTiming("Maintenance.Begin", IntPtr.Zero, timer);

            try
            {
                _explorerService.ReleaseCachedComObjects();
                AppLogger.LogDiagnosticTiming("Maintenance.UiRelease", IntPtr.Zero, timer);
                _ = ComThreadService.Instance.InvokeAsync(() =>
                {
                    AppLogger.LogDiagnosticTiming("Maintenance.WorkerStart", IntPtr.Zero, timer);
                    _explorerService.ReleaseCachedComObjects();
                    AppLogger.LogDiagnosticTiming("Maintenance.WorkerRelease", IntPtr.Zero, timer);
                });

                System.Runtime.InteropServices.Marshal.CleanupUnusedObjectsInCurrentContext();
                AppLogger.LogDiagnosticTiming("Maintenance.UiComplete", IntPtr.Zero, timer);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MemoryMaintenanceService", "Failed to perform periodic memory maintenance.", ex);
            }
        }

    }
}
