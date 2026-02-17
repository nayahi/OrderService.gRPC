using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Caching.Memory;

namespace OrderService.gRPC.Services
{
    /// <summary>
    /// Modelo de configuración de un Feature Flag
    /// Almacenado como JSON en Secrets Manager: {"Enabled": true, "Rollout": 10}
    /// </summary>
    public class FlagConfig
    {
        public bool Enabled { get; set; }
        /// <summary>Porcentaje de tráfico a enrutar (0-100) para canary deployments</summary>
        public int Rollout { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Interfaz para desacoplar — facilita testing y permite mock
    /// </summary>
    public interface IFeatureFlagService
    {
        Task<bool> IsEnabledAsync(string flagName);
        Task<FlagConfig?> GetFlagConfigAsync(string flagName);
    }

    /// <summary>
    /// SEMANA 5: Feature Flags via Secrets Manager (LocalStack) con MemoryCache.
    /// Cada flag es un secreto con key "feature-flags/{nombre}" y valor JSON.
    /// Cache TTL: 30 segundos — cambios se propagan sin redesplegar.
    /// </summary>
    public class FeatureFlagService : IFeatureFlagService
    {
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly IMemoryCache _cache;
        private readonly ILogger<FeatureFlagService> _logger;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
        private const string CachePrefix = "ff_";

        public FeatureFlagService(
            IAmazonSecretsManager secretsManager,
            IMemoryCache cache,
            ILogger<FeatureFlagService> logger)
        {
            _secretsManager = secretsManager;
            _cache = cache;
            _logger = logger;
        }

        public async Task<bool> IsEnabledAsync(string flagName)
        {
            var config = await GetFlagConfigAsync(flagName);
            return config?.Enabled ?? false;
        }

        public async Task<FlagConfig?> GetFlagConfigAsync(string flagName)
        {
            var cacheKey = $"{CachePrefix}{flagName}";

            if (_cache.TryGetValue(cacheKey, out FlagConfig? cached))
                return cached;

            try
            {
                var response = await _secretsManager.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = $"feature-flags/{flagName}" });

                var config = JsonSerializer.Deserialize<FlagConfig>(response.SecretString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (config is not null)
                {
                    _cache.Set(cacheKey, config, CacheTtl);
                    _logger.LogInformation(
                        "🏷️ Flag '{Flag}': Enabled={Enabled}, Rollout={Rollout}%",
                        flagName, config.Enabled, config.Rollout);
                }
                return config;
            }
            catch (ResourceNotFoundException)
            {
                _logger.LogWarning("🏷️ Flag '{Flag}' no existe — default OFF", flagName);
                var off = new FlagConfig { Enabled = false, Rollout = 0 };
                _cache.Set(cacheKey, off, CacheTtl);
                return off;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🏷️ Error leyendo flag '{Flag}' — default OFF", flagName);
                return new FlagConfig { Enabled = false, Rollout = 0 };
            }
        }
    }
}