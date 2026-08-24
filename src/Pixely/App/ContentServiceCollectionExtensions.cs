using Pixely.Content;
using Pixely.DependencyInjection;

namespace Pixely.App;

public static class ContentServiceCollectionExtensions
{
    public static ServiceCollection AddContentFromDirectory(this ServiceCollection services, string directory)
    {
        PrepareContentConfiguration(services);
        return AddSources(services, new FileSystemBuilder().AddContentFromDirectory(directory));
    }

    public static ServiceCollection AddFileSystem(this ServiceCollection services, VirtualFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fileSystem);
        EnsureRoot(services);
        EnsureFileSystemRegistration(services);
        services.AddSingleton(new FileSystemSource(fileSystem));
        return services;
    }

    public static ServiceCollection AddContentFromProjectDirectory(this ServiceCollection services, string directory)
    {
        PrepareContentConfiguration(services);
        return AddSources(services, new FileSystemBuilder().AddContentFromProjectDirectory(directory));
    }

    public static ServiceCollection AddContentFromDirectoryPattern(this ServiceCollection services, string pattern)
    {
        PrepareContentConfiguration(services);
        return AddSources(services, new FileSystemBuilder().AddContentFromDirectoryPattern(pattern));
    }

    public static ServiceCollection AddContentFromZipPattern(this ServiceCollection services, string pattern)
    {
        PrepareContentConfiguration(services);
        return AddSources(services, new FileSystemBuilder().AddContentFromZipPattern(pattern));
    }

    public static ServiceCollection AddFileSystemCache(this ServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureRoot(services);
        EnsureFileSystemRegistration(services);
        if (!services.IsRegistered<FileSystemCacheConfiguration>())
        {
            services.AddSingleton(new FileSystemCacheConfiguration());
        }
        return services;
    }

    internal static void EnsureFileSystemRegistration(ServiceCollection services)
    {
        if (services.IsRegistered<FileSystemConfiguration>())
        {
            return;
        }

        services.AddSingleton(new FileSystemConfiguration());
        services.AddSingleton<VirtualFileSystem>(CreateFileSystem);
    }

    private static ServiceCollection AddSources(ServiceCollection services, FileSystemBuilder builder)
    {
        foreach (VirtualFileSystem fileSystem in builder.FileSystems)
        {
            services.AddSingleton(new FileSystemSource(fileSystem));
        }
        return services;
    }

    private static void PrepareContentConfiguration(ServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureRoot(services);
        EnsureFileSystemRegistration(services);
    }

    private static VirtualFileSystem CreateFileSystem(ServiceProvider provider)
    {
        FileSystemBuilder builder = new();
        foreach (FileSystemSource source in provider.GetServices<FileSystemSource>())
        {
            builder.AddSourceFileSystem(source.FileSystem);
        }
        if (provider.GetService<FileSystemCacheConfiguration>() != null)
        {
            builder.WithCache();
        }
        return builder.Create();
    }

    private static void EnsureRoot(ServiceCollection services)
    {
        if (!services.IsRoot)
        {
            throw new InvalidOperationException("Content can only be configured on a root service collection.");
        }
    }

    private sealed record FileSystemSource(VirtualFileSystem FileSystem);

    private sealed class FileSystemCacheConfiguration
    {
    }

    private sealed class FileSystemConfiguration
    {
    }
}
