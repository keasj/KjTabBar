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
            Func<Func<ExplorerWindowEvaluationResult>, Task<ExplorerWindowEvaluationResult>> invokeComAsync)
        {
            _windowTracking = windowTracking;
            _explorerLaunchTracker = explorerLaunchTracker;
            _evaluationService = evaluationService;
            _interactionService = interactionService;
            _outcomeCoordinator = outcomeCoordinator;
            _invokeComAsync = invokeComAsync;
        }

        public async Task ProcessAsync(
            IntPtr hwnd,
            TabBarViewModel validTarget,
            Func<string, TabBarViewModel> findControlPanelTarget,
            Func<TabBarViewModel> findValidTarget,
            Func<TabBarViewModel, string, bool> hasEquivalentControlPanelTab,
            Func<TabBarViewModel, bool> hasActiveControlPanelTab,
            Func<IntPtr, bool> isManagedWindow,
            Func<IntPtr, bool> isIgnoredWindow)
        {
            try
            {
                int retryCount = 0;
                _windowTracking.AbsorbPathRetryCounts.TryGetValue(hwnd, out retryCount);

                bool isDesktopCandidate = _windowTracking.DesktopLaunchCandidates.Contains(hwnd);
                bool isDesktopInteractiveCandidate = _windowTracking.DesktopInteractiveLaunchCandidates.Contains(hwnd);
                bool isControlPanelTabLaunchCandidate = _windowTracking.ControlPanelTabLaunchCandidates.Contains(hwnd);
                bool wasManagedControlPanelLaunchSource = _explorerLaunchTracker.WasManagedControlPanelLaunchSource();
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

                ExplorerWindowEvaluationResult result = await _invokeComAsync(delegate
                {
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

                _outcomeCoordinator.ApplyOutcome(hwnd, retryCount, result, validTarget, controlPanelTarget);
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
