using System;
using System.IO;
using System.Threading.Tasks;
using KjTabBar.Services;
using KjTabBar.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class DesktopRepeatedLaunchServiceTests
    {
        private const string Assets = @"C:\Assets";
        private static TabBarViewModel CreateTarget()
        {
            MockExplorerService explorer = new MockExplorerService();
            explorer.GetCurrentPathFunc = hwnd => Assets;
            return new TabBarViewModel((IntPtr)100, new MockUserSettings(), explorer);
        }

        private static DesktopRepeatedLaunchService CreateService(TabBarViewModel target,
            Func<IntPtr, int, IntPtr, Task<string>> resolve = null, Func<DateTime> clock = null)
        {
            return new DesktopRepeatedLaunchService(() => target, source => source == (IntPtr)10,
                hwnd => hwnd == (IntPtr)100, () => (IntPtr)100,
                resolve ?? ((source, child, host) => Task.FromResult(Assets)), clock ?? (() => DateTime.UtcNow));
        }

        [TestMethod]
        public void PcInvocationFromDifferentFolder_RestoresMaximizedAndAddsDestination()
        {
            VerifyPcRestore(false, false, 1);
        }

        [TestMethod]
        public void PcInvocationFromPcTab_RestoresMaximized()
        {
            VerifyPcRestore(true, false, 1);
        }

        [TestMethod]
        public void CancelledPcInvocation_DoesNotRestore()
        {
            VerifyPcRestore(false, true, 0);
        }

        [TestMethod]
        public void PcInvocationBeforeTabSynchronization_RestoresMaximized()
        {
            VerifyPcRestore(false, false, 1, false);
        }

        private static void VerifyPcRestore(bool alreadyOnPc, bool cancel, int expectedRestores, bool synchronizeTab = true)
        {
            const string pc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            TabBarViewModel target = CreateTarget();
            if (alreadyOnPc) target.ActiveTab.Path = pc;
            int count = target.Tabs.Count;
            int restores = 0;
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            using (DesktopRepeatedLaunchService service = new DesktopRepeatedLaunchService(
                () => target, source => true, hwnd => true, () => (IntPtr)100,
                (source, child, host) => completion.Task, () => DateTime.UtcNow,
                hwnd => true, hwnd => restores++))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                Task pending = service.OnForegroundAsync((IntPtr)100);
                // Explorer navigation can update the existing tab before resolution completes.
                if (synchronizeTab) target.ActiveTab.Path = pc;
                if (cancel) service.CancelForNewWindow();
                completion.SetResult(pc);
                pending.GetAwaiter().GetResult();
                Assert.AreEqual(expectedRestores, restores);
                Assert.AreEqual(count + (!alreadyOnPc && !cancel ? 1 : 0), target.Tabs.Count);
            }
        }

        [TestMethod]
        public void RestoreMaximizedDecision_RejectsStaleFlagOnNormalWindow()
        {
            Assert.IsTrue(DesktopRepeatedLaunchService.ShouldRestoreMaximized(new KjTabBar.Helpers.NativeMethods.WINDOWPLACEMENT { showCmd = 3 }));
            Assert.IsTrue(DesktopRepeatedLaunchService.ShouldRestoreMaximized(new KjTabBar.Helpers.NativeMethods.WINDOWPLACEMENT { showCmd = 2, flags = 2 }));
            Assert.IsFalse(DesktopRepeatedLaunchService.ShouldRestoreMaximized(new KjTabBar.Helpers.NativeMethods.WINDOWPLACEMENT { showCmd = 2, flags = 0 }));
            Assert.IsFalse(DesktopRepeatedLaunchService.ShouldRestoreMaximized(new KjTabBar.Helpers.NativeMethods.WINDOWPLACEMENT { showCmd = 1, flags = 2 }));
        }

        [TestMethod]
        public void ReusedMinimizedMaximizedHost_RestoresCapturedStateWithoutAddingTab()
        {
            VerifyCapturedRestoreState(true, false, 1);
        }

        [TestMethod]
        public void ReusedNormalHost_DoesNotForceMaximize()
        {
            VerifyCapturedRestoreState(false, false, 0);
        }

        [TestMethod]
        public void CancelledReuse_DoesNotRestoreStaleMaximizedState()
        {
            VerifyCapturedRestoreState(true, true, 0);
        }

        private static void VerifyCapturedRestoreState(bool minimizedFromMaximized, bool cancel, int expectedRestores)
        {
            TabBarViewModel target = CreateTarget();
            int count = target.Tabs.Count;
            int restores = 0;
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            using (DesktopRepeatedLaunchService service = new DesktopRepeatedLaunchService(
                () => target, source => source == (IntPtr)10, hwnd => true, () => (IntPtr)100,
                (source, child, host) => completion.Task, () => DateTime.UtcNow,
                hwnd => minimizedFromMaximized, hwnd =>
                {
                    Assert.AreEqual((IntPtr)100, hwnd);
                    Assert.AreEqual(count, target.Tabs.Count);
                    restores++;
                }))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                minimizedFromMaximized = false; // Shell has already restored this host to normal size.
                Task pending = service.OnForegroundAsync((IntPtr)100);
                if (cancel) service.CancelForNewWindow();
                completion.SetResult(Assets);
                pending.GetAwaiter().GetResult();
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                Assert.AreEqual(expectedRestores, restores);
                Assert.AreEqual(count, target.Tabs.Count);
            }
        }

        [TestMethod]
        public void ExplicitDesktopInvocation_ReusedVisibleHost_KeepsActiveTab()
        {
            TabBarViewModel target = CreateTarget();
            int count = target.Tabs.Count;
            using (DesktopRepeatedLaunchService service = CreateService(target))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                Assert.AreEqual(count, target.Tabs.Count);
                Assert.AreEqual(Assets, target.ActiveTab.Path);
            }
        }

        [TestMethod]
        public void ReusedHost_SelectsExistingSpecialTabAndPreservesOriginalFolder()
        {
            string[] paths = new string[] { "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "::{645FF040-5081-101B-9F08-00AA002F954E}",
                "::{26EE0668-A00A-44D7-9371-BEB064C98683}" };
            foreach (string path in paths)
            {
                using (TabBarViewModel target = CreateTarget())
                {
                    TabItemViewModel original = target.ActiveTab;
                    target.InsertTabWithPath(path, 1, true);
                    TabItemViewModel destination = target.ActiveTab;
                    target.SelectTab(original);
                    TaskCompletionSource<string> resolved = new TaskCompletionSource<string>();
                    using (DesktopRepeatedLaunchService service = CreateService(target, (s, c, h) => resolved.Task))
                    {
                        service.CaptureInvocation((IntPtr)10, -4, 20);
                        Task operation = service.OnForegroundAsync((IntPtr)100);
                        original.Path = path; // Shell synchronization arrived before invocation resolution.
                        resolved.SetResult(path);
                        operation.GetAwaiter().GetResult();
                        Assert.AreEqual(2, target.Tabs.Count, path);
                        Assert.AreSame(destination, target.ActiveTab, path);
                        Assert.AreEqual(Assets, original.Path, path);
                    }
                }
            }
        }

        [TestMethod]
        public void ReusedSpecialHost_KeepsActiveDuplicate()
        {
            string path = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            using (TabBarViewModel target = CreateTarget())
            {
                target.InsertTabWithPath(path, 1, true);
                target.DuplicateTab(target.ActiveTab);
                TabItemViewModel active = target.ActiveTab;
                using (DesktopRepeatedLaunchService service = CreateService(target, (s, c, h) => Task.FromResult(path)))
                {
                    service.CaptureInvocation((IntPtr)10, -4, 20);
                    service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                    Assert.AreEqual(3, target.Tabs.Count);
                    Assert.AreSame(active, target.ActiveTab);
                }
            }
        }
        [TestMethod]
        public void ForegroundWithoutInvocation_DoesNotDuplicate()
        {
            TabBarViewModel target = CreateTarget();
            int count = target.Tabs.Count;
            using (DesktopRepeatedLaunchService service = CreateService(target))
            {
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                service.CaptureInvocation((IntPtr)99, -4, 20);
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                Assert.AreEqual(count, target.Tabs.Count);
            }
        }

        [TestMethod]
        public void DifferentShortcutTarget_AddsDestinationWithoutOverwritingCurrentTab()
        {
            TabBarViewModel target = CreateTarget();
            int count = target.Tabs.Count;
            using (DesktopRepeatedLaunchService service = CreateService(target, (s, c, h) => Task.FromResult(@"C:\Other")))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                Assert.AreEqual(count + 1, target.Tabs.Count);
                Assert.AreEqual(Assets, target.Tabs[0].Path);
                Assert.AreEqual(@"C:\Other", target.ActiveTab.Path);
            }
        }

        [TestMethod]
        public void NewWindowDuringResolution_CancelsDuplicateInsertion()
        {
            TabBarViewModel target = CreateTarget();
            int count = target.Tabs.Count;
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            using (DesktopRepeatedLaunchService service = CreateService(target, (s, c, h) => completion.Task))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                Task pending = service.OnForegroundAsync((IntPtr)100);
                service.CancelForNewWindow();
                completion.SetResult(Assets);
                pending.GetAwaiter().GetResult();
                Assert.AreEqual(count, target.Tabs.Count);
            }
        }

        [TestMethod]
        public void TabChangedDuringResolution_DiscardsStaleResult()
        {
            TabBarViewModel target = CreateTarget();
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            using (DesktopRepeatedLaunchService service = CreateService(target, (s, c, h) => completion.Task))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                Task pending = service.OnForegroundAsync((IntPtr)100);
                target.AddTabWithPath(Assets);
                int count = target.Tabs.Count;
                completion.SetResult(Assets);
                pending.GetAwaiter().GetResult();
                Assert.AreEqual(count, target.Tabs.Count);
            }
        }

        [TestMethod]
        public void ExpiredInvocation_DoesNotBecomeALaterClick()
        {
            TabBarViewModel target = CreateTarget();
            int count = target.Tabs.Count;
            DateTime now = DateTime.UtcNow;
            using (DesktopRepeatedLaunchService service = CreateService(target, null, () => now))
            {
                service.CaptureInvocation((IntPtr)10, -4, 20);
                now = now.AddSeconds(3);
                service.OnForegroundAsync((IntPtr)100).GetAwaiter().GetResult();
                Assert.AreEqual(count, target.Tabs.Count);
            }
        }

        [TestMethod]
        public void ShortcutName_MustMatchExactlyAndBeUnambiguous()
        {
            string root = Path.Combine(Path.GetTempPath(), "KjTabBar-Invoke-" + Guid.NewGuid().ToString("N"));
            string user = Path.Combine(root, "user");
            string common = Path.Combine(root, "common");
            Directory.CreateDirectory(user); Directory.CreateDirectory(common);
            try
            {
                string link = Path.Combine(user, "Assets - ショートカット.lnk");
                File.WriteAllBytes(link, new byte[0]);
                string[] directories = { user, common };
                Assert.AreEqual(link, DesktopRepeatedLaunchService.FindUniqueShortcut("Assets - ショートカット", directories));
                Assert.AreEqual(link, DesktopRepeatedLaunchService.FindUniqueShortcut("Assets - ショートカット.lnk", directories));
                Assert.IsNull(DesktopRepeatedLaunchService.FindUniqueShortcut("Assets", directories));
                File.WriteAllBytes(Path.Combine(common, Path.GetFileName(link)), new byte[0]);
                Assert.IsNull(DesktopRepeatedLaunchService.FindUniqueShortcut("Assets - ショートカット", directories));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
