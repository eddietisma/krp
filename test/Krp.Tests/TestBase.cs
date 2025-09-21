using AutoFixture;
using AutoFixture.AutoMoq;

namespace Krp.Tests;

public abstract class TestBase
{
    protected IFixture Fixture { get; set; }

    [TestInitialize]
    public void InitializeTest()
    {
        Fixture = new Fixture().Customize(new AutoMoqCustomization
        {
            ConfigureMembers = true,
        });

        Fixture.Behaviors.Remove(new ThrowingRecursionBehavior());
        Fixture.Behaviors.Add(new OmitOnRecursionBehavior());
    }
}