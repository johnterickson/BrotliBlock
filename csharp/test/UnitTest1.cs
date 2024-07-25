namespace test;

using broccoli_sharp;

[TestClass]
public class UnitTest1
{
    [TestMethod]
    public void TestMethod1()
    {
        Broccoli.Concat(
            new[]
            {
                new MemoryStream(),
                new MemoryStream(),
            },
            new MemoryStream());
    }
}