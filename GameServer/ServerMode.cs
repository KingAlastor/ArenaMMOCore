namespace GameServer
{
    /// <summary>
    /// Selects the simulation profile used by the game server.
    /// </summary>
    public enum ServerMode
    {
        /// <summary>
        /// Fast-tick arena profile optimized for smaller sessions.
        /// </summary>
        Arena,

        /// <summary>
        /// MMO profile tuned for larger worlds and lower tick frequency.
        /// </summary>
        MMO
    }
}
