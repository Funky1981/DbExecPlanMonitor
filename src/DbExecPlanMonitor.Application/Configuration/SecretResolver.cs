using Microsoft.Extensions.Configuration;

namespace DbExecPlanMonitor.Application.Configuration;

/// <summary>
/// Resolves secrets from various sources.
/// </summary>
/// <remarks>
/// Provides a unified interface for resolving secrets from:
/// - Environment variables
/// - Azure Key Vault (when configured)
/// - User Secrets (development)
/// - Configuration providers
/// 
/// Connection strings are resolved using the pattern:
/// ConnectionStrings:{InstanceId}
/// 
/// Or via environment variables:
/// ConnectionStrings__{InstanceId}
/// </remarks>
public interface ISecretResolver
{
    /// <summary>
    /// Gets a secret value by key.
    /// </summary>
    /// <param name="key">The secret key (e.g., "SmtpPassword").</param>
    /// <returns>The secret value, or null if not found.</returns>
    string? GetSecret(string key);

    /// <summary>
    /// Gets a connection string for an instance.
    /// </summary>
    /// <param name="instanceId">The instance identifier.</param>
    /// <returns>The connection string, or null if not found.</returns>
    string? GetConnectionString(string instanceId);

    /// <summary>
    /// Checks if a secret exists.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <returns>True if the secret exists.</returns>
    bool HasSecret(string key);
}

/// <summary>
/// Default implementation using IConfiguration.
/// </summary>
/// <remarks>
/// This implementation relies on the .NET configuration system,
/// which can pull values from:
/// - appsettings.json
/// - appsettings.{Environment}.json
/// - Environment variables
/// - User secrets (in development)
/// - Azure Key Vault (when configured)
/// - Command-line arguments
/// </remarks>
public sealed class ConfigurationSecretResolver : ISecretResolver
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the ConfigurationSecretResolver.
    /// </summary>
    public ConfigurationSecretResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string? GetSecret(string key)
    {
        // Try direct lookup first
        var value = _configuration[key];
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Try Secrets section
        value = _configuration[$"Secrets:{key}"];
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Try environment variable with common patterns
        // Environment variables use __ instead of :
        var envKey = key.Replace(":", "__");
        value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetConnectionString(string instanceId)
    {
        // Standard .NET connection string lookup
        var connectionString = _configuration.GetConnectionString(instanceId);
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }

        // Try alternative patterns
        connectionString = _configuration[$"ConnectionStrings:{instanceId}"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }

        // Try environment variable
        var envKey = $"ConnectionStrings__{instanceId}";
        connectionString = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }

        return null;
    }

    /// <inheritdoc />
    public bool HasSecret(string key)
    {
        return !string.IsNullOrEmpty(GetSecret(key));
    }
}
