using System;
using System.Threading;
using System.Threading.Tasks;
using KjTabBar.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject
{
    [TestClass]
    public class ComThreadServiceTests
    {
        [TestMethod]
        public void InvokeAsync_Returns_Result_From_STA_Worker()
        {
            using (ComThreadService service = new ComThreadService(4, TimeSpan.FromSeconds(1)))
            {
                int result = service.InvokeAsync(delegate { return 42; }).GetAwaiter().GetResult();

                Assert.AreEqual(42, result);
            }
        }

        [TestMethod]
        public void InvokeAsync_CanceledQueuedWorkIsSkipped()
        {
            using (ComThreadService service = new ComThreadService(4, TimeSpan.FromSeconds(5)))
            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (ManualResetEventSlim release = new ManualResetEventSlim())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Task first = service.InvokeAsync(() => { entered.Set(); release.Wait(); });
                bool called = false;
                try
                {
                    Assert.IsTrue(entered.Wait(1000));
                    Task<bool> queued = service.InvokeAsync(() => { called = true; return true; }, cancellation.Token);
                    cancellation.Cancel();
                    try
                    {
                        queued.GetAwaiter().GetResult();
                        Assert.Fail("Expected cancellation before the first operation finishes.");
                    }
                    catch (OperationCanceledException) { }
                }
                finally { release.Set(); first.GetAwaiter().GetResult(); }
                service.InvokeAsync(() => true).GetAwaiter().GetResult();
                Assert.IsFalse(called);
            }
        }

        [TestMethod]
        public void InvokeAsync_Times_Out_When_Worker_Action_Does_Not_Return()
        {
            using (ComThreadService service = new ComThreadService(4, TimeSpan.FromMilliseconds(30)))
            {
                Task task = service.InvokeAsync(delegate { Thread.Sleep(250); });

                try
                {
                    task.GetAwaiter().GetResult();
                    Assert.Fail("Expected a TimeoutException.");
                }
                catch (TimeoutException)
                {
                }
            }
        }
    }
}
