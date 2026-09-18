using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using KjTabBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerMonitorTimerTests
    {
        [TestMethod]
        public void Timer_PostsNormalPriorityWithoutPumpingUiMessages()
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            int postingThread = callerThread;
            DispatcherPriority priority = DispatcherPriority.Invalid;
            bool ticked = false;
            using (ManualResetEventSlim posted = new ManualResetEventSlim())
            {
                DispatcherHookEventHandler handler = delegate(object sender, DispatcherHookEventArgs args)
                {
                    postingThread = Thread.CurrentThread.ManagedThreadId;
                    priority = args.Operation.Priority;
                    posted.Set();
                };
                dispatcher.Hooks.OperationPosted += handler;
                try
                {
                    using (ExplorerMonitorTimer timer = new AppRuntimeCoordinator().CreateMonitorTimer(
                        TimeSpan.FromMilliseconds(10), (sender, args) => ticked = true))
                    {
                        Assert.IsTrue(posted.Wait(TimeSpan.FromSeconds(5)), "Scheduling must not require a UI timer message.");
                        Assert.AreEqual(DispatcherPriority.Normal, priority);
                        Assert.AreNotEqual(callerThread, postingThread);
                        Assert.IsFalse(ticked, "The timer thread must not execute the UI work.");
                    }
                }
                finally
                {
                    dispatcher.Hooks.OperationPosted -= handler;
                }
            }
        }

        [TestMethod]
        public void DelayedDispatcher_CoalescesTicksAndAllowsNextCycle()
        {
            List<Action> pending = new List<Action>();
            int cycles = 0;
            using (ExplorerMonitorTimer timer = new ExplorerMonitorTimer(Timeout.InfiniteTimeSpan, pending.Add, () => cycles++))
            {
                for (int i = 0; i < 20; i++) timer.RequestTick();
                Assert.AreEqual(1, pending.Count);
                Assert.AreEqual(0, cycles, "Explorer work must not run on the timer thread.");
                pending[0]();
                Assert.AreEqual(1, cycles);
                timer.RequestTick();
                Assert.AreEqual(2, pending.Count);
            }
        }

        [TestMethod]
        public void Dispose_PreventsQueuedAndFutureCycles()
        {
            List<Action> pending = new List<Action>();
            int cycles = 0;
            ExplorerMonitorTimer timer = new ExplorerMonitorTimer(Timeout.InfiniteTimeSpan, pending.Add, () => cycles++);
            timer.RequestTick();
            timer.Dispose();
            pending[0]();
            timer.RequestTick();
            Assert.AreEqual(0, cycles);
            Assert.AreEqual(1, pending.Count);
        }

        [TestMethod]
        public void FailedQueue_DoesNotPermanentlySuppressMonitoring()
        {
            List<Action> pending = new List<Action>();
            bool reject = true;
            using (ExplorerMonitorTimer timer = new ExplorerMonitorTimer(Timeout.InfiniteTimeSpan, callback =>
            {
                if (reject) throw new InvalidOperationException("Dispatcher unavailable");
                pending.Add(callback);
            }, () => { }))
            {
                timer.RequestTick();
                reject = false;
                timer.RequestTick();
                Assert.AreEqual(1, pending.Count);
            }
        }
    }
}
