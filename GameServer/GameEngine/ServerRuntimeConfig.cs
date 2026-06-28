using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameServer.GameEngine
{
    /// <summary>
    /// Runtime switches loaded from configuration and command-line arguments.
    /// </summary>
    public sealed class ServerRuntimeConfig
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Enables interest-based visibility filtering in MMO mode.
        /// </summary>
        public bool EnableInterestGrid { get; init; } = false;

        /// <summary>
        /// Logical size of each interest-grid cell.
        /// </summary>
        public float GridCellSize { get; init; } = 32f;

        /// <summary>
        /// Upper bound for entities included in a single client snapshot.
        /// </summary>
        public int MaxVisibleEntitiesPerClient { get; init; } = 128;

        public int TickRate { get; init; } = 128;

        /// <summary>
        /// Loads runtime configuration from appsettings.json and command-line overrides.
        /// </summary>
        public static ServerRuntimeConfig Load(string[] args)
        {
            ServerRuntimeConfig config = new ServerRuntimeConfig();
            const string appSettingsPath = "appsettings.json";

            if (File.Exists(appSettingsPath))
            {
                string json = File.ReadAllText(appSettingsPath);
                ServerRuntimeConfig? loaded = JsonSerializer.Deserialize<ServerRuntimeConfig>(json, JsonOptions);
                if (loaded != null) config = loaded;
            }

            return config;
        }
    }
}