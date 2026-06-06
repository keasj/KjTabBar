using System;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;
using KjTabBar.Views;

namespace KjTabBar.Services
{
    internal class ExplorerWindowOutcomeCoordinator
    {
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly ExplorerWindowInteractionService _interactionService;
        private readonly Action<string, string, Exception> _logError;
        private readonly Action<IntPtr> _ignoreExplorerWindow;
        private readonly Func<IUserSettings> _getUserSettings;
        private readonly Action<IntPtr, TabBarWindow> _registerTabBar;

        public ExplorerWindowOutcomeCoordinator(
            ExplorerWindowTrackingState windowTracking,
            ExplorerWindowInteractionService interactionService,
            Action<IntPtr> ignoreExplorerWindow,
            Func<IUserSettings> getUserSettings,
            Action<IntPtr, TabBarWindow> registerTabBar)
            : this(
                  windowTracking,
                  interactionService,
                  delegate (string source, string message, Exception ex) { AppLogger.LogError(source, message, ex); },
                  ignoreExplorerWindow,
                  getUserSettings,
                  registerTabBar)
        {
        }

        internal ExplorerWindowOutcomeCoordinator(
            ExplorerWindowTrackingState windowTracking,
            ExplorerWindowInteractionService interactionService,
            Action<string, string, Exception> logError,
            Action<IntPtr> ignoreExplorerWindow,
            Func<IUserSettings> getUserSettings,
            Action<IntPtr, TabBarWindow> registerTabBar)
        {
            _windowTracking = windowTracking;
            _interactionService = interactionService;
            _logError = logError;
            _ignoreExplorerWindow = ignoreExplorerWindow;
            _getUserSettings = getUserSettings;
            _registerTabBar = registerTabBar;
        }

        public virtual void ApplyOutcome(
            IntPtr hwnd,
            int retryCount,
            ExplorerWindowEvaluationResult result,
            TabBarViewModel validTarget,
            TabBarViewModel controlPanelTarget)
        {
            if (result == null)
            {
                return;
            }

            if (result.Action == AbsorptionAction.CreateNewTabBar && validTarget != null)
            {
                return;
            }

            switch (result.Action)
            {
                case AbsorptionAction.WaitAndRetryIncrement:
                    _windowTracking.AbsorbPathRetryCounts[hwnd] = retryCount + 1;
                    break;

                case AbsorptionAction.AbsorbWithFallback:
                case AbsorptionAction.Absorb:
                    _windowTracking.ClearAbsorptionState(hwnd);
                    TabBarViewModel targetToUse =
                        (result.Action == AbsorptionAction.Absorb && result.IsControlPanelPath && controlPanelTarget != null)
                        ? controlPanelTarget
                        : validTarget;
                    TryAbsorbExplorerWindow(hwnd, targetToUse, result.ResolvedPath, result.AllowSpecialPath);
                    break;

                case AbsorptionAction.CreateNewTabBar:
                    _windowTracking.ClearAbsorptionState(hwnd);
                    TryCreateNewTabBar(hwnd);
                    break;

                case AbsorptionAction.Ignore:
                    _windowTracking.ClearAbsorptionState(hwnd);
                    _ignoreExplorerWindow(hwnd);
                    break;
            }
        }

        private void TryAbsorbExplorerWindow(IntPtr hwnd, TabBarViewModel targetViewModel, string path, bool allowSpecialPath)
        {
            try
            {
                _interactionService.AbsorbExplorerWindow(hwnd, targetViewModel, path, allowSpecialPath, _ignoreExplorerWindow);
            }
            catch (Exception ex)
            {
                _logError("App", "AbsorbExplorerWindow failed.", ex);
                _interactionService.RestoreHiddenWindow(hwnd);
            }
        }

        private void TryCreateNewTabBar(IntPtr hwnd)
        {
            try
            {
                _interactionService.CreateNewTabBar(hwnd, _getUserSettings(), _registerTabBar);
            }
            catch (Exception ex)
            {
                _logError("App", "CreateNewTabBar failed.", ex);
            }
        }
    }
}
