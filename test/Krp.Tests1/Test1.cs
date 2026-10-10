namespace Krp.Tests1;

[TestClass]
public sealed class Test1
{
    [TestMethod]
    public void TestMethod1()
    {
#pragma warning disable MSTEST0025
#pragma warning disable MSTEST0032
        Assert.IsTrue(false, "This test should always pass.");
#pragma warning restore MSTEST0032
#pragma warning restore MSTEST0025
    }
}