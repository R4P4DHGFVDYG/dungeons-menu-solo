using System.Collections.Generic;
using NeoRune;
using UE.AssetRegistry;
using UE.CoreUObject;
using UE.MinecartStations;
using UE.Dungeons;
using UE.Engine;
using UE.GameplayAbilities;
using UE.GameplayTags;
using UE.InventorySystem;
using UE.ItemGenerator;
using UE.NavigationSystem;
using UE.SpicewoodGAS;
using UE.SWCoreGAS;
using UE.SWCoreGameplay;
using UE.WorldTimeOfDay;

namespace DungeonsMenu;

public class GameAdapter : UObject, IMenuSpawnResponse
{
    UObject? context;
    APawn? combatPlayer;
    UAbilitySystemComponent? combatAsc;
    bool savedCanBeDamaged;
    bool changedCanBeDamaged;
    bool combatActive;
    bool combatRestoreBlocked;
    bool loggedPotionRefill;
    List<FGameplayAttribute> savedAttributes;
    List<float> savedValues;
    List<float> appliedValues;
    List<AActor> spawnedMobs;
    List<string> mobTags;
    List<string> itemIds;
    List<FSWSessionUID> itemUids;
    APawn? equipmentPlayer;
    APawn? spawnPlayer;
    string spawnLevel;
    bool rejectPendingMobs;
    bool mobRegistryScanned;
    List<AActor> knownMobActors;
    List<string> mobClassTags;
    List<string> mobClassPaths;
    string spawnRequestTag;
    double spawnDeadline;
    double nextSpawnScan;
    double watchUntil;
    double nextWatch;
    int pendingMobs;
    string spawnStatus;
    string combatStatus;
    // The combat options in force, in Rules.CombatKeys order (Rules.Neutral when off).
    List<float> combatOptions;
    // The options saved by "Aplicar", put back on each new hero ("" when none); reapplied a moment after the hero appears.
    string rememberedCombat;
    APawn? rememberPlayer;
    double reapplyAt;
    public ItemVisuals? Visuals;

    public void Initialize(UObject owner)
    {
        context = owner;
        Visuals = UGameplayStatics.SpawnObject(Unreal.ClassOf<ItemVisuals>(), this) as ItemVisuals;
        if (Visuals != null) Visuals.Initialize(owner);
        savedAttributes = new List<FGameplayAttribute>();
        savedValues = new List<float>();
        appliedValues = new List<float>();
        spawnedMobs = new List<AActor>();
        knownMobActors = new List<AActor>();
        mobClassTags = new List<string>();
        mobClassPaths = new List<string>();
        spawnRequestTag = "";
        mobTags = new List<string>();
        itemIds = new List<string>();
        itemUids = new List<FSWSessionUID>();
        spawnLevel = "";
        spawnStatus = "";
        combatStatus = "";
        combatOptions = NeutralOptions();
        rememberedCombat = Settings.Get(CombatSetting, "");
        slowLevel = "";
        gameSpeed = 1;
        Diag($"adapter ready level={World.LevelName(owner)} remembered={rememberedCombat != ""}");
    }

    const string CombatSetting = "combat";

    public bool CanWrite(out string reason)
    {
        reason = "Contexto do jogo indisponível.";
        return context != null && SessionGuard.CanWrite(context, out reason);
    }

    public string Status()
    {
        if (!CanWrite(out var reason)) return reason;
        return "Solo/offline • integração experimental • " + World.LevelName(context!)
            + (spawnStatus == "" ? "" : " • " + spawnStatus)
            + (combatStatus == "" ? "" : " • " + combatStatus);
    }

    UAbilitySystemComponent? AbilitySystem() => context == null ? null
        : UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(World.Player(context));

    UInventoryManagerComponent? Inventory() => context == null ? null
        : UInventoryManagerComponent.GetComponent(World.Player(context));

    public string ReadResources()
    {
        var asc = AbilitySystem();
        if (asc == null) return "Entre em uma partida para ler os recursos.";
        string result = "";
        foreach (var set in asc.SpawnedAttributes)
        {
            if (set is UATR_Currency currency)
                result += $"Esmeraldas: {currency.Emeralds.CurrentValue} | Fragmentos de eco: {currency.SpringStone.CurrentValue}";
            if (set is UATR_XP xp) result += $" | Pontos de encantamento: {xp.EnchantmentPoints.CurrentValue}";
        }
        return result == "" ? "Atributos do personagem ainda não disponíveis." : result;
    }

    /// <summary>One balance as whole units ("emeralds", "echo_shards" or "enchantment_points"), or "—" outside a match.</summary>
    public string ResourceAmount(string key)
    {
        var asc = AbilitySystem();
        if (asc == null) return "—";
        foreach (var set in asc.SpawnedAttributes)
        {
            if (set is UATR_Currency currency && key == "emeralds") return $"{(int)currency.Emeralds.CurrentValue}";
            if (set is UATR_Currency shards && key == "echo_shards") return $"{(int)shards.SpringStone.CurrentValue}";
            if (set is UATR_XP xp && key == "enchantment_points") return $"{(int)xp.EnchantmentPoints.CurrentValue}";
        }
        return "—";
    }

