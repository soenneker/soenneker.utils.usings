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

    [LocalFact]
    public async ValueTask Default()
    {
        await _util.AddMissing("", CancellationToken);
    }
}
