using Microsoft.VisualStudio.TestTools.UnitTesting;
using KjTabBar.Helpers;

namespace UnitTestProject
{
    [TestClass]
    public class ShellDragDropHelperTests
    {
        [TestMethod]
        public void TryGetPidlSizeWithinCidaBuffer_Accepts_TerminatorOnly_Pidl()
        {
            byte[] bytes = new byte[] { 0, 0 };
            int size;

            bool isValid = ShellDragDropHelper.TryGetPidlSizeWithinCidaBuffer(bytes, 0, out size);

            Assert.IsTrue(isValid);
            Assert.AreEqual(2, size);
        }

        [TestMethod]
        public void TryGetPidlSizeWithinCidaBuffer_Rejects_Item_That_Extends_Beyond_Buffer()
        {
            byte[] bytes = new byte[] { 4, 0, 1 };
            int size;

            bool isValid = ShellDragDropHelper.TryGetPidlSizeWithinCidaBuffer(bytes, 0, out size);

            Assert.IsFalse(isValid);
            Assert.AreEqual(0, size);
        }

        [TestMethod]
        public void ParseCIDA_Returns_Null_When_Parent_Offset_Points_Into_Header()
        {
            byte[] bytes = new byte[]
            {
                1, 0, 0, 0,
                0, 0, 0, 0,
                12, 0, 0, 0,
                0, 0
            };

            string[] paths = ShellDragDropHelper.ParseCIDA(bytes);

            Assert.IsNull(paths);
        }
        [TestMethod]
        public void ParseCIDA_Returns_Null_When_Child_Pidl_Extends_Beyond_Buffer()
        {
            byte[] bytes = new byte[]
            {
                1, 0, 0, 0,
                12, 0, 0, 0,
                14, 0, 0, 0,
                0, 0,
                4, 0, 1
            };

            string[] paths = ShellDragDropHelper.ParseCIDA(bytes);

            Assert.IsNull(paths);
        }
    }
}