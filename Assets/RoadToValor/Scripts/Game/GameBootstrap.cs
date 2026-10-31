using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Zero-setup entry point: pressing Play in ANY scene (even an empty one) starts the game.
    /// To keep a scene free of the game, add an empty GameObject named "RTV_NoAutoBoot".
    /// A scene can also contain a GameObject with GameRoot already attached; then nothing extra is created.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameRoot.Instance != null) return;
            if (GameObject.Find("RTV_NoAutoBoot") != null) return;
            var go = new GameObject("RoadToValor");
            go.AddComponent<GameRoot>();
        }
    }
}
