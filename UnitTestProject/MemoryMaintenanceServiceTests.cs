using System;
using KjTabBar.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class MemoryMaintenanceServiceTests
    {
        [TestMethod]
        public void ShouldRunFullGarbageCollection_Returns_False_When_Managed_Memory_Is_Low()
        {
            bool shouldRun = MemoryMaintenanceService.ShouldRunFullGarbageCollection(
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                DateTime.MinValue,
                16L * 1024L * 1024L);

            Assert.IsFalse(shouldRun);
        }

        [TestMethod]
        public void ShouldRunFullGarbageCollection_Returns_True_When_Memory_Is_High_And_No_Previous_Full_Gc()
        {
            bool shouldRun = MemoryMaintenanceService.ShouldRunFullGarbageCollection(
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                DateTime.MinValue,
                256L * 1024L * 1024L);

            Assert.IsTrue(shouldRun);
        }

        [TestMethod]
        public void ShouldRunFullGarbageCollection_Respects_Minimum_Interval()
        {
            DateTime nowUtc = new DateTime(2026, 6, 1, 0, 10, 0, DateTimeKind.Utc);
            DateTime lastFullGcUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            bool shouldRun = MemoryMaintenanceService.ShouldRunFullGarbageCollection(
                nowUtc,
                lastFullGcUtc,
                256L * 1024L * 1024L);

            Assert.IsFalse(shouldRun);
        }

        [TestMethod]
        public void ShouldRunFullGarbageCollection_Returns_True_After_Minimum_Interval()
        {
            DateTime nowUtc = new DateTime(2026, 6, 1, 0, 16, 0, DateTimeKind.Utc);
            DateTime lastFullGcUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            bool shouldRun = MemoryMaintenanceService.ShouldRunFullGarbageCollection(
                nowUtc,
                lastFullGcUtc,
                256L * 1024L * 1024L);

            Assert.IsTrue(shouldRun);
        }
    }
}