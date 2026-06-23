using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace UnitTestProject
{
    [TestClass]
    public class SetupCustomActionsTests
    {
        [TestMethod]
        public void BuildStartupRunCommand_Quotes_ExecutablePath()
        {
            string command = SetupCustomActions.BuildStartupRunCommand(@"C:\Program Files\KjTabBar\KjTabBar.exe");

            Assert.AreEqual("\"C:\\Program Files\\KjTabBar\\KjTabBar.exe\"", command);
        }

        [TestMethod]
        public void BuildStartupRunCommand_Returns_Null_For_Empty_Path()
        {
            string command = SetupCustomActions.BuildStartupRunCommand(string.Empty);

            Assert.IsNull(command);
        }

        [TestMethod]
        public void BuildPostInstallHelperArguments_Uses_Helper_Mode()
        {
            string arguments = SetupCustomActions.BuildPostInstallHelperArguments(
                123,
                1);

            StringAssert.Contains(arguments, SetupCustomActions.PostInstallHelperArgument);
            StringAssert.Contains(arguments, "123");
            StringAssert.Contains(arguments, "1");
            Assert.IsFalse(arguments.Contains("powershell"));
            Assert.IsFalse(arguments.Contains("EncodedCommand"));
        }

        [TestMethod]
        public void IsPostInstallHelperRequest_Returns_True_For_Helper_Argument()
        {
            Assert.IsTrue(SetupCustomActions.IsPostInstallHelperRequest(new string[] { SetupCustomActions.PostInstallHelperArgument, "123", "1" }));
            Assert.IsFalse(SetupCustomActions.IsPostInstallHelperRequest(new string[] { "--other" }));
            Assert.IsFalse(SetupCustomActions.IsPostInstallHelperRequest(null));
        }

        [TestMethod]
        public void IsPostInstallHelperEnvironmentTrusted_Returns_False_For_Unrelated_Path()
        {
            Assert.IsFalse(SetupCustomActions.IsPostInstallHelperEnvironmentTrusted(
                @"C:\Temp\Other.exe",
                @"C:\Temp"));
        }

        [TestMethod]
        public void IsRegularUserSid_Returns_True_Only_For_Interactive_User_Sids()
        {
            Assert.IsTrue(SetupEnvironmentResolver.IsRegularUserSid("S-1-5-21-1000-2000-3000-4000"));
            Assert.IsFalse(SetupEnvironmentResolver.IsRegularUserSid("S-1-5-18"));
            Assert.IsFalse(SetupEnvironmentResolver.IsRegularUserSid("S-1-5-19"));
            Assert.IsFalse(SetupEnvironmentResolver.IsRegularUserSid("S-1-5-20"));
            Assert.IsFalse(SetupEnvironmentResolver.IsRegularUserSid(".DEFAULT"));
            Assert.IsFalse(SetupEnvironmentResolver.IsRegularUserSid(string.Empty));
        }

        [TestMethod]
        public void IsInstalledExecutablePathMatch_Compares_Normalized_Paths_Case_Insensitively()
        {
            Assert.IsTrue(SetupCustomActions.IsInstalledExecutablePathMatch(
                @"C:\Program Files\KjTabBar\KjTabBar.exe",
                @"c:\program files\KjTabBar\.\KjTabBar.exe"));

            Assert.IsFalse(SetupCustomActions.IsInstalledExecutablePathMatch(
                @"C:\Program Files\KjTabBar\KjTabBar.exe",
                @"C:\Other\KjTabBar.exe"));

            Assert.IsFalse(SetupCustomActions.IsInstalledExecutablePathMatch(
                @"C:\Program Files\KjTabBar\KjTabBar.exe",
                null));
        }

        [TestMethod]
        public void TryResolveInstalledExecutablePath_Prefers_TargetDir_When_Provided()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "KjTabBar_SetupTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string exePath = Path.Combine(tempDir, "KjTabBar.exe");
                File.WriteAllText(exePath, string.Empty);

                string resolvedExePath;
                string resolvedTargetDir;
                bool resolved = SetupCustomActions.TryResolveInstalledExecutablePath(tempDir + "\\", @"C:\Temp\ShadowCopy\KjTabBar.exe", out resolvedExePath, out resolvedTargetDir);

                Assert.IsTrue(resolved);
                Assert.AreEqual(exePath, resolvedExePath);
                Assert.AreEqual(tempDir, resolvedTargetDir);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        public void TryResolveInstalledExecutablePath_Falls_Back_To_Assembly_Path_When_TargetDir_Is_Missing()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "KjTabBar_SetupTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string assemblyPath = Path.Combine(tempDir, "KjTabBar.exe");
                File.WriteAllText(assemblyPath, string.Empty);

                string resolvedExePath;
                string resolvedTargetDir;
                bool resolved = SetupCustomActions.TryResolveInstalledExecutablePath(null, assemblyPath, out resolvedExePath, out resolvedTargetDir);

                Assert.IsTrue(resolved);
                Assert.AreEqual(assemblyPath, resolvedExePath);
                Assert.AreEqual(tempDir, resolvedTargetDir);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }


        [TestMethod]
        public void GetProcessOwnerSid_Returns_Current_Process_User_Sid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                string sid = SetupEnvironmentResolver.GetProcessOwnerSid(Process.GetCurrentProcess().Id);

                Assert.IsNotNull(identity.User);
                Assert.AreEqual(identity.User.Value, sid);
            }
        }
    }
}
