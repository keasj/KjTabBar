using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerWindowDecisionLogicTests
    {
        [TestMethod]
        public void ShouldReevaluateIgnoredWindow_Returns_True_Only_Without_Target()
        {
            Assert.IsTrue(ExplorerWindowDecisionLogic.ShouldReevaluateIgnoredWindow(false));
            Assert.IsFalse(ExplorerWindowDecisionLogic.ShouldReevaluateIgnoredWindow(true));
        }

        [TestMethod]
        public void ShouldRetryTransientDesktopPlaceholder_Returns_True_For_Interactive_Transient_Path()
        {
            bool result = ExplorerWindowDecisionLogic.ShouldRetryTransientDesktopPlaceholder(
                true,
                true,
                null,
                true,
                0,
                8);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldRetryTransientDesktopPlaceholder_Returns_False_When_Title_Is_Resolved()
        {
            bool result = ExplorerWindowDecisionLogic.ShouldRetryTransientDesktopPlaceholder(
                true,
                true,
                "Home",
                true,
                0,
                8);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldRetryTransientDesktopPlaceholder_Returns_False_Without_Interactive_Signal()
        {
            bool result = ExplorerWindowDecisionLogic.ShouldRetryTransientDesktopPlaceholder(
                true,
                false,
                null,
                true,
                0,
                8);

            Assert.IsFalse(result);
        }
    }
}
