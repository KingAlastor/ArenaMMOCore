namespace GameServer
{
    /// <summary>
    /// Runtime switches loaded from configuration and command-line arguments.
    /// </summary>
    public sealed class ServerRuntimeConfig
    {
        /// <summary>
        /// Active server mode used to select simulation defaults.
        /// </summary>
        public ServerMode ServerMode { get; set; } = ServerMode.MMO;

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
    }
}
