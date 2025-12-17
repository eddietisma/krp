namespace Krp.Tests
{
    [TestClass]
    public sealed class Test1
    {
        [TestMethod]
        public void TestMethod1()
        {
#pragma warning disable MSTEST0032
            Assert.IsTrue(true, "This test should always pass.");
#pragma warning restore MSTEST0032
        }
    }
}
