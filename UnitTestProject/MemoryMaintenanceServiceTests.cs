using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class MemoryMaintenanceServiceTests
    {
        private sealed class BlockingExplorer : MockExplorerService, IExplorerService
        {
            internal readonly System.Threading.ManualResetEventSlim Entered = new System.Threading.ManualResetEventSlim();
            internal readonly System.Threading.ManualResetEventSlim Release = new System.Threading.ManualResetEventSlim();
            internal int Calls;
            internal int CallingThread;
            public new void ReleaseCachedComObjects()
            {
                System.Threading.Interlocked.Increment(ref Calls);
                CallingThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Entered.Set();
                Release.Wait(1000);
            }
        }

        [TestMethod]
        public void Maintenance_DoesNotBlockCallerOrReleaseTwice()
        {
            BlockingExplorer explorer = new BlockingExplorer();
            int caller = System.Threading.Thread.CurrentThread.ManagedThreadId;
            try
            {
                MemoryMaintenanceService service = new MemoryMaintenanceService(explorer);
                System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
                service.PerformIfDue();
                long elapsed = timer.ElapsedMilliseconds;
                Assert.IsTrue(explorer.Entered.Wait(3000));
                explorer.Release.Set();
                KjTabBar.Services.ComThreadService.Instance.InvokeAsync(() => true).GetAwaiter().GetResult();
                Assert.AreNotEqual(caller, explorer.CallingThread);
                Assert.IsTrue(elapsed < 500, "The caller waited for Shell cleanup: " + elapsed + " ms");
                service.PerformIfDue();
                KjTabBar.Services.ComThreadService.Instance.InvokeAsync(() => true).GetAwaiter().GetResult();
                Assert.AreEqual(1, explorer.Calls, "Release once per maintenance interval.");
            }
            finally
            {
                explorer.Release.Set();
                KjTabBar.Services.ComThreadService.Instance.InvokeAsync(() => true).GetAwaiter().GetResult();
                explorer.Entered.Dispose();
                explorer.Release.Dispose();
            }
        }

        [TestMethod]
        public void MaintenanceInterval_Is_Three_Minutes()
        {
            Assert.AreEqual(3.0, MemoryMaintenanceService.GetMaintenanceInterval().TotalMinutes);
        }
    }
}
