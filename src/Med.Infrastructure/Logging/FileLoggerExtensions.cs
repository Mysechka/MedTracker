using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Med.Infrastructure.Logging;

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(
        this ILoggingBuilder builder,
        string? logDirectory = null,
        LogLevel minLevel = LogLevel.Information)
    {
        builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(logDirectory, minLevel));
        return builder;
    }
}
