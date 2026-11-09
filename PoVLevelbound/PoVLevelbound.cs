using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.UI;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace PoVLevelbound
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("treextr.pathofvalheiman", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "Dorn.PoVLevelbound",
            Name = "PoV Levelbound",
            Version = "1.0.0";
        internal const string BlockedRpc = "PoVLevelbound_Blocked";
        const string SyncedBiomesKey = "biomas.yml";
        const string SyncedMaterialsKey = "materiais.yml";
        const string SyncedEquipmentKey = "equipamentos.yml";
        const string SyncedConstructionsKey = "construcoes.yml";
        const string SyncedResourcesKey = "recursos.yml";
        const string SyncedAltarsKey = "altares.yml";
        internal static Plugin Instance;
        static readonly ConfigSync Sync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version,
            ModRequired = true,
        };
        static readonly CustomSyncedValue<Dictionary<string, string>> YamlData =
            new CustomSyncedValue<Dictionary<string, string>>(Sync, "Levelbound YAML");
        static readonly Heightmap.Biome[] Progression =
        {
            Heightmap.Biome.Meadows,
            Heightmap.Biome.BlackForest,
            Heightmap.Biome.Swamp,
            Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains,
            Heightmap.Biome.Mistlands,
            Heightmap.Biome.Ocean,
            Heightmap.Biome.AshLands,
            Heightmap.Biome.DeepNorth,
        };
        static readonly HashSet<string> EquipmentTypes = new HashSet<string>(
            new[] {
                "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow",
                "Shield", "Helmet", "Chest", "Ammo", "Legs", "Torch", "Shoulder",
                "Utility", "Tool", "Hands", "Trinket"
            },
            StringComparer.OrdinalIgnoreCase
        );
        readonly Dictionary<string, Heightmap.Biome> itemBiomes = new Dictionary<
            string,
            Heightmap.Biome
        >(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Heightmap.Biome> constructionBiomes = new Dictionary<
            string,
            Heightmap.Biome
        >(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Heightmap.Biome> resourceBiomes = new Dictionary<
            string,
            Heightmap.Biome
        >(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> altarLevels = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase
        );
        ConfigEntry<bool> locked,
            admin,
            generate,
            generateDefaults,
            diagnostic;
        ConfigEntry<bool> blockPickup,
            blockCraft,
            blockUpgrade,
            blockEquip,
            blockConsume,
            blockPlacement,
            blockRepair,
            blockRemoval,
            blockResources,
            blockAltars;
        ConfigEntry<string> blockedActionMessage;
        ConfigEntry<float> blockedActionCooldown;
        readonly Dictionary<Heightmap.Biome, ConfigEntry<int>> globalLevels =
            new Dictionary<Heightmap.Biome, ConfigEntry<int>>();
        FileSystemWatcher watcher,
            configWatcher;
        Coroutine debounceCoroutine;
        DateTime lastConfigReload = DateTime.MinValue;
        MethodInfo povProgressPeek;
        FieldInfo povProgressLevel;
        bool defaultsGenerated;
        float nextItemMessage;
        string Folder
        {
            get { return Path.Combine(Paths.ConfigPath, "PoVLevelbound"); }
        }
        string Biomes
        {
            get { return Path.Combine(Folder, "biomas.yml"); }
        }
        string Materials
        {
            get { return Path.Combine(Folder, "materiais.yml"); }
        }
        string Equipment
        {
            get { return Path.Combine(Folder, "equipamentos.yml"); }
        }
        string Constructions
        {
            get { return Path.Combine(Folder, "construcoes.yml"); }
        }
        string Resources
        {
            get { return Path.Combine(Folder, "recursos.yml"); }
        }
        string Altars
        {
            get { return Path.Combine(Folder, "altares.yml"); }
        }
        string Helper
        {
            get { return Path.Combine(Folder, "todos-itens-equipaveis.yml"); }
        }

        void Awake()
        {
            Instance = this;
            Directory.CreateDirectory(Folder);
            BindAll();
            CreateFiles();
            YamlData.ValueChanged += DebouncedLoad;
            if (Sync.IsSourceOfTruth)
                YamlData.AssignLocalValue(ReadYaml());
            Watch();
            WatchConfig();
            DebouncedLoad();
            new Harmony(Guid).PatchAll();
            StartCoroutine(InstallPathOfValheimanTooltipPatch());
            StartCoroutine(InstallMarketplaceTooltipPatch());
            StartCoroutine(InstallMarketplacePurchasePatches());
            ValidateReflectionFields();
            StartCoroutine(GenerateDefaultsWhenReady());
            Logger.LogInfo("Levelbound " + Version + " loaded.");
        }

        // PoV is loaded after Levelbound, so its private tooltip model is not
        // available during PatchAll(). Wait for its assembly, then patch its
        // final data model without taking a compile-time dependency on PoV.
        IEnumerator InstallPathOfValheimanTooltipPatch()
        {
            for (int frame = 0; frame < 300; ++frame)
            {
                Type tooltipType = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    tooltipType = assembly.GetType("PassiveTree.UI.ItemTooltip", false);
                    if (tooltipType != null)
                        break;
                }
                if (tooltipType != null)
                {
                    MethodInfo build = AccessTools.Method(
                        tooltipType,
                        "Build",
                        new[] { typeof(ItemDrop.ItemData) }
                    );
                    MethodInfo postfix = AccessTools.Method(
                        typeof(PathOfValheimanModelPatch),
                        "Postfix"
                    );
                    if (build != null && postfix != null)
                    {
                        new Harmony(Guid + ".PathOfValheimanTooltip").Patch(
                            build,
                            postfix: new HarmonyMethod(postfix)
                        );
                        Logger.LogInfo("Levelbound: Path of Valheiman custom tooltip compatibility enabled.");
                    }
                    yield break;
                }
                yield return null;
            }
            Logger.LogDebug("Levelbound: Path of Valheiman was not detected; using vanilla tooltips.");
        }

        IEnumerator InstallMarketplaceTooltipPatch()
        {
            for (int frame = 0; frame < 300; ++frame)
            {
                Type marketplaceUtils = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    marketplaceUtils = assembly.GetType("Marketplace.Utils", false);
                    if (marketplaceUtils != null)
                        break;
                }
                if (marketplaceUtils != null)
                {
                    MethodInfo tooltip = AccessTools.Method(
                        marketplaceUtils,
                        "GetItemForTooltip",
                        new[] {
                            typeof(string), typeof(int), typeof(int), typeof(int), typeof(long),
                            typeof(string), typeof(string), typeof(byte), typeof(Action<ItemDrop.ItemData>)
                        }
                    );
                    MethodInfo prefix = AccessTools.Method(typeof(MarketplaceTooltipPatch), "Prefix");
                    MethodInfo postfix = AccessTools.Method(
                        typeof(MarketplaceTooltipPatch),
                        "Postfix"
                    );
                    if (tooltip != null && prefix != null && postfix != null)
                    {
                        new Harmony(Guid + ".MarketplaceTooltip").Patch(
                            tooltip,
                            prefix: new HarmonyMethod(prefix),
                            postfix: new HarmonyMethod(postfix)
                        );
                        Logger.LogInfo("Levelbound: Marketplace tooltip compatibility enabled.");
                    }
                    yield break;
                }
                yield return null;
            }
            Logger.LogDebug("Levelbound: Marketplace was not detected; using vanilla tooltips.");
        }

        IEnumerator InstallMarketplacePurchasePatches()
        {
            for (int frame = 0; frame < 300; ++frame)
            {
                Type clientType = null;
                Type serverType = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (clientType == null)
                        clientType = assembly.GetType("Marketplace.Modules.MainMarketplace.Marketplace_UI", false);
                    if (serverType == null)
                        serverType = assembly.GetType("Marketplace.Modules.MainMarketplace.Marketplace_Main_Server", false);
                }
                if (clientType != null && serverType != null)
                {
                    Harmony harmony = new Harmony(Guid + ".MarketplacePurchase");
                    MethodInfo buy = AccessTools.Method(clientType, "BUYITEMCLICK");
                    MethodInfo select = AccessTools.Method(clientType, "OnClickBUYItem");
                    MethodInfo request = AccessTools.Method(serverType, "RequestBuyItem");
                    if (buy != null)
                        harmony.Patch(buy, prefix: new HarmonyMethod(AccessTools.Method(typeof(MarketplacePurchasePatch), "ClientBuyPrefix")));
                    if (select != null)
                        harmony.Patch(select, postfix: new HarmonyMethod(AccessTools.Method(typeof(MarketplacePurchasePatch), "ClientSelectionPostfix")));
                    if (request != null)
                        harmony.Patch(request, prefix: new HarmonyMethod(AccessTools.Method(typeof(MarketplacePurchasePatch), "ServerBuyPrefix")));
                    Logger.LogInfo("Levelbound: Marketplace purchase protection enabled.");
                    yield break;
                }
                yield return null;
            }
            Logger.LogDebug("Levelbound: Marketplace purchase protection was not installed because Marketplace was not detected.");
        }

        void OnDestroy()
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            if (configWatcher != null)
            {
                configWatcher.EnableRaisingEvents = false;
                configWatcher.Dispose();
            }
        }

        internal void Warn(string message)
        {
            Logger.LogWarning(message);
        }

        ConfigEntry<T> Bind<T>(string s, string k, T d, string text)
        {
            var e = Config.Bind(s, k, d, text);
            Sync.AddConfigEntry(e);
            return e;
        }

        void BindAll()
        {
            locked = Bind(
                "General",
                "LockConfiguration",
                true,
                "Prevents clients from overriding server configuration."
            );
            admin = Bind(
                "General",
                "AdminBypass",
                true,
                "Administrators and the host bypass restrictions."
            );
            generate = Bind(
                "General",
                "GenerateEquipableItemList",
                false,
                "Generates the grouped reference list once. It is never applied."
            );
            generateDefaults = Bind(
                "General",
                "GenerateFilesAutomatically",
                true,
                "Generates rules once on the server, then turns itself off. Re-enable it to regenerate rules after adding content mods."
            );
            Config.Remove(new ConfigDefinition("Geral", "GerarPadroesVanillaAutomaticamente"));
            diagnostic = Bind(
                "General",
                "DiagnosticMode",
                false,
                "Logs loaded rules and unresolved recipes."
            );
            blockPickup = Bind("Item Restrictions", "BlockPickup", true, "Blocks picking up items listed in materiais.yml and equipamentos.yml.");
            blockCraft = Bind("Item Restrictions", "BlockCrafting", true, "Blocks crafting items listed in materiais.yml and equipamentos.yml.");
            blockUpgrade = Bind("Item Restrictions", "BlockUpgrading", true, "Blocks upgrading items listed in materiais.yml and equipamentos.yml.");
            blockEquip = Bind("Item Restrictions", "BlockEquipping", true, "Blocks equipping items listed in equipamentos.yml.");
            blockConsume = Bind("Item Restrictions", "BlockConsuming", true, "Blocks consuming items listed in materiais.yml.");
            blockPlacement = Bind("Building Restrictions", "BlockPlacement", true, "Blocks placing pieces listed in construcoes.yml.");
            blockRepair = Bind("Building Restrictions", "BlockRepair", true, "Blocks repairing pieces listed in construcoes.yml.");
            blockRemoval = Bind("Building Restrictions", "BlockRemoval", true, "Blocks removing pieces listed in construcoes.yml.");
            blockResources = Bind("Resource Restrictions", "BlockResources", true, "Blocks resources listed in recursos.yml.");
            blockAltars = Bind("Altar Restrictions", "BlockAltars", true, "Blocks altars listed in altares.yml.");
            blockedActionMessage = Bind(
                "Messages",
                "BlockedActionMessage",
                "<color=#ff6666>Action blocked</color>: requires PoV level {required}. Your level: {current}.",
                "Use {required} and {current}. Legacy placeholders {nivel} and {atual} are also supported."
            );
            blockedActionCooldown = Bind(
                "Messages",
                "BlockedActionMessageCooldown",
                2f,
                "Minimum interval in seconds between blocked-action messages."
            );
            AddGlobalLevel("Meadows", Heightmap.Biome.Meadows, 1);
            AddGlobalLevel("BlackForest", Heightmap.Biome.BlackForest, 19);
            AddGlobalLevel("Swamp", Heightmap.Biome.Swamp, 43);
            AddGlobalLevel("Mountain", Heightmap.Biome.Mountain, 77);
            AddGlobalLevel("Plains", Heightmap.Biome.Plains, 97);
            AddGlobalLevel("Mistlands", Heightmap.Biome.Mistlands, 130);
            AddGlobalLevel("Ocean", Heightmap.Biome.Ocean, 130);
            AddGlobalLevel("AshLands", Heightmap.Biome.AshLands, 181);
            AddGlobalLevel("DeepNorth", Heightmap.Biome.DeepNorth, 200);
            RemoveLegacyConfig();
            Sync.AddLockingConfigEntry(locked);
        }

        void AddGlobalLevel(string name, Heightmap.Biome biome, int level)
        {
            globalLevels[biome] = Bind(
                "Biome Levels",
                "Level" + name,
                level,
                "Minimum Path of Valheiman level for " + name + ". Use 0 to disable."
            );
        }

        void RemoveLegacyConfig()
        {
            string[] currentPortugueseGeneral =
            {
                "TravarConfiguracaoNoServidor", "IgnorarRestricoesParaAdmin",
                "GerarListaDeItensEquipaveis", "GerarArquivosAutomaticamente", "ModoDiagnostico"
            };
            foreach (string key in currentPortugueseGeneral)
                Config.Remove(new ConfigDefinition("Geral", key));
            foreach (var pair in new[] {
                new { Section = "Bloqueios de Itens", Keys = new[] { "BloquearColeta", "BloquearCraft", "BloquearMelhoria", "BloquearEquipar", "BloquearConsumir" } },
                new { Section = "Bloqueios de Construções", Keys = new[] { "BloquearColocacao", "BloquearReparo", "BloquearRemocao" } },
                new { Section = "Bloqueios de Recursos", Keys = new[] { "BloquearRecursos" } },
                new { Section = "Bloqueios de Altares", Keys = new[] { "BloquearAltares" } },
                new { Section = "Mensagens", Keys = new[] { "MensagemDeAcaoBloqueada", "IntervaloEntreMensagensDeAcao" } }
            })
                foreach (string key in pair.Keys)
                    Config.Remove(new ConfigDefinition(pair.Section, key));
            foreach (Heightmap.Biome biome in Progression)
                Config.Remove(new ConfigDefinition("Niveis por Bioma", "Nivel" + BiomeName(biome)));

            var sections = new List<string> { "Regras Globais" };
            for (int i = 0; i < Progression.Length; ++i)
                sections.Add(
                    "Biomas "
                        + (i + 1).ToString("D2")
                        + " - "
                        + BiomeName(Progression[i])
                );
            string[] keys =
            {
                "NivelMinimo", "BloquearAcessoAoBioma", "BloquearCriacao",
                "BloquearEquipar", "BloquearConsumir", "BloquearEntradaEPermanencia",
                "BloquearDungeon", "BloquearTeleporte", "BloquearConstrucao",
                "BloquearReparo", "BloquearRemocaoDePeca", "BloquearAlteracaoDeTerreno",
                "BloquearPortaOuPortao", "BloquearBauOuConteiner", "BloquearEstacao",
                "BloquearAltar", "BloquearComerciante", "BloquearMapa",
                "BloquearMarcadorDoMapa", "BloquearCraft", "BloquearMelhoria",
                "BloquearUsarItem", "BloquearConsumirItem", "BloquearRecursosDoBioma",
                "BloquearSpawnDeCriaturas"
            };
            foreach (string section in sections)
                foreach (string key in keys)
                    Config.Remove(new ConfigDefinition(section, key));
            Config.Remove(new ConfigDefinition("Geral", "UsarRegrasPorBioma"));
            Config.Remove(new ConfigDefinition("Mensagens", "MostrarAvisoAoEntrarNoBioma"));
            Config.Remove(new ConfigDefinition("Mensagens", "DuracaoDaMensagemDeAcao"));
            foreach (Heightmap.Biome biome in Progression)
                Config.Remove(
                    new ConfigDefinition(
                        "Regras Globais - Niveis por Bioma",
                        "Nivel" + BiomeName(biome)
                    )
                );
            Config.Save();
        }

        void CreateFiles()
        {
            if (!File.Exists(Materials))
            {
                string migrated = File.Exists(Biomes) ? File.ReadAllText(Biomes) : "";
                File.WriteAllText(
                    Materials,
                    MappingTemplate(
                        "materials, ingredients, food and other inventory items",
                        "Iron",
                        "Legacy biomas.yml rules are imported; place new rules here."
                    )
                        + (string.IsNullOrEmpty(migrated)
                            ? ""
                            : "\n# Rules automatically imported from biomas.yml.\n" + migrated)
                );
                if (!string.IsNullOrEmpty(migrated))
                    Logger.LogInfo("Levelbound: existing rules from biomas.yml were migrated to materiais.yml. Use materiais.yml for new rules going forward.");
            }
            if (!File.Exists(Equipment))
                File.WriteAllText(
                    Equipment,
                    MappingTemplate(
                        "weapons, armor, shields, tools, ammunition and accessories",
                        "SwordIron",
                        "Registered equipment can be listed here; Unclassified does not apply a level."
                    ) + "\nUnclassified:\n#  - EquipmentWithoutClassifiableRecipe\n"
                );
            if (!File.Exists(Constructions))
                File.WriteAllText(
                    Constructions,
                    MappingTemplate(
                        "build-menu pieces",
                        "fire_pit",
                        "Pieces receive a restriction, message and requirement in the hammer interface."
                    )
                );
            if (!File.Exists(Resources))
                File.WriteAllText(
                    Resources,
                    MappingTemplate(
                        "world resources",
                        "MineRock_Copper",
                        "Listed trees, ores, rocks and pickables require the biome level."
                    )
                );
            if (!File.Exists(Altars))
                File.WriteAllText(
                    Altars,
                    "# Levelbound: direct levels for standard Valheim altars.\n"
                        + "# The key can be the location/altar prefab or its associated boss prefab.\n"
                        + "# Level 0 disables the restriction. Known aliases are retained for compatibility.\n\n"
                        + "Eikthyrnir: 1\nEikthyr: 1\n"
                        + "GDKing: 10\ngd_king: 10\n"
                        + "Bonemass: 20\n"
                        + "Dragonqueen: 30\nDragon: 30\n"
                        + "GoblinKing: 40\n"
                        + "SeekerQueen: 50\n"
                        + "FaderLocation: 60\nFader: 60\n"
                );
        }

        string MappingTemplate(string kind, string example, string behavior)
        {
            var s = new StringBuilder(
                "# Levelbound: this YAML maps prefabs of "
                    + kind
                    + " to a biome.\n# "
                    + behavior
                    + "\n# Levels are configured in the CFG and inherited from the assigned biome.\n# Do not rename the eight biome sections.\n# Reference never applies rules.\n\nReference:\n  Examples:\n    - "
                    + example
                    + "\n\n"
            );
            foreach (var b in Progression)
                s.Append(BiomeName(b)).Append(":\n#  - NomeDoPrefab\n");
            return s.ToString();
        }

        Dictionary<string, string> ReadYaml()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(Biomes))
                d[SyncedBiomesKey] = File.ReadAllText(Biomes);
            if (File.Exists(Materials))
                d[SyncedMaterialsKey] = File.ReadAllText(Materials);
            if (File.Exists(Equipment))
                d[SyncedEquipmentKey] = File.ReadAllText(Equipment);
            if (File.Exists(Constructions))
                d[SyncedConstructionsKey] = File.ReadAllText(Constructions);
            if (File.Exists(Resources))
                d[SyncedResourcesKey] = File.ReadAllText(Resources);
            if (File.Exists(Altars))
                d[SyncedAltarsKey] = File.ReadAllText(Altars);
            return d;
        }

        void Watch()
        {
            try
            {
                watcher = new FileSystemWatcher(Folder, "*.yml");
                watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                watcher.Changed += Changed;
                watcher.Created += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += Changed;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Could not watch YAML files: " + e.Message);
            }
        }

        void WatchConfig()
        {
            try
            {
                string configPath = Config.ConfigFilePath;
                configWatcher = new FileSystemWatcher(
                    Path.GetDirectoryName(configPath),
                    Path.GetFileName(configPath)
                );
                configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                configWatcher.NotifyFilter =
                    NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.FileName
                    | NotifyFilters.CreationTime;
                configWatcher.Changed += ConfigChanged;
                configWatcher.Created += ConfigChanged;
                configWatcher.Renamed += ConfigChanged;
                configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Could not watch the CFG: " + e.Message);
            }
        }

        void ConfigChanged(object sender, FileSystemEventArgs args)
        {
            if (!Sync.IsSourceOfTruth)
                return;
            if ((DateTime.UtcNow - lastConfigReload).TotalMilliseconds < 750)
                return;
            lastConfigReload = DateTime.UtcNow;
            try
            {
                Config.Reload();
                if (generateDefaults.Value && !defaultsGenerated)
                    StartCoroutine(GenerateDefaultsWhenReady());
                Logger.LogInfo("CFG reloaded and synchronized in real time.");
            }
            catch (Exception e)
            {
                Logger.LogWarning("Failed to reload CFG: " + e.Message);
            }
        }

        void Changed(object o, FileSystemEventArgs e)
        {
            if (!Sync.IsSourceOfTruth)
                return;
            try
            {
                YamlData.AssignLocalValue(ReadYaml());
                DebouncedLoad();
            }
            catch (Exception x)
            {
                Logger.LogWarning("Failed to read YAML files: " + x.Message);
            }
        }

        void DebouncedLoad()
        {
            // True debounce: resets the timer on every event and only fires
            // after the last change, preventing partial-write reads.
            if (debounceCoroutine != null)
                StopCoroutine(debounceCoroutine);
            debounceCoroutine = StartCoroutine(DelayedLoad());
        }

        IEnumerator DelayedLoad()
        {
            yield return new WaitForSeconds(0.5f);
            debounceCoroutine = null;
            LoadRules();
        }

        void ValidateReflectionFields()
        {
            if (CurrentContainerField == null)
                Logger.LogError("Levelbound: InventoryGui.m_currentContainer not found — container transfer blocking is disabled. A Valheim update may have renamed this field.");
            if (typeof(InventoryGui).GetField("m_selectedRecipe", BindingFlags.Instance | BindingFlags.NonPublic) == null)
                Logger.LogError("Levelbound: InventoryGui.m_selectedRecipe not found — craft/upgrade blocking is disabled. A Valheim update may have renamed this field.");
            if (typeof(InventoryGui).GetField("m_craftUpgradeItem", BindingFlags.Instance | BindingFlags.NonPublic) == null)
                Logger.LogWarning("Levelbound: InventoryGui.m_craftUpgradeItem not found — upgrade detection may not work correctly.");
            if (typeof(InventoryGui).GetField("m_dragItem", BindingFlags.Instance | BindingFlags.NonPublic) == null)
                Logger.LogWarning("Levelbound: InventoryGui.m_dragItem not found — drag-swap blocking from chest is disabled. A Valheim update may have renamed this field.");
            if (typeof(InventoryGui).GetField("m_dragInventory", BindingFlags.Instance | BindingFlags.NonPublic) == null)
                Logger.LogWarning("Levelbound: InventoryGui.m_dragInventory not found — drag-swap blocking from chest is disabled. A Valheim update may have renamed this field.");
        }

        void LoadRules()
        {
            itemBiomes.Clear();
            constructionBiomes.Clear();
            resourceBiomes.Clear();
            altarLevels.Clear();
            string text;
            var data = YamlData.Value;
            if (data != null && data.TryGetValue(SyncedBiomesKey, out text))
                ParseMappings(text, itemBiomes);
            if (data != null && data.TryGetValue(SyncedMaterialsKey, out text))
                ParseMappings(text, itemBiomes);
            if (data != null && data.TryGetValue(SyncedEquipmentKey, out text))
                ParseMappings(text, itemBiomes);
            if (data != null && data.TryGetValue(SyncedConstructionsKey, out text))
                ParseMappings(text, constructionBiomes);
            if (data != null && data.TryGetValue(SyncedResourcesKey, out text))
                ParseMappings(text, resourceBiomes);
            if (data != null && data.TryGetValue(SyncedAltarsKey, out text))
                ParseDirectLevels(text, altarLevels);
            Logger.LogInfo(
                "Synchronized YAML loaded: "
                    + itemBiomes.Count
                    + " items and "
                    + constructionBiomes.Count
                    + " build pieces, "
                    + resourceBiomes.Count
                    + " resources and "
                    + altarLevels.Count
                    + " altar identifiers."
            );
        }

        static string V(string s)
        {
            int colon = s.IndexOf(':');
            return colon < 0 ? "" : s.Substring(colon + 1).Trim().Trim('"', '\'');
        }

        static string BiomeName(Heightmap.Biome b)
        {
            return b == Heightmap.Biome.BlackForest ? "BlackForest"
                : b == Heightmap.Biome.AshLands ? "AshLands"
                : b.ToString();
        }

        static bool TryBiome(string text, out Heightmap.Biome biome)
        {
            foreach (var b in Progression)
                if (string.Equals(BiomeName(b), text, StringComparison.OrdinalIgnoreCase))
                {
                    biome = b;
                    return true;
                }
            biome = Heightmap.Biome.None;
            return false;
        }

        void ParseMappings(string yaml, Dictionary<string, Heightmap.Biome> target)
        {
            Heightmap.Biome current = Heightmap.Biome.None;
            foreach (
                string row in yaml.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                string line = row.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                if (!line.StartsWith("-") && line.EndsWith(":"))
                {
                    Heightmap.Biome b;
                    string sectionName = line.Substring(0, line.Length - 1).Trim();
                    if (TryBiome(sectionName, out b))
                    {
                        current = b;
                    }
                    else
                    {
                        current = Heightmap.Biome.None;
                        // Warn only for unknown names that aren't reserved non-biome sections.
                        if (!string.Equals(sectionName, "Reference", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(sectionName, "Unclassified", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(sectionName, "Examples", StringComparison.OrdinalIgnoreCase))
                            Logger.LogWarning("Levelbound: unrecognized YAML section '" + sectionName + "' — check the biome name for typos. Rules in this section will be ignored.");
                    }
                    continue;
                }
                if (current == Heightmap.Biome.None || !line.StartsWith("-"))
                    continue;
                string prefab = line.Substring(1).Trim();
                if (prefab.StartsWith("PrefabName:"))
                    prefab = V(prefab);
                if (!string.IsNullOrEmpty(prefab))
                    target[prefab] = current;
            }
        }

        HashSet<string> ParseAllListedPrefabs(string yaml)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(yaml))
                return result;
            foreach (string row in yaml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = row.Trim();
                if (!line.StartsWith("-") || line.StartsWith("#"))
                    continue;
                string prefab = line.Substring(1).Trim();
                if (prefab.StartsWith("PrefabName:"))
                    prefab = V(prefab);
                if (!string.IsNullOrEmpty(prefab))
                    result.Add(prefab);
            }
            return result;
        }

        bool IsEquipment(GameObject prefab)
        {
            ItemDrop drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
            return drop != null
                && drop.m_itemData != null
                && drop.m_itemData.m_shared != null
                && EquipmentTypes.Contains(drop.m_itemData.m_shared.m_itemType.ToString());
        }

        void AppendUnclassifiedEquipment(IEnumerable<string> names)
        {
            var sorted = new List<string>(names);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            if (sorted.Count == 0)
                return;
            var text = new StringBuilder("\n# Registered equipment without a classifiable recipe.\nUnclassified:\n");
            foreach (string name in sorted)
                text.Append("  - ").Append(name).Append('\n');
            File.AppendAllText(Equipment, text.ToString());
        }

        void ParseDirectLevels(string yaml, Dictionary<string, int> target)
        {
            foreach (string row in yaml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = row.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                string prefab = line.Substring(0, colon).Trim().Trim('"', '\'');
                int level;
                if (int.TryParse(line.Substring(colon + 1).Trim(), out level))
                    target[prefab] = Math.Max(0, level);
            }
        }

        // Path of Valheiman persists the character level in Player.m_customData.
        // PoVMonsterLevels reads PassiveTree.Progress.Peek(player).Level first;
        // the persisted value below is the safe fallback while PoV initializes.
        const string PoVLevelKey = "pt.level";

        internal int Level(Player player = null)
        {
            Player target = player ?? Player.m_localPlayer;
            if (target == null)
                return 0;
            try
            {
                int level;
                if (TryPoVProgressLevel(target, out level))
                    return level;

                string value;
                return target.m_customData != null
                    && target.m_customData.TryGetValue(PoVLevelKey, out value)
                    && int.TryParse(value, out level)
                    ? Math.Max(1, level)
                    // PoV starts every new character at level one. Its custom
                    // data can be populated a few frames after Player spawn.
                    : 1;
            }
            catch
            {
                // A player may be despawned while an RPC/action is processed.
                // An unavailable value must never bypass a level requirement.
                return 0;
            }
        }

        bool TryPoVProgressLevel(Player player, out int level)
        {
            level = 0;
            try
            {
                if (povProgressPeek == null || povProgressLevel == null)
                {
                    Type progress = null;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        progress = assembly.GetType("PassiveTree.Progress", false);
                        if (progress != null)
                            break;
                    }
                    if (progress == null)
                        return false;
                    povProgressPeek = progress.GetMethod(
                        "Peek",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Player) },
                        null
                    );
                    povProgressLevel = progress.GetField(
                        "Level",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                }
                object progressState = povProgressPeek == null
                    ? null
                    : povProgressPeek.Invoke(null, new object[] { player });
                object rawLevel = progressState == null || povProgressLevel == null
                    ? null
                    : povProgressLevel.GetValue(progressState);
                if (rawLevel is int)
                {
                    level = Math.Max(1, (int)rawLevel);
                    return true;
                }
            }
            catch
            {
                // The persisted PoV value remains available as a fallback.
            }
            return false;
        }

        internal bool Bypass(Player player = null)
        {
            if (!admin.Value)
                return false;

            // LocalPlayerIsAdminOrHost is a local-client query. It must never
            // be used to approve an arbitrary remote player on a dedicated
            // server.
            if (player == null || player == Player.m_localPlayer)
                return ZNet.instance == null || ZNet.instance.LocalPlayerIsAdminOrHost();

            // Dedicated servers can validate a remote administrator by that
            // player's name; LocalPlayerIsAdminOrHost cannot do this.
            return ZNet.instance != null && ZNet.instance.IsAdmin(player.GetPlayerName());
        }

        int RequiredLevel(Heightmap.Biome biome)
        {
            ConfigEntry<int> level;
            return globalLevels.TryGetValue(biome, out level) ? Math.Max(0, level.Value) : 0;
        }

        static string PrefabName(GameObject gameObject)
        {
            if (gameObject == null)
                return "";
            string prefab = gameObject.name;
            const string clone = "(Clone)";
            return prefab.EndsWith(clone, StringComparison.Ordinal)
                ? prefab.Substring(0, prefab.Length - clone.Length)
                : prefab;
        }

        bool ItemRule(ItemDrop.ItemData item, out Heightmap.Biome biome, string prefabOverride = null)
        {
            biome = Heightmap.Biome.None;
            if (!string.IsNullOrEmpty(prefabOverride) && itemBiomes.TryGetValue(prefabOverride, out biome))
                return true;
            if (item == null)
                return false;
            if (item.m_dropPrefab != null && itemBiomes.TryGetValue(PrefabName(item.m_dropPrefab), out biome))
                return true;
            return false;
        }

        internal bool Blocked(
            ItemDrop.ItemData item,
            ActionKind action,
            out int requirement,
            Player player = null,
            string prefabOverride = null
        )
        {
            requirement = 0;
            if (Bypass(player))
                return false;
            Heightmap.Biome b;
            if (!ItemRule(item, out b, prefabOverride))
                return false;
            int required = RequiredLevel(b);
            if (required <= 0 || Level(player) >= required)
                return false;
            bool enabled = action == ActionKind.Craft ? blockCraft.Value
                : action == ActionKind.Upgrade ? blockUpgrade.Value
                : action == ActionKind.Equip ? blockEquip.Value
                : blockConsume.Value;
            if (!enabled)
                return false;
            requirement = required;
            return true;
        }

        internal bool ItemLevelLocked(
            ItemDrop.ItemData item,
            out int requirement,
            Player player = null
        )
        {
            requirement = 0;
            if (Bypass(player))
                return false;
            Heightmap.Biome biome;
            if (!ItemRule(item, out biome))
                return false;
            requirement = RequiredLevel(biome);
            return requirement > 0 && Level(player) < requirement;
        }

        internal bool BlockItemPickup(ItemDrop.ItemData item, Player player)
        {
            int requirement;
            if (!blockPickup.Value || !ItemLevelLocked(item, out requirement, player))
                return false;
            Tell(player, requirement);
            return true;
        }

        static readonly FieldInfo CurrentContainerField = typeof(InventoryGui).GetField(
            "m_currentContainer",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        internal bool IsContainerToPlayerTransfer(Inventory destination, Inventory source)
        {
            Player player = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            if (
                player == null
                || gui == null
                || destination == null
                || source == null
                || destination != player.GetInventory()
                || CurrentContainerField == null
            )
                return false;
            Container container = CurrentContainerField.GetValue(gui) as Container;
            return container != null && source == container.GetInventory();
        }

        internal bool BlockContainerTransfer(
            Inventory destination,
            Inventory source,
            ItemDrop.ItemData item
        )
        {
            return IsContainerToPlayerTransfer(destination, source)
                && BlockItemPickup(item, Player.m_localPlayer);
        }

        internal bool AllowAutoPickup(ItemDrop drop)
        {
            if (!blockPickup.Value || drop == null || Player.m_localPlayer == null)
                return true;
            int requirement;
            return !ItemLevelLocked(drop.m_itemData, out requirement, Player.m_localPlayer);
        }

        internal bool BlockPickupPrefab(GameObject prefab, Player player)
        {
            if (!blockPickup.Value || prefab == null || Bypass(player))
                return false;
            Heightmap.Biome biome;
            if (!itemBiomes.TryGetValue(PrefabName(prefab), out biome))
                return false;
            int requirement = RequiredLevel(biome);
            if (requirement <= 0 || Level(player) >= requirement)
                return false;
            Tell(player, requirement);
            return true;
        }

        internal void Tell(Player player, int n)
        {
            if (player == null)
                return;
            // Server-side block: the server has no local MessageHud, so route the
            // feedback to the owning client via RPC instead of discarding it.
            if (player != Player.m_localPlayer)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                    return;
                ZNetView view = player.GetComponent<ZNetView>();
                ZDO zdo = view == null ? null : view.GetZDO();
                if (zdo != null)
                    ZRoutedRpc.instance.InvokeRoutedRPC(zdo.GetOwner(), BlockedRpc, n);
                return;
            }
            if (Time.time < nextItemMessage)
                return;
            nextItemMessage = Time.time + Mathf.Max(0f, blockedActionCooldown.Value);
            string configuredMessage = blockedActionMessage
                .Value.Replace("{required}", n.ToString())
                .Replace("{current}", Level(player).ToString())
                .Replace("{nivel}", n.ToString())
                .Replace("{atual}", Level(player).ToString());
            player.Message(
                MessageHud.MessageType.Center,
                configuredMessage,
                0,
                null,
                false
            );
        }

        internal string RequirementText(ItemDrop.ItemData item, ActionKind action, string prefabOverride = null)
        {
            bool enabled = action == ActionKind.Craft ? blockCraft.Value
                : action == ActionKind.Upgrade ? blockUpgrade.Value
                : action == ActionKind.Equip ? blockEquip.Value
                : blockConsume.Value;
            if (!enabled)
                return "";
            Heightmap.Biome b;
            if (!ItemRule(item, out b, prefabOverride))
                return "";
            int required = RequiredLevel(b);
            if (required <= 0)
                return "";
            int current = Level();
            string color = current >= required ? "#66ff66" : "#ff6666";
            return "\n<color=" + color + ">" + RequirementLabel(required, current) + "</color>";
        }

        string RequirementLabel(int required, int current)
        {
            string language = Localization.instance == null ? "English" : Localization.instance.GetSelectedLanguage();
            language = language ?? "English";
            if (language.StartsWith("Portuguese", StringComparison.OrdinalIgnoreCase))
                return "Requer nível " + required + " (seu nível: " + current + ")";
            if (language.StartsWith("Spanish", StringComparison.OrdinalIgnoreCase))
                return "Requiere nivel " + required + " (tu nivel: " + current + ")";
            if (language.StartsWith("French", StringComparison.OrdinalIgnoreCase))
                return "Niveau requis : " + required + " (votre niveau : " + current + ")";
            if (language.StartsWith("German", StringComparison.OrdinalIgnoreCase))
                return "Benötigt Stufe " + required + " (deine Stufe: " + current + ")";
            if (language.StartsWith("Russian", StringComparison.OrdinalIgnoreCase))
                return "Требуется уровень " + required + " (ваш уровень: " + current + ")";
            if (language.StartsWith("Turkish", StringComparison.OrdinalIgnoreCase))
                return "Gereken seviye " + required + " (seviyen: " + current + ")";
            return "Requires level " + required + " (your level: " + current + ")";
        }

        // Path of Valheiman builds its own tooltip surface after the vanilla
        // ItemData.GetTooltip call. Keep this marker separate from the
        // localized message used by Tell(), so a later UI refresh can safely
        // detect an already-inserted requirement.
        internal bool AppendRequirement(ref string text, ItemDrop.ItemData item, ActionKind action, string prefabOverride = null)
        {
            if (item == null || text == null)
                return false;
            string requirement = RequirementText(item, action, prefabOverride);
            if (string.IsNullOrEmpty(requirement) || text.IndexOf(requirement.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            text += requirement;
            return true;
        }

        internal bool MarketplaceItemLocked(string prefab, Player player, out int requirement)
        {
            requirement = 0;
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null)
                return false;
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop drop = itemPrefab == null ? null : itemPrefab.GetComponent<ItemDrop>();
            return drop != null && drop.m_itemData != null && ItemLevelLocked(drop.m_itemData, out requirement, player);
        }

        internal string RequirementTooltip(int n)
        {
            int current = Level(Player.m_localPlayer);
            return blockedActionMessage.Value
                .Replace("{required}", n.ToString())
                .Replace("{current}", current.ToString())
                .Replace("{nivel}", n.ToString())
                .Replace("{atual}", current.ToString());
        }

        bool ConstructionRule(Piece piece, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;
            if (piece == null || piece.gameObject == null)
                return false;
            return constructionBiomes.TryGetValue(PrefabName(piece.gameObject), out biome);
        }

        internal bool BlockConstruction(
            Piece piece,
            Player player,
            ConfigEntry<bool> toggle,
            string action
        )
        {
            if (!toggle.Value || player == null || Bypass(player))
                return false;
            Heightmap.Biome biome;
            if (!ConstructionRule(piece, out biome))
                return false;
            int required = RequiredLevel(biome);
            int current = Level(player);
            if (required <= 0 || current >= required)
                return false;
            if (diagnostic.Value)
                Logger.LogInfo(
                    action + " bloqueado: prefab="
                        + piece.gameObject.name
                        + ", jogador="
                        + player.GetPlayerName()
                        + ", nivel="
                        + current
                        + ", requerido="
                        + required
                );
            Tell(player, required);
            return true;
        }

        internal string ConstructionRequirementText(Piece piece)
        {
            if (!blockPlacement.Value)
                return "";
            Heightmap.Biome biome;
            if (!ConstructionRule(piece, out biome))
                return "";
            int required = RequiredLevel(biome);
            if (required <= 0)
                return "";
            int current = Level(Player.m_localPlayer);
            string color = current >= required ? "#66ff66" : "#ff6666";
            return "\n<color=" + color + ">" + RequirementLabel(required, current) + "</color>";
        }

        internal bool BlockPlacement(Piece piece, Player player)
        {
            return BlockConstruction(piece, player, blockPlacement, "Colocacao");
        }

        internal bool BlockRepair(Piece piece, Player player)
        {
            return BlockConstruction(piece, player, blockRepair, "Reparo");
        }

        internal bool BlockRemoval(Piece piece, Player player)
        {
            return BlockConstruction(piece, player, blockRemoval, "Remocao");
        }

        internal bool BlockResource(GameObject resource, Player player)
        {
            if (!blockResources.Value || resource == null || player == null || Bypass(player))
                return false;
            Heightmap.Biome biome;
            if (!resourceBiomes.TryGetValue(PrefabName(resource), out biome))
                return false;
            int required = RequiredLevel(biome);
            if (required <= 0 || Level(player) >= required)
                return false;
            Tell(player, required);
            return true;
        }

        internal bool BlockResourceDamage(GameObject resource, HitData hit)
        {
            Player player = hit == null ? null : hit.GetAttacker() as Player;
            return BlockResource(resource, player ?? Player.m_localPlayer);
        }

        bool TryAltarLevel(OfferingBowl altar, out int required)
        {
            required = 0;
            if (altar == null)
                return false;
            Transform current = altar.transform;
            while (current != null)
            {
                if (altarLevels.TryGetValue(PrefabName(current.gameObject), out required))
                    return required > 0;
                current = current.parent;
            }
            return altar.m_bossPrefab != null
                && altarLevels.TryGetValue(PrefabName(altar.m_bossPrefab), out required)
                && required > 0;
        }

        internal bool BlockAltar(OfferingBowl altar, Player player)
        {
            int required;
            if (
                !blockAltars.Value
                || player == null
                || Bypass(player)
                || !TryAltarLevel(altar, out required)
                || Level(player) >= required
            )
                return false;
            Tell(player, required);
            return true;
        }

        internal string AltarRequirementText(OfferingBowl altar)
        {
            if (!blockAltars.Value)
                return "";
            int required;
            if (!TryAltarLevel(altar, out required))
                return "";
            int current = Level(Player.m_localPlayer);
            string color = current >= required ? "#66ff66" : "#ff6666";
            return "\n<color=" + color + ">" + RequirementLabel(required, current) + "</color>";
        }

        IEnumerator GenerateDefaultsWhenReady()
        {
            for (int i = 0; i < 120 && !defaultsGenerated; ++i)
            {
                if (
                    generateDefaults.Value
                    && ZNet.instance != null
                    && ZNet.instance.IsServer()
                    && ObjectDB.instance != null
                    && ZoneSystem.instance != null
                    && ZNetScene.instance != null
                )
                {
                    // Give the base game and content plugins time to finish
                    // registering recipes, creatures and conversion tables.
                    yield return new WaitForSeconds(10f);
                    GenerateVanillaDefaults(ObjectDB.instance);
                    yield break;
                }
                yield return new WaitForSeconds(1f);
            }
        }

        int BiomeOrder(Heightmap.Biome biome)
        {
            return Array.IndexOf(Progression, biome);
        }

        Heightmap.Biome FirstProgressionBiome(Heightmap.Biome mask)
        {
            foreach (Heightmap.Biome biome in Progression)
                if ((mask & biome) != 0)
                    return biome;
            return Heightmap.Biome.None;
        }

        bool SetEarlier(
            Dictionary<string, Heightmap.Biome> target,
            HashSet<string> locked,
            string prefab,
            Heightmap.Biome biome
        )
        {
            if (string.IsNullOrEmpty(prefab) || biome == Heightmap.Biome.None || locked.Contains(prefab))
                return false;
            Heightmap.Biome old;
            if (
                target.TryGetValue(prefab, out old)
                && BiomeOrder(old) <= BiomeOrder(biome)
            )
                return false;
            target[prefab] = biome;
            return true;
        }

        void SeedItem(
            GameObject item,
            Heightmap.Biome biome,
            Dictionary<string, Heightmap.Biome> items,
            HashSet<string> locked
        )
        {
            if (item != null && item.GetComponent<ItemDrop>() != null)
                SetEarlier(items, locked, PrefabName(item), biome);
        }

        void SeedKnownVanillaMaterials(
            ObjectDB db,
            Dictionary<string, Heightmap.Biome> items,
            HashSet<string> locked
        )
        {
            // Some progression materials only exist inside locations/dungeons. Their
            // location prefabs are soft references and are not guaranteed to be loaded
            // on a dedicated server, so the normal world scan cannot discover them.
            // This small set anchors the recipe graph; weapons, armour, food and pieces
            // are still inferred dynamically from the live ObjectDB recipes.
            var known = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
            Action<Heightmap.Biome, string[]> add = (biome, names) =>
            {
                foreach (string name in names)
                    known[name] = biome;
            };
            add(Heightmap.Biome.Meadows, new[] {
                "Wood", "Stone", "Flint", "Resin", "LeatherScraps", "DeerHide",
                "DeerMeat", "BoarMeat", "NeckTail", "Dandelion", "Mushroom",
                "Raspberry", "Honey", "FineWood", "Feathers"
            });
            add(Heightmap.Biome.BlackForest, new[] {
                "CopperOre", "TinOre", "Copper", "Tin", "Bronze", "BronzeNails",
                "RoundLog", "CoreWood", "GreydwarfEye", "SurtlingCore", "TrollHide",
                "CarrotSeeds", "Carrot", "Thistle", "Blueberries", "MushroomYellow",
                "AncientSeed", "BoneFragments", "Coins", "YmirRemains"
            });
            add(Heightmap.Biome.Swamp, new[] {
                "IronScrap", "ScrapIron", "Iron", "IronNails", "ElderBark",
                "AncientBark", "Guck", "Bloodbag", "Entrails", "Ooze", "Root",
                "TurnipSeeds", "Turnip", "Chain", "WitheredBone"
            });
            add(Heightmap.Biome.Mountain, new[] {
                "SilverOre", "Silver", "Obsidian", "FreezeGland", "WolfPelt",
                "WolfFang", "DragonEgg", "OnionSeeds", "Onion", "Crystal",
                "FenringHair", "JuteRed"
            });
            add(Heightmap.Biome.Plains, new[] {
                "BlackMetalScrap", "BlackMetal", "BlackMetalNails", "LinenThread",
                "Flax", "Barley", "Cloudberry", "Needle", "LoxPelt", "Tar",
                "YagluthDrop", "YagluthThing"
            });
            add(Heightmap.Biome.Mistlands, new[] {
                "BlackMarble", "Softtissue", "Sap", "Eitr", "RefinedEitr",
                "Carapace", "Mandible", "ScaleHide", "JuteBlue", "RoyalJelly",
                "BlackCore", "Magecap", "JotunPuffs", "GiantBloodSack", "QueenDrop"
            });
            add(Heightmap.Biome.AshLands, new[] {
                "FlametalOre", "FlametalOreNew", "Flametal", "FlametalNew",
                "AskHide", "AsksvinHide", "MorgenSinew", "CharredBone",
                "ProustitePowder", "CelestialFeather", "BonemawSerpentTooth",
                "SulfurStone", "Fiddlehead", "SmokePuff", "FaderDrop"
            });

            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null)
                    continue;
                Heightmap.Biome biome;
                if (known.TryGetValue(PrefabName(prefab), out biome))
                    SeedItem(prefab, biome, items, locked);
            }
        }

        void SeedDropTable(
            DropTable table,
            Heightmap.Biome biome,
            Dictionary<string, Heightmap.Biome> items,
            HashSet<string> locked
        )
        {
            if (table == null || table.m_drops == null)
                return;
            foreach (DropTable.DropData drop in table.m_drops)
                SeedItem(drop.m_item, biome, items, locked);
        }

        bool SeedResource(
            GameObject prefab,
            Heightmap.Biome biome,
            Dictionary<string, Heightmap.Biome> items,
            HashSet<string> lockedItems,
            Dictionary<string, Heightmap.Biome> resources,
            HashSet<string> lockedResources
        )
        {
            if (prefab == null || biome == Heightmap.Biome.None)
                return false;
            bool recognized = false;
            Pickable pickable = prefab.GetComponent<Pickable>();
            if (pickable != null)
            {
                recognized = true;
                SeedItem(pickable.m_itemPrefab, biome, items, lockedItems);
                SeedDropTable(pickable.m_extraDrops, biome, items, lockedItems);
            }
            PickableItem pickableItem = prefab.GetComponent<PickableItem>();
            if (pickableItem != null)
            {
                recognized = true;
                if (pickableItem.m_itemPrefab != null)
                    SeedItem(pickableItem.m_itemPrefab.gameObject, biome, items, lockedItems);
                if (pickableItem.m_randomItemPrefabs != null)
                    foreach (PickableItem.RandomItem random in pickableItem.m_randomItemPrefabs)
                        if (random.m_itemPrefab != null)
                            SeedItem(random.m_itemPrefab.gameObject, biome, items, lockedItems);
            }
            MineRock rock = prefab.GetComponent<MineRock>();
            if (rock != null)
            {
                recognized = true;
                SeedDropTable(rock.m_dropItems, biome, items, lockedItems);
            }
            MineRock5 rock5 = prefab.GetComponent<MineRock5>();
            if (rock5 != null)
            {
                recognized = true;
                SeedDropTable(rock5.m_dropItems, biome, items, lockedItems);
            }
            TreeBase tree = prefab.GetComponent<TreeBase>();
            if (tree != null)
            {
                recognized = true;
                SeedDropTable(tree.m_dropWhenDestroyed, biome, items, lockedItems);
                if (
                    tree.m_logPrefab != null
                    && !string.Equals(
                        PrefabName(tree.m_logPrefab),
                        PrefabName(prefab),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    SeedResource(tree.m_logPrefab, biome, items, lockedItems, resources, lockedResources);
            }
            TreeLog log = prefab.GetComponent<TreeLog>();
            if (log != null)
            {
                recognized = true;
                SeedDropTable(log.m_dropWhenDestroyed, biome, items, lockedItems);
                if (
                    log.m_subLogPrefab != null
                    && !string.Equals(
                        PrefabName(log.m_subLogPrefab),
                        PrefabName(prefab),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    SeedResource(log.m_subLogPrefab, biome, items, lockedItems, resources, lockedResources);
            }
            if (recognized)
                SetEarlier(resources, lockedResources, PrefabName(prefab), biome);
            return recognized;
        }

        bool TryRequirementBiome(
            Piece.Requirement[] requirements,
            Dictionary<string, Heightmap.Biome> items,
            out Heightmap.Biome biome
        )
        {
            biome = Heightmap.Biome.None;
            if (requirements == null || requirements.Length == 0)
                return false;
            int highest = -1;
            foreach (Piece.Requirement requirement in requirements)
            {
                if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0)
                    continue;
                Heightmap.Biome ingredient;
                if (items.TryGetValue(PrefabName(requirement.m_resItem.gameObject), out ingredient))
                    highest = Math.Max(highest, BiomeOrder(ingredient));
            }
            if (highest < 0)
                return false;
            biome = Progression[highest];
            return true;
        }

        bool PropagateConversions(
            Dictionary<string, Heightmap.Biome> items,
            HashSet<string> lockedItems
        )
        {
            bool changed = false;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null)
                    continue;
                Smelter smelter = prefab.GetComponent<Smelter>();
                if (smelter != null)
                    foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                        if (conversion != null && conversion.m_from != null && conversion.m_to != null)
                        {
                            Heightmap.Biome biome;
                            if (items.TryGetValue(PrefabName(conversion.m_from.gameObject), out biome))
                                changed |= SetEarlier(items, lockedItems, PrefabName(conversion.m_to.gameObject), biome);
                        }
                CookingStation cooking = prefab.GetComponent<CookingStation>();
                if (cooking != null)
                    foreach (CookingStation.ItemConversion conversion in cooking.m_conversion)
                        if (conversion != null && conversion.m_from != null && conversion.m_to != null)
                        {
                            Heightmap.Biome biome;
                            if (items.TryGetValue(PrefabName(conversion.m_from.gameObject), out biome))
                                changed |= SetEarlier(items, lockedItems, PrefabName(conversion.m_to.gameObject), biome);
                        }
                Fermenter fermenter = prefab.GetComponent<Fermenter>();
                if (fermenter != null)
                    foreach (Fermenter.ItemConversion conversion in fermenter.m_conversion)
                        if (conversion != null && conversion.m_from != null && conversion.m_to != null)
                        {
                            Heightmap.Biome biome;
                            if (items.TryGetValue(PrefabName(conversion.m_from.gameObject), out biome))
                                changed |= SetEarlier(items, lockedItems, PrefabName(conversion.m_to.gameObject), biome);
                        }
            }
            return changed;
        }

        void AppendMappings(
            string path,
            Dictionary<string, Heightmap.Biome> all,
            HashSet<string> original,
            string title
        )
        {
            var additions = new SortedDictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in all)
                if (!original.Contains(pair.Key))
                    additions[pair.Key] = pair.Value;
            if (additions.Count == 0)
                return;
            var text = new StringBuilder("\n# " + title + " - gerado automaticamente pelo Levelbound.\n");
            foreach (Heightmap.Biome biome in Progression)
            {
                var names = new List<string>();
                foreach (var pair in additions)
                    if (pair.Value == biome)
                        names.Add(pair.Key);
                if (names.Count == 0)
                    continue;
                text.Append('\n').Append(BiomeName(biome)).Append(":\n");
                foreach (string name in names)
                    text.Append("  - ").Append(name).Append('\n');
            }
            File.AppendAllText(path, text.ToString());
        }

        void GenerateVanillaDefaults(ObjectDB db)
        {
            if (defaultsGenerated || db == null)
                return;
            defaultsGenerated = true;
            var originalItems = new HashSet<string>(itemBiomes.Keys, StringComparer.OrdinalIgnoreCase);
            var explicitEquipment = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
            string equipmentText = File.Exists(Equipment) ? File.ReadAllText(Equipment) : "";
            ParseMappings(equipmentText, explicitEquipment);
            var listedEquipment = ParseAllListedPrefabs(equipmentText);
            var originalResources = new HashSet<string>(resourceBiomes.Keys, StringComparer.OrdinalIgnoreCase);
            var originalConstructions = new HashSet<string>(constructionBiomes.Keys, StringComparer.OrdinalIgnoreCase);
            var items = new Dictionary<string, Heightmap.Biome>(itemBiomes, StringComparer.OrdinalIgnoreCase);
            var resources = new Dictionary<string, Heightmap.Biome>(resourceBiomes, StringComparer.OrdinalIgnoreCase);
            var constructions = new Dictionary<string, Heightmap.Biome>(constructionBiomes, StringComparer.OrdinalIgnoreCase);

            foreach (ZoneSystem.ZoneVegetation vegetation in ZoneSystem.instance.m_vegetation)
                if (vegetation != null)
                    SeedResource(
                        vegetation.m_prefab,
                        FirstProgressionBiome(vegetation.m_biome),
                        items,
                        originalItems,
                        resources,
                        originalResources
                    );

            var spawnSystems = new HashSet<SpawnSystem>();
            foreach (
                SpawnSystem spawnSystem in UnityEngine.Object.FindObjectsByType<SpawnSystem>(
                    FindObjectsSortMode.None
                )
            )
                spawnSystems.Add(spawnSystem);
            if (ZoneSystem.instance.m_zoneCtrlPrefab != null)
                foreach (
                    SpawnSystem spawnSystem in ZoneSystem.instance.m_zoneCtrlPrefab.GetComponentsInChildren<SpawnSystem>(
                        true
                    )
                )
                    spawnSystems.Add(spawnSystem);
            foreach (SpawnSystem spawnSystem in spawnSystems)
                foreach (SpawnSystemList list in spawnSystem.m_spawnLists)
                    foreach (SpawnSystem.SpawnData spawn in list.m_spawners)
                    {
                        if (spawn == null || spawn.m_prefab == null)
                            continue;
                        CharacterDrop drops = spawn.m_prefab.GetComponent<CharacterDrop>();
                        Heightmap.Biome biome = FirstProgressionBiome(spawn.m_biome);
                        if (drops == null || biome == Heightmap.Biome.None)
                            continue;
                        foreach (CharacterDrop.Drop drop in drops.m_drops)
                            if (drop != null)
                                SeedItem(drop.m_prefab, biome, items, originalItems);
                    }

            string[] bossDrops = { "HardAntler", "CryptKey", "Wishbone", "DragonTear", "YagluthDrop", "QueenDrop", "FaderDrop" };
            for (int i = 0; i < bossDrops.Length && i < Progression.Length; ++i)
                foreach (GameObject item in db.m_items)
                    if (item != null && string.Equals(PrefabName(item), bossDrops[i], StringComparison.OrdinalIgnoreCase))
                        SeedItem(item, Progression[i], items, originalItems);

            SeedKnownVanillaMaterials(db, items, originalItems);

            for (int pass = 0; pass < 32; ++pass)
            {
                bool changed = PropagateConversions(items, originalItems);
                foreach (Recipe recipe in db.m_recipes)
                {
                    if (recipe == null || recipe.m_item == null)
                        continue;
                    Heightmap.Biome biome;
                    if (TryRequirementBiome(recipe.m_resources, items, out biome))
                        changed |= SetEarlier(items, originalItems, PrefabName(recipe.m_item.gameObject), biome);
                }
                if (!changed)
                    break;
            }

            if (diagnostic.Value)
            {
                int unresolved = 0;
                foreach (Recipe recipe in db.m_recipes)
                {
                    if (
                        recipe == null
                        || recipe.m_item == null
                        || items.ContainsKey(PrefabName(recipe.m_item.gameObject))
                    )
                        continue;
                    var missing = new List<string>();
                    if (recipe.m_resources != null)
                        foreach (Piece.Requirement requirement in recipe.m_resources)
                            if (
                                requirement != null
                                && requirement.m_resItem != null
                                && requirement.m_amount > 0
                                && !items.ContainsKey(PrefabName(requirement.m_resItem.gameObject))
                            )
                                missing.Add(PrefabName(requirement.m_resItem.gameObject));
                    if (missing.Count > 0)
                    {
                        if (unresolved < 100)
                            Logger.LogWarning(
                                "Unclassified recipe: "
                                    + PrefabName(recipe.m_item.gameObject)
                                    + "; ingredients without biome: "
                                    + string.Join(", ", missing.ToArray())
                            );
                        unresolved++;
                    }
                }
                Logger.LogInfo("Unclassified recipes: " + unresolved + ".");
            }

            var piecePrefabs = new HashSet<GameObject>();
            foreach (GameObject itemPrefab in db.m_items)
            {
                ItemDrop item = itemPrefab == null ? null : itemPrefab.GetComponent<ItemDrop>();
                PieceTable table = item == null || item.m_itemData == null || item.m_itemData.m_shared == null
                    ? null
                    : item.m_itemData.m_shared.m_buildPieces;
                if (table == null || table.m_pieces == null)
                    continue;
                foreach (GameObject piecePrefab in table.m_pieces)
                    if (piecePrefab != null)
                        piecePrefabs.Add(piecePrefab);
            }

            // Dedicated servers do not always populate every PieceTable in the same
            // way as a client. Networked piece prefabs are authoritative and include
            // pieces registered by content mods as well.
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                if (prefab != null && prefab.GetComponent<Piece>() != null)
                    piecePrefabs.Add(prefab);

            foreach (GameObject piecePrefab in piecePrefabs)
            {
                Piece piece = piecePrefab.GetComponent<Piece>();
                Heightmap.Biome biome;
                if (piece != null && TryRequirementBiome(piece.m_resources, items, out biome))
                    SetEarlier(constructions, originalConstructions, PrefabName(piecePrefab), biome);
            }

            var materialMappings = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
            var equipmentMappings = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
            var unclassifiedEquipment = new List<string>();
            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null)
                    continue;
                string name = PrefabName(prefab);
                Heightmap.Biome biome;
                if (IsEquipment(prefab))
                {
                    if (items.TryGetValue(name, out biome))
                        equipmentMappings[name] = biome;
                    else if (!listedEquipment.Contains(name))
                        unclassifiedEquipment.Add(name);
                }
                else if (items.TryGetValue(name, out biome))
                    materialMappings[name] = biome;
            }

            AppendMappings(Materials, materialMappings, originalItems, "Items inferred from the world and recipes");
            AppendMappings(
                Equipment,
                equipmentMappings,
                new HashSet<string>(explicitEquipment.Keys, StringComparer.OrdinalIgnoreCase),
                "Game and mod equipment classified by recipes"
            );
            AppendUnclassifiedEquipment(unclassifiedEquipment);
            AppendMappings(Resources, resources, originalResources, "Registered resources inferred from the world");
            AppendMappings(Constructions, constructions, originalConstructions, "Registered build pieces inferred from recipes");
            YamlData.AssignLocalValue(ReadYaml());
            // Cancel any pending debounce and load immediately — the files were
            // just written by this method and are guaranteed to be complete.
            if (debounceCoroutine != null)
            {
                StopCoroutine(debounceCoroutine);
                debounceCoroutine = null;
            }
            LoadRules();
            // Automatic generation is intentionally a one-shot operation. It
            // remains available by setting GenerateFilesAutomatically to true.
            generateDefaults.Value = false;
            Config.Save();
            Logger.LogInfo(
                "Automatic file generation completed: +"
                    + materialMappings.Count
                    + " classified items, "
                    + equipmentMappings.Count
                    + " classified equipment, "
                    + unclassifiedEquipment.Count
                    + " unclassified equipment, +"
                    + (resources.Count - originalResources.Count)
                    + " resources and +"
                    + (constructions.Count - originalConstructions.Count)
                    + " build pieces."
            );
        }

        string Origin(GameObject g)
        {
            try
            {
                foreach (Component c in g.GetComponents<Component>())
                {
                    if (c == null)
                        continue;
                    string n = c.GetType().Assembly.GetName().Name;
                    if (n != "assembly_valheim" && n != "UnityEngine" && n != "Assembly-CSharp")
                        return n;
                }
            }
            catch { }
            return "Valheim";
        }

        internal void Generate(ObjectDB db)
        {
            if (!generate.Value || File.Exists(Helper) || db == null)
                return;
            var valid = new HashSet<string>
            {
                "Consumable",
                "OneHandedWeapon",
                "TwoHandedWeapon",
                "TwoHandedWeaponLeft",
                "Bow",
                "Shield",
                "Helmet",
                "Chest",
                "Ammo",
                "Legs",
                "Torch",
                "Shoulder",
                "Utility",
                "Tool",
                "Hands",
                "Trinket",
            };
            var groups = new SortedDictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (GameObject g in db.m_items)
            {
                ItemDrop d = g.GetComponent<ItemDrop>();
                if (
                    d == null
                    || d.m_itemData == null
                    || d.m_itemData.m_shared == null
                    || !valid.Contains(d.m_itemData.m_shared.m_itemType.ToString())
                )
                    continue;
                string mod = Origin(g);
                List<string> list;
                if (!groups.TryGetValue(mod, out list))
                    groups[mod] = list = new List<string>();
                list.Add(g.name);
            }
            var s = new StringBuilder(
                "# Levelbound reference: grouped by source mod. It is NOT read or synchronized.\n# Copy materials to materiais.yml and equipment to equipamentos.yml.\n\nReference:\n"
            );
            foreach (var group in groups)
            {
                s.Append("  ").Append(group.Key).Append(":\n");
                group.Value.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string prefab in group.Value)
                    s.Append("    - ").Append(prefab).Append("\n");
            }
            File.WriteAllText(Helper, s.ToString());
            Logger.LogInfo("Levelbound: reference list created at: " + Helper);
            Logger.LogInfo("Levelbound: to regenerate the reference list after adding mods, delete the file above and restart with GenerateEquipableItemList = true.");
        }

        internal enum ActionKind
        {
            Craft,
            Upgrade,
            Equip,
            Consume,
        }
    }

    [HarmonyPatch(typeof(Game), "RequestRespawn")]
    class GenPatch
    {
        static void Postfix()
        {
            if (Plugin.Instance != null)
                Plugin.Instance.Generate(ObjectDB.instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "IsEquipable")]
    class CanEquipPatch
    {
        static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
        {
            int n;
            if (
                __result
                && Plugin.Instance != null
                && Plugin.Instance.Blocked(__instance, Plugin.ActionKind.Equip, out n)
            )
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    class EquipPatch
    {
        static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            int n;
            if (
                !__instance.IsPlayer()
                || Plugin.Instance == null
                || !Plugin.Instance.Blocked(
                    item,
                    Plugin.ActionKind.Equip,
                    out n,
                    __instance as Player
                )
            )
                return true;
            Plugin.Instance.Tell(__instance as Player, n);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "CanConsumeItem")]
    class EatPatch
    {
        static void Postfix(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            int n;
            if (
                __result
                && Plugin.Instance != null
                && Plugin.Instance.Blocked(item, Plugin.ActionKind.Consume, out n, __instance)
            )
            {
                __result = false;
                Plugin.Instance.Tell(__instance, n);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
    class CraftPressedPatch
    {
        static readonly FieldInfo Selected = typeof(InventoryGui).GetField(
            "m_selectedRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        static bool Prefix(InventoryGui __instance)
        {
            if (__instance == null || Plugin.Instance == null || Selected == null)
                return true;
            object pair = Selected.GetValue(__instance);
            if (pair == null)
                return true;
            PropertyInfo p = pair.GetType().GetProperty("Recipe");
            Recipe recipe = p == null ? null : p.GetValue(pair, null) as Recipe;
            if (recipe == null || recipe.m_item == null)
                return true;
            ItemDrop d = recipe.m_item.GetComponent<ItemDrop>();
            if (d == null)
                return true;
            if (d.m_itemData != null && d.m_itemData.m_dropPrefab == null)
                d.m_itemData.m_dropPrefab = recipe.m_item.gameObject;
            string prefab = recipe.m_item.gameObject.name;
            PropertyInfo itemDataProp = pair.GetType().GetProperty("ItemData");
            ItemDrop.ItemData upgradeItem = itemDataProp == null ? null : itemDataProp.GetValue(pair, null) as ItemDrop.ItemData;
            Plugin.ActionKind action = upgradeItem == null ? Plugin.ActionKind.Craft : Plugin.ActionKind.Upgrade;
            int n;
            if (!Plugin.Instance.Blocked(d.m_itemData, action, out n, Player.m_localPlayer, prefab))
                return true;
            Plugin.Instance.Tell(Player.m_localPlayer, n);
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    class CraftPatch
    {
        static readonly FieldInfo CraftRecipeField = typeof(InventoryGui).GetField(
            "m_craftRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        static readonly FieldInfo Selected = typeof(InventoryGui).GetField(
            "m_selectedRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        static readonly FieldInfo UpgradeItem = typeof(InventoryGui).GetField(
            "m_craftUpgradeItem",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        static bool Prefix(InventoryGui __instance)
        {
            if (__instance == null || Plugin.Instance == null)
                return true;
            Recipe recipe = null;
            if (CraftRecipeField != null)
                recipe = CraftRecipeField.GetValue(__instance) as Recipe;
            if (recipe == null && Selected != null)
            {
                object pair = Selected.GetValue(__instance);
                if (pair != null)
                {
                    PropertyInfo p = pair.GetType().GetProperty("Recipe");
                    recipe = p == null ? null : p.GetValue(pair, null) as Recipe;
                }
            }
            if (recipe == null || recipe.m_item == null)
                return true;
            ItemDrop d = recipe.m_item.GetComponent<ItemDrop>();
            if (d == null)
                return true;
            if (d.m_itemData != null && d.m_itemData.m_dropPrefab == null)
                d.m_itemData.m_dropPrefab = recipe.m_item.gameObject;
            string prefab = recipe.m_item.gameObject.name;
            Plugin.ActionKind action = UpgradeItem == null || UpgradeItem.GetValue(__instance) == null
                ? Plugin.ActionKind.Craft
                : Plugin.ActionKind.Upgrade;
            int n;
            if (!Plugin.Instance.Blocked(d.m_itemData, action, out n, Player.m_localPlayer, prefab))
                return true;
            Plugin.Instance.Tell(Player.m_localPlayer, n);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip", new Type[] { typeof(int) })]
    class TooltipPatch
    {
        static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (Plugin.Instance != null && __instance != null && __instance.m_shared != null)
            {
                Plugin.ActionKind action = __instance.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                    ? Plugin.ActionKind.Consume
                    : Plugin.ActionKind.Equip;
                Plugin.Instance.AppendRequirement(ref __result, __instance, action);
            }
        }
    }

    // This patch is installed dynamically by Plugin after Path of Valheiman
    // loads. Its Model type is private to PoV, therefore __result is accessed
    // via reflection and Levelbound remains fully standalone for vanilla.
    class PathOfValheimanModelPatch
    {
        static FieldInfo useHint;

        static void Postfix(ItemDrop.ItemData item, object __result)
        {
            if (Plugin.Instance == null || item == null || item.m_shared == null || __result == null)
                return;
            if (useHint == null)
                useHint = AccessTools.Field(__result.GetType(), "UseHint");
            if (useHint == null)
                return;
            string text = useHint.GetValue(__result) as string ?? "";
            Plugin.ActionKind action = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                ? Plugin.ActionKind.Consume
                : Plugin.ActionKind.Equip;
            string requirement = Plugin.Instance.RequirementText(item, action).Trim();
            if (string.IsNullOrEmpty(requirement) || text.IndexOf(requirement, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            useHint.SetValue(__result, string.IsNullOrEmpty(text) ? requirement : text + "\n" + requirement);
        }
    }

    // Marketplace creates a fully populated temporary ItemData before it
    // returns the panel text. Capture that object so custom PoV data (affixes,
    // rarity, quality and variants) are retained in the Marketplace display.
    class MarketplaceTooltipPatch
    {
        internal class Capture
        {
            internal ItemDrop.ItemData Item;
            internal string Prefab;
        }

        static void Prefix(string prefab, ref Action<ItemDrop.ItemData> callback, out Capture __state)
        {
            Capture capture = new Capture();
            capture.Prefab = prefab;
            __state = capture;
            Action<ItemDrop.ItemData> original = callback;
            callback = item =>
            {
                capture.Item = item;
                if (original != null)
                    original(item);
            };
        }

        static void Postfix(Capture __state, ref string __result)
        {
            if (Plugin.Instance == null || __state == null || __state.Item == null)
                return;
            ItemDrop.ItemData item = __state.Item;
            if (item.m_shared == null)
                return;
            string povText = MarketplacePoVDescription.Create(item);
            if (!string.IsNullOrEmpty(povText))
                __result += "\n\n" + povText;
            Plugin.ActionKind action = item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                ? Plugin.ActionKind.Consume
                : Plugin.ActionKind.Equip;
            string requirement = Plugin.Instance.RequirementText(item, action, __state.Prefab).Trim();
            if (
                !string.IsNullOrEmpty(requirement)
                && __result.IndexOf(requirement, StringComparison.OrdinalIgnoreCase) < 0
            )
                __result = requirement + "\n\n" + __result;
        }
    }

    // Reflection keeps PoV optional and avoids a hard assembly reference.
    class MarketplacePoVDescription
    {
        static Type items;
        static Type tooltip;
        static Type texts;
        static bool searched;

        static bool Ready()
        {
            if (!searched)
            {
                searched = true;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (items == null)
                        items = assembly.GetType("Valheiman.Api.Items", false);
                    if (tooltip == null)
                        tooltip = assembly.GetType("Valheiman.Api.Tooltip", false);
                    if (texts == null)
                        texts = assembly.GetType("Valheiman.Api.Texts", false);
                }
            }
            return items != null && tooltip != null;
        }

        internal static string Create(ItemDrop.ItemData item)
        {
            if (!Ready() || item == null)
                return "";
            try
            {
                bool identified = (bool)AccessTools.Method(items, "IsIdentified").Invoke(null, new object[] { item });
                int level = (int)AccessTools.Method(items, "ItemLevel").Invoke(null, new object[] { item });
                var text = new StringBuilder();
                if (identified)
                {
                    MethodInfo describe = AccessTools.Method(tooltip, "Describe");
                    object[] args = { item, null };
                    string description = describe.Invoke(null, args) as string;
                    AppendRows(text, AccessTools.Method(tooltip, "Implicits").Invoke(null, new object[] { item }) as System.Collections.IEnumerable);
                    AppendRows(text, args[1] as System.Collections.IEnumerable);
                    if (!string.IsNullOrWhiteSpace(description))
                        text.Append('\n').Append(description);
                }
                else
                {
                    string unidentified = texts == null ? "Unidentified" : AccessTools.Method(texts, "Get").Invoke(null, new object[] { "ui.item.unidentified", "Unidentified" }) as string;
                    text.Append('\n').Append(unidentified);
                }
                if (text.Length == 0 && level <= 0)
                    return "";
                string rarity = AccessTools.Method(items, "RarityTitle").Invoke(null, new[] { AccessTools.Method(items, "RarityOf").Invoke(null, new object[] { item }) }) as string;
                var result = new StringBuilder("<color=#E6C77A>Path of Valheim</color>");
                if (!string.IsNullOrWhiteSpace(rarity))
                    result.Append('\n').Append(rarity);
                if (level > 0)
                    result.Append("\nilvl ").Append(level);
                result.Append(text);
                return System.Text.RegularExpressions.Regex.Replace(result.ToString(), "</?link\\b[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch (Exception error)
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.Warn("Levelbound: could not render PoV details in Marketplace: " + error.Message);
                return "";
            }
        }

        static void AppendRows(StringBuilder text, System.Collections.IEnumerable rows)
        {
            if (rows == null)
                return;
            foreach (object row in rows)
            {
                if (row == null)
                    continue;
                Type type = row.GetType();
                string name = AccessTools.Property(type, "Name").GetValue(row, null) as string;
                string effect = AccessTools.Property(type, "Effect").GetValue(row, null) as string;
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(effect))
                    continue;
                text.Append('\n');
                if (!string.IsNullOrWhiteSpace(name))
                    text.Append(name);
                if (!string.IsNullOrWhiteSpace(effect))
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        text.Append(": ");
                    text.Append(effect);
                }
            }
        }
    }

    // Marketplace removes the local currency immediately before sending its
    // purchase RPC. The client prefix prevents that loss in normal play; the
    // server prefix remains authoritative and rejects forged/direct requests.
    class MarketplacePurchasePatch
    {
        static FieldInfo currentBuyData;
        static FieldInfo buyTab;
        static FieldInfo marketContext;
        static MethodInfo getSlotById;
        static PropertyInfo clientItemPrefab;
        static PropertyInfo slotItemPrefab;

        static bool ClientBuyPrefix()
        {
            string prefab;
            int requirement;
            if (!CurrentClientPrefab(out prefab) || !Plugin.Instance.MarketplaceItemLocked(prefab, Player.m_localPlayer, out requirement))
                return true;
            Plugin.Instance.Tell(Player.m_localPlayer, requirement);
            return false;
        }

        static void ClientSelectionPostfix()
        {
            if (Plugin.Instance == null)
                return;
            string prefab;
            int requirement = 0;
            bool locked = CurrentClientPrefab(out prefab)
                && Plugin.Instance.MarketplaceItemLocked(prefab, Player.m_localPlayer, out requirement);
            SetClientBuyButton(!locked, locked ? Plugin.Instance.RequirementTooltip(requirement) : null);
        }

        static bool CurrentClientPrefab(out string prefab)
        {
            prefab = null;
            if (Plugin.Instance == null)
                return false;
            Type ui = AccessTools.TypeByName("Marketplace.Modules.MainMarketplace.Marketplace_UI");
            if (ui == null)
                return false;
            if (currentBuyData == null)
                currentBuyData = AccessTools.Field(ui, "CurrentBuyData");
            object data = currentBuyData == null ? null : currentBuyData.GetValue(null);
            if (data == null)
                return false;
            if (clientItemPrefab == null)
                clientItemPrefab = AccessTools.Property(data.GetType(), "ItemPrefab");
            prefab = clientItemPrefab == null ? null : clientItemPrefab.GetValue(data, null) as string;
            return !string.IsNullOrEmpty(prefab);
        }

        static void SetClientBuyButton(bool interactable, string tooltipText)
        {
            Type ui = AccessTools.TypeByName("Marketplace.Modules.MainMarketplace.Marketplace_UI");
            if (ui == null)
                return;
            if (buyTab == null)
                buyTab = AccessTools.Field(ui, "BUYTAB");
            Transform tab = buyTab == null ? null : buyTab.GetValue(null) as Transform;
            Transform buttonTransform = tab == null ? null : tab.Find("AFTERPRESS/BUTTON");
            Button button = buttonTransform == null ? null : buttonTransform.GetComponent<Button>();
            if (button == null)
                return;
            button.interactable = interactable;
            UITooltip tooltip = button.GetComponent<UITooltip>();
            if (tooltip != null && !string.IsNullOrEmpty(tooltipText))
                tooltip.m_text = Localization.instance == null ? tooltipText : Localization.instance.Localize(tooltipText);
        }

        static bool ServerBuyPrefix(long sender, ZPackage data)
        {
            if (Plugin.Instance == null || data == null)
                return true;
            int slotId;
            try
            {
                slotId = new ZPackage(data.GetArray()).ReadInt();
            }
            catch
            {
                return true;
            }
            Type database = AccessTools.TypeByName("Marketplace.DB.DB/Marketplace_Context_LiteDB");
            if (database == null)
                return true;
            if (marketContext == null)
                marketContext = AccessTools.Field(database, "_context");
            object context = marketContext == null ? null : marketContext.GetValue(null);
            if (context == null)
                return true;
            if (getSlotById == null)
                getSlotById = AccessTools.Method(context.GetType(), "GetSlotByID");
            object slot = getSlotById == null ? null : getSlotById.Invoke(context, new object[] { slotId });
            if (slot == null)
                return true;
            if (slotItemPrefab == null)
                slotItemPrefab = AccessTools.Property(slot.GetType(), "ItemPrefab");
            string prefab = slotItemPrefab == null ? null : slotItemPrefab.GetValue(slot, null) as string;
            Player player = Player.GetPlayer(sender);
            int requirement;
            if (player == null || !Plugin.Instance.MarketplaceItemLocked(prefab, player, out requirement))
                return true;
            Plugin.Instance.Tell(player, requirement);
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "Pickup")]
    class PickupPatch
    {
        static bool Prefix(Humanoid __instance, GameObject __0)
        {
            if (!__instance.IsPlayer() || Plugin.Instance == null || __0 == null)
                return true;
            ItemDrop drop = __0.GetComponent<ItemDrop>();
            Player player = __instance as Player;
            if (drop == null || !Plugin.Instance.BlockItemPickup(drop.m_itemData, player))
                return true;
            return false;
        }
    }

    // A drag-and-drop swap between the player inventory and a chest runs as two
    // separate moves.  If the chest item is locked, blocking only its second move
    // is too late: the player's item may already have been removed.  Validate the
    // item that would enter the player inventory before either move starts.
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    class DragSwapBlockPatch
    {
        static readonly FieldInfo DragItem = typeof(InventoryGui).GetField(
            "m_dragItem",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        static readonly FieldInfo DragInventory = typeof(InventoryGui).GetField(
            "m_dragInventory",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        static readonly FieldInfo CurrentContainer = typeof(InventoryGui).GetField(
            "m_currentContainer",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        static bool Prefix(
            InventoryGui __instance,
            InventoryGrid grid,
            ItemDrop.ItemData item,
            Vector2i pos,
            InventoryGrid.Modifier mod
        )
        {
            if (Plugin.Instance == null || DragItem == null || DragInventory == null || CurrentContainer == null)
                return true;

            // A swap is possible only when a dragged item is dropped on an occupied slot.
            ItemDrop.ItemData draggedItem = DragItem.GetValue(__instance) as ItemDrop.ItemData;
            if (draggedItem == null || item == null || draggedItem == item)
                return true;

            Inventory draggedFrom = DragInventory.GetValue(__instance) as Inventory;
            Player player = Player.m_localPlayer;
            if (player == null || draggedFrom == null)
                return true;

            Container container = CurrentContainer.GetValue(__instance) as Container;
            if (container == null || grid == null)
                return true;

            Inventory playerInventory = player.GetInventory();
            Inventory chestInventory = container.GetInventory();
            ItemDrop.ItemData incomingItem;

            if (draggedFrom == playerInventory && grid.GetInventory() == chestInventory)
            {
                // Player dragged their item over an occupied chest slot. The item
                // already in that chest slot would be moved into the player inventory.
                incomingItem = item;
            }
            else if (draggedFrom == chestInventory && grid.GetInventory() == playerInventory)
            {
                // Chest item dragged over an occupied player slot.
                incomingItem = draggedItem;
            }
            else
            {
                return true;
            }

            // Cancel the entire swap before Valheim moves either item.
            int requirement;
            if (!Plugin.Instance.ItemLevelLocked(incomingItem, out requirement, player))
                return true;

            Plugin.Instance.Tell(player, requirement);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(Inventory),
        "MoveItemToThis",
        new Type[] { typeof(Inventory), typeof(ItemDrop.ItemData) }
    )]
    class ContainerMoveItemPatch
    {
        static bool Prefix(
            Inventory __instance,
            Inventory fromInventory,
            ItemDrop.ItemData item
        )
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockContainerTransfer(__instance, fromInventory, item);
        }
    }

    [HarmonyPatch(
        typeof(Inventory),
        "MoveItemToThis",
        new Type[] {
            typeof(Inventory),
            typeof(ItemDrop.ItemData),
            typeof(int),
            typeof(int),
            typeof(int)
        }
    )]
    class ContainerMoveItemAmountPatch
    {
        static bool Prefix(
            Inventory __instance,
            Inventory fromInventory,
            ItemDrop.ItemData item,
            ref bool __result
        )
        {
            if (
                Plugin.Instance == null
                || !Plugin.Instance.BlockContainerTransfer(__instance, fromInventory, item)
            )
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), "MoveAll")]
    class ContainerMoveAllPatch
    {
        static bool Prefix(Inventory __instance, Inventory fromInventory)
        {
            if (
                Plugin.Instance == null
                || !Plugin.Instance.IsContainerToPlayerTransfer(__instance, fromInventory)
            )
                return true;

            var items = new List<ItemDrop.ItemData>(fromInventory.GetAllItems());
            foreach (ItemDrop.ItemData item in items)
            {
                if (Plugin.Instance.BlockContainerTransfer(__instance, fromInventory, item))
                    continue;
                __instance.MoveItemToThis(fromInventory, item);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Interact")]
    class ItemDropInteractPatch
    {
        static bool Prefix(ItemDrop __instance, Humanoid __0)
        {
            return !__0.IsPlayer()
                || Plugin.Instance == null
                || !Plugin.Instance.BlockItemPickup(__instance.m_itemData, __0 as Player);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "CanPickup")]
    class ItemDropAutoPickupPatch
    {
        static void Postfix(ItemDrop __instance, bool autoPickupDelay, ref bool __result)
        {
            if (
                __result
                && autoPickupDelay
                && Plugin.Instance != null
                && !Plugin.Instance.AllowAutoPickup(__instance)
            )
                __result = false;
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Pickup")]
    class ItemDropPickupPatch
    {
        static bool Prefix(ItemDrop __instance, Humanoid __0)
        {
            return !__0.IsPlayer()
                || Plugin.Instance == null
                || !Plugin.Instance.BlockItemPickup(__instance.m_itemData, __0 as Player);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    class RecipeVisualPatch
    {
        static readonly FieldInfo Selected = typeof(InventoryGui).GetField(
            "m_selectedRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        static void Postfix(InventoryGui __instance)
        {
            if (__instance == null || Plugin.Instance == null || Selected == null)
                return;
            object pair = Selected.GetValue(__instance);
            if (pair == null)
                return;
            PropertyInfo p = pair.GetType().GetProperty("Recipe");
            Recipe recipe = p == null ? null : p.GetValue(pair, null) as Recipe;
            if (recipe == null || recipe.m_item == null)
                return;
            ItemDrop d = recipe.m_item.GetComponent<ItemDrop>();
            if (d == null)
                return;
            if (d.m_itemData != null && d.m_itemData.m_dropPrefab == null)
                d.m_itemData.m_dropPrefab = recipe.m_item.gameObject;
            string prefab = recipe.m_item.gameObject.name;
            PropertyInfo itemDataProp = pair.GetType().GetProperty("ItemData");
            ItemDrop.ItemData upgradeItem = itemDataProp == null ? null : itemDataProp.GetValue(pair, null) as ItemDrop.ItemData;
            Plugin.ActionKind action = upgradeItem == null ? Plugin.ActionKind.Craft : Plugin.ActionKind.Upgrade;
            string reqText = Plugin.Instance.RequirementText(d.m_itemData, action, prefab).Trim();
            if (
                !string.IsNullOrEmpty(reqText)
                && __instance.m_recipeDecription.text.IndexOf(reqText, StringComparison.OrdinalIgnoreCase) < 0
            )
                __instance.m_recipeDecription.text = reqText + "\n\n" + __instance.m_recipeDecription.text;
            int n;
            if (Plugin.Instance.Blocked(d.m_itemData, action, out n, Player.m_localPlayer, prefab))
            {
                __instance.m_craftButton.interactable = false;
                UITooltip tooltip = __instance.m_craftButton.GetComponent<UITooltip>();
                if (tooltip != null)
                {
                    tooltip.m_text = Localization.instance != null
                        ? Localization.instance.Localize(Plugin.Instance.RequirementTooltip(n))
                        : Plugin.Instance.RequirementTooltip(n);
                }
            }
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), "Interact")]
    class AltarPatch
    {
        static bool Prefix(OfferingBowl __instance, Humanoid __0)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockAltar(__instance, __0 as Player);
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), "GetHoverText")]
    class AltarInterfacePatch
    {
        static void Postfix(OfferingBowl __instance, ref string __result)
        {
            if (Plugin.Instance != null)
                __result += Plugin.Instance.AltarRequirementText(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "TryPlacePiece")]
    class BuildPatch
    {
        static bool Prefix(Player __instance, Piece __0)
        {
            return Plugin.Instance == null || !Plugin.Instance.BlockPlacement(__0, __instance);
        }
    }

    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    class BuildInterfacePatch
    {
        static void Postfix(Hud __instance, Piece piece)
        {
            if (
                Plugin.Instance == null
                || __instance == null
                || __instance.m_pieceDescription == null
            )
                return;
            __instance.m_pieceDescription.text += Plugin.Instance.ConstructionRequirementText(piece);
        }
    }

    [HarmonyPatch(typeof(Player), "Repair")]
    class RepairPatch
    {
        static bool Prefix(Player __instance)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockRepair(__instance.GetHoveringPiece(), __instance);
        }
    }

    [HarmonyPatch(typeof(Player), "RemovePiece")]
    class RemovePatch
    {
        static bool Prefix(Player __instance)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockRemoval(__instance.GetHoveringPiece(), __instance);
        }
    }

    [HarmonyPatch(typeof(Pickable), "Interact")]
    class PickablePatch
    {
        static bool Prefix(Pickable __instance, Humanoid __0)
        {
            return !__0.IsPlayer()
                || Plugin.Instance == null
                || (
                    !Plugin.Instance.BlockResource(__instance.gameObject, __0 as Player)
                    && !Plugin.Instance.BlockPickupPrefab(__instance.m_itemPrefab, __0 as Player)
                );
        }
    }

    [HarmonyPatch(typeof(PickableItem), "Interact")]
    class PickableItemPatch
    {
        static bool Prefix(PickableItem __instance, Humanoid __0)
        {
            return !__0.IsPlayer()
                || __instance.m_itemPrefab == null
                || Plugin.Instance == null
                || (
                    !Plugin.Instance.BlockResource(__instance.gameObject, __0 as Player)
                    && !Plugin.Instance.BlockPickupPrefab(
                        __instance.m_itemPrefab.gameObject,
                        __0 as Player
                    )
                );
        }
    }

    [HarmonyPatch(typeof(MineRock), "Damage")]
    class MineRockPatch
    {
        static bool Prefix(MineRock __instance, HitData __0)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockResourceDamage(__instance.gameObject, __0);
        }
    }

    [HarmonyPatch(typeof(MineRock5), "Damage")]
    class MineRock5Patch
    {
        static bool Prefix(MineRock5 __instance, HitData __0)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockResourceDamage(__instance.gameObject, __0);
        }
    }

    [HarmonyPatch(typeof(TreeBase), "Damage")]
    class TreePatch
    {
        static bool Prefix(TreeBase __instance, HitData __0)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockResourceDamage(__instance.gameObject, __0);
        }
    }

    [HarmonyPatch(typeof(Destructible), "Damage")]
    class DestructiblePatch
    {
        static bool Prefix(Destructible __instance, HitData __0)
        {
            return Plugin.Instance == null
                || !Plugin.Instance.BlockResourceDamage(__instance.gameObject, __0);
        }
    }

    // Registers the client-side RPC handler that receives the blocked-action
    // message forwarded from the dedicated server by Plugin.Tell().
    [HarmonyPatch(typeof(Game), "Start")]
    class RpcRegisterPatch
    {
        static void Postfix()
        {
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.Register<int>(Plugin.BlockedRpc, (_, required) =>
                {
                    if (Plugin.Instance != null)
                        Plugin.Instance.Tell(Player.m_localPlayer, required);
                });
            }
        }
    }
}
