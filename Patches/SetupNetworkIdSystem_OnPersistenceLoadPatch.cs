using HarmonyLib;
using ProjectM.Network;

namespace KindredInnkeeper.Patches;

[HarmonyPatch(typeof(SetupNetworkIdSystem_OnPersistenceLoad), nameof(SetupNetworkIdSystem_OnPersistenceLoad.OnCreate))]
public static class SetupNetworkIdSystem_OnPersistenceLoadPatch
{
    public static void Prefix()
    {
        Plugin.LogInstance.LogInfo("SetupNetworkIdSystem_OnPersistenceLoadPatch OnCreate Prefix");
    }
}