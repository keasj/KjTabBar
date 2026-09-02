using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class MemoryMaintenanceServiceTests
    {
        [TestMethod]
        public void MaintenanceInterval_Is_Three_Minutes()
        {
            Assert.AreEqual(3.0, MemoryMaintenanceService.GetMaintenanceInterval().TotalMinutes);
        }
    }
}
