using UnityEngine;

namespace JianghuGo.Unity.Presentation
{
    /// <summary>
    /// Creates the game's composition root at startup, so the build needs no hand-authored scene: the CI build script
    /// produces an empty bootstrap scene and everything else is built in code.
    /// </summary>
    public static class AutoStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindFirstObjectByType<GameBootstrap>() == null)
            {
                Object.DontDestroyOnLoad(new GameObject("Jianghu Go").AddComponent<GameBootstrap>().gameObject);
            }
        }
    }
}
