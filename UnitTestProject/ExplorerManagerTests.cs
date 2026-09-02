using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Models;
using System;

namespace UnitTestProject
{
    [TestClass]
    public class ExplorerManagerTests
    {
        [TestMethod]
        public void NormalizeKnownPath_Preserves_Home_As_Special_Path()
        {
            ExplorerManager manager = new ExplorerManager();

            string normalized = manager.NormalizeKnownPath("shell:Home");

            Assert.AreEqual(manager.HomeFolderPath, normalized);
        }

        [TestMethod]
        public void MapLocationNameToKnownShellPath_Preserves_Home_As_Special_Path()
        {
            ExplorerManager manager = new ExplorerManager();

            string mapped = manager.MapLocationNameToKnownShellPath("Home");

            Assert.AreEqual(manager.HomeFolderPath, mapped);
        }

        [TestMethod]
        public void GetExternalExplorerLaunchPath_Normalizes_ControlPanel_Item()
        {
            ExplorerManager manager = new ExplorerManager();

            string launchPath = manager.GetExternalExplorerLaunchPath(manager.ProgramsAndFeaturesPath);

            Assert.AreEqual("::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\" + manager.ProgramsAndFeaturesPath, launchPath);
        }

        [TestMethod]
        public void ShouldLaunchPlainExplorerForNewWindow_ReturnsTrue_ForControlPanelRoot()
        {
            ExplorerManager manager = new ExplorerManager();

            bool result = manager.ShouldLaunchPlainExplorerForNewWindow(manager.AllControlPanelPath);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ShouldLaunchPlainExplorerForNewWindow_ReturnsFalse_ForControlPanelItem()
        {
            ExplorerManager manager = new ExplorerManager();

            bool result = manager.ShouldLaunchPlainExplorerForNewWindow(manager.ProgramsAndFeaturesPath);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void GetNewWindowNavigationPath_ReturnsShellControlPanelFolder_ForControlPanelRoot()
        {
            ExplorerManager manager = new ExplorerManager();

            string path = manager.GetNewWindowNavigationPath(manager.AllControlPanelPath);

            Assert.AreEqual("shell:ControlPanelFolder", path);
        }

        [TestMethod]
        public void GetNewWindowNavigationPath_ReturnsOriginalPath_ForControlPanelItem()
        {
            ExplorerManager manager = new ExplorerManager();

            string path = manager.GetNewWindowNavigationPath(manager.ProgramsAndFeaturesPath);

            Assert.AreEqual(manager.ProgramsAndFeaturesPath, path);
        }

        [TestMethod]
        public void IsTabPathCurrentlyAvailable_Allows_Unc_Path()
        {
            ShellPathAvailabilityEvaluator evaluator = new ShellPathAvailabilityEvaluator(
                delegate (string path) { return path; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            bool available = evaluator.IsTabPathCurrentlyAvailable(@"\\server\share\folder");

            Assert.IsTrue(available);
        }

        [TestMethod]
        public void IsNavigablePath_Allows_Unc_Path_Without_Existence_Check()
        {
            ShellPathAvailabilityEvaluator evaluator = new ShellPathAvailabilityEvaluator(
                delegate (string path) { return path; },
                delegate (string path) { return false; },
                delegate (string path) { return false; });

            bool navigable = evaluator.IsNavigablePath(@"\\server\share\folder");

            Assert.IsTrue(navigable);
        }

        [TestMethod]
        public void MatchesTargetWindow_Returns_True_For_Root_Ancestor()
        {
            ShellExplorerWindowMatcher matcher = new ShellExplorerWindowMatcher(
                delegate (object obj, string propertyName) { return null; },
                delegate (System.IntPtr hwnd, uint flags) { return (System.IntPtr)10; });

            bool matched = matcher.MatchesTargetWindow((System.IntPtr)20, (System.IntPtr)10);

            Assert.IsTrue(matched);
        }

        [TestMethod]
        public void TryGetWindowHwnd_Returns_False_For_Invalid_Com_Value()
        {
            ShellExplorerWindowMatcher matcher = new ShellExplorerWindowMatcher(
                delegate (object obj, string propertyName) { return "not-a-number"; },
                delegate (System.IntPtr hwnd, uint flags) { return System.IntPtr.Zero; });

            System.IntPtr actualHwnd;
            bool result = matcher.TryGetWindowHwnd(new object(), out actualHwnd);

            Assert.IsFalse(result);
            Assert.AreEqual(System.IntPtr.Zero, actualHwnd);
        }

        [TestMethod]
        public void FindFolderItemByPath_Falls_Back_To_ParseName()
        {
            string parseNameInvoked = null;
            ShellFolderItemSelectionHelper helper = new ShellFolderItemSelectionHelper(
                delegate (object obj, string propertyName) { return null; },
                delegate (object obj, string methodName, object[] args)
                {
                    if (methodName == "Item")
                    {
                        return null;
                    }

                    if (methodName == "ParseName")
                    {
                        parseNameInvoked = args[0] as string;
                        return "parsed-item";
                    }

                    return null;
                },
                delegate (object obj) { },
                new ShellItemPathResolver(delegate (string path) { return path; }),
                delegate (string source, string key, string message, System.Exception ex, System.TimeSpan interval) { });

            object result = helper.FindFolderItemByPath(new object(), new object(), 0, @"C:\Work\Item.txt");

            Assert.AreEqual("parsed-item", result);
            Assert.AreEqual("Item.txt", parseNameInvoked);
        }

        [TestMethod]
        public void GetComCollectionCount_Returns_Zero_On_Invalid_Value()
        {
            ShellFolderItemSelectionHelper helper = new ShellFolderItemSelectionHelper(
                delegate (object obj, string propertyName) { return "bad"; },
                delegate (object obj, string methodName, object[] args) { return null; },
                delegate (object obj) { },
                new ShellItemPathResolver(delegate (string path) { return path; }),
                delegate (string source, string key, string message, System.Exception ex, System.TimeSpan interval) { });

            int count = helper.GetComCollectionCount(new object());

            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void ReadSelectedItemPaths_Returns_Item_Paths()
        {
            ShellSelectedItemsReader reader = new ShellSelectedItemsReader(
                delegate (object obj, string propertyName)
                {
                    if (propertyName == "Count" && obj is FakeSelectedItems)
                    {
                        return 2;
                    }

                    if (propertyName == "Path")
                    {
                        return obj as string;
                    }

                    return null;
                },
                delegate (object obj, string methodName, object[] args)
                {
                    if (methodName == "SelectedItems")
                    {
                        return new FakeSelectedItems();
                    }

                    if (methodName == "Item")
                    {
                        return ((int)args[0] == 0) ? @"C:\A" : @"C:\B";
                    }

                    return null;
                },
                delegate (object obj) { },
                delegate (string source, string key, string message, System.Exception ex, System.TimeSpan interval) { });

            System.Collections.Generic.List<string> paths = reader.ReadSelectedItemPaths(new object());

            CollectionAssert.AreEqual(new string[] { @"C:\A", @"C:\B" }, paths);
        }

        [TestMethod]
        public void ReadFolderPath_Trims_Trailing_Null()
        {
            ShellFolderPathReader reader = new ShellFolderPathReader(
                delegate (object obj, string propertyName)
                {
                    if (propertyName == "Document" && obj is FakeWindow)
                    {
                        return new FakeDocument();
                    }
                    if (propertyName == "Folder" && obj is FakeDocument)
                    {
                        return new FakeFolder();
                    }
                    if (propertyName == "Self" && obj is FakeFolder)
                    {
                        return new FakeFolderSelf();
                    }
                    if (propertyName == "Path" && obj is FakeFolderSelf)
                    {
                        return "C:\\Work\\Folder\0";
                    }

                    return null;
                },
                delegate (object obj) { });

            string path = reader.ReadFolderPath(new FakeWindow());

            Assert.AreEqual(@"C:\Work\Folder", path);
        }

        [TestMethod]
        public void Resolve_Prefers_SpecialVirtualFolder_From_LocationName()
        {
            ShellCurrentPathResolver resolver = new ShellCurrentPathResolver(
                delegate (string locationName)
                {
                    return locationName == "ホーム" ? "::{679F85CB-0220-4080-B29B-5540CC05AAB6}" : null;
                },
                delegate (string path) { return false; },
                delegate (string path) { return path == "::{21EC2020-3AEA-1069-A2DD-08002B30309D}"; },
                delegate (string path) { return null; },
                delegate (string path) { return false; });

            string resolved = resolver.Resolve("ignored", "ホーム", @"C:\Users\TestUser");

            Assert.AreEqual("::{679F85CB-0220-4080-B29B-5540CC05AAB6}", resolved);
        }

        [TestMethod]
        public void Resolve_Prefers_ControlPanelRoot_From_LocationName()
        {
            ShellCurrentPathResolver resolver = new ShellCurrentPathResolver(
                delegate (string locationName)
                {
                    return locationName == "Control Panel" ? "::{21EC2020-3AEA-1069-A2DD-08002B30309D}" : null;
                },
                delegate (string path) { return false; },
                delegate (string path) { return path == "::{21EC2020-3AEA-1069-A2DD-08002B30309D}"; },
                delegate (string path) { return null; },
                delegate (string path) { return false; });

            string resolved = resolver.Resolve("ignored", "Control Panel", @"C:\OldItem");

            Assert.AreEqual("::{21EC2020-3AEA-1069-A2DD-08002B30309D}", resolved);
        }

        [TestMethod]
        public void Resolve_Prefers_ControlPanelItem_From_FolderPath_When_LocationName_Is_ControlPanelRoot()
        {
            ShellCurrentPathResolver resolver = new ShellCurrentPathResolver(
                delegate (string locationName)
                {
                    return locationName == "Control Panel" ? "::{21EC2020-3AEA-1069-A2DD-08002B30309D}" : null;
                },
                delegate (string path)
                {
                    return path == "::{21EC2020-3AEA-1069-A2DD-08002B30309D}" ||
                           path == "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}";
                },
                delegate (string path) { return path == "::{21EC2020-3AEA-1069-A2DD-08002B30309D}"; },
                delegate (string path)
                {
                    return path == @"::{26EE0668-A00A-44D7-9371-BEB064C98683}\0\::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}"
                        ? "::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}"
                        : null;
                },
                delegate (string path) { return false; });

            string resolved = resolver.Resolve(
                "ignored",
                "Control Panel",
                @"::{26EE0668-A00A-44D7-9371-BEB064C98683}\0\::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}");

            Assert.AreEqual("::{025A5937-A6BE-4686-A844-36FE4BEC8B6D}", resolved);
        }

        [TestMethod]
        public void Resolve_Falls_Back_To_FolderPath_When_No_Other_Path_Is_Resolved()
        {
            ShellCurrentPathResolver resolver = new ShellCurrentPathResolver(
                delegate (string locationName) { return null; },
                delegate (string path) { return false; },
                delegate (string path) { return false; },
                delegate (string path) { return null; },
                delegate (string path) { return false; });

            string resolved = resolver.Resolve(null, null, @"::{SomeVirtualPath}");

            Assert.AreEqual(@"::{SomeVirtualPath}", resolved);
        }

        [TestMethod]
        public void Navigate_Uses_Fallback_Navigate_When_ParseDisplayName_Fails()
        {
            string invokedMethod = null;
            object[] invokedArgs = null;
            ShellWindowNavigator navigator = new ShellWindowNavigator(
                delegate (object obj, string methodName, object[] args)
                {
                    invokedMethod = methodName;
                    invokedArgs = args;
                    return null;
                },
                delegate (object obj, string methodName, object[] args)
                {
                    invokedMethod = methodName;
                    invokedArgs = args;
                },
                delegate (string path) { return Tuple.Create(1, IntPtr.Zero); },
                delegate (IntPtr pidl) { return 0u; },
                delegate (IntPtr pidl) { });

            bool navigated = navigator.Navigate(new object(), "::{TestPath}");

            Assert.IsTrue(navigated);
            Assert.AreEqual("Navigate", invokedMethod);
            Assert.AreEqual("::{TestPath}", invokedArgs[0]);
        }

        [TestMethod]
        public void Navigate_Uses_Navigate2_For_Clsid_Path_When_ParseDisplayName_Succeeds()
        {
            string invokedMethod = null;
            object[] invokedArgs = null;
            IntPtr pidl = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(4);
            try
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(pidl, 0);
                ShellWindowNavigator navigator = new ShellWindowNavigator(
                    delegate (object obj, string methodName, object[] args)
                    {
                        invokedMethod = methodName;
                        invokedArgs = args;
                        return null;
                    },
                    delegate (object obj, string methodName, object[] args)
                    {
                        invokedMethod = methodName;
                        invokedArgs = args;
                    },
                    delegate (string path) { return Tuple.Create(0, pidl); },
                    delegate (IntPtr currentPidl) { return 4u; },
                    delegate (IntPtr currentPidl) { });

                bool navigated = navigator.Navigate(new object(), "::{TestPath}");

                Assert.IsTrue(navigated);
                Assert.AreEqual("Navigate2", invokedMethod);
                Assert.IsInstanceOfType(invokedArgs[0], typeof(byte[]));
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pidl);
            }
        }

        [TestMethod]
        public void Navigate_Returns_False_When_Normal_Path_Invocation_Fails()
        {
            ShellWindowNavigator navigator = new ShellWindowNavigator(
                delegate { throw new InvalidOperationException("Navigate failed"); },
                delegate { });

            bool navigated = navigator.Navigate(new object(), @"C:\Folder");

            Assert.IsFalse(navigated);
        }

        [TestMethod]
        public void ReadTitle_Returns_Title_From_ShellNamespace()
        {
            ShellNamespaceTitleReader reader = new ShellNamespaceTitleReader(
                delegate (object obj, string methodName, object[] args)
                {
                    return methodName == "NameSpace" ? new FakeNamespace() : null;
                },
                delegate (object obj, string propertyName)
                {
                    if (propertyName == "Title" && obj is FakeNamespace)
                    {
                        return "Control Panel";
                    }

                    return null;
                },
                delegate (object obj) { });

            string title = reader.ReadTitle(new object(), "shell:ControlPanelFolder");

            Assert.AreEqual("Control Panel", title);
        }

        [TestMethod]
        public void ReadTitle_Returns_Self_Name_When_ParentFolder_Title_Is_Empty()
        {
            ShellParentFolderTitleReader reader = new ShellParentFolderTitleReader(
                delegate (object obj, string methodName, object[] args)
                {
                    return methodName == "NameSpace" ? new FakeFolder() : null;
                },
                delegate (object obj, string propertyName)
                {
                    if (propertyName == "ParentFolder" && obj is FakeFolder)
                    {
                        return new FakeParentFolder();
                    }
                    if (propertyName == "Title" && obj is FakeParentFolder)
                    {
                        return null;
                    }
                    if (propertyName == "Self" && obj is FakeParentFolder)
                    {
                        return new FakeParentItem();
                    }
                    if (propertyName == "Name" && obj is FakeParentItem)
                    {
                        return "Parent";
                    }

                    return null;
                },
                delegate (object obj) { });

            string title = reader.ReadTitle(new object(), "C:\\Work");

            Assert.AreEqual("Parent", title);
        }

        private sealed class FakeSelectedItems
        {
        }

        private sealed class FakeNamespace
        {
        }

        private sealed class FakeWindow
        {
        }

        private sealed class FakeDocument
        {
        }

        private sealed class FakeFolder
        {
        }

        private sealed class FakeParentFolder
        {
        }

        private sealed class FakeParentItem
        {
        }

        private sealed class FakeFolderSelf
        {
        }
    }
}
