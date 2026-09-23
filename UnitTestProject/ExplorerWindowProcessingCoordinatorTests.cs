using System;
using System.Threading.Tasks;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowProcessingCoordinatorTests
    {
        [TestMethod]
        public async Task AbsorbNormalFolder_DoesNotCloseReusedParkedHost()
        {
            await VerifyNormalAbsorptionHost(false, true);
        }

        [TestMethod]
        public async Task AbsorbNormalFolder_PreparesParkedHostBeforeNavigation()
        {
            await VerifyNormalAbsorptionHost(false);
        }

        [TestMethod]
        public async Task AbsorbNormalFolder_RevealsIncomingWindowIfHostSwitchFails()
        {
            await VerifyNormalAbsorptionHost(true);
        }

        private static async Task VerifyNormalAbsorptionHost(bool rejectRebind, bool reusedHost = false)
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = hwnd => hwnd == (IntPtr)100 ? explorer.AllControlPanelPath : @"C:\Assets";
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            tracking.RememberParkedExplorerOrigin((IntPtr)100, (IntPtr)200);
            TabBarViewModel vm = new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer);
            int initialCount = vm.Tabs.Count;
            IntPtr shown = IntPtr.Zero;
            IntPtr closed = IntPtr.Zero;
            bool rebound = false;
            ExplorerHostSwitchCoordinator host = new ExplorerHostSwitchCoordinator(explorer, tracking,
                delegate (TabBarViewModel target, IntPtr hwnd)
                {
                    if (rejectRebind) return false;
                    target.SetExplorerHwnd(hwnd); rebound = true; return true;
                }, hwnd => shown = hwnd, delegate { }, delegate { }, delegate { return true; },
                delegate { return new System.Collections.Generic.List<IntPtr>(); }, explorer.GetCurrentPath,
                delegate { return false; }, delegate { });
            ExplorerWindowInteractionService interaction = new ExplorerWindowInteractionService(
                explorer, tracking, TestTabPersistenceFactory.Create(), delegate { return string.Empty; },
                hwnd => shown = hwnd, delegate { }, hwnd => closed = hwnd, delegate { return null; }, delegate { });
            ExplorerWindowOutcomeCoordinator outcome = new ExplorerWindowOutcomeCoordinator(
                tracking, interaction, delegate { }, delegate { return new MockUserSettings(); }, delegate { });
            ExplorerWindowProcessingCoordinator processor = new ExplorerWindowProcessingCoordinator(
                tracking, null, null, interaction,
                new CapturingOutcomeCoordinator(outcome, target =>
                {
                    Assert.IsTrue(rebound, "Prepare the folder host before inserting/navigating the tab.");
                    Assert.AreEqual((IntPtr)200, target.ExplorerHwnd);
                }), delegate { return Task.FromResult<ExplorerWindowEvaluationResult>(null); },
                null, delegate { return true; });
            processor.FindHostSwitchCoordinator = target => host;

            await processor.ApplyOutcomeAsync(reusedHost ? (IntPtr)200 : (IntPtr)300, 0, new ExplorerWindowEvaluationResult
            { Action = AbsorptionAction.Absorb, ResolvedPath = @"C:\Assets" }, vm, null);

            Assert.AreEqual(rejectRebind ? initialCount : initialCount + 1, vm.Tabs.Count);
            Assert.AreEqual(rejectRebind || reusedHost ? IntPtr.Zero : (IntPtr)300, closed);
            Assert.AreEqual(rejectRebind ? (IntPtr)300 : (IntPtr)200, shown);
            Assert.AreEqual(rejectRebind ? (IntPtr)100 : (IntPtr)200, vm.ExplorerHwnd);
            if (!rejectRebind) Assert.AreEqual(@"C:\Assets", vm.ActiveTab.Path);
        }

        [TestMethod]
        public async Task ProcessAsync_EmptyFirstPath_RetriesOnceWithoutReleasingProcessingGuard()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            IntPtr hwnd = (IntPtr)20;
            tracking.ProcessingExplorerWindows.Add(hwnd);
            int evaluations = 0;
            int delays = 0;
            ExplorerWindowProcessingCoordinator coordinator = CreateCoordinator(tracking,
                delegate
                {
                    evaluations++;
                    return Task.FromResult(new ExplorerWindowEvaluationResult
                    {
                        Action = AbsorptionAction.WaitAndRetryIncrement,
                        ResolvedPath = null
                    });
                },
                delegate (int milliseconds)
                {
                    Assert.AreEqual(100, milliseconds);
                    Assert.IsTrue(tracking.ProcessingExplorerWindows.Contains(hwnd));
                    delays++;
                    return Task.CompletedTask;
                }, delegate { return true; });

            await coordinator.ProcessAsync(hwnd, null, null, delegate { return null; },
                null, null, delegate { return false; }, delegate { return false; });
            Assert.AreEqual(2, evaluations, "A still empty path must return to normal polling after one retry.");
            Assert.AreEqual(1, delays);
            Assert.IsFalse(tracking.ProcessingExplorerWindows.Contains(hwnd));
        }

        [TestMethod]
        public async Task ProcessAsync_EarlyRetryStopsIfWindowClosesDuringDelay()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            int evaluations = 0;
            ExplorerWindowProcessingCoordinator coordinator = CreateCoordinator(tracking,
                delegate
                {
                    evaluations++;
                    return Task.FromResult(new ExplorerWindowEvaluationResult
                    {
                        Action = AbsorptionAction.WaitAndRetryIncrement
                    });
                }, delegate { return Task.CompletedTask; }, delegate { return false; });
            await coordinator.ProcessAsync((IntPtr)20, null, null, delegate { return null; },
                null, null, delegate { return false; }, delegate { return false; });
            Assert.AreEqual(1, evaluations);
        }

        [TestMethod]
        public async Task ProcessAsync_ResolvedTransientPath_KeepsNormalRetryTiming()
        {
            ExplorerWindowTrackingState tracking = new ExplorerWindowTrackingState();
            ExplorerWindowProcessingCoordinator coordinator = CreateCoordinator(tracking,
                delegate
                {
                    return Task.FromResult(new ExplorerWindowEvaluationResult
                    {
                        Action = AbsorptionAction.WaitAndRetryIncrement,
                        ResolvedPath = "::{26EE0668-A00A-44D7-9371-BEB064C98683}"
                    });
                },
                delegate { Assert.Fail("Do not shorten stabilization waits for resolved special paths."); return Task.CompletedTask; },
                delegate { return true; });
            await coordinator.ProcessAsync((IntPtr)20, null, null, delegate { return null; },
                null, null, delegate { return false; }, delegate { return false; });
        }

        [TestMethod]
        public async Task ProcessAsync_RemovesProcessingWindow_WhenEvaluationThrows()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.ProcessingExplorerWindows.Add((IntPtr)10);

            ExplorerWindowProcessingCoordinator coordinator = CreateCoordinator(
                trackingState,
                delegate (Func<ExplorerWindowEvaluationResult> callback)
                {
                    throw new InvalidOperationException("boom");
                });

            await coordinator.ProcessAsync(
                (IntPtr)10,
                null,
                delegate (string path) { return null; },
                delegate { return null; },
                delegate (TabBarViewModel vm, string path) { return false; },
                delegate (TabBarViewModel vm) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; });

            Assert.IsFalse(trackingState.ProcessingExplorerWindows.Contains((IntPtr)10));
        }

        [TestMethod]
        public async Task ProcessAsync_UsesLatestValidTarget_ForOutcomeApplication()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.ProcessingExplorerWindows.Add((IntPtr)20);

            MockExplorerService explorerService = new MockExplorerService();
            DesktopForegroundTracker foregroundTracker = new DesktopForegroundTracker();
            ExplorerLaunchTracker launchTracker = new ExplorerLaunchTracker(
                foregroundTracker,
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return IntPtr.Zero; },
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });
            ExplorerWindowEvaluationService evaluationService = new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));
            ExplorerWindowInteractionService interactionService = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate (TabBarWindow window) { });

            TabBarViewModel initialTarget = new TabBarViewModel((IntPtr)1, new MockUserSettings(), explorerService);
            TabBarViewModel latestTarget = new TabBarViewModel((IntPtr)2, new MockUserSettings(), explorerService);
            TabBarViewModel appliedTarget = null;

            ExplorerWindowOutcomeCoordinator outcomeCoordinator = new ExplorerWindowOutcomeCoordinator(
                trackingState,
                interactionService,
                delegate (string source, string message, Exception ex) { },
                delegate (IntPtr hwnd) { },
                delegate { return new MockUserSettings(); },
                delegate (IntPtr hwnd, TabBarWindow window) { });

            ExplorerWindowProcessingCoordinator coordinator = new ExplorerWindowProcessingCoordinator(
                trackingState,
                launchTracker,
                evaluationService,
                interactionService,
                new CapturingOutcomeCoordinator(
                    outcomeCoordinator,
                    delegate (TabBarViewModel target) { appliedTarget = target; }),
                delegate (Func<ExplorerWindowEvaluationResult> callback)
                {
                    return Task.FromResult(new ExplorerWindowEvaluationResult
                    {
                        Action = AbsorptionAction.Ignore,
                        ResolvedPath = @"C:\MockPath",
                        IsControlPanelPath = false
                    });
                });

            await coordinator.ProcessAsync(
                (IntPtr)20,
                initialTarget,
                delegate (string path) { return null; },
                delegate { return latestTarget; },
                delegate (TabBarViewModel vm, string path) { return false; },
                delegate (TabBarViewModel vm) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; });

            Assert.AreSame(latestTarget, appliedTarget);
        }

        private static ExplorerWindowProcessingCoordinator CreateCoordinator(
            ExplorerWindowTrackingState trackingState,
            Func<Func<ExplorerWindowEvaluationResult>, Task<ExplorerWindowEvaluationResult>> invokeComAsync,
            Func<int, Task> delayAsync = null, Func<IntPtr, bool> isWindow = null)
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerLaunchTracker launchTracker = new ExplorerLaunchTracker(
                new DesktopForegroundTracker(),
                trackingState,
                delegate (IntPtr hwnd) { return false; },
                delegate (IntPtr hwnd) { return false; },
                delegate { return IntPtr.Zero; },
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd, uint flags) { return hwnd; },
                delegate (IntPtr hwnd) { return true; });
            ExplorerWindowEvaluationService evaluationService = new ExplorerWindowEvaluationService(explorerService, new DesktopPathClassifier(explorerService));
            ExplorerWindowInteractionService interactionService = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate { return null; },
                delegate (TabBarWindow window) { });
            ExplorerWindowOutcomeCoordinator outcomeCoordinator = new ExplorerWindowOutcomeCoordinator(
                trackingState,
                interactionService,
                delegate (string source, string message, Exception ex) { },
                delegate (IntPtr hwnd) { },
                delegate { return new MockUserSettings(); },
                delegate (IntPtr hwnd, TabBarWindow window) { });

            return new ExplorerWindowProcessingCoordinator(
                trackingState,
                launchTracker,
                evaluationService,
                interactionService,
                outcomeCoordinator,
                invokeComAsync, delayAsync, isWindow);
        }

        private sealed class CapturingOutcomeCoordinator : ExplorerWindowOutcomeCoordinator
        {
            private readonly ExplorerWindowOutcomeCoordinator _inner;
            private readonly Action<TabBarViewModel> _capture;

            public CapturingOutcomeCoordinator(ExplorerWindowOutcomeCoordinator inner, Action<TabBarViewModel> capture)
                : base(new ExplorerWindowTrackingState(), null, delegate (IntPtr hwnd) { }, delegate { return null; }, delegate (IntPtr hwnd, TabBarWindow window) { })
            {
                _inner = inner;
                _capture = capture;
            }

            internal override async Task ApplyOutcomeAsync(IntPtr hwnd, int retryCount, ExplorerWindowEvaluationResult result, TabBarViewModel validTarget, TabBarViewModel controlPanelTarget, bool operationReserved = false)
            {
                _capture(validTarget);
                await _inner.ApplyOutcomeAsync(hwnd, retryCount, result, validTarget, controlPanelTarget, operationReserved);
            }
        }
    }
}
