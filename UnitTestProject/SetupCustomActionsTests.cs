using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar;

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
        public void BuildPostInstallScript_Registers_Startup_Run_Value()
        {
            string script = SetupCustomActions.BuildPostInstallScript(
                123,
                1);

            StringAssert.Contains(script, "$env:KJTB_EXE_PATH");
            StringAssert.Contains(script, "$env:KJTB_WORKING_DIRECTORY");
            StringAssert.Contains(script, @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Run");
            StringAssert.Contains(script, @"Registry::HKEY_USERS\");
            StringAssert.Contains(script, "Invoke-CimMethod");
            StringAssert.Contains(script, "GetOwnerSid");
            StringAssert.Contains(script, "Set-ItemProperty");
            StringAssert.Contains(script, "KjTabBar");
            StringAssert.Contains(script, "Write-KjtbSetupLog");
            StringAssert.Contains(script, "Shortcut update failed");
            StringAssert.Contains(script, "Shortcut COM setup failed");
            StringAssert.Contains(script, "Target SID resolution failed");
            StringAssert.Contains(script, "Startup Run registration failed");
        }

        [TestMethod]
        public void BuildPostInstallScript_Starts_App_Through_Explorer_Without_Install_Argument()
        {
            string script = SetupCustomActions.BuildPostInstallScript(
                123,
                1);

            StringAssert.Contains(script, "Start-Process -FilePath 'explorer.exe'");
            Assert.IsFalse(script.Contains("--kjtb-install-startup"));
        }

        [TestMethod]
        public void IsRegularUserSid_Returns_True_Only_For_Interactive_User_Sids()
        {
            Assert.IsTrue(SetupCustomActions.IsRegularUserSid("S-1-5-21-1000-2000-3000-4000"));
            Assert.IsFalse(SetupCustomActions.IsRegularUserSid("S-1-5-18"));
            Assert.IsFalse(SetupCustomActions.IsRegularUserSid("S-1-5-19"));
            Assert.IsFalse(SetupCustomActions.IsRegularUserSid("S-1-5-20"));
            Assert.IsFalse(SetupCustomActions.IsRegularUserSid(".DEFAULT"));
            Assert.IsFalse(SetupCustomActions.IsRegularUserSid(string.Empty));
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
    }
}
