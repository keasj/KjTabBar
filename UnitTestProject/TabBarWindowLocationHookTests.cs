using System;
using KjTabBar.Helpers;
using KjTabBar.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class TabBarWindowLocationHookTests
    {
        [TestMethod]
        public void ShouldHandleLocationChangeEvent_ReturnsTrue_ForTrackedExplorerWindowMove()
        {
            bool result = TabBarWindow.ShouldHandleLocationChangeEvent(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                (IntPtr)10,
                0,
                (IntPtr)10);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldHandleLocationChangeEvent_ReturnsFalse_ForChildObjectMove()
        {
            bool result = TabBarWindow.ShouldHandleLocationChangeEvent(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                (IntPtr)10,
                1,
                (IntPtr)10);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ShouldHandleLocationChangeEvent_ReturnsFalse_ForDifferentExplorerWindow()
        {
            bool result = TabBarWindow.ShouldHandleLocationChangeEvent(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                (IntPtr)11,
                0,
                (IntPtr)10);

            Assert.IsFalse(result);
        }
    }
}