    public string SetResource(string resource, int amount, bool add)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Amount(amount)) return "Valor permitido: 0 a 1.000.000.";
        var asc = AbilitySystem();
        if (asc == null) return "Sistema de atributos indisponível.";
        var key = resource.ToLower();
        foreach (var set in asc.SpawnedAttributes)
        {
            if (set is UATR_Currency currency)
            {
                if (key == "emeralds" || key == "esmeraldas")
                {
                    float value = add ? currency.Emeralds.CurrentValue + amount : amount;
                    if (value > 1000000) return "O total excederia 1.000.000.";
                    if (value > currency.EmeraldsMax.CurrentValue)
                        SetBase(asc, Unreal.ClassOf<UATR_Currency>(), "EmeraldsMax", value);
                    SetBase(asc, Unreal.ClassOf<UATR_Currency>(), "Emeralds", value);
                    return ResourceResult(currency.Emeralds.CurrentValue, value);
                }
                if (key == "springstone" || key == "echo" || key == "echo_shards" || key == "fragmentos de eco")
                {
                    float value = add ? currency.SpringStone.CurrentValue + amount : amount;
                    if (value > 1000000) return "O total excederia 1.000.000.";
                    if (value > currency.SpringStoneMax.CurrentValue)
                        SetBase(asc, Unreal.ClassOf<UATR_Currency>(), "SpringStoneMax", value);
                    SetBase(asc, Unreal.ClassOf<UATR_Currency>(), "SpringStone", value);
                    return ResourceResult(currency.SpringStone.CurrentValue, value);
                }
            }
            if (set is UATR_XP xp && (key == "enchantmentpoints" || key == "enchantment_points" || key == "points" || key == "pontos"))
            {
                float value = add ? xp.EnchantmentPoints.CurrentValue + amount : amount;
                if (value > 1000000) return "O total excederia 1.000.000.";
                if (value > xp.EnchantmentPointsMax.CurrentValue)
                    SetBase(asc, Unreal.ClassOf<UATR_XP>(), "EnchantmentPointsMax", value);
                if (value > xp.EnchantmentPointsCap.CurrentValue)
                    SetBase(asc, Unreal.ClassOf<UATR_XP>(), "EnchantmentPointsCap", value);
                SetBase(asc, Unreal.ClassOf<UATR_XP>(), "EnchantmentPoints", value);
                return ResourceResult(xp.EnchantmentPoints.CurrentValue, value);
            }
        }
        return "Recurso desconhecido ou atributo ausente no personagem.";
    }

    static string ResourceResult(float actual, float requested) => actual == requested
        ? $"Valor lido após alteração: {actual}. Aguarde o salvamento do jogo; persistência ainda em validação."
        : $"O jogo retornou {actual}, diferente de {requested}; nenhuma confirmação de sucesso.";

    static void SetBase(UAbilitySystemComponent asc, TSubclassOf<UAttributeSet> type, string name, float value)
        => USWCoreGASUtil.SetNumericAttributeBase(asc, USWCoreGASUtil.GetAttributeChecked(type, name), value);

    // Native code is encrypted on disk, so behaviour is diagnosed from these lines in SaveGames/NeoRune_DungeonsMenu.sav.
    static void Diag(string text) => MenuLog.Write("[diag] " + text);

    public List<string> ItemCatalog(string query)
    {
        var result = new List<string>();
        var inventory = Inventory();
        if (inventory == null || context == null) return result;
        var player = World.Player(context);
        foreach (var slot in inventory.GetInventory())
            foreach (var tag in UInventoryHelperLibrary.GetAllPossibleItemsForSlot(player, slot.TypeTag))
            {
                var name = tag.TagName.ToString();
                if (!result.Contains(name) && name.ToLower().Contains(query.ToLower())) result.Add(name);
            }
        return result;
    }

    // Soul Storm gear is decided by native code (IsSoulStormGearItem, encrypted on disk) and nothing about it is in the
    // character save, so this asks the game about every item once and logs what the flagged ones have in common.
    bool soulStormProbed;

    public void ProbeSoulStorm()
    {
        if (soulStormProbed || context == null) return;
        var player = World.Player(context);
        var inventory = Inventory();
        if (player == null || inventory == null) return;
        soulStormProbed = true;
        int total = 0, storm = 0;
        var sources = new List<string>();
        string samples = "";
        string stormType = "";
        string normalType = "";
        foreach (var item in inventory.GetAllItems())
        {
            total++;
            var data = item.ItemData;
            var origin = data.DropSource.TagName.ToString() + "|" + data.QuestSource.TagName.ToString();
            if (!sources.Contains(origin)) sources.Add(origin);
            if (!UInventoryHelperLibrary.IsSoulStormGearItem(player, data.SessionUID))
            {
                if (normalType == "") normalType = data.TypeTag.TagName.ToString();
                continue;
            }
            storm++;
            if (stormType == "") stormType = data.TypeTag.TagName.ToString();
            if (storm <= 12) samples += $"{data.TypeTag.TagName}/{data.RarityTag.TagName}/origem={origin}; ";
        }
        string origins = "";
        foreach (var origin in sources) origins += origin + " ";
        Diag($"soulstorm itens={total} soulstorm={storm} origens={origins} exemplos={samples}");
        if (stormType != "") Diag($"soulstorm tipo {stormType}: {TypeTags(stormType)}");
        if (normalType != "") Diag($"normal tipo {normalType}: {TypeTags(normalType)}");
    }

    // The game's Portuguese names live in its encrypted .pak, so the menu writes them out once: every enchantment (with the
    // game's description) and every item, as "tag = name", to SaveGames/NeoRune_DungeonsMenu_Names.sav.
    const string NamesSlot = "NeoRune_DungeonsMenu_Names";

    public void DumpNamesOnce()
    {
        if (context == null || Visuals == null || UGameplayStatics.DoesSaveGameExist(NamesSlot, 0)) return;
        var player = World.Player(context);
        var world = player != null ? UKismetSystemLibrary.GetOuterObject(UKismetSystemLibrary.GetOuterObject(player)) as UWorld : null;
        var data = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<UiCatalogData>()) as UiCatalogData;
        if (world == null || data == null) return;
        var lines = new List<string>();
        foreach (var entry in USWTypeSystemHelperUtils.GetAllEnchantments(world))
        {
            var tag = entry.Key.TagName.ToString();
            var description = UKismetTextLibrary.Conv_TextToString(UInventoryHelperLibrary.GetEnchantmentDescription(context, entry.Key));
            lines.Add("encantamento " + tag + " = " + Visuals.Title(tag) + " || " + description);
        }
        var tags = new List<string>();
        var slots = new List<string>();
        ItemCatalogSlots(tags, slots);
        for (int i = 0; i < tags.Count; i++) lines.Add("item " + tags[i] + " = " + Visuals.Title(tags[i]) + " || " + slots[i]);
        data.Entries = lines;
        UGameplayStatics.SaveGameToSlot(data, NamesSlot, 0);
        Diag($"names dumped={lines.Count}");
    }

    // Checks the menu's lists against the game's own: per equipment slot, the enchantments the menu shows versus every
    // base enchantment the game lets that slot take; and the item catalog versus every item type in the registry.
    // Written once to SaveGames/NeoRune_DungeonsMenu_Audit.sav (read with work/read-audit.py).
    const string AuditSlot = "NeoRune_DungeonsMenu_Audit";

    public void AuditOnce()
    {
        if (context == null || UGameplayStatics.DoesSaveGameExist(AuditSlot, 0)) return;
        var player = World.Player(context);
        var world = player != null ? UKismetSystemLibrary.GetOuterObject(UKismetSystemLibrary.GetOuterObject(player)) as UWorld : null;
        var data = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<UiCatalogData>()) as UiCatalogData;
        if (world == null || data == null) return;
        var lines = new List<string>();
        var slots = new List<string> { "SW.ItemSlot.Equipment.MeleeWeapon", "SW.ItemSlot.Equipment.RangedWeapon", "SW.ItemSlot.Equipment.Armor.Helmet", "SW.ItemSlot.Equipment.Armor.Chest", "SW.ItemSlot.Equipment.Armor.Leggings", "SW.ItemSlot.Equipment.Armor.Boots" };
        foreach (var slot in slots)
        {
            var slotTag = new FGameplayTag { TagName = slot };
            string shown = "";
            var shownList = new List<string>();
            foreach (var entry in USWTypeSystemHelperUtils.GetFilteredEnchantmentsBySlot(world, slotTag))
            {
                shownList.Add(entry.Key.TagName.ToString());
                shown += entry.Key.TagName.ToString() + " ";
            }
            string allowed = "";
            string missing = "";
            foreach (var entry in USWTypeSystemHelperUtils.GetAllEnchantments(world))
            {
                var tag = entry.Key.TagName.ToString();
                // Base enchantments only ("SW.Enchantment.X"); their levels add a fourth part.
                if (tag.LastIndexOf(".") != 14 || !USWTypeSystemHelperUtils.CanImbueEnchantment(world, entry.Key, slotTag)) continue;
                allowed += tag + " ";
                if (!shownList.Contains(tag)) missing += tag + " ";
            }
            lines.Add($"slot {slot} menu({shownList.Count}): {shown}");
            lines.Add($"slot {slot} jogo-aceita: {allowed}");
            lines.Add($"slot {slot} faltando-no-menu: {missing}");
        }
        foreach (var entry in USWTypeSystemHelperUtils.GetAllEnchantments(world))
        {
            var tag = entry.Key.TagName.ToString();
            if (tag.LastIndexOf(".") != 14) continue;
            string relevant = "";
            foreach (var slot in entry.Value.RelevantSlots.GameplayTags) relevant += slot.TagName.ToString() + " ";
            lines.Add($"encantamento {tag} slots: {relevant}");
        }
        var tags = new List<string>();
        var itemSlots = new List<string>();
        ItemCatalogSlots(tags, itemSlots);
        var known = new List<string>();
        foreach (var tag in tags) known.Add(tag.Replace(".", "_").ToLower());
        int registered = 0, outside = 0;
        var registry = UAssetRegistryHelpers.GetAssetRegistry();
        if (registry != null && registry.GetAssetsByPath("/Game/Spicewood/GameplayLogic/SWTypeSystemPrimaryDataAsset", out var assets, true, true))
            foreach (var asset in assets)
            {
                var name = asset.AssetName.ToString();
                if (!name.ToLower().StartsWith("sw_item_")) continue;
                registered++;
                if (known.Contains(name.ToLower())) continue;
                outside++;
                lines.Add("item fora do catalogo: " + name);
            }
        lines.Add($"itens catalogo={tags.Count} tipos-registrados={registered} fora={outside}");
        data.Entries = lines;
        UGameplayStatics.SaveGameToSlot(data, AuditSlot, 0);
        Diag($"audit enchant-slots={slots.Count} items catalogo={tags.Count} registrados={registered} fora={outside}");
    }

    string TypeTags(string tag)
    {
        var system = USWTypeSystem.GetSWTypeSystemFromUObject(context);
        if (system == null) return "sem type system";
        var type = system.AS_GetType(new FGameplayTag { TagName = tag });
        string text = "pais=";
        foreach (var parent in type.Parents.GameplayTags) text += parent.TagName.ToString() + " ";
        text += " conjunto=";
        foreach (var member in type.FinalizedTypeset.GameplayTags) text += member.TagName.ToString() + " ";
        return text;
    }

    /// <summary>Every item the hero can hold, with the inventory slot each one belongs to (for the menu's filters).</summary>
    public void ItemCatalogSlots(List<string> tags, List<string> slots)
    {
        tags.Clear();
        slots.Clear();
        var inventory = Inventory();
        if (inventory == null || context == null) return;
        var player = World.Player(context);
        foreach (var slot in inventory.GetInventory())
            foreach (var tag in UInventoryHelperLibrary.GetAllPossibleItemsForSlot(player, slot.TypeTag))
            {
                var name = tag.TagName.ToString();
                if (tags.Contains(name)) continue;
                tags.Add(name);
                slots.Add(slot.TypeTag.TagName.ToString());
            }
    }

    public string SpawnItem(string tag, int quantity) => SpawnItem(tag, quantity, "");

    /// <summary>Creates items; <paramref name="rarity"/> is a SW.Rarity tag, or "" for unique items as unique and others as generated.</summary>
    public string SpawnItem(string tag, int quantity, string rarity)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Quantity(quantity)) return "Quantidade permitida: 1 a 20.";
        if (!ItemCatalog("").Contains(tag)) return "Selecione um item do catálogo válido do personagem.";
        var inventory = Inventory();
        var player = World.Player(context!);
        if (inventory == null || player == null) return "Inventário indisponível.";
        var itemTag = new FGameplayTag { TagName = tag };
        var category = UInventoryHelperLibrary.GetItemCategoryTag(player, itemTag);
        int limit = UInventoryHelperLibrary.GetInventoryStorageLimit(player, category);
        int occupied = 0;
        foreach (var item in inventory.GetAllItems())
            if (UInventoryHelperLibrary.GetItemCategoryTag(player, item.ItemData.TypeTag).TagName == category.TagName) occupied++;
        if (limit <= 0) return "Não foi possível confirmar a capacidade desta categoria.";
        if (occupied + quantity > limit) return $"Inventário sem espaço: {occupied}/{limit} nesta categoria.";
        if (rarity == "" && tag.ToLower().Contains("unique")) rarity = UniqueRarity;
        var existing = new List<ulong>();
        foreach (var item in inventory.GetAllItems()) existing.Add(item.ItemData.SessionUID.UID);
        int before = CountItems(inventory, tag, false);
        for (int i = 0; i < quantity; i++) UActorUtil.SpawnAndAddItem(player, itemTag, new List<FGameplayTag>());
        int added = CountItems(inventory, tag, false) - before;
        // SpawnAndAddItem ignores its extra tags and the loot rarity bonus (log of 0.1.4: WarHammer_Unique1=SW.Rarity.Common),
        // so the menu sets RarityTag on the new inventory entries themselves, which the game's own tooltips read.
        int fixedRarity = rarity != "" ? SetRarity(inventory, tag, rarity, existing, 0) : 0;
        string rarityText = rarity != "" ? $" Raridade {ItemVisuals.RarityName(ItemVisuals.RarityIndex(rarity))} aplicada em {fixedRarity}." : "";
        Diag($"item {tag} rarity={rarity} added={added} fixedRarity={fixedRarity} read={LastRarity(inventory, tag)}");
        return added == quantity ? $"Inventário confirmou {added} item(ns).{rarityText} Aguarde o salvamento do jogo."
            : $"Criação solicitada: {quantity}; confirmados imediatamente: {added}.{rarityText} Confira o inventário antes de repetir.";
    }

    const string UniqueRarity = "SW.Rarity.Unique";

    // Sets RarityTag on one item (onlyUid) or on every item of a type not listed in keepUids (the ones created just now).
    static int SetRarity(UInventoryManagerComponent inventory, string tag, string rarity, List<ulong> keepUids, ulong onlyUid)
    {
        int changed = 0;
        var container = inventory.ReplicatedItems;
        var slots = container.Items;
        for (int s = 0; s < slots.Count; s++)
        {
            var slot = slots[s];
            var entries = slot.ItemInventoryEntries;
            bool slotChanged = false;
            for (int e = 0; e < entries.Count; e++)
            {
                var entry = entries[e];
                var uid = entry.ItemData.SessionUID.UID;
                bool target = onlyUid != 0 ? uid == onlyUid
                    : entry.ItemData.TypeTag.TagName.ToString() == tag && !keepUids.Contains(uid);
                if (!target || entry.ItemData.RarityTag.TagName.ToString() == rarity) continue;
                var data = entry.ItemData;
                data.RarityTag = new FGameplayTag { TagName = rarity };
                entry.ItemData = data;
                entries[e] = entry;
                slotChanged = true;
                changed++;
            }
            if (!slotChanged) continue;
            slot.ItemInventoryEntries = entries;
            slots[s] = slot;
        }
        if (changed == 0) return 0;
        container.Items = slots;
        inventory.ReplicatedItems = container;
        return changed;
    }

    static string AllRarities(UInventoryManagerComponent inventory)
    {
        var names = new List<string>();
        foreach (var item in inventory.GetAllItems())
        {
            var name = item.ItemData.TypeTag.TagName.ToString() + "=" + item.ItemData.RarityTag.TagName.ToString();
            if (!names.Contains(name)) names.Add(name);
        }
        string text = "";
        foreach (var name in names) text += name + "; ";
        return text;
    }

    static string LastRarity(UInventoryManagerComponent inventory, string tag)
    {
        string rarity = "nenhuma";
        foreach (var item in inventory.GetAllItems())
            if (item.ItemData.TypeTag.TagName.ToString() == tag) rarity = item.ItemData.RarityTag.TagName.ToString();
        return rarity;
    }

    static int CountItems(UInventoryManagerComponent inventory, string tag, bool uniqueOnly)
    {
        int count = 0;
        foreach (var item in inventory.GetAllItems())
            if (item.ItemData.TypeTag.TagName.ToString() == tag && (!uniqueOnly || item.ItemData.RarityTag.TagName.ToString() == UniqueRarity))
                count += item.StackCount > 0 ? item.StackCount : 1;
        return count;
    }

    public List<string> MobCatalog(string query)
    {
        var result = new List<string>();
        if (context == null || World.Player(context) == null) return result;
        if (!mobRegistryScanned)
        {
            mobRegistryScanned = true;
            int total = 0, mobAssets = 0, loaded = 0;
            string sample = "";
            var registry = UAssetRegistryHelpers.GetAssetRegistry();
            // Type assets are named after their tag with underscores (log of 0.1.4: SW_Area_Camp_A1), not "Mob_".
            if (registry != null && registry.GetAssetsByPath("/Game/Spicewood/GameplayLogic/SWTypeSystemPrimaryDataAsset", out var assets, true, true))
                foreach (var asset in assets)
                {
                    var name = asset.AssetName.ToString();
                    total++;
                    if (name.Contains("Zombie") && sample.Length < 300) sample += name + " ";
                    if (!name.StartsWith("SW_Mob") && !name.Contains("_Mob_")) continue;
                    mobAssets++;
                    if (loaded >= 400) continue;
                    var path = asset.PackageName.ToString() + "." + name;
                    var data = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path))) as USWTypeSystemTypeDataAsset;
                    if (data == null) continue;
                    loaded++;
                    var type = data.Type.ToString();
                    AddMobType(type);
                    // The type asset references the mob's Blueprint class; keep its path for a direct spawn.
                    string refs = "";
                    foreach (var reference in data.AssetReferences)
                    {
                        if (reference == null) continue;
                        var refPath = UKismetSystemLibrary.GetPathName(reference);
                        if (loaded <= 3) refs += refPath + " ";
                        if (!refPath.EndsWith("_C") || mobClassTags.Contains(type)) continue;
                        if (Unreal.LoadClass<APawn>(refPath) == null) continue;
                        mobClassTags.Add(type);
                        mobClassPaths.Add(refPath);
                    }
                    if (loaded <= 3) Diag($"mob type {type} refs={refs}");
                }
            Diag($"mob registry={(registry != null)} assets={total} mob_assets={mobAssets} loaded={loaded} classes={mobClassPaths.Count}");
        }
        // Every live character carries its type in an SWTypeComponent; the soul-state tracker only knows a few mobs (it
        // returned None for all of them in 0.1.4).
        int actors = 0;
        string actorTypes = "";
        foreach (var actor in World.FindAll(context, Unreal.ClassOf<AMobCharacter>()))
        {
            actors++;
            var type = USWTypeComponent.GetTypeTagFromActor(actor, true).TagName.ToString();
            if (actors <= 6) actorTypes += UKismetSystemLibrary.GetDisplayName(actor) + "=" + type + " ";
            AddMobType(type);
        }
        Diag($"mob actors={actors} types={mobTags.Count} sample={actorTypes}");
        foreach (var name in mobTags) if (name.ToLower().Contains(query.ToLower())) result.Add(name);
        return result;
    }

    void AddMobType(string name)
    {
        if (name != "" && name != "None" && !mobTags.Contains(name)) mobTags.Add(name);
    }

    public string SpawnMob(string tag, int quantity)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Quantity(quantity)) return "Quantidade permitida: 1 a 20.";
        if (!MobCatalog("").Contains(tag)) return "Selecione um mob do catálogo carregado pelo jogo.";
        if (pendingMobs > 0) return "Aguarde as criações pendentes antes de repetir.";
        var player = World.Player(context!);
        var spawner = USubsystemBlueprintLibrary.GetWorldSubsystem(context!, Unreal.ClassOf<UActorSpawnSubsystem>()) as UActorSpawnSubsystem;
        if (player == null || spawner == null) return "Sistema de criação de entidades indisponível.";
        spawnPlayer = player;
        spawnLevel = World.LevelName(context!);
        rejectPendingMobs = false;
        // The spawn subsystem never called this object back in 0.1.5, so new pawns are found by comparing the live ones
        // (not only AMobCharacter: animals and summons may use other classes).
        knownMobActors = World.FindAll(context!, Unreal.ClassOf<APawn>());
        spawnRequestTag = tag;
        spawnDeadline = World.RealTime(context!) + SpawnWaitSeconds;
        var origin = player.K2_GetActorLocation();
        // 0.1.7 in game: RequestSpawnActorBP queued a zombie in the camp and nothing ever appeared. When the type asset names
        // a Blueprint class, the actor is spawned directly (this also works while the menu pauses the game).
        int classIndex = mobClassTags.IndexOf(tag);
        if (classIndex >= 0)
        {
            var mobClass = Unreal.LoadClass<APawn>(mobClassPaths[classIndex]);
            int created = 0;
            for (int i = 0; mobClass != null && i < quantity; i++)
            {
                var spot = SpawnPoint(origin);
                // The type system builds the mob (mesh, abilities, AI) from its SWTypeComponent tag. A plain spawn left the
                // tag None and the zombie invisible and still (0.1.9), so the tag is set before the spawn finishes.
                var transform = UKismetMathLibrary.MakeTransform(spot, new FRotator(), new FVector { X = 1, Y = 1, Z = 1 });
                var actor = UGameplayStatics.BeginDeferredActorSpawnFromClass(context!, mobClass, transform,
                    ESpawnActorCollisionHandlingMethod.AdjustIfPossibleButAlwaysSpawn, null, ESpawnActorScaleMethod.MultiplyWithRoot);
                if (actor == null) continue;
                var typeComponent = actor.GetComponentByClass(Unreal.ClassOf<USWTypeComponent>()) as USWTypeComponent;
                if (typeComponent != null) typeComponent.SetTypeTag(new FGameplayTag { TagName = tag });
                actor = UGameplayStatics.FinishSpawningActor(actor, transform, ESpawnActorScaleMethod.MultiplyWithRoot);
                if (actor == null || !UKismetSystemLibrary.IsValid(actor)) continue;
                if (typeComponent != null && !IsTag(USWTypeComponent.GetTypeTagFromActor(actor, true))) typeComponent.SetTypeTag(new FGameplayTag { TagName = tag });
                Diag($"mob type component={(typeComponent != null)} tag={USWTypeComponent.GetTypeTagFromActor(actor, true).TagName} setup={(typeComponent != null && typeComponent.bHasRanSetup)}");
                var pawn = actor as APawn;
                if (pawn != null && pawn.GetController() == null) pawn.SpawnDefaultController();
                spawnedMobs.Add(actor);
                created++;
                Diag($"mob direct spawned {UKismetSystemLibrary.GetDisplayName(actor)} at {spot.X:0},{spot.Y:0},{spot.Z:0} hero {origin.X:0},{origin.Y:0},{origin.Z:0} hidden={actor.bHidden} controller={(pawn != null && pawn.GetController() != null)}");
            }
            Diag($"mob direct {tag} class={mobClassPaths[classIndex]} created={created}");
            watchUntil = World.RealTime(context!) + 10;
            spawnStatus = $"Mobs confirmados: {spawnedMobs.Count}";
            if (created > 0) return $"{created} mob(s) criado(s) perto do herói. Feche o menu para eles agirem.";
        }
        int queued = 0, offNav = 0;
        for (int i = 0; i < quantity; i++)
        {
            if (!UNavigationSystemV1.K2_GetRandomReachablePointInRadius(context!, origin, out var point, 600, null!, null!))
            {
                // No navmesh around the hero (0.1.6: "Nenhuma posição navegável"): use a ring around the hero instead.
                double angle = UKismetMathLibrary.RandomFloatInRange(0, 360);
                point = new FVector { X = origin.X + UKismetMathLibrary.DegCos(angle) * 300, Y = origin.Y + UKismetMathLibrary.DegSin(angle) * 300, Z = origin.Z };
                offNav++;
            }
            // Navmesh points lie on the floor; a capsule placed there collides and "don't spawn if colliding" dropped most
            // requests in 0.1.6. Lift it and let the engine adjust.
            point.Z += SpawnLift;
            var transform = UKismetMathLibrary.MakeTransform(point, new FRotator(), new FVector { X = 1, Y = 1, Z = 1 });
            pendingMobs++;
            queued++;
            spawner.RequestSpawnActorBP(new FGameplayTag { TagName = tag }, transform, EActorSpawnPriority.EPriority_Normal,
                player, player, ESpawnActorCollisionHandlingMethod.AdjustIfPossibleButAlwaysSpawn, this, null!);
        }
        Diag($"mob spawn {tag} queued={queued} offNav={offNav} pending={pendingMobs}");
        return $"{queued} criação(ões) solicitada(s). O status confirma as respostas do jogo.";
    }

    public string RemoveSpawnedMobs()
    {
        if (!CanWrite(out var reason)) return reason;
        rejectPendingMobs = true;
        int count = 0;
        foreach (var actor in spawnedMobs)
            if (UKismetSystemLibrary.IsValid(actor)) { actor.K2_DestroyActor(); count++; }
        spawnedMobs.Clear();
        return $"Remoção solicitada para {count} mob(s) deste menu; respostas pendentes serão descartadas.";
    }

    public List<string> EquipmentCatalog()
    {
        var result = new List<string>();
        itemIds.Clear();
        itemUids.Clear();
        equipmentPlayer = null;
        if (context == null || World.Player(context) == null) return result;
        equipmentPlayer = World.Player(context);
        foreach (var item in UInventoryHelperLibrary.GetAllEquippedEnchantableItems(World.Player(context)))
        {
            var id = (itemIds.Count + 1).ToString() + " | " + item.ItemData.TypeTag.TagName.ToString();
            result.Add(id);
            itemIds.Add(id);
            itemUids.Add(item.ItemData.SessionUID);
            Diag($"equip {id} slot={item.EquippedSlot.TagName} enchanted={UInventoryHelperLibrary.IsItemEnchanted(item.ItemData)}");
        }
        Diag($"equip enchantable={result.Count}");
        return result;
    }

    bool FindEquipment(string itemId, out FInventoryEntry item)
    {
        item = new FInventoryEntry();
        if (context == null || equipmentPlayer == null || equipmentPlayer != World.Player(context)) return false;
        for (int i = 0; i < itemIds.Count; i++)
            if (itemIds[i] == itemId)
                return UInventoryHelperLibrary.GetInventoryEntryFromID(World.Player(context), itemUids[i], out item)
                    && UInventoryHelperLibrary.IsItemEquipped(item);
        return false;
    }

    // ---------------------------------------------------------------- Free mode: any enchantment, any level, item power

    public const int MaxFreeLevel = 20;
    const string EnchantmentBatch = "SW.Item.Effect.Enchantment";

    /// <summary>Every base enchantment in the game, whatever slot it is meant for.</summary>
    public List<string> AllEnchantments()
    {
        var result = new List<string>();
        var player = context != null ? World.Player(context) : null;
        var world = player != null ? GameWorld(player) : null;
        if (world == null) return result;
        foreach (var entry in USWTypeSystemHelperUtils.GetAllEnchantments(world))
        {
            var tag = entry.Key.TagName.ToString();
            if (tag.LastIndexOf(".") == 14 && !result.Contains(tag)) result.Add(tag);
        }
        return result;
    }

    // The regular levels (".I", ".II", ...) of a base enchantment, in order.
    List<FGameplayTag> EnchantLevels(UWorld world, string baseTag)
    {
        var levels = new List<FGameplayTag>();
        foreach (var child in USWTypeSystemHelperUtils.GetEnchantmentChildrenOrdered(world, new FGameplayTag { TagName = baseTag }))
            if (IsTag(child) && !USWTypeSystemHelperUtils.IsTagLevelUnique(child)) levels.Add(child);
        return levels;
    }

    public int NormalMaxLevel(string baseTag)
    {
        var player = context != null ? World.Player(context) : null;
        var world = player != null ? GameWorld(player) : null;
        if (world == null) return 1;
        int count = EnchantLevels(world, baseTag).Count;
        return count > 0 ? count : 1;
    }

    static float EffectValue(UWorld world, FGameplayTag levelTag)
    {
        if (!USWTypeSystemHelperUtils.GetEnchantmentData(world, levelTag, out var info)) return 0;
        return info.HasFixedEffectValue ? info.FixedEffectValue : info.EffectMaxValue;
    }

    float PotionMax()
    {
        var asc = AbilitySystem();
        if (asc == null) return -1;
        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_Health health) return health.HealthPotionChargesMax.CurrentValue;
        return -1;
    }

    /// <summary>
    /// Writes an enchantment straight into an equipped item's enchantment effects, skipping the game's slot and level
    /// rules: the level's template and the effect strength the game's data gives it, and beyond the last level the
    /// strength keeps growing by the step between the last two levels. The item is re-equipped so its effects apply.
    /// </summary>
    public string WriteEnchantment(string itemId, string baseTag, int level)
    {
        if (!CanWrite(out var reason)) return reason;
        if (level < 1 || level > MaxFreeLevel) return $"Nível livre: 1 a {MaxFreeLevel}.";
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; selecione de novo.";
        var player = World.Player(context!);
        var world = player != null ? GameWorld(player) : null;
        var inventory = Inventory();
        if (world == null || inventory == null) return "Inventário indisponível.";
        var levels = EnchantLevels(world, baseTag);
        if (levels.Count == 0) levels.Add(new FGameplayTag { TagName = baseTag });
        int top = levels.Count;
        var template = level <= top ? levels[level - 1] : levels[top - 1];
        float topValue = EffectValue(world, levels[top - 1]);
        float step = top >= 2 ? topValue - EffectValue(world, levels[top - 2]) : topValue;
        if (step <= 0) step = topValue > 0 ? topValue : 1;
        float intensity = level <= top ? EffectValue(world, levels[level - 1]) : topValue + (level - top) * step;
        float potionsBefore = PotionMax();
        var uid = item.ItemData.SessionUID;
        if (!WriteEnchantmentEffect(inventory, uid.UID, baseTag, template, intensity)) return "O item não foi encontrado no inventário do jogo.";
        // Re-equipping makes the game apply the item's effects again with the new enchantment.
        var slot = item.EquippedSlot;
        inventory.UnequipItemFromSlot(slot);
        bool equipped = inventory.EquipItemBySessionUID(slot, uid);
        FindEquipment(itemId, out item);
        var applied = UInventoryHelperLibrary.GetEnchantmentSpecificTagAppliedOnItem(item.ItemData).TagName.ToString();
        float potionsAfter = PotionMax();
        Diag($"free enchant {baseTag} level={level} template={template.TagName} intensity={intensity} applied={applied} reequip={equipped} potions {potionsBefore}->{potionsAfter}");
        string over = level > top ? $" (acima do máximo normal {top}: experimental)" : "";
        return $"Gravado no item: nível {level}{over}, força {intensity:0.##}. Reequipado: {(equipped ? "sim" : "não, equipe de novo pelo inventário")}.";
    }

    // Replaces the enchantment in the item's enchantment batch (adding the batch when the item has none).
    static bool WriteEnchantmentEffect(UInventoryManagerComponent inventory, ulong uid, string baseTag, FGameplayTag template, float intensity)
    {
        var container = inventory.ReplicatedItems;
        var slots = container.Items;
        for (int s = 0; s < slots.Count; s++)
        {
            var slot = slots[s];
            var entries = slot.ItemInventoryEntries;
            for (int e = 0; e < entries.Count; e++)
            {
                var entry = entries[e];
                if (entry.ItemData.SessionUID.UID != uid) continue;
                var data = entry.ItemData;
                var batches = data.Effects;
                var effects = new List<FItemEffect>();
                effects.Add(new FItemEffect
                {
                    TypeTag = new FGameplayTag { TagName = baseTag },
                    Intensity = intensity,
                    EnchantmentPointsInvested = 0,
                    GeneratorData = new FEffectGeneratorData { GeneratorParentTemplate = template },
                });
                int at = -1;
                for (int b = 0; b < batches.Count; b++)
                    if (batches[b].TypeTag.TagName.ToString() == EnchantmentBatch) at = b;
                if (at >= 0)
                {
                    var batch = batches[at];
                    batch.EffectsInThisBatch = effects;
                    batches[at] = batch;
                }
                else batches.Add(new FItemEffectBatch { TypeTag = new FGameplayTag { TagName = EnchantmentBatch }, EffectsInThisBatch = effects });
                data.Effects = batches;
                entry.ItemData = data;
                entries[e] = entry;
                slot.ItemInventoryEntries = entries;
                slots[s] = slot;
                container.Items = slots;
                inventory.ReplicatedItems = container;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Takes the enchantment off an equipped item: the game's disenchant first (as at the Enchantsmith, with its refund),
    /// and if the game keeps it, the item's enchantment effects are removed directly and the item is re-equipped.
    /// </summary>
    public string ClearEnchantment(string itemId)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; selecione de novo.";
        var player = World.Player(context!);
        var inventory = Inventory();
        if (player == null || inventory == null) return "Inventário indisponível.";
        if (!UInventoryHelperLibrary.IsItemEnchanted(item.ItemData)) return "Este item não tem encantamento.";
        var uid = item.ItemData.SessionUID;
        var before = UInventoryHelperLibrary.GetEnchantmentSpecificTagAppliedOnItem(item.ItemData).TagName.ToString();
        bool native = UInventoryHelperLibrary.TryDisenchantItem(player, uid);
        bool still = !FindEquipment(itemId, out item) || UInventoryHelperLibrary.IsItemEnchanted(item.ItemData);
        bool direct = false;
        bool equipped = true;
        if (still)
        {
            direct = ClearEnchantmentEffect(inventory, uid.UID);
            var slot = item.EquippedSlot;
            inventory.UnequipItemFromSlot(slot);
            equipped = inventory.EquipItemBySessionUID(slot, uid);
            still = FindEquipment(itemId, out item) && UInventoryHelperLibrary.IsItemEnchanted(item.ItemData);
        }
        Diag($"disenchant {itemId} was={before} native={native} direct={direct} reequip={equipped} still={still}");
        if (still) return "O jogo manteve o encantamento; nenhuma alteração confirmada.";
        if (!direct) return "Encantamento removido pelo jogo, como no Encantador.";
        return $"Encantamento removido direto do item. Reequipado: {(equipped ? "sim" : "não, equipe de novo pelo inventário")}.";
    }

    // Removes the item's enchantment batch.
    static bool ClearEnchantmentEffect(UInventoryManagerComponent inventory, ulong uid)
    {
        var container = inventory.ReplicatedItems;
        var slots = container.Items;
        for (int s = 0; s < slots.Count; s++)
        {
            var slot = slots[s];
            var entries = slot.ItemInventoryEntries;
            for (int e = 0; e < entries.Count; e++)
            {
                var entry = entries[e];
                if (entry.ItemData.SessionUID.UID != uid) continue;
                var data = entry.ItemData;
                var batches = data.Effects;
                bool removed = false;
                for (int b = batches.Count - 1; b >= 0; b--)
                    if (batches[b].TypeTag.TagName.ToString() == EnchantmentBatch) { batches.RemoveAt(b); removed = true; }
                if (!removed) return false;
                data.Effects = batches;
                entry.ItemData = data;
                entries[e] = entry;
                slot.ItemInventoryEntries = entries;
                slots[s] = slot;
                container.Items = slots;
                inventory.ReplicatedItems = container;
                return true;
            }
        }
        return false;
    }

    public bool IsEnchanted(string itemId) => FindEquipment(itemId, out var item) && UInventoryHelperLibrary.IsItemEnchanted(item.ItemData);

    public int ItemPower(string itemId) => FindEquipment(itemId, out var item) && UInventoryHelperLibrary.GetItemPowerLevel(item.ItemData, out var power) ? power : 0;

    /// <summary>Sets an equipped item's power: the game's own upgrade first, the stored value directly if it refuses.</summary>
    public string SetItemPower(string itemId, int power)
    {
        if (!CanWrite(out var reason)) return reason;
        if (power < 1 || power > 999) return "Poder permitido: 1 a 999.";
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; selecione de novo.";
        var inventory = Inventory();
        if (inventory == null) return "Inventário indisponível.";
        int before = ItemPower(itemId);
        var uid = item.ItemData.SessionUID;
        bool upgraded = inventory.UpgradeItemPower(item.EquippedSlot, uid, power);
        int after = ItemPower(itemId);
        bool direct = false;
        if (after != power)
        {
            direct = WriteItemPower(inventory, uid.UID, power);
            var slot = item.EquippedSlot;
            inventory.UnequipItemFromSlot(slot);
            inventory.EquipItemBySessionUID(slot, uid);
            after = ItemPower(itemId);
        }
        Diag($"item power {itemId} {before}->{after} requested={power} upgrade={upgraded} direct={direct}");
        return after == power ? $"Poder do item agora: {after} (antes {before})." : $"O jogo manteve o poder em {after}; pedido {power}.";
    }

    static bool WriteItemPower(UInventoryManagerComponent inventory, ulong uid, int power)
    {
        var container = inventory.ReplicatedItems;
        var slots = container.Items;
        for (int s = 0; s < slots.Count; s++)
        {
            var slot = slots[s];
            var entries = slot.ItemInventoryEntries;
            for (int e = 0; e < entries.Count; e++)
            {
                var entry = entries[e];
                if (entry.ItemData.SessionUID.UID != uid) continue;
                var data = entry.ItemData;
                var generator = data.GeneratorData;
                var values = generator.PowerGeneratorValues;
                values.ItemPower = power;
                generator.PowerGeneratorValues = values;
                data.GeneratorData = generator;
                entry.ItemData = data;
                entries[e] = entry;
                slot.ItemInventoryEntries = entries;
                slots[s] = slot;
                container.Items = slots;
                inventory.ReplicatedItems = container;
                return true;
            }
        }
        return false;
    }

    /// <summary>The item tag inside an equipment id ("1 | SW.Item.Sword" -> "SW.Item.Sword").</summary>
    public static string EquipmentTag(string itemId)
    {
        int at = itemId.IndexOf(" | ");
        return at >= 0 ? itemId.Substring(at + 3) : itemId;
    }

    public string EquippedRarity(string itemId) => FindEquipment(itemId, out var item) ? item.ItemData.RarityTag.TagName.ToString() : "";

    /// <summary>Changes the rarity of one equipped item (its tag only: the effects it rolled when created stay).</summary>
    public string SetEquippedRarity(string itemId, string rarity)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; selecione de novo.";
        var inventory = Inventory();
        if (inventory == null) return "Inventário indisponível.";
        int changed = SetRarity(inventory, "", rarity, new List<ulong>(), item.ItemData.SessionUID.UID);
        var now = EquippedRarity(itemId);
        Diag($"equip rarity {itemId} -> {rarity} changed={changed} read={now}");
        return now == rarity ? $"Raridade do equipamento agora: {ItemVisuals.RarityName(ItemVisuals.RarityIndex(rarity))}. Aguarde o salvamento do jogo."
            : $"O jogo manteve a raridade {now}; nenhuma alteração confirmada.";
    }

    public List<string> EnchantmentCatalog(string itemId, string query)
    {
        var result = new List<string>();
        if (!FindEquipment(itemId, out var item)) return result;
        var names = new List<string>();
        EnchantTiers(World.Player(context!)!, item, "", names);
        // ".Unique" entries are the built-in enchantment of unique items: the game refuses to apply them (audit of 0.2.5).
        foreach (var name in names)
            if (!name.EndsWith(".Unique") && name.ToLower().Contains(query.ToLower())) result.Add(name);
        return result;
    }

    static UWorld? GameWorld(APawn player)
        => UKismetSystemLibrary.GetOuterObject(UKismetSystemLibrary.GetOuterObject(player)) as UWorld;

    // Lists the enchantments the game allows on the item's slot, whether or not the hero owns the book. Owned books are only
    // a fallback: listing them alone left the catalog empty on a hero without books. Returns the tiers of `wanted`, or -1.
    static int EnchantTiers(APawn player, FInventoryEntry item, string wanted, List<string> names)
    {
        int tiers = -1;
        int bySlot = 0, all = 0, imbue = 0, owned = 0;
        string allSample = "";
        var world = GameWorld(player);
        if (world != null)
        {
            foreach (var entry in USWTypeSystemHelperUtils.GetFilteredEnchantmentsBySlot(world, item.EquippedSlot))
            {
                bySlot++;
                tiers = CollectEnchant(entry.Key.TagName.ToString(), entry.Value.UpgradeTiers, wanted, names, tiers);
            }
            bool useAll = names.Count == 0;
            foreach (var entry in USWTypeSystemHelperUtils.GetAllEnchantments(world))
            {
                all++;
                if (all <= 4) allSample += entry.Key.TagName.ToString() + "(slots=" + entry.Value.RelevantSlots.GameplayTags.Count + ") ";
                if (!USWTypeSystemHelperUtils.CanImbueEnchantment(world, entry.Key, item.EquippedSlot)) continue;
                imbue++;
                if (useAll) tiers = CollectEnchant(entry.Key.TagName.ToString(), entry.Value.UpgradeTiers, wanted, names, tiers);
            }
        }
        bool useOwned = names.Count == 0;
        foreach (var entry in UInventoryHelperLibrary.GetValidOwnedEnchantmentsForItem(player, item.ItemData))
        {
            owned++;
            if (useOwned) tiers = CollectEnchant(entry.Key.TagName.ToString(), entry.Value.UpgradeTiers, wanted, names, tiers);
        }
        Diag($"enchant world={(world != null)} slot={item.EquippedSlot.TagName} item={item.ItemData.TypeTag.TagName} bySlot={bySlot} all={all} imbue={imbue} owned={owned} wanted={wanted} tiers={tiers} allSample={allSample}");
        return tiers;
    }

    static int CollectEnchant(string name, int upgradeTiers, string wanted, List<string> names, int tiers)
    {
        if (!names.Contains(name)) names.Add(name);
        return name == wanted ? upgradeTiers : tiers;
    }

    // GetValidOwnedEnchantmentsForItem lists every valid enchantment, book or not (0.1.6: Shockwave "owned" without a book),
    // so ownership is read from the books actually in the inventory.
    static bool OwnsEnchantment(APawn player, string enchantTag, string levelTag)
    {
        foreach (var book in UInventoryHelperLibrary.GetAllOwnedEnchantmentBooks(player))
        {
            var enchant = UInventoryHelperLibrary.GetEnchantmentTagFromBook(player, book.ItemData.TypeTag).TagName.ToString();
            if (enchant == enchantTag || enchant == levelTag) return true;
        }
        return false;
    }

    static bool IsTag(FGameplayTag tag)
    {
        var name = tag.TagName.ToString();
        return name != "" && name != "None";
    }

    // First regular level (".I"). GetNextLevelTag alone returned the Unique level for 4-tier enchantments in 0.1.5
    // (FireAspect -> FlameBelch.Unique), which the game refuses on regular gear.
    static FGameplayTag FirstEnchantLevel(APawn player, FGameplayTag baseTag)
    {
        var world = GameWorld(player);
        if (world != null)
            foreach (var child in USWTypeSystemHelperUtils.GetEnchantmentChildrenOrdered(world, baseTag))
                if (IsTag(child) && !USWTypeSystemHelperUtils.IsTagLevelUnique(child)) return child;
        var next = USWTypeSystemHelperUtils.GetNextLevelTag(player, baseTag);
        if (IsTag(next) && !USWTypeSystemHelperUtils.IsTagLevelUnique(next)) return next;
        return baseTag;
    }

    // Enchanting consumes a book from the inventory; the menu creates the matching book the same way it creates items.
    static bool GiveEnchantmentBook(APawn player, string enchantTag, string levelTag)
    {
        int books = 0;
        string sample = "";
        foreach (var book in UInventoryHelperLibrary.GetAllPossibleItemsForSlot(player, new FGameplayTag { TagName = EnchantmentBookSlot }))
        {
            books++;
            var enchant = UInventoryHelperLibrary.GetEnchantmentTagFromBook(player, book).TagName.ToString();
            if (books <= 4) sample += book.TagName.ToString() + "->" + enchant + " ";
            if (enchant == enchantTag || enchant == levelTag)
            {
                UActorUtil.SpawnAndAddItem(player, book, new List<FGameplayTag>());
                Diag($"book given {book.TagName} for {enchantTag}");
                return true;
            }
        }
        Diag($"book not found for {enchantTag}; books={books} sample={sample}");
        return false;
    }

    const string EnchantmentBookSlot = "SW.ItemSlot.Inventory.EnchantmentBook";

    static string EnchantBlockers(APawn player, FSWSessionUID uid, FGameplayTag slot, FGameplayTag tag)
    {
        string text = "";
        foreach (var why in UGA_ItemEnchant.CanEnchant(player, uid, slot, tag))
        {
            if (why == EGA_ItemEnchant_FailReason.Cost) text += "faltam pontos de encantamento (aba Recursos); ";
            else if (why == EGA_ItemEnchant_FailReason.HasThisEnchant) text += "o item já tem este encantamento; ";
            else if (why == EGA_ItemEnchant_FailReason.HasOtherEnchant) text += "o item já tem outro encantamento; ";
            else if (why == EGA_ItemEnchant_FailReason.Invalid) text += "combinação inválida ou livro ausente; ";
            else text += "motivo desconhecido; ";
        }
        Diag($"canEnchant slot={slot.TagName} tag={tag.TagName} -> {(text == "" ? "ok" : text)}");
        return text;
    }

    static string UpgradeBlockers(APawn player, FSWSessionUID uid)
    {
        string text = "";
        foreach (var why in UGA_ItemEnchantUpgrade.CanEnchantUpgrade(player, uid))
        {
            if (why == EGA_ItemEnchantUpgrade_FailReason.Cost) text += "faltam pontos de encantamento (aba Recursos); ";
            else if (why == EGA_ItemEnchantUpgrade_FailReason.VendorLevel) text += "o Encantador da vila precisa de nível maior; ";
            else if (why == EGA_ItemEnchantUpgrade_FailReason.MaxLevel) text += "nível máximo atingido; ";
            else if (why == EGA_ItemEnchantUpgrade_FailReason.NotUpgradeable) text += "este encantamento não tem melhorias; ";
            else if (why == EGA_ItemEnchantUpgrade_FailReason.NotEnchanted) text += "o item não está encantado; ";
            else if (why == EGA_ItemEnchantUpgrade_FailReason.Invalid) text += "melhoria inválida; ";
        }
        return text;
    }

    public string Enchant(string itemId, string enchantTag, int tier)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Tier(tier)) return "Nível inválido.";
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; atualize a seleção.";
        var player = World.Player(context!);
        var tag = new FGameplayTag { TagName = enchantTag };
        int maxTier = EnchantTiers(player!, item, enchantTag, new List<string>());
        if (maxTier < 0) return "Encantamento não pertence ao catálogo disponível para este equipamento.";
        if (maxTier < 1) maxTier = 1;
        if (tier > maxTier) return $"O jogo declara {maxTier} níveis. Níveis acima do máximo estão indisponíveis até comprovar o efeito real.";
        var uid = item.ItemData.SessionUID;
        var applied = UInventoryHelperLibrary.GetEnchantmentTagAppliedOnItem(item.ItemData);
        if (applied.TagName != tag.TagName)
        {
            if (UInventoryHelperLibrary.IsItemEnchanted(item.ItemData)) return "O item já tem outro encantamento: use REMOVER ENCANTAMENTO no painel do item antes de trocar.";
            // The game's Enchantsmith passes GetItemSlotTag and the first level tag (GetNextLevelTag); the base tag alone was
            // answered with Invalid in 0.1.4 even with the book owned.
            var slot = UInventoryHelperLibrary.GetItemSlotTag(player, item.ItemData.TypeTag);
            if (!IsTag(slot)) slot = item.EquippedSlot;
            var levelTag = FirstEnchantLevel(player!, tag);
            Diag($"enchant apply base={tag.TagName} level={levelTag.TagName} slot={slot.TagName} equipped={item.EquippedSlot.TagName}");
            if (levelTag.TagName == tag.TagName && maxTier <= 1 && EnchantBlockers(player!, uid, slot, tag) != "")
                return "Este encantamento não tem níveis normais (exclusivo de item único); o jogo não permite aplicá-lo.";
            var useSlot = slot;
            var useTag = levelTag;
            string blockers = EnchantBlockers(player!, uid, useSlot, useTag);
            if (blockers != "" && EnchantBlockers(player!, uid, item.EquippedSlot, levelTag) == "") { useSlot = item.EquippedSlot; blockers = ""; }
            if (blockers != "" && EnchantBlockers(player!, uid, slot, tag) == "") { useTag = tag; blockers = ""; }
            bool gaveBook = false;
            bool owned = OwnsEnchantment(player!, enchantTag, levelTag.TagName.ToString());
            Diag($"enchant book owned={owned}");
            if (blockers != "" && !owned)
            {
                gaveBook = GiveEnchantmentBook(player!, enchantTag, levelTag.TagName.ToString());
                blockers = EnchantBlockers(player!, uid, useSlot, useTag);
            }
            string book = gaveBook ? " Livro criado no inventário." : "";
            if (blockers != "") return "O jogo recusou o encantamento: " + blockers + book;
            bool done = UGA_ItemEnchant.DoEnchant(player, uid, useSlot, useTag);
            Diag($"doEnchant slot={useSlot.TagName} tag={useTag.TagName} -> {done}");
            if (!done) return "O jogo recusou o encantamento sem informar o motivo." + book;
        }
        if (!UInventoryHelperLibrary.GetInventoryEntryFromID(player, uid, out item)) return "A ação foi enviada, mas a releitura do equipamento falhou.";
        var specific = UInventoryHelperLibrary.GetEnchantmentSpecificTagAppliedOnItem(item.ItemData);
        if (!USWTypeSystemHelperUtils.GetTagLevel(specific, out var level)) return "Encantamento enviado; nível não confirmado pelo jogo.";
        if (level > tier) return "O nível atual já é maior que o solicitado; redução indisponível.";
        while (level < tier)
        {
            string upgradeBlockers = UpgradeBlockers(player!, uid);
            if (upgradeBlockers != "") return $"Nível {level} confirmado; melhoria bloqueada: {upgradeBlockers}";
            if (!UGA_ItemEnchantUpgrade.DoEnchantUpgrade(player, uid)) return $"O jogo interrompeu a melhoria no nível {level} sem informar o motivo.";
            if (!UInventoryHelperLibrary.GetInventoryEntryFromID(player, uid, out item)) return "Melhoria enviada; releitura do equipamento indisponível.";
            specific = UInventoryHelperLibrary.GetEnchantmentSpecificTagAppliedOnItem(item.ItemData);
            if (!USWTypeSystemHelperUtils.GetTagLevel(specific, out var actual) || actual <= level) return "A melhoria não confirmou avanço de nível; confira o equipamento antes de repetir.";
            level = actual;
        }
        return $"O jogo confirmou nível {level}. Efeito em combate e persistência ainda precisam de validação.";
    }

    static List<float> NeutralOptions()
    {
        var values = new List<float>();
        foreach (var key in Rules.CombatKeys()) values.Add(Rules.Neutral(key));
        return values;
    }

    /// <summary>The option in force: 1 or 0 for a switch, the factor for a multiplier.</summary>
    public float Option(string key)
    {
        int at = Rules.CombatKeys().IndexOf(key);
        return at >= 0 && at < combatOptions.Count ? combatOptions[at] : Rules.Neutral(key);
    }

    public bool On(string key) => Option(key) != Rules.Neutral(key);

    /// <summary>Applies the combat options (values in Rules.CombatKeys order) and remembers them for the next heroes.</summary>
    public string SetCombat(List<float> values) => SetCombat(values, true);

    string SetCombat(List<float> values, bool remember)
    {
        var keys = Rules.CombatKeys();
        if (values.Count != keys.Count) return "Opções de combate incompletas.";
        bool any = false;
        for (int i = 0; i < keys.Count; i++)
        {
            if (!Rules.CombatValue(keys[i], values[i])) return $"Valor fora do limite em {CombatName(keys[i])}: {Rules.CombatMin(keys[i])} a {Rules.CombatMax(keys[i])}.";
            if (values[i] != Rules.Neutral(keys[i])) any = true;
        }
        if (!any)
        {
            Restore();
            if (remember) ForgetCombat();
            return combatStatus;
        }
        if (!CanWrite(out var reason)) return reason;
        RefreshCombatRecovery();
        if (combatRestoreBlocked) return combatStatus;
        Restore();
        RefreshCombatRecovery();
        if (combatRestoreBlocked) return combatStatus;
        var asc = AbilitySystem();
        var player = World.Player(context!);
        if (asc == null || player == null) return "Personagem indisponível.";
        float god = values[keys.IndexOf("god")];
        float cooldown = values[keys.IndexOf("cooldown")];
        float crit = values[keys.IndexOf("crit")];
        float damage = values[keys.IndexOf("damage")];
        float attack = values[keys.IndexOf("attack")];
        float critDamage = values[keys.IndexOf("critdamage")];
        float speed = values[keys.IndexOf("speed")];
        float jump = values[keys.IndexOf("jump")];
        float xpFactor = values[keys.IndexOf("xp")];
        float loot = values[keys.IndexOf("loot")];
        float rarity = values[keys.IndexOf("rarity")];
        float drops = values[keys.IndexOf("drops")];
        float duplicate = values[keys.IndexOf("duplicate")];
        bool hasXp = false, hasLoot = false;
        bool hasDamage = false, hasMovement = false, hasArtifact = false, hasHealth = false, hasCompanion = false;
        bool hasMelee = false, hasRanged = false, hasJump = false, hasSoul = false;
        foreach (var set in asc.SpawnedAttributes)
        {
            if (set is UATR_Damage) hasDamage = true;
            if (set is UATR_Movement) hasMovement = true;
            if (set is UATR_Artifact) hasArtifact = true;
            if (set is UATR_Health) hasHealth = true;
            if (set is UATR_Companion) hasCompanion = true;
            if (set is UATR_MeleeAttack) hasMelee = true;
            if (set is UATR_RangedAttack) hasRanged = true;
            if (set is UATR_Jump) hasJump = true;
            if (set is UATR_Soul) hasSoul = true;
            if (set is UATR_XP) hasXp = true;
            if (set is UATR_Loot) hasLoot = true;
        }
        if (xpFactor != 1 && !hasXp) return "Atributo de experiência indisponível; configuração não aplicada.";
        if ((loot != 1 || rarity != 0 || drops != 0 || duplicate != 0) && !hasLoot) return "Atributos de saque indisponíveis; configuração não aplicada.";
        if ((damage != 1 || crit != 0 || critDamage != 1) && !hasDamage) return "Atributo de dano indisponível; configuração não aplicada.";
        if ((speed != 1 || values[keys.IndexOf("rolls")] != 0) && !hasMovement) return "Atributo de movimento indisponível; configuração não aplicada.";
        if (god != 0 && !hasHealth) return "Atributo de vida indisponível; invencibilidade não aplicada.";
        if (cooldown != 0 && (!hasMovement || !hasArtifact || !hasHealth || !hasCompanion))
            return "O personagem não expôs todos os atributos de cooldown; configuração não aplicada.";
        if (attack != 1 && !hasMelee && !hasRanged) return "Atributos de ataque indisponíveis; configuração não aplicada.";
        if (values[keys.IndexOf("ammo")] != 0 && !hasRanged) return "Atributo de flechas indisponível; configuração não aplicada.";
        if (values[keys.IndexOf("souls")] != 0 && !hasSoul) return "Atributo de almas indisponível; configuração não aplicada.";
        if (jump != 1 && !hasJump) return "Atributo de pulo indisponível; configuração não aplicada.";
        combatPlayer = player;
        combatAsc = asc;
        combatActive = true;
        savedCanBeDamaged = player.bCanBeDamaged;
        changedCanBeDamaged = god != 0;
        if (god != 0) player.bCanBeDamaged = false;
        bool confirmed = god == 0 || !player.bCanBeDamaged;
        foreach (var set in asc.SpawnedAttributes)
        {
            if (set is UATR_Damage dmg)
            {
                if (damage != 1 && !Change(Unreal.ClassOf<UATR_Damage>(), "BaseDamageMultiplier", dmg.BaseDamageMultiplier.BaseValue, dmg.BaseDamageMultiplier.BaseValue * damage)) confirmed = false;
                // A chance far above 1 makes every hit critical whether the game counts it as a fraction or a percentage.
                if (crit != 0 && !Change(Unreal.ClassOf<UATR_Damage>(), "CriticalHitChance", dmg.CriticalHitChance.BaseValue, 100)) confirmed = false;
                if (critDamage != 1 && !Change(Unreal.ClassOf<UATR_Damage>(), "CriticalHitMultiplier", dmg.CriticalHitMultiplier.BaseValue, dmg.CriticalHitMultiplier.BaseValue * critDamage)) confirmed = false;
            }
            if (set is UATR_Movement move)
            {
                if (speed != 1 && !Change(Unreal.ClassOf<UATR_Movement>(), "MovementSpeedMultiplier", move.MovementSpeedMultiplier.BaseValue, move.MovementSpeedMultiplier.BaseValue * speed)) confirmed = false;
                if (cooldown != 0 && !Change(Unreal.ClassOf<UATR_Movement>(), "RollCooldownModifier", move.RollCooldownModifier.BaseValue, 0)) confirmed = false;
            }
            if (set is UATR_MeleeAttack melee && attack != 1 && !Change(Unreal.ClassOf<UATR_MeleeAttack>(), "MeleeAttackSpeed", melee.MeleeAttackSpeed.BaseValue, melee.MeleeAttackSpeed.BaseValue * attack)) confirmed = false;
            if (set is UATR_RangedAttack ranged && attack != 1 && !Change(Unreal.ClassOf<UATR_RangedAttack>(), "RangedAttackSpeed", ranged.RangedAttackSpeed.BaseValue, ranged.RangedAttackSpeed.BaseValue * attack)) confirmed = false;
            if (set is UATR_Jump jumping && jump != 1 && !Change(Unreal.ClassOf<UATR_Jump>(), "JumpVerticalMultiplier", jumping.JumpVerticalMultiplier.BaseValue, jumping.JumpVerticalMultiplier.BaseValue * jump)) confirmed = false;
            if (set is UATR_Artifact artifact && cooldown != 0 && !Change(Unreal.ClassOf<UATR_Artifact>(), "ArtifactCooldownMultiplier", artifact.ArtifactCooldownMultiplier.BaseValue, 0)) confirmed = false;
            if (set is UATR_Health health)
            {
                // Game damage goes through GAS and ignores bCanBeDamaged; the minimum clamp keeps health from reaching zero.
                if (god != 0 && !Change(Unreal.ClassOf<UATR_Health>(), "HealthMin", health.HealthMin.BaseValue, health.HealthMax.CurrentValue)) confirmed = false;
                if (cooldown != 0 && !Change(Unreal.ClassOf<UATR_Health>(), "HealthPotionCooldownMultiplier", health.HealthPotionCooldownMultiplier.BaseValue, 0)) confirmed = false;
            }
            if (set is UATR_XP xpSet && xpFactor != 1 && !Change(Unreal.ClassOf<UATR_XP>(), "XPYieldMultiplier", xpSet.XPYieldMultiplier.BaseValue, Scaled(xpSet.XPYieldMultiplier.BaseValue, xpFactor))) confirmed = false;
            if (set is UATR_Loot lootSet)
            {
                if (loot != 1 && !Change(Unreal.ClassOf<UATR_Loot>(), "LootingMultiplier", lootSet.LootingMultiplier.BaseValue, Scaled(lootSet.LootingMultiplier.BaseValue, loot))) confirmed = false;
                // Chances are set far above 1, as with the critical hit: certain whether the game reads fractions or percentages.
                if (rarity != 0 && !Change(Unreal.ClassOf<UATR_Loot>(), "RarityBonusChance", lootSet.RarityBonusChance.BaseValue, 100)) confirmed = false;
                if (drops != 0 && !Change(Unreal.ClassOf<UATR_Loot>(), "DropChanceIncrease", lootSet.DropChanceIncrease.BaseValue, 100)) confirmed = false;
                if (duplicate != 0 && !Change(Unreal.ClassOf<UATR_Loot>(), "DropDuplicationChance", lootSet.DropDuplicationChance.BaseValue, 100)) confirmed = false;
            }
            if (set is UATR_Companion companion && cooldown != 0 && !Change(Unreal.ClassOf<UATR_Companion>(), "CompanionCooldownMultiplier", companion.CompanionCooldownMultiplier.BaseValue, 0)) confirmed = false;
        }
        if (!confirmed)
        {
            Restore();
            return "O jogo não confirmou todos os atributos; reversão solicitada. " + combatStatus;
        }
        combatOptions = new List<float>();
        combatOptions.AddRange(values);
        if (remember) RememberCombat();
        combatStatus = "Atributos temporários conferidos; efeito real em combate em validação.";
        return combatStatus + " Cooldowns já ativos podem permanecer.";
    }

    // ---------------------------------------------------------------- Hero: level and village vendors

    /// <summary>The hero's level, maximum level, XP and XP needed for the next level; level -1 outside a match.</summary>
    public void HeroLevelInfo(out int level, out int maximum, out float xp, out float next)
    {
        level = -1;
        maximum = -1;
        xp = 0;
        next = 0;
        var asc = AbilitySystem();
        if (asc == null) return;
        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_XP hero)
            {
                level = (int)hero.Level.CurrentValue;
                maximum = (int)hero.LevelMax.CurrentValue;
                xp = hero.XP.CurrentValue;
                next = hero.XPForNextLevel.CurrentValue;
            }
    }

    /// <summary>
    /// Raises the hero to a level: first the game's own way (XP up to each level's requirement, so its level-up runs),
    /// and if the game doesn't level up on the spot, the level itself is written (experimental).
    /// </summary>
    public string SetHeroLevel(int target)
    {
        if (!CanWrite(out var reason)) return reason;
        var asc = AbilitySystem();
        if (asc == null) return "Sistema de atributos indisponível.";
        HeroLevelInfo(out var level, out var maximum, out var xp, out var next);
        if (level < 0) return "Atributos de nível indisponíveis.";
        if (!Rules.HeroLevel(target, maximum)) return $"Nível permitido: 1 a {maximum}.";
        if (target < level) return $"O herói já está no nível {level}; baixar o nível não é suportado.";
        if (target == level) return $"O herói já está no nível {level}.";
        int start = level;
        float startXp = xp;
        for (int guard = 0; level < target && guard < 200; guard++)
        {
            SetBase(asc, Unreal.ClassOf<UATR_XP>(), "XP", next > xp ? next : xp + 1);
            HeroLevelInfo(out var now, out maximum, out xp, out next);
            if (now <= level) break;
            level = now;
        }
        bool byXp = level > start;
        bool direct = false;
        if (level < target)
        {
            // XP alone didn't level up right away: put the XP back (a late level-up would add one more) and write the level.
            if (!byXp) SetBase(asc, Unreal.ClassOf<UATR_XP>(), "XP", startXp);
            SetBase(asc, Unreal.ClassOf<UATR_XP>(), "Level", target);
            direct = true;
            HeroLevelInfo(out level, out maximum, out xp, out next);
        }
        Diag($"hero level {start}->{level} target={target} max={maximum} byXp={byXp} direct={direct} xp={xp} next={next}");
        if (level != target) return $"O jogo manteve o nível {level}; pedido {target}.";
        return direct ? $"Nível {level} gravado direto (experimental: confira depois de salvar e recarregar)."
            : $"O jogo subiu o herói para o nível {level} pela experiência.";
    }

    static string VendorAttribute(string vendor) =>
        vendor == "enchantsmith" ? "EnchantsmithUpgradeLevel" : vendor == "blacksmith" ? "OldBlacksmithUpgradeLevel" : "VillageMerchantUpgradeLevel";

    public static string VendorName(string vendor) =>
        vendor == "enchantsmith" ? "Encantador" : vendor == "blacksmith" ? "Ferreiro" : "Mercador";

    /// <summary>A village vendor's level and maximum ("enchantsmith", "blacksmith", "merchant"); -1 when unknown.</summary>
    public void VendorLevelInfo(string vendor, out int level, out int maximum)
    {
        level = -1;
        maximum = -1;
        var asc = AbilitySystem();
        if (asc == null) return;
        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_MerchantInfo info)
            {
                if (vendor == "enchantsmith") { level = (int)info.EnchantsmithUpgradeLevel.CurrentValue; maximum = (int)info.EnchantsmithUpgradeLevelMax.CurrentValue; }
                else if (vendor == "blacksmith") { level = (int)info.OldBlacksmithUpgradeLevel.CurrentValue; maximum = (int)info.OldBlacksmithUpgradeLevelMax.CurrentValue; }
                else { level = (int)info.VillageMerchantUpgradeLevel.CurrentValue; maximum = (int)info.VillageMerchantUpgradeLevelMax.CurrentValue; }
            }
    }

    public string SetVendorLevel(string vendor, int target)
    {
        if (!CanWrite(out var reason)) return reason;
        var asc = AbilitySystem();
        VendorLevelInfo(vendor, out var level, out var maximum);
        if (asc == null || level < 0) return "Os níveis da vila não estão no herói (atributo ausente).";
        if (!Rules.VendorLevel(target, maximum)) return $"Nível do {VendorName(vendor)}: 0 a {maximum}.";
        SetBase(asc, Unreal.ClassOf<UATR_MerchantInfo>(), VendorAttribute(vendor), target);
        VendorLevelInfo(vendor, out var now, out maximum);
        Diag($"vendor {vendor} {level}->{now} target={target} max={maximum}");
        return now == target ? $"{VendorName(vendor)} agora no nível {now} (antes {level}). Persistência ainda em validação."
            : $"O jogo manteve o {VendorName(vendor)} no nível {now}; pedido {target}.";
    }

    /// <summary>Refills the Village Merchant's stock refreshes.</summary>
    public string RefillMerchantRefreshes()
    {
        if (!CanWrite(out var reason)) return reason;
        var asc = AbilitySystem();
        if (asc == null) return "Sistema de atributos indisponível.";
        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_MerchantInfo info)
            {
                float before = info.VillageMerchantRefreshCharges.CurrentValue;
                float max = info.VillageMerchantRefreshChargesMax.CurrentValue;
                SetBase(asc, Unreal.ClassOf<UATR_MerchantInfo>(), "VillageMerchantRefreshCharges", max);
                float after = info.VillageMerchantRefreshCharges.CurrentValue;
                Diag($"merchant refreshes {before}->{after} max={max}");
                return after == max ? $"Trocas do Mercador: {after} de {max}." : $"O jogo manteve {after} trocas (máximo {max}).";
            }
        return "Os dados do Mercador não estão no herói (atributo ausente).";
    }

    // A factor on an attribute. Some "multipliers" are bonuses added to 1 and start at 0 (LootingMultiplier read 0 in
    // 0.3.1, so ×7 left it at 0): those get the factor less one added instead.
    static float Scaled(float before, float factor) => before != 0 ? before * factor : factor - 1;

    // ---------------------------------------------------------------- World: time of day, game speed, soul storm, mobs, places
    // Only public game functions: the private OnRep_ItemData stopped the whole adapter in 0.3.1.

    UWorldTimeOfDayHandlerSubsystem? TimeOfDay() => context == null ? null
        : USubsystemBlueprintLibrary.GetWorldSubsystem(context, Unreal.ClassOf<UWorldTimeOfDayHandlerSubsystem>()) as UWorldTimeOfDayHandlerSubsystem;

    /// <summary>The hour of the day and whether it is night, or "" where the level has no day cycle.</summary>
    public string TimeText()
    {
        var time = TimeOfDay();
        if (time == null) return "";
        return $"{Short(time.GetCurrentTime())} h de {Short(time.GetFullDayHours())} • {(time.IsNight() ? "noite" : "dia")}";
    }

    /// <summary>The length of the game's day in its hours, or 0 where the level has no day cycle.</summary>
    public float DayHours()
    {
        var time = TimeOfDay();
        return time != null ? time.GetFullDayHours() : 0;
    }

    /// <summary>
    /// A moment of the game's own day: 0 dawn, 1 midday, 2 dusk, 3 midnight. Its day doesn't follow a 24-hour clock: 0.3.3
    /// read day at 0, 4.4 and 6 and night at 12 and 18, so the day is the first half and the night the second. (Asking the
    /// game for its phase start times in 0.3.4 stopped the button without a trace.)
    /// </summary>
    public string SetDayMoment(int moment)
    {
        var time = TimeOfDay();
        if (time == null) return "Esta fase não tem ciclo de dia e noite.";
        float day = time.GetFullDayHours();
        return SetTimeOfDay(day * (moment == 0 ? 0 : moment == 1 ? 0.25f : moment == 2 ? 0.5f : 0.75f));
    }

    public string SetTimeOfDay(float hour)
    {
        if (!CanWrite(out var reason)) return reason;
        var time = TimeOfDay();
        if (time == null) return "Esta fase não tem ciclo de dia e noite.";
        float day = time.GetFullDayHours();
        if (!Rules.Hour(hour, day)) return $"Hora permitida: 0 a {Short(day)}.";
        float before = time.GetCurrentTime();
        time.SetTime(hour, 0);
        float after = time.GetCurrentTime();
        Diag($"time of day {before}->{after} requested={hour} day={day} night={time.IsNight()}");
        return $"Hora pedida: {Short(hour)} h (antes {Short(before)} h, agora {Short(after)} h). A luz pode levar um instante para mudar.";
    }

    public float GameSpeed => gameSpeed;

    /// <summary>The whole game's speed (0.25 to 2); 1 is normal. A new level starts at normal speed.</summary>
    public string SetGameSpeed(float factor)
    {
        if (context == null) return "Contexto do jogo indisponível.";
        if (!Rules.GameSpeed(factor)) return "Velocidade do jogo: 0,25 a 2.";
        if (factor != 1 && !CanWrite(out var reason)) return reason;
        gameSpeed = factor;
        slowLevel = World.LevelName(context);
        UGameplayStatics.SetGlobalTimeDilation(context, factor);
        Diag($"time dilation {factor} read={UGameplayStatics.GetGlobalTimeDilation(context)}");
        return factor == 1 ? "Velocidade normal do jogo." : $"Velocidade do jogo: ×{Short(factor)}.";
    }

    float gameSpeed;

    // The area the hero stands in (soul storms are per area).
    FGameplayTag CurrentArea()
    {
        var player = context != null ? World.Player(context) : null;
        var movement = player != null ? player.GetComponentByClass(Unreal.ClassOf<USWMovementComponent>()) as USWMovementComponent : null;
        return movement != null ? movement.GetCurrentArea() : new FGameplayTag();
    }

    public string StartSoulStorm()
    {
        if (!CanWrite(out var reason)) return reason;
        var storms = USubsystemBlueprintLibrary.GetWorldSubsystem(context!, Unreal.ClassOf<USWSoulStormScheduleSubsystem>()) as USWSoulStormScheduleSubsystem;
        if (storms == null) return "Esta fase não tem Tempestade de Almas.";
        var area = CurrentArea();
        if (IsTag(area)) storms.StartSoulStormAndTimer(area, SoulStormSeconds);
        else storms.StartSoulStorm();
        Diag($"soul storm start area={area.TagName}");
        return "Tempestade de Almas pedida" + (IsTag(area) ? $" na área {area.TagName}" : "") + ". Experimental: o jogo pode recusar fora das áreas que têm tempestade.";
    }

    const float SoulStormSeconds = 300;

    public string EndSoulStorm()
    {
        if (!CanWrite(out var reason)) return reason;
        var storms = USubsystemBlueprintLibrary.GetWorldSubsystem(context!, Unreal.ClassOf<USWSoulStormScheduleSubsystem>()) as USWSoulStormScheduleSubsystem;
        var area = CurrentArea();
        if (storms == null || !IsTag(area)) return "Área atual desconhecida; não há tempestade para encerrar.";
        storms.EndSoulStorm(area);
        Diag($"soul storm end area={area.TagName}");
        return $"Fim da Tempestade de Almas pedido na área {area.TagName}.";
    }

    // Hostile mobs around the hero: the game's own hostility check, so pets, summons and villagers are left alone.
    List<AActor> HostileMobsNear(float radius)
    {
        var result = new List<AActor>();
        var player = context != null ? World.Player(context) : null;
        var hero = player as ABaseCharacter;
        if (player == null || hero == null) return result;
        foreach (var actor in World.FindAll(context!, Unreal.ClassOf<AMobCharacter>()))
        {
            var mob = actor as ABaseCharacter;
            if (mob == null || !UKismetSystemLibrary.IsValid(mob) || actor.GetDistanceTo(player) > radius) continue;
            if (!mob.IsHostileTowards(hero)) continue;
            result.Add(actor);
        }
        return result;
    }

    /// <summary>Kills the hostile mobs within the radius the game's way (they die and drop their loot).</summary>
    public string KillNearbyMobs(float radius)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Radius(radius)) return "Raio: 5 a 50 metros.";
        var mobs = HostileMobsNear(radius * 100);
        foreach (var actor in mobs) (actor as ABaseCharacter)?.Kill();
        Diag($"kill nearby radius={radius} mobs={mobs.Count}");
        return mobs.Count == 0 ? "Nenhum inimigo nesse raio." : $"{mobs.Count} inimigo(s) abatido(s) num raio de {Short(radius)} m.";
    }

    List<AActor> frozenMobs;

    /// <summary>Stops the hostile mobs within the radius in place (their own clock at 0), or lets the frozen ones go.</summary>
    public string ToggleFreeze(float radius)
    {
        if (frozenMobs == null) frozenMobs = new List<AActor>();
        if (frozenMobs.Count > 0)
        {
            int released = ReleaseFrozen();
            return $"{released} inimigo(s) liberado(s).";
        }
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Radius(radius)) return "Raio: 5 a 50 metros.";
        foreach (var actor in HostileMobsNear(radius * 100))
        {
            actor.CustomTimeDilation = 0;
            frozenMobs.Add(actor);
        }
        Diag($"freeze radius={radius} mobs={frozenMobs.Count}");
        return frozenMobs.Count == 0 ? "Nenhum inimigo nesse raio." : $"{frozenMobs.Count} inimigo(s) congelado(s). Clique de novo para liberar.";
    }

    public bool HasFrozen => frozenMobs != null && frozenMobs.Count > 0;

    int ReleaseFrozen()
    {
        int released = 0;
        if (frozenMobs == null) return 0;
        foreach (var actor in frozenMobs)
            if (UKismetSystemLibrary.IsValid(actor)) { actor.CustomTimeDilation = 1; released++; }
        frozenMobs.Clear();
        return released;
    }

    // Up to three saved places, each with the level it belongs to (a place is only valid in its own level).
    List<FVector> places;
    List<string> placeLevels;

    void EnsurePlaces()
    {
        // A list field starts empty, not null (0.3.2 saved into an empty list: "place 1 saved 0,0,0").
        if (places != null && places.Count == PlaceCount) return;
        places = new List<FVector>();
        placeLevels = new List<string>();
        for (int i = 0; i < PlaceCount; i++) { places.Add(new FVector()); placeLevels.Add(""); }
    }

    public const int PlaceCount = 3;

    /// <summary>Whether the place is saved, in this level.</summary>
    public bool HasPlace(int index)
    {
        EnsurePlaces();
        return context != null && index >= 0 && index < PlaceCount && placeLevels[index] != "" && placeLevels[index] == World.LevelName(context);
    }

    public string SavePlace(int index)
    {
        EnsurePlaces();
        var player = context != null ? World.Player(context) : null;
        if (player == null || index < 0 || index >= PlaceCount) return "Entre em uma partida para salvar a posição.";
        places[index] = player.K2_GetActorLocation();
        placeLevels[index] = World.LevelName(context!);
        Diag($"place {index + 1} saved {places[index].X:0},{places[index].Y:0},{places[index].Z:0} level={placeLevels[index]}");
        return $"Posição {index + 1} salva aqui.";
    }

    public string GoToPlace(int index)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!HasPlace(index)) return $"A posição {index + 1} está vazia ou é de outra fase.";
        var player = World.Player(context!)!;
        bool moved = player.K2_TeleportTo(places[index], player.K2_GetActorRotation());
        Diag($"place {index + 1} teleport moved={moved}");
        return moved ? $"Herói levado à posição {index + 1}." : "O jogo recusou o teleporte (lugar bloqueado?).";
    }

    /// <summary>The minecart stations the hero has unlocked, by tag (informative).</summary>
    public List<string> UnlockedStations()
    {
        var result = new List<string>();
        if (context == null) return result;
        var stations = USubsystemBlueprintLibrary.GetWorldSubsystem(context, Unreal.ClassOf<UMinecartStationSubsystem>()) as UMinecartStationSubsystem;
        if (stations == null || !stations.GetListOfUnlockedStations(out var unlocked)) return result;
        foreach (var tag in unlocked.GameplayTags) result.Add(tag.TagName.ToString());
        return result;
    }

    // ---------------------------------------------------------------- Remembered options, hotkeys and game speed

    void RememberCombat()
    {
        string text = "";
        foreach (var value in combatOptions) text += (text == "" ? "" : "|") + UKismetStringLibrary.Conv_DoubleToString(value);
        rememberedCombat = text;
        Settings.Set(CombatSetting, text);
    }

    /// <summary>Stops reapplying the options on new heroes (the player switched everything off or pressed Restaurar).</summary>
    public void ForgetCombat()
    {
        rememberedCombat = "";
        reapplyAt = 0;
        Settings.Remove(CombatSetting);
    }

    public bool Remembers => rememberedCombat != "";

    static List<float> ParseOptions(string text)
    {
        var values = new List<float>();
        var rest = text;
        while (rest != "")
        {
            int at = rest.IndexOf("|");
            var part = at >= 0 ? rest.Substring(0, at) : rest;
            rest = at >= 0 ? rest.Substring(at + 1) : "";
            values.Add((float)UKismetStringLibrary.Conv_StringToDouble(part));
        }
        // Choices saved by an older version have fewer options: the new ones start off.
        var keys = Rules.CombatKeys();
        while (values.Count < keys.Count) values.Add(Rules.Neutral(keys[values.Count]));
        return values;
    }

    // A new hero (a level change or a reload) gets the remembered options a moment after it appears: its attribute
    // sets come in after the pawn.
    void ReapplyRemembered()
    {
        if (rememberedCombat == "" || context == null) return;
        var player = World.Player(context);
        if (player == null) { rememberPlayer = null; return; }
        var now = World.RealTime(context);
        if (player != rememberPlayer)
        {
            rememberPlayer = player;
            reapplyAt = combatActive ? 0 : now + ReapplyDelay;
            reapplyGiveUp = now + ReapplyWindow;
            return;
        }
        if (reapplyAt <= 0 || now < reapplyAt || combatActive) return;
        // The hero may still be loading (no attribute sets, health not set): try again every few seconds for a while,
        // and log why it waited (one try only in 0.3.0 left no trace when it failed).
        string reason = combatStatus;
        if (combatRestoreBlocked || !CanWrite(out reason))
        {
            reapplyAt = now < reapplyGiveUp ? now + ReapplyRetry : 0;
            if (reapplyAt == 0) Diag($"combat remembered not applied: {reason}");
            return;
        }
        var result = SetCombat(ParseOptions(rememberedCombat), false);
        reapplyAt = !combatActive && now < reapplyGiveUp ? now + ReapplyRetry : 0;
        Diag($"combat remembered {rememberedCombat} -> {result}");
        if (!combatActive) return;
        combatStatus = "Combate lembrado reaplicado.";
        GameUI.Toast(context, "Menu Solo: combate lembrado reaplicado (" + ActiveNames() + ").", 3);
    }

    const double ReapplyDelay = 3;
    const double ReapplyRetry = 2;
    const double ReapplyWindow = 300;
    double reapplyGiveUp;

    /// <summary>The options in force, by name, for the hotkey toasts and the menu.</summary>
    public string ActiveNames()
    {
        string text = "";
        foreach (var key in Rules.CombatKeys())
            if (On(key)) text += (text == "" ? "" : ", ") + CombatName(key) + (Rules.IsSwitch(key) ? "" : " ×" + Short(Option(key)));
        return text == "" ? "nada ligado" : text;
    }

    // 2 -> "2", 1.5 -> "1.5", 0.25 -> "0.25".
    static string Short(float value)
    {
        int hundredths = (int)(value * 100 + 0.5f);
        if (hundredths % 100 == 0) return $"{hundredths / 100}";
        if (hundredths % 10 == 0) return $"{hundredths / 100},{(hundredths % 100) / 10}";
        int cents = hundredths % 100;
        return $"{hundredths / 100}," + (cents < 10 ? "0" : "") + $"{cents}";
    }

    public static string CombatName(string key) =>
        key == "god" ? "Invencibilidade" : key == "cooldown" ? "Sem recarga" : key == "ammo" ? "Flechas infinitas"
        : key == "souls" ? "Almas infinitas" : key == "rolls" ? "Rolagens infinitas" : key == "crit" ? "Crítico sempre"
        : key == "damage" ? "Dano" : key == "attack" ? "Velocidade de ataque" : key == "critdamage" ? "Dano crítico"
        : key == "speed" ? "Velocidade" : key == "jump" ? "Pulo" : key == "xp" ? "Experiência" : key == "loot" ? "Saque"
        : key == "rarity" ? "Raridade máxima" : key == "drops" ? "Drop garantido" : key == "duplicate" ? "Saque duplicado" : key;

    /// <summary>A hotkey: switches one option on or off, keeping the others as they are.</summary>
    public string QuickToggle(string key)
    {
        int at = Rules.CombatKeys().IndexOf(key);
        if (at < 0 || !Rules.IsSwitch(key)) return "Atalho desconhecido.";
        var values = new List<float>();
        values.AddRange(combatOptions);
        values[at] = values[at] != 0 ? 0 : 1;
        var result = SetCombat(values);
        // "Invencibilidade" is feminine; the other switches read fine with "ligado".
        var state = values[at] != 0 ? "ligad" : "desligad";
        return Option(key) == values[at] ? $"{CombatName(key)}: {state}{(key == "god" ? "a" : "o")}." : result;
    }

    /// <summary>A hotkey: every combat option off (and forgotten), and the game back at normal speed.</summary>
    public string StopAll()
    {
        Restore();
        ForgetCombat();
        if (gameSpeed != 1) SetGameSpeed(1);
        ReleaseFrozen();
        return combatRestoreBlocked ? combatStatus : "Tudo desligado: combate restaurado, inimigos liberados e velocidade normal.";
    }

    public bool SlowMotion => gameSpeed != 1;

    /// <summary>The hotkey: half-speed game time, or back to normal.</summary>
    public string ToggleSlowMotion()
    {
        var result = SetGameSpeed(gameSpeed != 1 ? 1 : SlowMotionFactor);
        return gameSpeed == SlowMotionFactor ? "Câmera lenta ligada (metade da velocidade)." : result;
    }

    const float SlowMotionFactor = 0.5f;
    string slowLevel;

    bool Change(TSubclassOf<UAttributeSet> type, string name, float before, float value)
    {
        var attribute = USWCoreGASUtil.GetAttributeChecked(type, name);
        savedAttributes.Add(attribute);
        savedValues.Add(before);
        USWCoreGASUtil.SetNumericAttributeBase(combatAsc!, attribute, value);
        var actual = UAbilitySystemBlueprintLibrary.GetFloatAttributeBaseFromAbilitySystemComponent(combatAsc!, attribute, out var found);
        appliedValues.Add(found ? actual : value);
        Diag($"tweak {name} {before}->{actual} requested={value} found={found}");
        return found && actual == value;
    }

    public void Tick()
    {
        RefreshCombatRecovery();
        // The hero died, changed or the level is going: the options are let go and come back with the hero (a death keeps
        // the same pawn, so it is marked as new for ReapplyRemembered).
        if (combatActive && (context == null || World.Player(context) != combatPlayer || !CanWrite(out var reason)))
        {
            Diag(context == null ? "combat let go: no level" : World.Player(context) != combatPlayer ? "combat let go: other hero" : "combat let go: hero not writable (dead or loading)");
            Restore();
            LetGameValuesStand();
            rememberPlayer = null;
        }
        if (combatActive) Refill();
        RaiseLootRarity();
        CheckCombatDrift();
        ReapplyRemembered();
        // A new level starts at the normal speed.
        if (gameSpeed != 1 && (context == null || World.LevelName(context) != slowLevel)) gameSpeed = 1;
        if (pendingMobs > 0 && context != null) TrackRequestedMobs();
        if (context != null && World.RealTime(context) < watchUntil) WatchSpawnedMobs();
        if (spawnPlayer != null && (context == null || World.Player(context) != spawnPlayer
            || World.LevelName(context) != spawnLevel || !CanWrite(out var spawnReason)))
        {
            rejectPendingMobs = true;
            DestroyTrackedMobs();
            spawnPlayer = null;
            spawnStatus = "Criações canceladas após mudança da sessão.";
        }
    }

    // Follows freshly created mobs for a few seconds: in 0.1.8 a directly spawned zombie was reported but never seen.
    void WatchSpawnedMobs()
    {
        var now = World.RealTime(context!);
        if (now < nextWatch) return;
        nextWatch = now + 2;
        var player = World.Player(context!);
        foreach (var actor in spawnedMobs)
        {
            bool valid = UKismetSystemLibrary.IsValid(actor);
            if (!valid) { Diag("mob watch: destroyed"); continue; }
            var at = actor.K2_GetActorLocation();
            float distance = player != null ? actor.GetDistanceTo(player) : -1;
            Diag($"mob watch {UKismetSystemLibrary.GetDisplayName(actor)} at {at.X:0},{at.Y:0},{at.Z:0} dist={distance:0} hidden={actor.bHidden} type={USWTypeComponent.GetTypeTagFromActor(actor, true).TagName}");
        }
    }

    FVector SpawnPoint(FVector origin)
    {
        if (!UNavigationSystemV1.K2_GetRandomReachablePointInRadius(context!, origin, out var point, 600, null!, null!))
        {
            double angle = UKismetMathLibrary.RandomFloatInRange(0, 360);
            point = new FVector { X = origin.X + UKismetMathLibrary.DegCos(angle) * 300, Y = origin.Y + UKismetMathLibrary.DegSin(angle) * 300, Z = origin.Z };
        }
        point.Z += SpawnLift;
        return point;
    }

    const double SpawnWaitSeconds = 8;
    const double SpawnLift = 100;

    void TrackRequestedMobs()
    {
        var now = World.RealTime(context!);
        if (now < nextSpawnScan) return;
        nextSpawnScan = now + 0.5;
        foreach (var actor in World.FindAll(context!, Unreal.ClassOf<APawn>()))
        {
            if (pendingMobs <= 0) break;
            if (knownMobActors.Contains(actor) || spawnedMobs.Contains(actor)) continue;
            var type = USWTypeComponent.GetTypeTagFromActor(actor, true).TagName.ToString();
            // A fresh actor may not have its type yet; check it again on the next scan.
            if (type == "" || type == "None") continue;
            knownMobActors.Add(actor);
            if (type != spawnRequestTag)
            {
                Diag($"new pawn {UKismetSystemLibrary.GetDisplayName(actor)} type={type} (requested {spawnRequestTag})");
                continue;
            }
            spawnedMobs.Add(actor);
            pendingMobs--;
            Diag($"mob found {UKismetSystemLibrary.GetDisplayName(actor)} for {spawnRequestTag}");
        }
        spawnStatus = $"Mobs confirmados: {spawnedMobs.Count}; pendentes: {pendingMobs}";
        if (pendingMobs > 0 && now >= spawnDeadline)
        {
            Diag($"mob spawn timeout {spawnRequestTag} pending={pendingMobs}");
            spawnStatus = $"O jogo não criou {pendingMobs} mob(s) de {spawnRequestTag} em {SpawnWaitSeconds} s; confirmados: {spawnedMobs.Count}.";
            pendingMobs = 0;
        }
    }

    // Every tick while the options are on: health with invincibility; with no cooldown the potion charges, as the Bottomless
    // Brew's extra charges come back on their own timer that the cooldown multiplier doesn't shorten (reported on 0.2.8);
    // and the arrows, souls and rolls of their switches.
    void Refill()
    {
        if (combatAsc == null || !UKismetSystemLibrary.IsValid(combatAsc)) return;
        foreach (var set in combatAsc.SpawnedAttributes)
        {
            if (set is UATR_Health health)
            {
                if (On("god") && health.Health.CurrentValue < health.HealthMax.CurrentValue)
                    SetBase(combatAsc, Unreal.ClassOf<UATR_Health>(), "Health", health.HealthMax.CurrentValue);
                if (On("cooldown") && health.HealthPotionCharges.CurrentValue < health.HealthPotionChargesMax.CurrentValue)
                {
                    if (!loggedPotionRefill)
                    {
                        loggedPotionRefill = true;
                        Diag($"potion refill charges={health.HealthPotionCharges.CurrentValue} max={health.HealthPotionChargesMax.CurrentValue} baseCooldown={health.HealthPotionBaseCooldown.CurrentValue} multiplier={health.HealthPotionCooldownMultiplier.CurrentValue}");
                    }
                    SetBase(combatAsc, Unreal.ClassOf<UATR_Health>(), "HealthPotionCharges", health.HealthPotionChargesMax.CurrentValue);
                }
            }
            if (set is UATR_RangedAttack ranged && On("ammo") && ranged.RangedAttackAmmoCount.CurrentValue < ranged.RangedAttackAmmoCountMax.CurrentValue)
            {
                if (!loggedAmmoRefill) { loggedAmmoRefill = true; Diag($"ammo refill {ranged.RangedAttackAmmoCount.CurrentValue}/{ranged.RangedAttackAmmoCountMax.CurrentValue}"); }
                SetBase(combatAsc, Unreal.ClassOf<UATR_RangedAttack>(), "RangedAttackAmmoCount", ranged.RangedAttackAmmoCountMax.CurrentValue);
            }
            if (set is UATR_Soul soul && On("souls") && soul.Souls.CurrentValue < soul.SoulsMax.CurrentValue)
            {
                if (!loggedSoulRefill) { loggedSoulRefill = true; Diag($"souls refill {soul.Souls.CurrentValue}/{soul.SoulsMax.CurrentValue}"); }
                SetBase(combatAsc, Unreal.ClassOf<UATR_Soul>(), "Souls", soul.SoulsMax.CurrentValue);
            }
            if (set is UATR_Movement move && On("rolls") && move.RollCharges.CurrentValue < move.RollChargesMax.CurrentValue)
            {
                if (!loggedRollRefill) { loggedRollRefill = true; Diag($"rolls refill {move.RollCharges.CurrentValue}/{move.RollChargesMax.CurrentValue}"); }
                SetBase(combatAsc, Unreal.ClassOf<UATR_Movement>(), "RollCharges", move.RollChargesMax.CurrentValue);
            }
        }
    }

    bool loggedAmmoRefill;
    bool loggedSoulRefill;
    bool loggedRollRefill;

    // "Raridade máxima": RarityBonusChance at 100 didn't change the rarity of drops (reported on 0.3.1), so while the switch
    // is on, gear that enters the inventory is raised to Special, the top rarity of regular gear, the same way created items
    // get theirs (RarityTag, which the game's tooltips and saves read). Unique and Special items are left alone.
    List<ulong> lootKnown;
    bool lootWatching;
    double nextLootScan;
    int raisedLoot;

    void RaiseLootRarity()
    {
        if (!combatActive || !On("rarity") || context == null)
        {
            lootWatching = false;
            return;
        }
        var inventory = Inventory();
        var player = World.Player(context);
        if (inventory == null || player == null) return;
        var now = World.RealTime(context);
        if (lootWatching && now < nextLootScan) return;
        nextLootScan = now + 1;
        if (!lootWatching)
        {
            // What the hero already has keeps its rarity.
            lootKnown = new List<ulong>();
            foreach (var item in inventory.GetAllItems()) lootKnown.Add(item.ItemData.SessionUID.UID);
            groundKnown = new List<AActor>();
            lootWatching = true;
            return;
        }
        RaiseGroundLoot(player);
        var raise = new List<ulong>();
        foreach (var item in inventory.GetAllItems())
        {
            var uid = item.ItemData.SessionUID.UID;
            if (lootKnown.Contains(uid)) continue;
            lootKnown.Add(uid);
            var rarity = item.ItemData.RarityTag.TagName.ToString();
            var slot = UInventoryHelperLibrary.GetItemSlotTag(player, item.ItemData.TypeTag).TagName.ToString();
            bool gear = slot.Contains("Equipment");
            if (raisedLoot < 5) Diag($"loot new {item.ItemData.TypeTag.TagName} rarity={rarity} slot={slot} gear={gear}");
            if (gear && (rarity == "SW.Rarity.Common" || rarity == "SW.Rarity.Rare")) raise.Add(uid);
        }
        foreach (var uid in raise)
        {
            int changed = SetRarity(inventory, "", TopRegularRarity, new List<ulong>(), uid);
            raisedLoot += changed;
            if (raisedLoot <= 5) Diag($"loot rarity raised changed={changed} total={raisedLoot}");
        }
        if (raise.Count > 0) combatStatus = $"Raridade máxima: {raisedLoot} item(ns) de saque elevados a Especial.";
    }

    const string TopRegularRarity = "SW.Rarity.Special";

    // Drops lying on the ground are SWItemActors whose GASItemComponent holds the item; the pickup copies it into the
    // inventory, so raising it there shows the new rarity before it is picked up (and the inventory check above stays as
    // the fallback).
    List<AActor> groundKnown;
    bool loggedGroundScan;
    int raisedGround;

    void RaiseGroundLoot(APawn player)
    {
        var actors = World.FindAll(context!, Unreal.ClassOf<ASWItemActor>());
        if (!loggedGroundScan) { loggedGroundScan = true; Diag($"ground scan items={actors.Count}"); }
        foreach (var actor in actors)
        {
            if (groundKnown.Contains(actor)) continue;
            groundKnown.Add(actor);
            var component = (actor as ASWItemActor)?.GASItemComponent;
            if (component == null) continue;
            var data = component.ItemData;
            var rarity = data.RarityTag.TagName.ToString();
            var slot = UInventoryHelperLibrary.GetItemSlotTag(player, data.TypeTag).TagName.ToString();
            if (!slot.Contains("Equipment") || (rarity != "SW.Rarity.Common" && rarity != "SW.Rarity.Rare")) continue;
            data.RarityTag = new FGameplayTag { TagName = TopRegularRarity };
            component.ItemData = data;
            raisedGround++;
            if (raisedGround <= 5) Diag($"ground loot {data.TypeTag.TagName} {rarity}->{component.ItemData.RarityTag.TagName} actor={UKismetSystemLibrary.GetDisplayName(actor)}");
        }
        // Forget the drops that were picked up or despawned.
        for (int i = groundKnown.Count - 1; i >= 0; i--)
            if (!UKismetSystemLibrary.IsValid(groundKnown[i])) groundKnown.RemoveAt(i);
    }

    public void Restore()
    {
        // An inconclusive restore must not be retried against values changed by the game.
        if (combatRestoreBlocked || !combatActive) return;
        int skipped = 0;
        if (changedCanBeDamaged)
        {
            if (combatPlayer == null || !UKismetSystemLibrary.IsValid(combatPlayer)) skipped++;
            else if (combatPlayer.bCanBeDamaged != savedCanBeDamaged)
            {
                if (combatPlayer.bCanBeDamaged) skipped++;
                else
                {
                    combatPlayer.bCanBeDamaged = savedCanBeDamaged;
                    if (combatPlayer.bCanBeDamaged != savedCanBeDamaged) skipped++;
                }
            }
        }
        if (combatAsc != null && UKismetSystemLibrary.IsValid(combatAsc))
            for (int i = 0; i < savedAttributes.Count; i++)
            {
                var actual = UAbilitySystemBlueprintLibrary.GetFloatAttributeBaseFromAbilitySystemComponent(combatAsc, savedAttributes[i], out var found);
                if (CombatRecoveryRules.Restored(found, actual, savedValues[i])) continue;
                // Do not overwrite a base value changed by equipment, a level-up or another game system.
                if (!CombatRecoveryRules.CanRestore(found, actual, appliedValues[i])) { skipped++; continue; }
                USWCoreGASUtil.SetNumericAttributeBase(combatAsc, savedAttributes[i], savedValues[i]);
                var restored = UAbilitySystemBlueprintLibrary.GetFloatAttributeBaseFromAbilitySystemComponent(combatAsc, savedAttributes[i], out var verified);
                if (!CombatRecoveryRules.Restored(verified, restored, savedValues[i])) skipped++;
            }
        else skipped += savedAttributes.Count;
        combatActive = false;
        combatOptions = NeutralOptions();
        combatRestoreBlocked = skipped > 0;
        if (!combatRestoreBlocked) ClearCombatSnapshot();
        combatStatus = skipped == 0 ? "Controles temporários desligados; atributos preservados/restaurados."
            : $"Reversão inconclusiva em {skipped} controle(s). Combate bloqueado para este personagem; recarregue o personagem antes de reativar.";
    }

    // When the hero dies the game puts its own attribute values back on the same pawn, so the options looked applied but
    // weren't (reported on 0.3.2). Once a second the applied values are read back; if the game changed them, the options
    // are let go (what still holds is restored, what the game reset is left to it) and the remembered ones come back as
    // soon as the hero is alive again.
    void CheckCombatDrift()
    {
        if (!combatActive || context == null) return;
        var now = World.RealTime(context);
        if (now < nextDriftCheck) return;
        nextDriftCheck = now + 1;
        var asc = AbilitySystem();
        bool sameAsc = asc != null && asc == combatAsc;
        string changed = sameAsc ? "" : "ASC";
        for (int i = 0; sameAsc && i < savedAttributes.Count && changed == ""; i++)
        {
            var actual = UAbilitySystemBlueprintLibrary.GetFloatAttributeBaseFromAbilitySystemComponent(combatAsc!, savedAttributes[i], out var found);
            if (!found || actual != appliedValues[i]) changed = $"{i}:{appliedValues[i]}->{actual}";
        }
        if (changed == "") return;
        Restore();
        LetGameValuesStand();
        Diag($"combat reset by the game ({changed}); remembered={rememberedCombat != ""}");
        combatStatus = rememberedCombat != "" ? "O jogo restaurou os atributos (morte ou renascimento); o combate lembrado volta quando o herói estiver vivo."
            : "O jogo restaurou os atributos (morte ou renascimento).";
        // Seen as a new hero: ReapplyRemembered waits a moment and retries until the hero is alive.
        rememberPlayer = null;
    }

    double nextDriftCheck;

    // After a death or respawn the values the game set itself are its own: a restore that found them changed has nothing
    // of the menu's left to undo, so it doesn't block the options for this hero.
    void LetGameValuesStand()
    {
        if (!combatRestoreBlocked) return;
        Diag($"combat restore let the game's values stand: {combatStatus}");
        ClearCombatSnapshot();
        combatRestoreBlocked = false;
    }

    void RefreshCombatRecovery()
    {
        if (!combatRestoreBlocked || context == null) return;
        var player = World.Player(context);
        var asc = AbilitySystem();
        if (!CombatRecoveryRules.CanReleaseLock(player != null && UKismetSystemLibrary.IsValid(player),
            asc != null && UKismetSystemLibrary.IsValid(asc), player == combatPlayer, asc == combatAsc)) return;
        ClearCombatSnapshot();
        combatRestoreBlocked = false;
        combatStatus = "Novo personagem e sistema de atributos detectados; controles temporários disponíveis e desligados.";
    }

    void ClearCombatSnapshot()
    {
        savedAttributes.Clear();
        savedValues.Clear();
        appliedValues.Clear();
        combatPlayer = null;
        combatAsc = null;
        changedCanBeDamaged = false;
    }

    void DestroyTrackedMobs()
    {
        foreach (var actor in spawnedMobs)
            if (UKismetSystemLibrary.IsValid(actor)) actor.K2_DestroyActor();
        spawnedMobs.Clear();
    }

    public void Shutdown()
    {
        Restore();
        if (gameSpeed != 1 && context != null) UGameplayStatics.SetGlobalTimeDilation(context, 1);
        gameSpeed = 1;
        ReleaseFrozen();
        rejectPendingMobs = true;
        DestroyTrackedMobs();
        spawnPlayer = null;
    }

    public void SpawnActor_PreSpawn(AActor actor) { }
    public void SpawnActor_PreConstructActor(TSubclassOf<UObject> actorClass) { }
    public void SpawnActor_PostConstruct(AActor actor) { }
    public void SpawnActor_PostSpawn(AActor actor)
    {
        if (actor != null && spawnedMobs.Contains(actor)) return;
        if (pendingMobs > 0) pendingMobs--;
        if (actor == null || !UKismetSystemLibrary.IsValid(actor)) { spawnStatus = "Criação retornou entidade inválida."; return; }
        if (!CanWrite(out var reason)) { actor.K2_DestroyActor(); spawnStatus = reason; return; }
        if (rejectPendingMobs || spawnPlayer == null || spawnPlayer != World.Player(context!) || spawnLevel != World.LevelName(context!))
        {
            actor.K2_DestroyActor();
            spawnStatus = "Resposta tardia de criação descartada após cancelamento/mudança da sessão.";
            return;
        }
        spawnedMobs.Add(actor);
        spawnStatus = $"Mobs confirmados: {spawnedMobs.Count}; pendentes: {pendingMobs}";
        Diag("mob spawned " + UKismetSystemLibrary.GetDisplayName(actor));
    }
    public void SpawnActor_FailedSpawn(TSoftClassPtr<AActor> actorClass, AActor constructedActor)
    {
        if (pendingMobs > 0) pendingMobs--;
        spawnStatus = $"O jogo recusou uma criação; pendentes: {pendingMobs}.";
        Diag("mob spawn failed callback");
    }
}

[UClass("/Script/SWCoreGameplay.ActorSpawningReactionInterface")]
public interface IMenuSpawnResponse
{
    void SpawnActor_PreSpawn(AActor actor);
    void SpawnActor_PreConstructActor(TSubclassOf<UObject> actorClass);
    void SpawnActor_PostConstruct(AActor actor);
    void SpawnActor_PostSpawn(AActor actor);
    void SpawnActor_FailedSpawn(TSoftClassPtr<AActor> actorClass, AActor constructedActor);
}
