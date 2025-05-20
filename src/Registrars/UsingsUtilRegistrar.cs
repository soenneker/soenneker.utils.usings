using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Usings.Abstract;

namespace Soenneker.Utils.Usings.Registrars;

/// <summary>
/// Applies code fixes for missing using directives in a C# project using Roslyn analyzers.
/// </summary>
public static class UsingsUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IUsingsUtil"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddUsingsUtilAsSingleton(this IServiceCollection services)
    {
        services.AddFileUtilAsSingleton().TryAddSingleton<IUsingsUtil, UsingsUtil>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IUsingsUtil"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddUsingsUtilAsScoped(this IServiceCollection services)
    {
        services.AddFileUtilAsScoped().TryAddScoped<IUsingsUtil, UsingsUtil>();

        return services;
    }
}
