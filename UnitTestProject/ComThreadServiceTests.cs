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
