using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using ConditionalConfigSync;
using HarmonyLib;

namespace StationItemReturn
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInDependency(ConditionalConfigSyncGuid, "1.0.6")]
    public sealed class StationItemReturn : BaseUnityPlugin
    {
        public const string PluginId = "shudnal.StationItemReturn";
        public const string PluginName = "Station Item Return";
        public const string PluginVersion = "1.0.0";
        public const string ConditionalConfigSyncGuid = "_shudnal.ConditionalConfigSync";

        internal static readonly ConfigSync ConfigSync = new ConfigSync(PluginId)
        {
            DisplayName = PluginName,
            CurrentVersion = PluginVersion,
            MinimumRequiredVersion = PluginVersion,
            ModRequired = false,
            ModRequirementMode = ModRequirementMode.Fixed
        };

        internal static StationItemReturn Instance;

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> ConfigLocked;
        internal static ConfigEntry<bool> LoggingEnabled;

        internal static ConfigEntry<bool> CookingStationItemReturnEnabled;
        internal static ConfigEntry<float> CookingStationItemReturnProgressLimit;
        internal static ConfigEntry<bool> CookingStationFuelReturnEnabled;

        internal static ConfigEntry<bool> FireplaceFuelReturnEnabled;
        internal static ConfigEntry<string> FireplaceExcludedPrefabs;

        internal static ConfigEntry<bool> SmelterInputReturnEnabled;
        internal static ConfigEntry<bool> SmelterFuelReturnEnabled;
        internal static ConfigEntry<string> SmelterFuelExcludedPrefabs;

        internal static ConfigEntry<bool> FermenterIngredientReturnEnabled;
        internal static ConfigEntry<float> FermenterIngredientReturnProgressLimit;
        internal static ConfigEntry<bool> TurretAmmoReturnEnabled;

        private static readonly HashSet<string> ExcludedFireplacePrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ExcludedSmelterFuelPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private sealed class ConfigurationManagerAttributes
        {
            public bool? ShowRangeAsPercent = false;
        }

        internal static bool IsAvailableForCurrentSession
        {
            get
            {
                if (ZNet.instance == null)
                {
                    return true;
                }

                if (ZNet.m_isServer)
                {
                    return true;
                }

                return ConfigSync.InitialSyncDone;
            }
        }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            BindConfiguration();

            harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginId);
            Game.isModded = true;
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        private void BindConfiguration()
        {
            ModEnabled = BindServerConfig("General", "Enabled", true, "Enable the mod. [Synced with Server]");
            LoggingEnabled = BindClientConfig("General", "Enable logging", false, "Enable diagnostic logging on this client.");
            ConfigLocked = Config.Bind("General", "Lock Configuration", true, "Configuration is locked and can be changed by server administrators only. [Synced with Server]");
            ConfigSync.AddLockingConfigEntry(ConfigLocked);

            CookingStationItemReturnEnabled = BindServerConfig("Cooking stations", "Return uncooked items", true, "Allow returning the last uncooked item from cooking stations and ovens. [Synced with Server]");
            CookingStationItemReturnProgressLimit = BindServerConfig(
                "Cooking stations",
                "Maximum return progress",
                0f,
                new ConfigDescription(
                    "Maximum cooking progress at which an uncooked item can still be returned. 0 disables this restriction; values above 0 limit returns to that fraction of the cooking time. [Synced with Server]",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = true }));
            CookingStationFuelReturnEnabled = BindServerConfig("Cooking stations", "Return fuel", false, "Allow returning one remaining fuel item from cooking stations and ovens that use a fixed fuel item. [Synced with Server]");

            FireplaceFuelReturnEnabled = BindServerConfig("Fireplaces", "Return fuel", false, "Allow returning one remaining fuel item from refillable fireplaces that use a fixed fuel item. Fireplaces do not normally refund fuel when destroyed, so this feature is disabled by default. [Synced with Server]");
            FireplaceExcludedPrefabs = BindServerConfig("Fireplaces", "Excluded prefabs", string.Empty, "Comma-, semicolon-, or line-separated exact prefab names that must not return fuel. Use this for fireplaces modified to accept multiple fuel item types because Valheim stores only a single fuel amount and does not preserve which fuel items were added. [Synced with Server]");

            FireplaceExcludedPrefabs.SettingChanged += (sender, args) => RefreshExcludedFireplacePrefabs();
            RefreshExcludedFireplacePrefabs();

            SmelterInputReturnEnabled = BindServerConfig("Smelters", "Return input items", true, "Allow returning the newest raw item from a smelter input queue. [Synced with Server]");
            SmelterFuelReturnEnabled = BindServerConfig("Smelters", "Return fuel", true, "Allow returning one remaining fuel item from smelters that use a fixed fuel item. [Synced with Server]");
            SmelterFuelExcludedPrefabs = BindServerConfig("Smelters", "Fuel return excluded prefabs", string.Empty, "Comma-, semicolon-, or line-separated exact prefab names that must not return fuel. Use this for smelters modified to accept multiple fuel item types because Valheim stores only a single fuel amount and does not preserve which fuel items were added. [Synced with Server]");

            SmelterFuelExcludedPrefabs.SettingChanged += (sender, args) => RefreshExcludedSmelterFuelPrefabs();
            RefreshExcludedSmelterFuelPrefabs();

            FermenterIngredientReturnEnabled = BindServerConfig("Fermenters", "Return ingredient", true, "Allow returning the original ingredient while a fermenter is still fermenting. [Synced with Server]");
            FermenterIngredientReturnProgressLimit = BindServerConfig(
                "Fermenters",
                "Maximum return progress",
                0f,
                new ConfigDescription(
                    "Maximum fermentation progress at which the original ingredient can still be returned. 0 disables this restriction; values above 0 limit returns to that fraction of the fermentation time. For example 25% means mead can only be returned in first 15 minutes of fermenting. [Synced with Server]",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { ShowRangeAsPercent = true }));

            TurretAmmoReturnEnabled = BindServerConfig("Turrets", "Return ammunition", false, "Allow returning one loaded ammunition item from turrets configured to refund ammunition when destroyed. Turrets do not preserve cheated provenance, so returned ammunition is always cleared of the cheated marker. [Synced with Server]");
        }

        private static void RefreshExcludedPrefabNames(ConfigEntry<string> entry, HashSet<string> names)
        {
            names.Clear();

            string value = entry?.Value ?? string.Empty;
            foreach (string prefabName in value.Split(
                new[] { ',', ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmedName = prefabName.Trim();
                if (!string.IsNullOrEmpty(trimmedName))
                {
                    names.Add(trimmedName);
                }
            }
        }

        private static void RefreshExcludedFireplacePrefabs()
        {
            RefreshExcludedPrefabNames(FireplaceExcludedPrefabs, ExcludedFireplacePrefabNames);
        }

        private static void RefreshExcludedSmelterFuelPrefabs()
        {
            RefreshExcludedPrefabNames(SmelterFuelExcludedPrefabs, ExcludedSmelterFuelPrefabNames);
        }

        internal static bool IsFireplaceExcluded(Fireplace fireplace)
        {
            if (!fireplace)
            {
                return false;
            }

            string prefabName = fireplace.m_nview && fireplace.m_nview.IsValid()
                ? fireplace.m_nview.GetPrefabName()
                : Utils.GetPrefabName(fireplace.gameObject);

            return !string.IsNullOrEmpty(prefabName)
                && ExcludedFireplacePrefabNames.Contains(prefabName);
        }

        internal static bool IsSmelterFuelExcluded(Smelter smelter)
        {
            if (!smelter)
            {
                return false;
            }

            string prefabName = smelter.m_nview && smelter.m_nview.IsValid()
                ? smelter.m_nview.GetPrefabName()
                : Utils.GetPrefabName(smelter.gameObject);

            return !string.IsNullOrEmpty(prefabName)
                && ExcludedSmelterFuelPrefabNames.Contains(prefabName);
        }

        private ConfigEntry<T> BindServerConfig<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            ConfigEntry<T> entry = Config.Bind(section, key, defaultValue, description);
            ConfigSync.AddConfigEntry(entry, ConfigSyncMode.AlwaysServerControlled);
            return entry;
        }

        private ConfigEntry<T> BindServerConfig<T>(string section, string key, T defaultValue, string description)
        {
            return BindServerConfig(section, key, defaultValue, new ConfigDescription(description));
        }

        private ConfigEntry<T> BindClientConfig<T>(string section, string key, T defaultValue, string description)
        {
            ConfigEntry<T> entry = Config.Bind(section, key, defaultValue, description);
            ConfigSync.AddConfigEntry(entry, ConfigSyncMode.AlwaysClientControlled);
            return entry;
        }

        internal static void LogInfo(object value)
        {
            if (LoggingEnabled?.Value == true)
            {
                Instance.Logger.LogInfo(value);
            }
        }

        internal static void LogWarning(object value)
        {
            Instance.Logger.LogWarning(value);
        }
    }
}
