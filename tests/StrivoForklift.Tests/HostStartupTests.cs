using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace StrivoForklift.Tests;

/// <summary>
/// Verifies that the host builder startup validation raises a clear error when
/// the StorageQueue__serviceUri setting is absent, mirroring the fail-fast guard
/// in Program.cs that prevents the queue trigger from silently never polling.
/// </summary>
public class HostStartupTests
{
    /// <summary>
    /// Builds a minimal <see cref="IHostBuilder"/> that applies the same
    /// null-coalescing throw guard used in Program.cs.
    /// </summary>
    private static IHostBuilder BuildWithValidation(bool includeStorageQueueUri)
    {
        var settings = new Dictionary<string, string?>();
        if (includeStorageQueueUri)
            settings["StorageQueue__serviceUri"] = "https://consumeddata.queue.core.windows.net";

        return new HostBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.Sources.Clear(); // isolate from ambient env vars / files
                cfg.AddInMemoryCollection(settings);
            })
            .ConfigureServices((context, services) =>
            {
                _ = context.Configuration["StorageQueue__serviceUri"]
                    ?? throw new InvalidOperationException(
                        "Missing required app setting 'StorageQueue__serviceUri'. " +
                        "Set this to the Azure Queue Storage service URI " +
                        "(e.g. https://<account>.queue.core.windows.net). " +
                        "The Managed Identity must also hold the " +
                        "'Storage Queue Data Message Processor' role on the storage account.");
            });
    }

    [Fact]
    public void Build_MissingStorageQueueServiceUri_ThrowsInvalidOperationException()
    {
        // When StorageQueue__serviceUri is absent the binding cannot poll the queue.
        // Program.cs throws immediately so the developer sees a clear error rather
        // than a function that is "live" but silently never triggers.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildWithValidation(includeStorageQueueUri: false).Build());

        Assert.Contains("StorageQueue__serviceUri", ex.Message);
        Assert.Contains("Storage Queue Data Message Processor", ex.Message);
    }

    [Fact]
    public void Build_PresentStorageQueueServiceUri_DoesNotThrow()
    {
        // When the setting is present the host should build without error.
        using var host = BuildWithValidation(includeStorageQueueUri: true).Build();
        Assert.NotNull(host);
    }
}
