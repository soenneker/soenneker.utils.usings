using Soenneker.Utils.Usings.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;
using System.Threading.Tasks;
using Soenneker.Facts.Local;

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

    [LocalFact]
    public async ValueTask AddMissing_should_add_missing()
    {
        await _util.AddMissing("C:\\git\\Soenneker\\GitHub\\soenneker.github.openapiclient\\src\\soenneker.github.openapiclient.csproj", true, 5, CancellationToken);
    }
}