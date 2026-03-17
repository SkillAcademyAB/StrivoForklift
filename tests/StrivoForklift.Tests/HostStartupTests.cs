using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace StrivoForklift.Tests;

/// <summary>
/// Verifies that the host builder startup validation raises a clear error when
/// the StorageQueue__serviceUri setting is absent or misconfigured, mirroring the
/// fail-fast guard in Program.cs that prevents the queue trigger from silently never polling.
/// </summary>
public class HostStartupTests
{
    private const string ValidServiceUri = "https://consumeddata.queue.core.windows.net";

    /// <summary>
    /// Builds a minimal <see cref="IHostBuilder"/> that applies the same validation
    /// logic used in Program.cs.
    /// </summary>
    /// <param name="serviceUri">
    /// The value to use for the StorageQueue:serviceUri setting, or
    /// <see langword="null"/> to omit the setting entirely.
    /// </param>
    private static IHostBuilder BuildWithValidation(string? serviceUri)
    {
        var settings = new Dictionary<string, string?>();
        // In-memory collection keys must use the ':' separator because .NET's
        // EnvironmentVariablesConfigurationProvider translates '__' → ':' before
        // values reach IConfiguration. Using ':' here mirrors production behaviour.
        if (serviceUri is not null)
            settings["StorageQueue:serviceUri"] = serviceUri;

        return new HostBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.Sources.Clear(); // isolate from ambient environment variables / files
                cfg.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
            {
                var storageQueueServiceUri = context.Configuration["StorageQueue:serviceUri"]
                    ?? throw new InvalidOperationException(
                        "Missing required app setting 'StorageQueue__serviceUri' " +
                        "(set as the environment variable 'StorageQueue__serviceUri', " +
                        "which IConfiguration exposes as 'StorageQueue:serviceUri'). " +
                        "Set this to the Azure Queue Storage service URI " +
                        "(e.g. https://<account>.queue.core.windows.net). " +
                        "The Managed Identity must also hold the " +
                        "'Storage Queue Data Message Processor' role on the storage account.");

                if (Uri.TryCreate(storageQueueServiceUri, UriKind.Absolute, out var parsedUri)
                    && parsedUri.AbsolutePath.Trim('/').Length > 0)
                {
                    throw new InvalidOperationException(
                        $"The app setting 'StorageQueue__serviceUri' has an unexpected path component " +
                        $"('{parsedUri.AbsolutePath}'). " +
                        $"This setting must be the storage-account-level queue service endpoint with no path " +
                        $"(e.g. https://<account>.queue.core.windows.net). " +
                        $"Remove the queue name from the URI — it is already declared in the QueueTrigger attribute.");
                }
            });
    }

    [Fact]
    public void Build_MissingStorageQueueServiceUri_ThrowsInvalidOperationException()
    {
        // When StorageQueue__serviceUri is absent the binding cannot poll the queue.
        // Program.cs throws immediately so the developer sees a clear error rather
        // than a function that is "live" but silently never triggers.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildWithValidation(serviceUri: null).Build());

        Assert.Contains("StorageQueue__serviceUri", ex.Message);
        Assert.Contains("Storage Queue Data Message Processor", ex.Message);
    }

    [Fact]
    public void Build_PresentStorageQueueServiceUri_DoesNotThrow()
    {
        // When the setting is a valid account-level URI the host should build without error.
        using var host = BuildWithValidation(ValidServiceUri).Build();
        Assert.NotNull(host);
        var config = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(ValidServiceUri, config["StorageQueue:serviceUri"]);
    }

    [Fact]
    public void Build_ServiceUriContainsQueueName_ThrowsInvalidOperationException()
    {
        // A common portal misconfiguration is appending the queue name to the service URI
        // (e.g. https://<account>.queue.core.windows.net/consumethis).
        // Program.cs must detect this and surface a clear, actionable error.
        var uriWithQueueName = ValidServiceUri + "/consumethis";

        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildWithValidation(uriWithQueueName).Build());

        Assert.Contains("/consumethis", ex.Message);
        Assert.Contains("QueueTrigger attribute", ex.Message);
    }
}

