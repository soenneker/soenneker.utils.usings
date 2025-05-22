using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.Usings.Abstract;

/// <summary>
/// Applies code fixes for missing using directives in a C# project using Roslyn analyzers.
/// </summary>
public interface IUsingsUtil
{
    /// <summary>
    /// Finds and fixes missing using statements (CS0246, CS0103) in all documents of the specified project.
    /// </summary>
    /// <param name="csprojPath">The full path to the .csproj file to process.</param>
    /// <param name="maxPasses"></param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <param name="loopUntilNoChanges"></param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask AddMissing(string csprojPath, bool loopUntilNoChanges = false, int maxPasses = 5, CancellationToken cancellationToken = default);
}