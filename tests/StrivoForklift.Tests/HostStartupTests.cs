using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace StrivoForklift.Tests;

/// <summary>
/// Verifies that the host builder startup validation raises a clear error when
/// the StorageQueue__serviceUri or Key Vault settings are absent or misconfigured,
/// mirroring the fail-fast guards in Program.cs.
/// </summary>
public class HostStartupTests
{
    private const string ValidServiceUri = "https://consumeddata.queue.core.windows.net";
    private const string ValidKeyVaultUri = "https://kv-bear.vault.azure.net/";
    private const string ValidDbUsernameSecretName = "sql-db-username";
    private const string ValidDbPasswordSecretName = "sql-db-password";

    /// <summary>
    /// Builds a minimal <see cref="IHostBuilder"/> that applies the same validation
    /// logic used in Program.cs for the StorageQueue settings.
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

    /// <summary>
    /// Builds a minimal <see cref="IHostBuilder"/> that applies the same Key Vault
    /// settings validation logic used in Program.cs, without actually connecting to
    /// Key Vault (the actual secret retrieval is out of scope for these unit tests).
    /// </summary>
    private static IHostBuilder BuildWithKeyVaultValidation(
        string? serviceUri,
        string? keyVaultUri,
        string? dbUsernameSecretName,
        string? dbPasswordSecretName)
    {
        var settings = new Dictionary<string, string?>();
        if (serviceUri is not null) settings["StorageQueue:serviceUri"] = serviceUri;
        if (keyVaultUri is not null) settings["KeyVault:Uri"] = keyVaultUri;
        if (dbUsernameSecretName is not null) settings["KeyVault:DbUsernameSecretName"] = dbUsernameSecretName;
        if (dbPasswordSecretName is not null) settings["KeyVault:DbPasswordSecretName"] = dbPasswordSecretName;

        return new HostBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.Sources.Clear();
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

                _ = context.Configuration["KeyVault:Uri"]
                    ?? throw new InvalidOperationException(
                        "Missing required app setting 'KeyVault:Uri' " +
                        "(set as the environment variable 'KeyVault__Uri'). " +
                        "Set this to the Azure Key Vault URI (e.g. https://kv-bear.vault.azure.net/). " +
                        "The Managed Identity must also hold the 'Key Vault Secrets User' role on the vault.");

                _ = context.Configuration["KeyVault:DbUsernameSecretName"]
                    ?? throw new InvalidOperationException(
                        "Missing required app setting 'KeyVault:DbUsernameSecretName' " +
                        "(set as the environment variable 'KeyVault__DbUsernameSecretName'). " +
                        "Set this to the name of the Key Vault secret that holds the database username.");

                _ = context.Configuration["KeyVault:DbPasswordSecretName"]
                    ?? throw new InvalidOperationException(
                        "Missing required app setting 'KeyVault:DbPasswordSecretName' " +
                        "(set as the environment variable 'KeyVault__DbPasswordSecretName'). " +
                        "Set this to the name of the Key Vault secret that holds the database password.");
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

    [Fact]
    public void Build_MissingKeyVaultUri_ThrowsInvalidOperationException()
    {
        // When KeyVault:Uri is absent Program.cs cannot connect to Key Vault to retrieve
        // the database credentials.  The host must throw immediately with a clear message.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildWithKeyVaultValidation(
                serviceUri: ValidServiceUri,
                keyVaultUri: null,
                dbUsernameSecretName: ValidDbUsernameSecretName,
                dbPasswordSecretName: ValidDbPasswordSecretName).Build());

        Assert.Contains("KeyVault:Uri", ex.Message);
        Assert.Contains("Key Vault Secrets User", ex.Message);
    }

    [Fact]
    public void Build_MissingDbUsernameSecretName_ThrowsInvalidOperationException()
    {
        // When the config key that names the database-username secret is absent, the host
        // cannot know which Key Vault secret to read for the username.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildWithKeyVaultValidation(
                serviceUri: ValidServiceUri,
                keyVaultUri: ValidKeyVaultUri,
                dbUsernameSecretName: null,
                dbPasswordSecretName: ValidDbPasswordSecretName).Build());

        Assert.Contains("KeyVault:DbUsernameSecretName", ex.Message);
    }

    [Fact]
    public void Build_MissingDbPasswordSecretName_ThrowsInvalidOperationException()
    {
        // When the config key that names the database-password secret is absent, the host
        // cannot know which Key Vault secret to read for the password.
        var ex = Assert.Throws<InvalidOperationException>(
            () => BuildWithKeyVaultValidation(
                serviceUri: ValidServiceUri,
                keyVaultUri: ValidKeyVaultUri,
                dbUsernameSecretName: ValidDbUsernameSecretName,
                dbPasswordSecretName: null).Build());

        Assert.Contains("KeyVault:DbPasswordSecretName", ex.Message);
    }

    [Fact]
    public void Build_AllKeyVaultSettingsPresent_DoesNotThrow()
    {
        // When all Key Vault config settings are supplied the host should build without
        // error (no actual Key Vault connection is attempted at this stage).
        using var host = BuildWithKeyVaultValidation(
            serviceUri: ValidServiceUri,
            keyVaultUri: ValidKeyVaultUri,
            dbUsernameSecretName: ValidDbUsernameSecretName,
            dbPasswordSecretName: ValidDbPasswordSecretName).Build();

        Assert.NotNull(host);
        var config = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(ValidKeyVaultUri, config["KeyVault:Uri"]);
        Assert.Equal(ValidDbUsernameSecretName, config["KeyVault:DbUsernameSecretName"]);
        Assert.Equal(ValidDbPasswordSecretName, config["KeyVault:DbPasswordSecretName"]);
    }
}

