using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using KindredInnkeeper.Models;
using ProjectM;
using VampireCommandFramework;

namespace KindredInnkeeper;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin
{
    Harmony _harmony;
    static Plugin Instance;
    public static Harmony Harmony => Instance._harmony;
    public static ManualLogSource LogInstance { get; private set; }


    public override void Load()
    {
        // Plugin startup logic
        Instance = this;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loaded!");
        LogInstance = Log;
        // Harmony patching
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

        // Register all commands in the assembly with VCF
        CommandRegistry.RegisterAll();
    }

    public override bool Unload()
    {
        CommandRegistry.UnregisterAssembly();
        _harmony?.UnpatchSelf();
        return true;
    }

    public void OnGameInitialized()
    {
        if (!HasLoaded())
        {
            Log.LogDebug("Attempt to initialize before everything has loaded.");
            return;
        }

        Core.InitializeAfterLoaded();
    }

    private static bool HasLoaded()
    {
        // Hack, check to make sure that entities loaded enough because this function
        // will be called when the plugin is first loaded, when this will return 0
        // but also during reload when there is data to initialize with.
        var collectionSystem = Core.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
        return collectionSystem?.SpawnableNameToPrefabGuidDictionary.Count > 0;
    }

}
