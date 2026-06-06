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
            Func<Func<ExplorerWindowEvaluationResult>, Task<ExplorerWindowEvaluationResult>> invokeComAsync)
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
                invokeComAsync);
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

            public override void ApplyOutcome(IntPtr hwnd, int retryCount, ExplorerWindowEvaluationResult result, TabBarViewModel validTarget, TabBarViewModel controlPanelTarget)
            {
                _capture(validTarget);
                _inner.ApplyOutcome(hwnd, retryCount, result, validTarget, controlPanelTarget);
            }
        }
    }
}
