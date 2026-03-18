using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrivoForklift.Data;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Fail fast if the queue trigger connection is not configured.
        // When this setting is absent the QueueTrigger binding silently stops polling,
        // leaving the queue full and the function apparently live but never triggered.
        // NOTE: .NET's EnvironmentVariablesConfigurationProvider translates '__' to ':' in
        // configuration keys, so the environment variable 'StorageQueue__serviceUri' is
        // accessible as 'StorageQueue:serviceUri' via IConfiguration.
        var storageQueueServiceUri = context.Configuration["StorageQueue:serviceUri"]
            ?? throw new InvalidOperationException(
                "Missing required app setting 'StorageQueue__serviceUri' " +
                "(set as the environment variable 'StorageQueue__serviceUri', " +
                "which IConfiguration exposes as 'StorageQueue:serviceUri'). " +
                "Set this to the Azure Queue Storage service URI " +
                "(e.g. https://<account>.queue.core.windows.net). " +
                "The Managed Identity must also hold the " +
                "'Storage Queue Data Message Processor' role on the storage account.");

        // Validate that the URI is the storage-account-level endpoint only.
        // A common misconfiguration is appending the queue name to the URI
        // (e.g. https://<account>.queue.core.windows.net/consumethis) — the queue
        // name must NOT appear here; it is already declared in the QueueTrigger attribute.
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

        // Fetch the database username and password from Azure Key Vault.
        // The Key Vault URI and the expected secret names are read from configuration so they
        // can be overridden per environment without changing code.
        // NOTE: DefaultAzureCredential uses the Function App's system-assigned managed identity
        // in Azure, and falls back to the developer's local credential (Azure CLI / Visual Studio)
        // during local development.
        var keyVaultUri = context.Configuration["KeyVault:Uri"]
            ?? throw new InvalidOperationException(
                "Missing required app setting 'KeyVault:Uri' " +
                "(set as the environment variable 'KeyVault__Uri'). " +
                "Set this to the Azure Key Vault URI (e.g. https://kv-bear.vault.azure.net/). " +
                "The Managed Identity must also hold the 'Key Vault Secrets User' role on the vault.");

        var dbUsernameSecretName = context.Configuration["KeyVault:DbUsernameSecretName"]
            ?? throw new InvalidOperationException(
                "Missing required app setting 'KeyVault:DbUsernameSecretName' " +
                "(set as the environment variable 'KeyVault__DbUsernameSecretName'). " +
                "Set this to the name of the Key Vault secret that holds the database username.");

        var dbPasswordSecretName = context.Configuration["KeyVault:DbPasswordSecretName"]
            ?? throw new InvalidOperationException(
                "Missing required app setting 'KeyVault:DbPasswordSecretName' " +
                "(set as the environment variable 'KeyVault__DbPasswordSecretName'). " +
                "Set this to the name of the Key Vault secret that holds the database password.");

        var dbServer = context.Configuration["SqlServer:Server"]
            ?? throw new InvalidOperationException(
                "Missing required app setting 'SqlServer:Server' " +
                "(set as the environment variable 'SqlServer__Server'). " +
                "Set this to the SQL Server host including port " +
                "(e.g. tcp:ingestdemo.database.windows.net,1433).");

        var dbDatabase = context.Configuration["SqlServer:Database"]
            ?? throw new InvalidOperationException(
                "Missing required app setting 'SqlServer:Database' " +
                "(set as the environment variable 'SqlServer__Database'). " +
                "Set this to the name of the SQL database (e.g. transaction_ingester).");

        var secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());

        string dbUsername;
        string dbPassword;
        try
        {
            dbUsername = secretClient.GetSecret(dbUsernameSecretName).Value.Value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve the database username secret '{dbUsernameSecretName}' from Key Vault '{keyVaultUri}'. " +
                $"Ensure the secret exists and the managed identity holds the 'Key Vault Secrets User' role on the vault. " +
                $"Inner exception: {ex.Message}", ex);
        }

        try
        {
            dbPassword = secretClient.GetSecret(dbPasswordSecretName).Value.Value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve the database password secret '{dbPasswordSecretName}' from Key Vault '{keyVaultUri}'. " +
                $"Ensure the secret exists and the managed identity holds the 'Key Vault Secrets User' role on the vault. " +
                $"Inner exception: {ex.Message}", ex);
        }

        var connectionString =
            $"Server={dbServer};Database={dbDatabase};" +
            $"User Id={dbUsername};Password={dbPassword};" +
            $"Encrypt=True;TrustServerCertificate=False;";

        services.AddDbContext<ForkliftDbContext>(options =>
            options.UseSqlServer(connectionString));
    })
    .Build();

host.Run();
