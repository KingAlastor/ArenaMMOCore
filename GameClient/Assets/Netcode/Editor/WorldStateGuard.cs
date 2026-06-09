// File Path: GameClient/Assets/Netcode/Editor/WorldStateGuard.cs

#if UNITY_EDITOR
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace GameClient.Editor
{
    /// <summary>
    /// Editor-only guard that ensures <c>World.DefaultGameObjectInjectionWorld</c> is always
    /// a valid, non-null Editor World before the SubScene inspector can repaint.
    ///
    /// ROOT CAUSE OF THE BUG:
    ///   <c>SubSceneInspectorUtility.GetLoadableScenes</c> (line 122) does:
    ///     var world = World.DefaultGameObjectInjectionWorld;
    ///     var entityManager = world.EntityManager;   // NullReferenceException if world == null
    ///
    ///   <c>World.Dispose()</c> sets <c>DefaultGameObjectInjectionWorld = null</c> whenever
    ///   the currently-default world is disposed.  On play-mode exit the game world is
    ///   disposed this way.  Normally a domain reload then fires <c>[InitializeOnLoad]</c>
    ///   which recreates the editor world.
    ///
    ///   HOWEVER: this project has <b>Enter Play Mode Options enabled with options = None</b>
    ///   (EditorSettings.asset: m_EnterPlayModeOptionsEnabled=1, m_EnterPlayModeOptions=0).
    ///   "None" disables BOTH domain reload AND scene reload on play-mode entry/exit.
    ///   Without a domain reload, <c>[InitializeOnLoad]</c> never fires again after the game
    ///   world is disposed, so <c>DefaultGameObjectInjectionWorld</c> stays null permanently
    ///   and the SubScene inspector crashes on every repaint.
    ///
    /// FIX:
    ///   Subscribe to <c>EditorApplication.playModeStateChanged</c>.  When
    ///   <c>PlayModeStateChange.EnteredEditMode</c> fires we are fully back in edit mode
    ///   (<c>isPlayingOrWillChangePlaymode == false</c>) so it is safe to call
    ///   <c>DefaultLazyEditModeInitialize()</c>, which creates a fresh Editor World and
    ///   reassigns <c>DefaultGameObjectInjectionWorld</c>.  Also hook
    ///   <c>EditorApplication.delayCall</c> for the initial domain-reload case.
    /// </summary>
    [InitializeOnLoad]
    internal static class WorldStateGuard
    {
        static WorldStateGuard()
        {
            // Play-mode-change hook: the critical path for "Enter Play Mode Options = None".
            // EnteredEditMode fires AFTER isPlayingOrWillChangePlaymode is reset to false,
            // making it the earliest safe moment to recreate the editor world.
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Domain-reload path: fires on the first editor tick after every domain reload
            // (initial load or script recompile) before any InspectorWindow.OnGUI callback.
            EditorApplication.delayCall += EnsureEditorWorldExists;
        }

        // ---------------------------------------------------------------------------
        // Play mode state handler
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Recreates the Editor World immediately after returning to edit mode so that
        /// the SubScene inspector finds a valid <c>DefaultGameObjectInjectionWorld</c> on
        /// the very next repaint.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EnsureEditorWorldExists();
            }
        }

        // ---------------------------------------------------------------------------
        // World existence guard
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Calls <c>DefaultLazyEditModeInitialize()</c> when the default injection world
        /// is absent or disposed and we are not mid-transition into play mode.
        /// </summary>
        private static void EnsureEditorWorldExists()
        {
            // Do not attempt world creation while entering/exiting play mode;
            // DefaultLazyEditModeInitialize has the same guard and would skip it anyway.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var current = World.DefaultGameObjectInjectionWorld;
            if (current != null && current.IsCreated)
                return; // World is healthy — nothing to do.

            // Primary path: let DOTS handle editor-world creation (respects internal
            // ordering requirements for SceneSystem, ResolveSceneReferenceSystem, etc.).
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();

            // Fallback: if the primary path still left the world null (e.g. because an
            // internal system threw during AddSystemToRootLevelSystemGroupsInternal),
            // create a bare editor world so the inspector has something valid to reference.
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                Debug.LogWarning(
                    "[WorldStateGuard] DefaultLazyEditModeInitialize() did not produce a " +
                    "valid Editor World. Creating a minimal fallback world to prevent " +
                    "NullReferenceException in SubSceneInspectorUtility.GetLoadableScenes. " +
                    "Check the console for system-creation errors from the DOTS pipeline.");

                var fallback = new World("Editor World (Fallback)", WorldFlags.Editor);
                World.DefaultGameObjectInjectionWorld = fallback;
            }
        }
    }
}
#endif
