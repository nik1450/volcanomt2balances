using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using I2.Loc;
using Microsoft.Extensions.Configuration;
using ShinyShoe.Logging;
using SimpleInjector;
using TrainworksReloaded.Base;
using TrainworksReloaded.Base.Card;
using TrainworksReloaded.Base.CardUpgrade;
using TrainworksReloaded.Base.Character;
using TrainworksReloaded.Base.Class;
using TrainworksReloaded.Base.Effect;
using TrainworksReloaded.Base.Localization;
using TrainworksReloaded.Base.Prefab;
using TrainworksReloaded.Base.Trait;
using TrainworksReloaded.Base.Trigger;
using TrainworksReloaded.Core;
using TrainworksReloaded.Core.Impl;
using TrainworksReloaded.Core.Interfaces;
using TrainworksReloaded.Core.Extensions;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace vol_UBP_v1.Plugin
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = new(MyPluginInfo.PLUGIN_GUID);
        public void Awake()
        {
            // Plugin startup logic
            Logger = base.Logger;

            var builder = Railhead.GetBuilder();
            builder.Configure(
                MyPluginInfo.PLUGIN_GUID,
                c =>
                {
                    // Load/merge JSON config files (later calls override earlier values)
                    c.AddJsonFile("json/plugin.json", optional: false, reloadOnChange: true);
                    c.AddJsonFile("json/global.json", optional: true,  reloadOnChange: true);

        // If you also want to ship your balance changes here (optional):
        // c.AddJsonFile("json/balance_changes.json", optional: true, reloadOnChange: true);
                }
            );

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
            
            // Uncomment if you need harmony patches, if you are writing your own custom effects.
            //var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            //harmony.PatchAll();
        }
    }
}
