using System;
using System.Threading.Tasks;
using KjTabBar.Helpers;
using KjTabBar.Models;
using KjTabBar.ViewModels;

namespace KjTabBar.Services
{
    internal sealed class ExplorerWindowProcessingCoordinator
    {
        private readonly ExplorerWindowTrackingState _windowTracking;
        private readonly ExplorerLaunchTracker _explorerLaunchTracker;
        private readonly ExplorerWindowEvaluationService _evaluationService;
        private readonly ExplorerWindowInteractionService _interactionService;
        private readonly ExplorerWindowOutcomeCoordinator _outcomeCoordinator;
        private readonly Func<Func<ExplorerWindowEvaluationResult>, Task<ExplorerWindowEvaluationResult>> _invokeComAsync;
        private readonly Func<int, Task> _delayAsync;
        private readonly Func<IntPtr, bool> _isWindow;
        internal Func<TabBarViewModel, ExplorerHostSwitchCoordinator> FindHostSwitchCoordinator { get; set; }

        public ExplorerWindowProcessingCoordinator(
            ExplorerWindowTrackingState windowTracking,
            ExplorerLaunchTracker explorerLaunchTracker,
            ExplorerWindowEvaluationService evaluationService,
            ExplorerWindowInteractionService interactionService,
            ExplorerWindowOutcomeCoordinator outcomeCoordinator)
            : this(
                  windowTracking,
                  explorerLaunchTracker,
                  evaluationService,
                  interactionService,
                  outcomeCoordinator,
                  delegate (Func<ExplorerWindowEvaluationResult> callback)
                  {
                      return Services.ComThreadService.Instance.InvokeAsync(callback);
                  })
        {
        }

        internal ExplorerWindowProcessingCoordinator(
            ExplorerWindowTrackingState windowTracking,
            ExplorerLaunchTracker explorerLaunchTracker,
            ExplorerWindowEvaluationService evaluationService,
            ExplorerWindowInteractionService interactionService,
            ExplorerWindowOutcomeCoordinator outcomeCoordinator,
            Func<Func<ExplorerWindowEvaluationResult>, Task<ExplorerWindowEvaluationResult>> invokeComAsync,
            Func<int, Task> delayAsync = null,
            Func<IntPtr, bool> isWindow = null)
        {
            _windowTracking = windowTracking;
            _explorerLaunchTracker = explorerLaunchTracker;
            _evaluationService = evaluationService;
            _interactionService = interactionService;
            _outcomeCoordinator = outcomeCoordinator;
            _invokeComAsync = invokeComAsync;
            _delayAsync = delayAsync ?? (milliseconds => Task.Delay(milliseconds));
            _isWindow = isWindow ?? NativeMethods.IsWindow;
        }

        internal async Task ApplyOutcomeAsync(IntPtr hwnd, int retryCount,
            ExplorerWindowEvaluationResult result, TabBarViewModel validTarget, TabBarViewModel controlPanelTarget)
        {
            ExplorerHostSwitchCoordinator hostSwitch = null;
            bool normalAbsorption = result != null && !result.IsControlPanelPath &&
                (result.Action == AbsorptionAction.Absorb || result.Action == AbsorptionAction.AbsorbWithFallback);
            if (normalAbsorption && validTarget != null && FindHostSwitchCoordinator != null)
            {
                hostSwitch = FindHostSwitchCoordinator(validTarget);
            }

            bool reserved = false;
            try
            {
                if (normalAbsorption && validTarget != null)
                {
                    reserved = validTarget.TryBeginExternalTabOperation();
                    if (!reserved) { _interactionService.RestoreUnabsorbedWindow(hwnd); return; }
                }
                long version = validTarget != null ? validTarget.SynchronizationVersion : 0;
                if (hostSwitch != null && !await hostSwitch.PrepareForPathAsync(validTarget, result.ResolvedPath,
                    () => validTarget.IsExternalOperationCurrent(version)))
                {
                    _interactionService.RestoreUnabsorbedWindow(hwnd);
                    return;
                }

                if (hostSwitch != null && (!_isWindow(hwnd) || !_isWindow(validTarget.ExplorerHwnd)))
                {
                    _interactionService.RestoreUnabsorbedWindow(hwnd);
                    return;
                }

                await _outcomeCoordinator.ApplyOutcomeAsync(hwnd, retryCount, result, validTarget, controlPanelTarget, reserved);
            }
            catch
            {
                if (hostSwitch != null) _interactionService.RestoreUnabsorbedWindow(hwnd);
                throw;
            }
            finally
            {
                try { if (reserved && hostSwitch != null) hostSwitch.CompletePendingReveal(); }
                finally { if (reserved) validTarget.EndExternalTabOperation(); }
            }
        }

