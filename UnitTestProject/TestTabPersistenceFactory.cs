using System;
using System.IO;
using KjTabBar.Models;

namespace UnitTestProject
{
    internal static class TestTabPersistenceFactory
    {
        public static TabPersistenceService Create()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "KjTabBar.Tests." + Guid.NewGuid().ToString("N") + ".tabs.txt");
            return new TabPersistenceService(path);
        }
    }
}
