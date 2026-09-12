using UnityEditor;

namespace Aftershock.Editor
{
    /// <summary>
    /// Clears menu→Main overrides when entering Play mode so pressing Play on Main.unity
    /// uses DisasterManager Inspector constants (Enter Play Mode Options / no domain reload).
    /// </summary>
    [InitializeOnLoad]
    static class GameSettingsPlayModeHook
    {
        static GameSettingsPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                GameSettings.ClearMenuOverrides();
        }
    }
}
