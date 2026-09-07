using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using Microsoft.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Services;

namespace UnitTestProject
{
    [TestClass]
    public class ShellWorkerTests
    {
        [TestMethod]
        public void Protocol_PreservesNullEmptyAndUnicodeValues()
        {
            string[] values = { null, string.Empty, "日本語のフォルダー", "C:\\Folder" };
            using (MemoryStream stream = new MemoryStream())
            {
                ShellWorkerProtocol.Write(stream, values);
                stream.Position = 0;
                CollectionAssert.AreEqual(values, ShellWorkerProtocol.Read(stream));
            }
        }

        [TestMethod]
        public void IsolatedManager_ReadsMetadataAndAvailability()
        {
            using (KjTabBar.Models.ExplorerManager explorer = new KjTabBar.Models.ExplorerManager(true))
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                Assert.IsFalse(string.IsNullOrEmpty(explorer.GetFolderName(windows)));
                Assert.IsTrue(explorer.GetPathAvailability(new[] { windows })[windows]);
                Assert.AreEqual(windows, explorer.NormalizeKnownPath(windows));
            }
        }

        [TestMethod]
        public void Protocol_AcceptsFrameworkStandardInputPreamble()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                stream.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                ShellWorkerProtocol.Write(stream, new[] { "0" });
                stream.Position = 0;
                CollectionAssert.AreEqual(new[] { "0" }, ShellWorkerProtocol.Read(stream));
            }
        }

        [TestMethod]
        public void ProductionWorker_HandlesReadOnlyRequests_AndExitsOnDispose()
        {
            int workerId;
            using (ShellWorkerClient client = new ShellWorkerClient())
            {
                workerId = int.Parse(client.Invoke(ShellOperation.Ping)[0]);
                Assert.AreNotEqual(Process.GetCurrentProcess().Id, workerId);
                string path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                Assert.IsFalse(string.IsNullOrEmpty(client.Invoke(ShellOperation.FolderName, path)[0]));
                Assert.IsTrue(Convert.FromBase64String(client.Invoke(ShellOperation.Icon, path)[0]).Length > 0);
            }
            AssertExited(workerId);
        }

        [TestMethod]
        public void HungWorker_IsTerminated_AndNextRequestUsesNewProcess()
        {
            WithFakeWorker(delegate(ShellWorkerClient client)
            {
                int workerId = int.Parse(client.Invoke(ShellOperation.Ping)[0]);
                try
                {
                    client.Invoke(ShellOperation.Ping, "block");
                    Assert.Fail("Expected timeout.");
                }
                catch (TimeoutException) { }
                AssertExited(workerId);
                int replacementId = int.Parse(client.Invoke(ShellOperation.Ping)[0]);
                Assert.AreNotEqual(workerId, replacementId);
            });
        }

        [TestMethod]
        public void CrashedWorker_DoesNotPoisonSubsequentRequests()
        {
            WithFakeWorker(delegate(ShellWorkerClient client)
            {
                try
                {
                    client.Invoke(ShellOperation.Ping, "exit");
                    Assert.Fail("Expected pipe failure.");
                }
                catch (IOException) { }
                Assert.IsTrue(int.Parse(client.Invoke(ShellOperation.Ping)[0]) > 0);
            });
        }

        [TestMethod]
        public void OversizedResponse_IsRejected_AndWorkerReplaced()
        {
            WithFakeWorker(delegate(ShellWorkerClient client)
            {
                int workerId = int.Parse(client.Invoke(ShellOperation.Ping)[0]);
                try
                {
                    client.Invoke(ShellOperation.Ping, "oversized");
                    Assert.Fail("Expected invalid data.");
                }
                catch (InvalidDataException) { }
                AssertExited(workerId);
                Assert.AreNotEqual(workerId, int.Parse(client.Invoke(ShellOperation.Ping)[0]));
            });
        }

        private static void AssertExited(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    Assert.IsTrue(process.HasExited);
            }
            catch (ArgumentException) { }
        }

        private static void WithFakeWorker(Action<ShellWorkerClient> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "KjTabBar.WorkerTest." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string exe = Path.Combine(directory, "FakeShellWorker.exe");
            try
            {
                using (CSharpCodeProvider provider = new CSharpCodeProvider())
                {
                    CompilerParameters parameters = new CompilerParameters(new[] { "System.dll", "System.Core.dll" }, exe)
                    {
                        GenerateExecutable = true,
                        GenerateInMemory = false
                    };
                    CompilerResults results = provider.CompileAssemblyFromSource(parameters, FakeWorkerSource);
                    if (results.Errors.HasErrors) Assert.Fail(results.Errors[0].ToString());
                }
                using (ShellWorkerClient client = new ShellWorkerClient(
                    () => new ProcessStartInfo(exe, "\"" + typeof(ShellWorkerClient).Assembly.Location + "\""),
                    TimeSpan.FromSeconds(2)))
                {
                    action(client);
                }
            }
            finally
            {
                for (int retry = 0; retry < 10; retry++)
                {
                    try { Directory.Delete(directory, true); break; }
                    catch (UnauthorizedAccessException) { if (retry == 9) throw; System.Threading.Thread.Sleep(100); }
                    catch (IOException) { if (retry == 9) throw; System.Threading.Thread.Sleep(100); }
                }
            }
        }

        private const string FakeWorkerSource = @"
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
class FakeWorker
{
    static void Main(string[] args)
    {
        Type protocol = Assembly.LoadFrom(args[0]).GetType(""KjTabBar.Services.ShellWorkerProtocol"");
        MethodInfo read = protocol.GetMethod(""Read"", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo write = protocol.GetMethod(""Write"", BindingFlags.NonPublic | BindingFlags.Static);
        Stream input = Console.OpenStandardInput(), output = Console.OpenStandardOutput();
        while (true)
        {
            string[] request;
            try { request = (string[])read.Invoke(null, new object[] { input }); }
            catch { return; }
            if (request.Length > 1 && request[1] == ""block"") Thread.Sleep(Timeout.Infinite);
            if (request.Length > 1 && request[1] == ""exit"") return;
            if (request.Length > 1 && request[1] == ""oversized"")
            {
                new BinaryWriter(output).Write(int.MaxValue);
                output.Flush();
                continue;
            }
            write.Invoke(null, new object[] { output, new[] { ""ok"", Process.GetCurrentProcess().Id.ToString() } });
        }
    }
}";
    }
}
