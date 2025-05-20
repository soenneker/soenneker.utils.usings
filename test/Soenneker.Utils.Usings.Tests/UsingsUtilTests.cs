using Soenneker.Utils.Usings.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Utils.Usings.Tests;

[Collection("Collection")]
public class UsingsUtilTests : FixturedUnitTest
{
    private readonly IUsingsUtil _util;

    public UsingsUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IUsingsUtil>(true);
    }

    [Fact]
    public void Default()
    {

    }
}
