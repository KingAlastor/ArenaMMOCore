// File Path: GameClient/Assets/Netcode/WorldBootstrap.cs

using Unity.Entities;
using UnityEngine;

namespace GameClient
{
    /// <summary>
    /// Custom World bootstrap that takes explicit ownership of Game World creation at
    /// play-mode entry (runtime).
    ///
    /// SCOPE — GAME WORLD ONLY:
    ///   <c>ICustomBootstrap.Initialize</c> is invoked by
    ///   <c>DefaultWorldInitialization.Initialize(name, editorWorld: false)</c> which is
    ///   called via <c>[RuntimeInitializeOnLoadMethod]</c> when entering play mode.
    ///   The <b>Editor World</b> is created through a separate path
    ///   (<c>DefaultLazyEditModeInitialize → Initialize(name, editorWorld: true)</c>)
    ///   that explicitly bypasses <c>ICustomBootstrap</c>, so this class has no effect
    ///   on editor-only NullReferenceException issues in the SubScene inspector.
    ///   Those are handled by <c>WorldStateGuard</c>.
    ///
    /// MEMORY LEAK FIX:
    ///   The "Persistent allocates 8 individual allocations" warning is caused by DOTS
    ///   internals allocating NativeContainers during a partial World initialisation that
    ///   is then aborted by the SubScene null-ref crash.  Completing game-world
    ///   initialisation cleanly here eliminates orphaned allocations at play-mode entry.
    /// </summary>
    public sealed class WorldBootstrap : ICustomBootstrap
    {
        /// <summary>
        /// Called once by <c>DefaultWorldInitialization</c> at play-mode entry.
        /// Returning <c>true</c> signals that this method has fully created the default
        /// World; Unity will not perform its own default-World setup.
        /// </summary>
        public bool Initialize(string defaultWorldName)
        {
            try
            {
                // -----------------------------------------------------------------------
                // Step 1: Create the Game World with the Game flag so SimulationSystemGroup,
                //         SceneSystem, and all gameplay systems are eligible for inclusion.
                // -----------------------------------------------------------------------
                var world = new World(defaultWorldName, WorldFlags.Game);

                // Expose immediately so editor callbacks that fire during play-mode entry
                // find a valid, non-null World instance.
                World.DefaultGameObjectInjectionWorld = world;

                // -----------------------------------------------------------------------
                // Step 2: Discover all ISystem / ComponentSystemBase types flagged for
                //         Game worlds (InitializationSystemGroup, SimulationSystemGroup,
                //         PresentationSystemGroup, Unity.Scenes.SceneSystemGroup, etc.).
                //         Completing this step allocates and properly registers all
                //         NativeContainers, preventing the 8-allocation persistent leak.
                // -----------------------------------------------------------------------
                var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);

                // -----------------------------------------------------------------------
                // Step 3: Hook the World into Unity's PlayerLoop so that ECS simulation
                //         ticks are driven by the normal Update / FixedUpdate path.
                // -----------------------------------------------------------------------
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);

                return true;
            }
            catch (System.Exception ex)
            {
                // If initialisation fails, log the real cause and return false so Unity
                // falls back to its own default World creation instead of leaving
                // DefaultGameObjectInjectionWorld null.
                Debug.LogError(
                    $"[WorldBootstrap] World initialisation threw an exception; " +
                    $"falling back to Unity default world creation.\n{ex}");

                // Clean up the partially-initialised world we may have assigned.
                if (World.DefaultGameObjectInjectionWorld != null &&
                    !World.DefaultGameObjectInjectionWorld.IsCreated)
                {
                    World.DefaultGameObjectInjectionWorld.Dispose();
                }
                World.DefaultGameObjectInjectionWorld = null;

                return false; // Let Unity handle world creation.
            }
        }
    }
}
