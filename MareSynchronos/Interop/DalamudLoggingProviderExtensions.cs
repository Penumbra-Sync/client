using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop;

public static class DalamudLoggingProviderExtensions
{
    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder)
    {
        builder.ClearProviders();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggingProvider>());

        return builder;
    }
}