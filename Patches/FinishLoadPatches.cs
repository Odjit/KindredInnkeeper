using HarmonyLib;
using Unity.Scenes;

namespace KindredInnkeeper.Patches;

[HarmonyPatch(typeof(SceneSystem), nameof(SceneSystem.ShutdownStreamingSupport))]
public static class FinishLoadPatch
{
    [HarmonyPostfix]
    public static void OneShot_AfterLoad_InitializationPatch()
    {
        Core.InnService.FinishedLoading();
        Plugin.Harmony.Unpatch(typeof(SceneSystem).GetMethod("ShutdownStreamingSupport"), typeof(InitializationPatch).GetMethod("OneShot_AfterLoad_InitializationPatch"));
    }
}
