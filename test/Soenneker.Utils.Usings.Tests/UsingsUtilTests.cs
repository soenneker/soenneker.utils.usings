using Soenneker.Utils.Usings.Abstract;
using Soenneker.Tests.HostedUnit;
using System.Threading.Tasks;
using Soenneker.Tests.Attributes.Local;

namespace Soenneker.Utils.Usings.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public class UsingsUtilTests : HostedUnitTest
{
    private readonly IUsingsUtil _util;

    public UsingsUtilTests(Host host) : base(host)
    {
        _util = Resolve<IUsingsUtil>(true);
    }

    [Test]
    public void Default()
    {
    }

    [LocalOnly]
    public async ValueTask AddMissing_should_add_missing()
    {
        await _util.AddMissing("C:\\git\\Soenneker\\GitHub\\soenneker.github.openapiclient\\src\\soenneker.github.openapiclient.csproj", true, 5, CancellationToken);
    }
}