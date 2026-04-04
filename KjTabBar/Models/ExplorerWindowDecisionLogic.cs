using System;

namespace KjTabBar.Models
{
    public static class ExplorerWindowDecisionLogic
    {
        public static bool ShouldReevaluateIgnoredWindow(bool hasValidTabBarTarget)
        {
            return !hasValidTabBarTarget;
        }

        public static bool ShouldRetryTransientDesktopPlaceholder(
            bool hasValidTabBarTarget,
            bool isDesktopInteractiveCandidate,
            string titlePath,
            bool isTransientPlaceholderPath,
            int retryCount,
            int maxRetryCount)
        {
            if (!hasValidTabBarTarget)
            {
                return false;
            }

            if (!isDesktopInteractiveCandidate)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(titlePath))
            {
                return false;
            }

            if (!isTransientPlaceholderPath)
            {
                return false;
            }

            return retryCount < maxRetryCount;
        }
    }
}