        public async Task ProcessAsync(
            IntPtr hwnd,
            TabBarViewModel validTarget,
            Func<string, TabBarViewModel> findControlPanelTarget,
            Func<TabBarViewModel> findValidTarget,
            Func<TabBarViewModel, string, bool> hasEquivalentControlPanelTab,
            Func<TabBarViewModel, bool> hasActiveControlPanelTab,
            Func<IntPtr, bool> isManagedWindow,
            Func<IntPtr, bool> isIgnoredWindow, bool allowEarlyRetry = true)
        {
            try
            {
                int retryCount = 0;
                _windowTracking.AbsorbPathRetryCounts.TryGetValue(hwnd, out retryCount);

                bool isDesktopCandidate = _windowTracking.DesktopLaunchCandidates.Contains(hwnd);
                bool isDesktopInteractiveCandidate = _windowTracking.DesktopInteractiveLaunchCandidates.Contains(hwnd);
                bool isControlPanelTabLaunchCandidate = _windowTracking.ControlPanelTabLaunchCandidates.Contains(hwnd);
                bool wasManagedControlPanelLaunchSource =
                    _windowTracking.ManagedControlPanelLaunchWindows.Contains(hwnd) ||
                    _explorerLaunchTracker.WasManagedControlPanelLaunchSource();
                if (wasManagedControlPanelLaunchSource)
                {
                    _windowTracking.ManagedControlPanelLaunchWindows.Add(hwnd);
                }
                bool isValidTargetForegroundRelated =
                    validTarget != null &&
                    (_explorerLaunchTracker.IsForegroundRelatedWindow(validTarget.ExplorerHwnd) ||
                     _explorerLaunchTracker.WasForegroundRelatedWindow(validTarget.ExplorerHwnd));
                bool hasActiveControlPanelTabOnValidTarget =
                    isValidTargetForegroundRelated &&
                    hasActiveControlPanelTab != null &&
                    hasActiveControlPanelTab(validTarget);

                if (!isDesktopCandidate && _explorerLaunchTracker.TryRegisterDesktopLaunchCandidate(hwnd))
                {
                    isDesktopCandidate = true;
                    isDesktopInteractiveCandidate = _windowTracking.DesktopInteractiveLaunchCandidates.Contains(hwnd);
                }

                bool isHiddenPending = _windowTracking.HiddenPendingAbsorb.ContainsKey(hwnd);

                System.Diagnostics.Stopwatch evaluationTimer = AppLogger.StartDiagnosticTiming();
                ExplorerWindowEvaluationResult result = await _invokeComAsync(delegate
                {
                    AppLogger.LogDiagnosticTiming("Evaluation.QueueWait", hwnd, evaluationTimer);
                    ExplorerWindowEvaluationInput input = new ExplorerWindowEvaluationInput
                    {
                        ExplorerHwnd = hwnd,
                        RetryCount = retryCount,
                        IsDesktopCandidate = isDesktopCandidate,
                        IsDesktopInteractiveCandidate = isDesktopInteractiveCandidate,
                        IsHiddenPending = isHiddenPending,
                        IsControlPanelTabLaunchCandidate = isControlPanelTabLaunchCandidate,
                        WasManagedControlPanelLaunchSource = wasManagedControlPanelLaunchSource,
                        HasActiveControlPanelTabOnValidTarget = hasActiveControlPanelTabOnValidTarget,
                        HasValidTarget = validTarget != null
                    };

                    return _evaluationService.Evaluate(
                        input,
                        _interactionService.GetDesktopVirtualPathFromWindowTitle,
                        delegate (string path)
                        {
                            return findControlPanelTarget != null && findControlPanelTarget(path) != null;
                        },
                        delegate (string path)
                        {
                            if (findControlPanelTarget == null || hasEquivalentControlPanelTab == null)
                            {
                                return false;
                            }

                            TabBarViewModel target = findControlPanelTarget(path);
                            return target != null && hasEquivalentControlPanelTab(target, path);
                        },
                        delegate (string path)
                        {
                            if (findControlPanelTarget == null || hasActiveControlPanelTab == null || string.IsNullOrEmpty(path))
                            {
                                return false;
                            }

                            TabBarViewModel target = findControlPanelTarget(path);
                            if (target == null)
                            {
                                return false;
                            }

                            return hasActiveControlPanelTab(target);
                        });
                });

                AppLogger.LogDiagnosticTiming("Evaluation.Completed", hwnd, evaluationTimer);
                if (isManagedWindow != null && isManagedWindow(hwnd))
                {
                    return;
                }

                if (isIgnoredWindow != null && isIgnoredWindow(hwnd))
                {
                    return;
                }

                TabBarViewModel latestValidTarget = findValidTarget != null ? findValidTarget() : null;
                if (latestValidTarget != null)
                {
                    validTarget = latestValidTarget;
                }

                TabBarViewModel controlPanelTarget = null;
                if (result.IsControlPanelPath && findControlPanelTarget != null)
                {
                    controlPanelTarget = findControlPanelTarget(result.ResolvedPath);
                }

                await ApplyOutcomeAsync(hwnd, retryCount, result, validTarget, controlPanelTarget);

                // A newly shown first host may not be registered with Shell yet.
                // Keep the HWND in ProcessingExplorerWindows during one bounded early retry.
                if (allowEarlyRetry && retryCount == 0 && validTarget == null &&
                    result.Action == AbsorptionAction.WaitAndRetryIncrement &&
                    string.IsNullOrEmpty(result.ResolvedPath))
                {
                    await _delayAsync(100);
                    if (_isWindow(hwnd) &&
                        (findValidTarget == null || findValidTarget() == null) &&
                        (isManagedWindow == null || !isManagedWindow(hwnd)) &&
                        (isIgnoredWindow == null || !isIgnoredWindow(hwnd)))
                    {
                        AppLogger.LogDiagnostic("ReopenDecision", "EarlyRetry hwnd=" + hwnd);
                        await ProcessAsync(hwnd, null, findControlPanelTarget, findValidTarget,
                            hasEquivalentControlPanelTab, hasActiveControlPanelTab,
                            isManagedWindow, isIgnoredWindow, false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("App", "ProcessNewExplorerWindowAsync failed.", ex);
            }
            finally
            {
                _windowTracking.ProcessingExplorerWindows.Remove(hwnd);
            }
        }
    }
}
