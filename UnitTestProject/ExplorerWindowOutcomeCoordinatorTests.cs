using System;
using KjTabBar.Models;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowOutcomeCoordinatorTests
    {
        [TestMethod]
        public void ApplyOutcome_WaitAndRetry_StoresIncrementedRetryCount()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            ExplorerWindowOutcomeCoordinator coordinator = CreateCoordinator(trackingState);

            coordinator.ApplyOutcome(
                (IntPtr)10,
                2,
                new ExplorerWindowEvaluationResult { Action = AbsorptionAction.WaitAndRetryIncrement },
                null,
                null);

            Assert.AreEqual(3, trackingState.AbsorbPathRetryCounts[(IntPtr)10]);
        }

        [TestMethod]
        public void ApplyOutcome_CreateNewTabBar_WithExistingTarget_DoesNothing()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            bool createCalled = false;
            ExplorerWindowOutcomeCoordinator coordinator = CreateCoordinator(
                trackingState,
                delegate { createCalled = true; });

            coordinator.ApplyOutcome(
                (IntPtr)20,
                0,
                new ExplorerWindowEvaluationResult { Action = AbsorptionAction.CreateNewTabBar },
                new TabBarViewModel(IntPtr.Zero, new MockUserSettings(), new MockExplorerService()),
                null);

            Assert.IsFalse(createCalled);
        }

        [TestMethod]
        public void ApplyOutcome_Ignore_ClearsStateAndCallsIgnore()
        {
            ExplorerWindowTrackingState trackingState = new ExplorerWindowTrackingState();
            trackingState.AbsorbPathRetryCounts[(IntPtr)30] = 1;
            trackingState.ControlPanelTabLaunchCandidates.Add((IntPtr)30);

            IntPtr ignoredHwnd = IntPtr.Zero;
            ExplorerWindowOutcomeCoordinator coordinator = CreateCoordinator(
                trackingState,
                null,
                delegate (IntPtr hwnd) { ignoredHwnd = hwnd; });

            coordinator.ApplyOutcome(
                (IntPtr)30,
                0,
                new ExplorerWindowEvaluationResult { Action = AbsorptionAction.Ignore },
                null,
                null);

            Assert.AreEqual((IntPtr)30, ignoredHwnd);
            Assert.IsFalse(trackingState.AbsorbPathRetryCounts.ContainsKey((IntPtr)30));
            Assert.IsFalse(trackingState.ControlPanelTabLaunchCandidates.Contains((IntPtr)30));
        }

        private static ExplorerWindowOutcomeCoordinator CreateCoordinator(
            ExplorerWindowTrackingState trackingState,
            Action createNewTabBarAction = null,
            Action<IntPtr> ignoreAction = null)
        {
            MockExplorerService explorerService = new MockExplorerService();
            ExplorerWindowInteractionService interactionService = new ExplorerWindowInteractionService(
                explorerService,
                trackingState,
                TestTabPersistenceFactory.Create(),
                delegate (IntPtr hwnd) { return string.Empty; },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate (IntPtr hwnd) { },
                delegate
                {
                    if (createNewTabBarAction != null)
                    {
                        createNewTabBarAction();
                    }
                    return null;
                },
                delegate (TabBarWindow window) { });

            return new ExplorerWindowOutcomeCoordinator(
                trackingState,
                interactionService,
                delegate (string source, string message, Exception ex) { },
                ignoreAction ?? delegate (IntPtr hwnd) { },
                delegate { return new MockUserSettings(); },
                delegate (IntPtr hwnd, TabBarWindow window) { });
        }
    }
}
