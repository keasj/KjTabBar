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
                1,
                @"C:\Program Files\KjTabBar\KjTabBar.exe",
                @"C:\Program Files\KjTabBar");

            StringAssert.Contains(script, @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Run");
            StringAssert.Contains(script, @"Registry::HKEY_USERS\");
            StringAssert.Contains(script, "Invoke-CimMethod");
            StringAssert.Contains(script, "GetOwnerSid");
            StringAssert.Contains(script, "Set-ItemProperty");
            StringAssert.Contains(script, "KjTabBar");
        }

        [TestMethod]
        public void BuildPostInstallScript_Starts_App_Through_Explorer_Without_Install_Argument()
        {
            string script = SetupCustomActions.BuildPostInstallScript(
                123,
                1,
                @"C:\Program Files\KjTabBar\KjTabBar.exe",
                @"C:\Program Files\KjTabBar");

            StringAssert.Contains(script, "Start-Process -FilePath 'explorer.exe'");
            Assert.IsFalse(script.Contains("--kjtb-install-startup"));
        }
    }
}
