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
using UE.SWCoreCommon;
using UE.SWCoreGameplay;
using UE.SWUIConfig;
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
    List<string> tweaksLogged;
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
    // Item effects the menu offers (SW.Effect tags): template family, strength at levels I to III (three per effect, 0
    // when unknown) and the unique-item template and strength.
    List<string> effectTags;
    List<string> effectFamilies;
    List<float> effectTiers;
    List<string> effectUniqueTemplates;
    List<float> effectUniqueValues;
    public ItemVisuals? Visuals;

    public void Initialize(UObject owner)
    {
        context = owner;
        Visuals = UGameplayStatics.SpawnObject(Unreal.ClassOf<ItemVisuals>(), this) as ItemVisuals;
        if (Visuals != null) Visuals.Initialize(owner);
        savedAttributes = new List<FGameplayAttribute>();
        savedValues = new List<float>();
        tweaksLogged = new List<string>();
        appliedValues = new List<float>();
        spawnedMobs = new List<AActor>();
        knownMobActors = new List<AActor>();
        mobClassTags = new List<string>();
        mobClassPaths = new List<string>();
        spawnRequestTag = "";
        mobTags = new List<string>();
        itemIds = new List<string>();
        itemUids = new List<FSWSessionUID>();
        effectTags = new List<string>();
        effectFamilies = new List<string>();
        effectTiers = new List<float>();
        effectUniqueTemplates = new List<string>();
        effectUniqueValues = new List<float>();
        spawnLevel = "";
        spawnStatus = "";
        combatStatus = "";
        combatOptions = NeutralOptions();
        rememberedCombat = Settings.Get(CombatSetting, "");
        chestsSeen = new List<AActor>();
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
        return "Solo/offline"
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
        ? $"Valor lido após alteração: {actual}. O jogo grava no próximo salvamento."
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
        bool talisman = IsTalisman(tag);
        if (talisman && TalismanLevel(tag, out var levels) > 0) return "O herói já tem este talismã: mude o nível dele no painel.";
        if (talisman) quantity = 1;
        var itemTag = new FGameplayTag { TagName = tag };
        var category = UInventoryHelperLibrary.GetItemCategoryTag(player, itemTag);
        int limit = UInventoryHelperLibrary.GetInventoryStorageLimit(player, category);
        int occupied = 0;
        foreach (var item in inventory.GetAllItems())
            if (UInventoryHelperLibrary.GetItemCategoryTag(player, item.ItemData.TypeTag).TagName == category.TagName) occupied++;
        Diag($"item {tag} category={category.TagName} limit={limit} occupied={occupied}");
        // A talisman's category may have no limit to confirm (0.4.0 couldn't create talismans, and this refusal was in its
        // log); the hero keeps one of each instead.
        if (limit <= 0 && !talisman) return "Não foi possível confirmar a capacidade desta categoria.";
        if (limit > 0 && occupied + quantity > limit) return $"Inventário sem espaço: {occupied}/{limit} nesta categoria.";
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
            if (SameTag(item.ItemData.TypeTag, tag) && (!uniqueOnly || item.ItemData.RarityTag.TagName.ToString() == UniqueRarity))
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
            var classless = new List<string>();
            var classlessRefs = new List<string>();
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
                    string actorRef = "";
                    foreach (var reference in data.AssetReferences)
                    {
                        if (reference == null) continue;
                        var refPath = UKismetSystemLibrary.GetPathName(reference);
                        refs += refPath + " ";
                        if (!refPath.EndsWith("_C") || mobClassTags.Contains(type)) continue;
                        if (Unreal.LoadClass<APawn>(refPath) == null)
                        {
                            // Not a pawn: kept in case it's the mob's own actor (a character's Blueprint, BP_…).
                            if (actorRef == "" && refPath.Contains("/Characters/") && refPath.Contains("/BP_")) actorRef = refPath;
                            continue;
                        }
                        mobClassTags.Add(type);
                        mobClassPaths.Add(refPath);
                    }
                    if (loaded <= 3) Diag($"mob type {type} refs={refs}");
                    // A type that names no pawn class (16 of 220, 0.4.4): an actor Blueprint it names will do, else one found
                    // by name below.
                    if (mobClassTags.Contains(type)) continue;
                    if (actorRef != "" && Unreal.LoadClass<AActor>(actorRef) != null)
                    {
                        mobClassTags.Add(type);
                        mobClassPaths.Add(actorRef);
                        Diag($"mob class actor {type} {actorRef}");
                        continue;
                    }
                    classless.Add(type);
                    classlessRefs.Add(refs);
                }
            // The Blueprint of a type that names none, by name among the characters' assets: SW.Mob.CaveSpider → BP_CaveSpider,
            // SW.Mob.Bee.Honeypot → BP_Bee_Honeypot or BP_Honeypot.
            if (classless.Count > 0 && registry != null && registry.GetAssetsByPath("/Game/Spicewood/Art/Characters", out var art, true, true))
                foreach (var asset in art)
                {
                    var name = asset.AssetName.ToString();
                    if (!name.StartsWith("BP_")) continue;
                    var key = name.Substring(3).Replace("_", "").ToLower();
                    foreach (var type in classless)
                    {
                        if (mobClassTags.Contains(type) || (key != MobKey(type, false) && key != MobKey(type, true))) continue;
                        var path = asset.PackageName.ToString() + "." + name + "_C";
                        if (Unreal.LoadClass<AActor>(path) == null) continue;
                        mobClassTags.Add(type);
                        mobClassPaths.Add(path);
                        Diag($"mob class by name {type} {path}");
                    }
                }
            for (int i = 0; i < classless.Count; i++)
                if (!mobClassTags.Contains(classless[i])) Diag($"mob noclass {classless[i]} refs={classlessRefs[i]}");
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
            // A mob seen near the hero gives its own class: one its type asset doesn't name can be created from then on.
            if (!ListedMob(type) || mobClassTags.Contains(type)) continue;
            var seenClass = GameUI.ClassPath(actor);
            if (Unreal.LoadClass<AActor>(seenClass) == null) continue;
            mobClassTags.Add(type);
            mobClassPaths.Add(seenClass);
            Diag($"mob class seen {type} {seenClass}");
        }
        Diag($"mob actors={actors} types={mobTags.Count} sample={actorTypes}");
        foreach (var name in mobTags) if (name.ToLower().Contains(query.ToLower())) result.Add(name);
        return result;
    }

    void AddMobType(string name)
    {
        if (ListedMob(name) && !mobTags.Contains(name)) mobTags.Add(name);
    }

    // The catalog lists mobs only: not the types they build on (SW.Mob.Default.*, SW.Mob.Variant.*), props named after mobs
    // (SW.PropActor.Mob.*) or NPCs seen near the hero. All but one of the 16 types with no class were of these (0.4.4 log).
    static bool ListedMob(string name) => name.StartsWith("SW.Mob.") && !name.StartsWith("SW.Mob.Default") && !name.StartsWith("SW.Mob.Variant");

    // A mob type as its Blueprint's name reads without "BP_" and underscores: the whole name or only its last part.
    static string MobKey(string type, bool lastPart)
    {
        var name = type.StartsWith("SW.Mob.") ? type.Substring(7) : type;
        if (lastPart && name.Contains(".")) name = name.Substring(name.LastIndexOf(".") + 1);
        return name.Replace(".", "").Replace("_", "").ToLower();
    }

    public string SpawnMob(string tag, int quantity)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.Quantity(quantity)) return "Quantidade permitida: 1 a 20.";
        if (!MobCatalog("").Contains(tag)) return "Selecione um mob do catálogo carregado pelo jogo.";
        if (pendingMobs > 0) return "Aguarde as criações pendentes antes de repetir.";
        var player = World.Player(context!);
        if (player == null) return "Personagem indisponível.";
        // Only a mob's own class makes it: the game's spawn queue (RequestSpawnActorBP) never made one in any test (a zombie
        // in the camp in 0.1.7, chickens, cave spiders and evokers before the classes were read).
        int classIndex = mobClassTags.IndexOf(tag);
        if (classIndex < 0) return "Este mob ainda não pode ser criado: chegue perto de um e abra a aba Mobs de novo.";
        var mobClass = Unreal.LoadClass<AActor>(mobClassPaths[classIndex]);
        if (mobClass == null) return "A classe deste mob não carregou.";
        spawnPlayer = player;
        spawnLevel = World.LevelName(context!);
        rejectPendingMobs = false;
        var origin = player.K2_GetActorLocation();
        int created = 0;
        for (int i = 0; i < quantity; i++)
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
        return created > 0 ? $"{created} mob(s) criado(s) perto do herói. Feche o menu para eles agirem."
            : "O jogo não criou o mob perto do herói.";
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

    // The worn items the menu edits: what the game lets the hero enchant, then the other worn weapons and armor (a common
    // item has no enchantment slot, so a hero in common gear had nothing listed: 1.0.0 test), and, for effects (0.4.4),
    // the worn artifacts last, so the others keep their ids in both modes. Artifacts carry rolled effects like weapons do
    // (SW.Item.Effect.Rerollable in the save), but the game never lets them be enchanted.
    public List<string> EquipmentCatalog(bool withArtifacts = false)
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
        var worn = UInventoryHelperLibrary.GetAllEquippedItems(World.Player(context), false, true, true);
        int gear = 0, artifacts = 0;
        for (int pass = 0; pass < 2; pass++)
            foreach (var item in worn)
            {
                // First pass: weapons and armor; second: artifacts, only for effects.
                var category = Rules.ItemCategory(item.EquippedSlot.TagName.ToString());
                bool artifact = category == "artifact";
                if (!artifact && category != "melee" && category != "ranged" && category != "armor") continue;
                if (artifact != (pass == 1) || (artifact && !withArtifacts)) continue;
                bool listed = false;
                foreach (var uid in itemUids)
                    if (uid.UID == item.ItemData.SessionUID.UID) listed = true;
                if (listed) continue;
                var id = (itemIds.Count + 1).ToString() + " | " + item.ItemData.TypeTag.TagName.ToString();
                result.Add(id);
                itemIds.Add(id);
                itemUids.Add(item.ItemData.SessionUID);
                if (artifact) artifacts++;
                else gear++;
                Diag($"equip {id} slot={item.EquippedSlot.TagName}");
            }
        Diag($"equip other={gear} artifacts={artifacts}");
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
        if (!Rules.FreeEnchantLevel(level)) return $"Nível livre: 1 a {Rules.MaxFreeLevel}.";
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

    // A worn item is taken off and put back so the game applies its effects again; true when it isn't worn.
    static bool Reequip(UInventoryManagerComponent inventory, FInventoryEntry item)
    {
        var slot = item.EquippedSlot;
        if (!IsTag(slot)) return true;
        inventory.UnequipItemFromSlot(slot);
        return inventory.EquipItemBySessionUID(slot, item.ItemData.SessionUID);
    }

    // ---------------------------------------------------------------- Talismans: one of each type, levels 1 to 3

    // The game spells one with a small "sw" (sw.Item.Talisman.Llama, which 0.4.1 refused to create), so tags are compared
    // without case here.
    public static bool IsTalisman(string tag) => tag.ToLower().StartsWith("sw.item.talisman.");

    static bool SameTag(FGameplayTag tag, string name) => tag.TagName.ToString().ToLower() == name.ToLower();

    const string UpgradableBatch = "SW.Item.Effect.Upgradable";

    /// <summary>The level (from 1) of the hero's talisman of a type and how many levels it has; 0 when the hero has none.</summary>
    public int TalismanLevel(string tag, out int levels)
    {
        levels = 0;
        var inventory = Inventory();
        if (inventory == null) return 0;
        foreach (var item in inventory.GetAllItems())
        {
            if (!SameTag(item.ItemData.TypeTag, tag)) continue;
            levels = item.ItemData.ItemProgression.ItemLevels.Count;
            return item.ItemData.ItemProgression.CurrentLevel + 1;
        }
        return 0;
    }

    /// <summary>
    /// What the hero's talisman of a type does at its level, in the game's words with the talisman's own strength (the
    /// game's sentence alone said "0% de dano extra" for the Lentes de Ametista at level 3, 0.4.4).
    /// </summary>
    public List<string> TalismanEffects(string tag)
    {
        var texts = new List<string>();
        var inventory = Inventory();
        var player = context != null ? World.Player(context) : null;
        if (inventory == null || player == null) return texts;
        foreach (var item in inventory.GetAllItems())
        {
            if (!SameTag(item.ItemData.TypeTag, tag)) continue;
            foreach (var batch in item.ItemData.Effects)
                foreach (var effect in batch.EffectsInThisBatch) texts.Add(EffectSentence(effect));
        }
        return texts;
    }

    /// <summary>Creates a talisman the hero doesn't have, then sets its level.</summary>
    public string SpawnTalisman(string tag, int level)
    {
        var result = SpawnItem(tag, 1, "");
        if (level <= 1 || TalismanLevel(tag, out var levels) == 0) return result;
        return result + " " + SetTalismanLevel(tag, level);
    }

    /// <summary>
    /// Sets the hero's talisman to a level the way the save keeps it (0.4.0 research: CurrentLevel counts from 0, CurrentXP
    /// is the XP earned, and the Upgradable batch holds the level's effects and tags from ItemLevels), then re-equips it if
    /// it is worn so the level's effects apply.
    /// </summary>
    public string SetTalismanLevel(string tag, int level)
    {
        if (!CanWrite(out var reason)) return reason;
        var inventory = Inventory();
        if (inventory == null) return "Inventário indisponível.";
        int before = TalismanLevel(tag, out var levels);
        if (before == 0) return "O herói não tem este talismã.";
        if (!Rules.TalismanLevel(level, levels)) return $"Nível do talismã: 1 a {levels}.";
        if (level == before) return $"O talismã já está no nível {level}.";
        var item = new FInventoryEntry();
        foreach (var entry in inventory.GetAllItems())
            if (SameTag(entry.ItemData.TypeTag, tag)) item = entry;
        float xp = TalismanStartXP(item.ItemData, level - 1);
        bool written = WriteTalismanLevel(inventory, item.ItemData.SessionUID.UID, level - 1, xp);
        bool equipped = Reequip(inventory, item);
        int after = TalismanLevel(tag, out levels);
        Diag($"talisman {tag} level {before}->{after} target={level} xp {item.ItemData.ItemProgression.CurrentXP}->{xp} written={written} worn={IsTag(item.EquippedSlot)} reequip={equipped}");
        return after == level ? $"Talismã agora no nível {after} (antes {before})." : $"O jogo manteve o talismã no nível {after}; pedido {level}.";
    }

    // The XP a talisman holds on reaching a level (index 0 is level 1): what the game asks to leave the level before. Read
    // as a running total, which is right for level 2; were it per level, level 3 would only show less progress, and it is
    // the last level anyway. 0 when the game gives no figure: too little XP never makes the game level a talisman down.
    float TalismanStartXP(FItemData data, int index)
    {
        if (index <= 0 || context == null) return 0;
        var copy = data;
        var progression = copy.ItemProgression;
        progression.CurrentLevel = index - 1;
        progression.CurrentXP = 0;
        copy.ItemProgression = progression;
        bool known = UInventoryHelperLibrary.GetNextLevelXPRequirement(context, copy, out var need);
        Diag($"talisman xp to leave level {index} known={known} need={need}");
        return known && need > 0 ? need : 0;
    }

    static bool WriteTalismanLevel(UInventoryManagerComponent inventory, ulong uid, int index, float xp)
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
                var progression = data.ItemProgression;
                var levels = progression.ItemLevels;
                if (index < 0 || index >= levels.Count) return false;
                progression.CurrentLevel = index;
                progression.CurrentXP = xp;
                data.ItemProgression = progression;
                var batches = data.Effects;
                var batch = new FItemEffectBatch
                {
                    TypeTag = new FGameplayTag { TagName = UpgradableBatch },
                    EffectsInThisBatch = levels[index].LevelEffects,
                    GrantedTagsInThisBatch = levels[index].LevelTags,
                };
                int at = -1;
                for (int b = 0; b < batches.Count; b++)
                    if (batches[b].TypeTag.TagName.ToString() == UpgradableBatch) at = b;
                if (at >= 0) batches[at] = batch;
                else batches.Add(batch);
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

    // ---------------------------------------------------------------- Item effects: the rolled and built-in effects of worn gear

    const string RerollableBatch = "SW.Item.Effect.Rerollable";

    // Effects seen on two heroes' items (0.4.0 save research): effect | template family | strength at I | II | III (empty when
    // not seen) | unique-item template | its strength. Effects on the hero's own items that aren't here are added at run time.
    static List<string> KnownEffects() => new List<string>
    {
        "AmmoCapacity|AmmoCapacity|1.2|1.4|1.6|SW.EffectTemplate.AmmoCapacity.Unique|1.45",
        "ArrowBurst|ArrowBurst|0.2||0.3||",
        "ArtifactHealing|||||SW.EffectTemplate.ArtifactHealing.Unique|0.06",
        "BeastBoss|BeastBoss|0.2||||",
        "Committed|Committed||0.25|0.3||",
        "Constitution|Constitution|0.1|0.2|0.3||",
        "Cooldown|Cooldown|-0.1|-0.15|-0.2||",
        "CriticalEdge|CriticalEdge|0.1|0.2|0.3||",
        "CriticalHit|CriticalHit|0.05|0.1|0.15||",
        "Deflect|Deflect|0.1|0.15|0.2|SW.EffectTemplate.Deflect.Unique|0.25",
        "DropChance|DropChance|0.01|0.02|0.04||",
        "Duelist|||||SW.EffectTemplate.Duelist.Unique|0.4",
        "EagleEye|EagleEye|0.1|0.15|||",
        "ElementalProtection|ElementalProtection|-0.1|-0.15|-0.2||",
        "EmeraldsIncrease|Prospector|0.05||||",
        "Expand|Expand|0.2||0.4|SW.EffectTemplate.Expand.Unique|0.55",
        "ExperienceIncrease|ExperienceIncrease|1.04|1.06|1.1||",
        "ExplosiveStrike|||||SW.EffectTemplate.ExplosiveStrike.Unique|1",
        "FireFocus|FireFocus|0.1|0.15||SW.EffectTemplate.FireFocus.Unique|0.2",
        "FriendsForever|FriendsForever||-0.1|-0.15||",
        "Friendship|Friendship|-0.05|-0.1||SW.EffectTemplate.Friendship.Unique|-0.2",
        "FrostFocus|FrostFocus||0.15|0.2||",
        "Gambler|Gambler|0.1|0.15|0.2||",
        "Hairtrigger|||||SW.EffectTemplate.Hairtrigger.Unique|0.4",
        "HealingFocus|HealingFocus|0.25|0.35|0.5|SW.EffectTemplate.HealingFocus.Unique|0.5",
        "HealthBoost|HealthBoost|1.2|1.25|1.35|SW.EffectTemplate.HealthBoost.Unique|1.3",
        "HealthyStrike|HealthyStrike|1|2|3||",
        "Jumpshot|||||SW.EffectTemplate.Jumpshot.Unique|0.06",
        "Knockback|Knockback|0.15|0.2||SW.EffectTemplate.Knockback.Unique|0.4",
        "LightningFocus|LightningFocus|0.1|0.15|0.2|SW.EffectTemplate.LightningFocus.Unique|0.25",
        "Looting|Looting||0.4|0.6|SW.EffectTemplate.Looting.Unique|0.8",
        "Lucky|Lucky|0.1|0.15|0.2||",
        "MasterMarksman|MasterMarksman||0.2|0.25||",
        "MasterStrike|MasterStrike||0.2|||",
        "MultiShot|MultiShot|0.2|0.3|0.4|SW.EffectTemplate.MultiShot.Unique|0.6",
        "Opportunist|Opportunist|||0.5||",
        "Opulence|Opulence|||0.03|SW.EffectTemplate.Opulence.Unique|0.05",
        "PoisonFocus|PoisonFocus||0.15|0.2|SW.EffectTemplate.PoisonFocus.Unique|0.2",
        "PotionCooldown|PotionMaster||-0.1|-0.2||",
        "Power|Power||0.2|0.3|SW.EffectTemplate.Power.Unique|0.3",
        "Precision|Precision|||0.2||",
        "ProjectileProtection|ProjectileProtection|-0.1|-0.15|-0.2||",
        "Protection|Protection|-0.1|-0.15|-0.2|SW.Effecttemplate.Protection.Unique|-0.25",
        "RapidStrike|RapidStrike||0.15|0.2|SW.EffectTemplate.RapidStrike.Unique|0.3",
        "Reconstruction|Reconstruction|-0.15|-0.2||SW.EffectTemplate.Reconstruction.Unique|-0.35",
        "Reeling|Reeling|||0.4|SW.EffectTemplate.Reeling.Unique|0.6",
        "Regeneration|Regeneration|||0.1||",
        "Resilience|Resilience|-0.2|-0.25|-0.3||",
        "RollCooldown|Acrobat|0.1|0.15|0.25||",
        "Saboteur|Saboteur|0.4|0.6|0.8||",
        "ShadowWalk|ShadowWalk|0.2||0.35||",
        "Sharpness|Sharpness||0.2|0.3||",
        "Sidestep|Sidestep|0.1|0.15||SW.EffectTemplate.Sidestep.Unique|0.2",
        "Sniper|Sniper|0.25|0.5|0.75||",
        "SoulFocus|SoulFocus|0.1|0.15|||",
        "SoulGather|SoulGather|0.4|0.8|1.4||",
        "SoulMax|BagOfSouls|1.25|1.3|1.35||",
        "SpeedBoost|SpeedBoost|0.05||0.15||",
        "StatusBuff|StatusBuff|1.1|1.15|1.25||",
        "Supercharge|Supercharge|0.3|0.35|0.4||",
        "SweepingEdge|SweepingEdge|||0.4|SW.EffectTemplate.SweepingEdge.Unique|0.5",
        "SwiftSneak|SwiftSneak|0.15||0.3||",
        "Thorns|Thorns|0.5|0.65|0.8|SW.EffectTemplate.Thorns.Unique|1",
        "Vanguard|Vanguard||0.35|0.5||",
        "Vestige|Vestige|0.1|0.15|0.2||",
        "Vivify|Vivify|0.15||0.5|SW.EffectTemplate.Vivify.Unique|0.7",
    };

    void LoadEffects()
    {
        if (effectTags.Count > 0) return;
        foreach (var line in KnownEffects())
        {
            var fields = Fields(line);
            if (fields.Count < 7) continue;
            effectTags.Add("SW.Effect." + fields[0]);
            effectFamilies.Add(fields[1] == "" ? "" : "SW.EffectTemplate." + fields[1]);
            for (int i = 2; i <= 4; i++) effectTiers.Add(fields[i] == "" ? 0 : (float)UKismetStringLibrary.Conv_StringToDouble(fields[i]));
            effectUniqueTemplates.Add(fields[5]);
            effectUniqueValues.Add(fields[6] == "" ? 0 : (float)UKismetStringLibrary.Conv_StringToDouble(fields[6]));
        }
        var inventory = Inventory();
        if (inventory == null) return;
        foreach (var item in inventory.GetAllItems())
            foreach (var batch in item.ItemData.Effects)
                foreach (var effect in batch.EffectsInThisBatch)
                {
                    var tag = effect.TypeTag.TagName.ToString();
                    if (!tag.StartsWith("SW.Effect.") || effectTags.Contains(tag)) continue;
                    effectTags.Add(tag);
                    effectFamilies.Add("");
                    effectTiers.Add(0);
                    effectTiers.Add(0);
                    effectTiers.Add(0);
                    effectUniqueTemplates.Add(effect.GeneratorData.GeneratorParentTemplate.TagName.ToString());
                    effectUniqueValues.Add(effect.Intensity);
                }
        Diag($"effects catalog={effectTags.Count}");
    }

    static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var rest = line;
        int at = rest.IndexOf("|");
        while (at >= 0)
        {
            fields.Add(rest.Substring(0, at));
            rest = rest.Substring(at + 1);
            at = rest.IndexOf("|");
        }
        fields.Add(rest);
        return fields;
    }

    /// <summary>Every effect the menu can put on an item.</summary>
    public List<string> EffectCatalog()
    {
        LoadEffects();
        var result = new List<string>();
        result.AddRange(effectTags);
        return result;
    }

    /// <summary>
    /// An effect's strength at a level: the game's own figure for I to III when known, otherwise on the line through the two
    /// highest known levels (above III it keeps growing by that step, as free enchantments do). An effect only seen on
    /// unique items takes that strength as its level III.
    /// </summary>
    public float EffectStrength(string tag, int level)
    {
        LoadEffects();
        int at = effectTags.IndexOf(tag);
        if (at < 0) return 0;
        if (level >= 1 && level <= 3 && effectTiers[at * 3 + level - 1] != 0) return effectTiers[at * 3 + level - 1];
        int high = 0;
        int low = 0;
        for (int tier = 3; tier >= 1; tier--)
        {
            if (effectTiers[at * 3 + tier - 1] == 0) continue;
            if (high == 0) high = tier;
            else if (low == 0) low = tier;
        }
        if (high == 0) return effectUniqueValues[at] * level / 3;
        float top = effectTiers[at * 3 + high - 1];
        if (low == 0) return top * level / high;
        float step = (top - effectTiers[at * 3 + low - 1]) / (high - low);
        return top + (level - high) * step;
    }

    // The template the effect is filed under at a level: the known level nearest to it (the game's templates for unseen
    // levels aren't known), or the unique one.
    string EffectTemplate(string tag, int level)
    {
        int at = effectTags.IndexOf(tag);
        if (at < 0) return "";
        int best = 0;
        for (int tier = 1; tier <= 3; tier++)
        {
            if (effectTiers[at * 3 + tier - 1] == 0) continue;
            int distance = tier > level ? tier - level : level - tier;
            int bestDistance = best > level ? best - level : level - best;
            if (best == 0 || distance <= bestDistance) best = tier;
        }
        if (best == 0 || effectFamilies[at] == "") return effectUniqueTemplates[at];
        return effectFamilies[at] + (best == 1 ? ".I" : best == 2 ? ".II" : ".III");
    }

    static string EffectText(APawn player, FItemEffect effect)
        => UKismetTextLibrary.Conv_TextToString(UInventoryHelperLibrary.GetTotalEffectDescription(player, effect));

    FItemEffect MakeEffect(string tag, int level) => new FItemEffect
    {
        TypeTag = new FGameplayTag { TagName = tag },
        Intensity = EffectStrength(tag, level),
        EnchantmentPointsInvested = 0,
        GeneratorData = new FEffectGeneratorData { GeneratorParentTemplate = new FGameplayTag { TagName = EffectTemplate(tag, level) } },
    };

    /// <summary>What an effect does at a level, in the game's words ("" outside a match).</summary>
    public string EffectPreview(string tag, int level) => EffectDescription(tag, EffectStrength(tag, level));

    /// <summary>
    /// What an effect does at a strength, in the game's words. The game's sentence shows the hero's total of that effect
    /// over the worn gear (0.4.1 showed 0% for effects nothing worn had), so its percentage is swapped for this strength:
    /// a share for most effects, the part above 1 for the multipliers. Effects counted in units keep the game's text.
    /// </summary>
    public string EffectDescription(string tag, float strength) =>
        EffectSentence(new FItemEffect { TypeTag = new FGameplayTag { TagName = tag }, Intensity = strength });

    // The same for an effect an item already has. A talisman's needs its own template: the game's sentence came out empty
    // for the bare tag and strength (1.0.0 test).
    string EffectSentence(FItemEffect effect)
    {
        var player = context != null ? World.Player(context) : null;
        if (player == null) return "";
        var tag = effect.TypeTag.TagName.ToString();
        float strength = effect.Intensity;
        var text = EffectText(player, effect);
        if (text == "") return EffectName(tag);
        var key = tag.StartsWith("SW.Effect.") ? tag.Substring(10) : tag;
        if (key == "HealthyStrike" || key == "ExplosiveStrike") return text;
        int percent = text.IndexOf("%");
        if (percent <= 0) return text;
        int start = percent;
        while (start > 0 && "0123456789.,-+".Contains(text.Substring(start - 1, 1))) start--;
        if (start == percent) return text;
        bool fromOne = key == "AmmoCapacity" || key == "ExperienceIncrease" || key == "HealthBoost" || key == "SoulMax" || key == "StatusBuff";
        float share = (fromOne ? strength - 1 : strength) * 100;
        if (share < 0) share = -share;
        int tenths = (int)(share * 10 + 0.5f);
        var number = tenths % 10 == 0 ? $"{tenths / 10}" : $"{tenths / 10},{tenths % 10}";
        return text.Substring(0, start) + number + text.Substring(percent);
    }

    /// <summary>An effect's short name in Portuguese ("" when the menu has none); the game names effects only internally.</summary>
    public static string EffectName(string tag)
    {
        var key = tag.StartsWith("SW.Effect.") ? tag.Substring(10) : tag;
        foreach (var line in EffectNames())
        {
            int at = line.IndexOf("=");
            if (line.Substring(0, at) == key) return line.Substring(at + 1);
        }
        return "";
    }

    static List<string> EffectNames() => new List<string>
    {
        "AmmoCapacity=Aljava ampla", "ArrowBurst=Rajada de flechas", "ArtifactHealing=Cura por artefato", "BeastBoss=Chefe das feras",
        "Committed=Comprometido", "Constitution=Constituição", "Cooldown=Recarga rápida", "CriticalEdge=Fio crítico",
        "CriticalHit=Acerto crítico", "Deflect=Desvio", "DropChance=Chance de espólio", "Duelist=Duelista", "EagleEye=Olho de águia",
        "ElementalProtection=Proteção elemental", "EmeraldsIncrease=Garimpeiro", "Expand=Expansão", "ExperienceIncrease=Mais experiência",
        "ExplosiveStrike=Golpe explosivo", "FireFocus=Foco em fogo", "FriendsForever=Amigos para sempre", "Friendship=Amizade",
        "FrostFocus=Foco em gelo", "Gambler=Apostador", "Hairtrigger=Gatilho sensível", "HealingFocus=Foco em cura",
        "HealthBoost=Vida extra", "HealthyStrike=Golpe saudável", "Jumpshot=Tiro no salto", "Knockback=Repulsão",
        "LightningFocus=Foco em raio", "Looting=Saque", "Lucky=Sorte", "MasterMarksman=Mestre atirador", "MasterStrike=Golpe de mestre",
        "MultiShot=Tiro múltiplo", "Opportunist=Oportunista", "Opulence=Opulência", "PoisonFocus=Foco em veneno",
        "PotionCooldown=Mestre das poções", "Power=Poder", "Precision=Precisão", "ProjectileProtection=Proteção contra projéteis",
        "Protection=Proteção", "RapidStrike=Golpe rápido", "Reconstruction=Reconstrução", "Reeling=Cambalear", "Regeneration=Regeneração",
        "Resilience=Resiliência", "RollCooldown=Acrobata", "Saboteur=Sabotador", "ShadowWalk=Passo sombrio", "Sharpness=Afiação",
        "Sidestep=Esquiva", "Sniper=Atirador de elite", "SoulFocus=Foco em almas", "SoulGather=Coleta de almas", "SoulMax=Bolsa de almas",
        "SpeedBoost=Velocidade", "StatusBuff=Reforço de efeitos", "Supercharge=Supercarga", "SweepingEdge=Lâmina varredora",
        "SwiftSneak=Furtividade rápida", "Thorns=Espinhos", "Vanguard=Vanguarda", "Vestige=Vestígio", "Vivify=Vivificar",
        "Finesse=Finesse", "FiringEmerald=Esmeralda disparada", "Plummeting=Queda livre", "SpeedIncrement=Embalo",
        "StatusOnHit.Strength=Golpe fortalecedor",
    };

    /// <summary>A worn item's effects (all but its enchantment), with the game's description of each.</summary>
    public void ItemEffects(string itemId, List<string> tags, List<string> texts)
    {
        tags.Clear();
        texts.Clear();
        if (!FindEquipment(itemId, out var item)) return;
        var player = World.Player(context!);
        foreach (var batch in item.ItemData.Effects)
        {
            if (batch.TypeTag.TagName.ToString() == EnchantmentBatch) continue;
            foreach (var effect in batch.EffectsInThisBatch)
            {
                tags.Add(effect.TypeTag.TagName.ToString());
                texts.Add(player != null ? EffectDescription(effect.TypeTag.TagName.ToString(), effect.Intensity) : "");
            }
        }
    }

    // The strength of an item's effect of a type, read back by its id; false when the item doesn't have it.
    bool ItemEffectStrength(FSWSessionUID uid, string tag, out float strength)
    {
        strength = 0;
        if (context == null || !UInventoryHelperLibrary.GetInventoryEntryFromID(World.Player(context), uid, out var item)) return false;
        foreach (var batch in item.ItemData.Effects)
        {
            if (batch.TypeTag.TagName.ToString() == EnchantmentBatch) continue;
            foreach (var effect in batch.EffectsInThisBatch)
                if (effect.TypeTag.TagName.ToString() == tag)
                {
                    strength = effect.Intensity;
                    return true;
                }
        }
        return false;
    }

    /// <summary>
    /// Puts an effect on a worn item at a level: it replaces the strength of the item's own effect of that type, or joins
    /// the item's rerollable effects (the Blacksmith's reforge may change it later). The item is re-equipped so it applies.
    /// </summary>
    public string SetItemEffect(string itemId, string tag, int level)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!Rules.EffectLevel(level)) return $"Nível do efeito: 1 a {Rules.MaxEffectLevel}.";
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; selecione de novo.";
        var inventory = Inventory();
        if (inventory == null) return "Inventário indisponível.";
        LoadEffects();
        if (!effectTags.Contains(tag)) return "Efeito desconhecido.";
        var effect = MakeEffect(tag, level);
        var uid = item.ItemData.SessionUID;
        int mode = WriteItemEffect(inventory, uid.UID, effect);
        if (mode == 0) return "O item não foi encontrado no inventário do jogo.";
        bool equipped = Reequip(inventory, item);
        bool found = ItemEffectStrength(uid, tag, out var read);
        Diag($"item effect {itemId} {tag} level={level} strength={effect.Intensity} template={effect.GeneratorData.GeneratorParentTemplate.TagName} {(mode == 1 ? "added" : "replaced")} reequip={equipped} read={found}/{read}");
        if (!found || read != effect.Intensity) return "O jogo não confirmou o efeito no item; confira o inventário.";
        // Above III the game's own tooltip keeps the template's (III) figure, but the strength is what acts: Recarga rápida on
        // a 4 s artifact came back in 3.1 s at level 3 and 1.7 s at level 10 (1.0.0 test).
        string over = level > 3 ? "; a dica do jogo mostra o valor do nível 3" : "";
        return $"Efeito {(mode == 1 ? "adicionado" : "trocado")} no item (nível {level}{over}). Reequipado: {(equipped ? "sim" : "não, equipe de novo pelo inventário")}.";
    }

    // Replaces the item's effect of the same type, or adds it to the rerollable batch (made when missing): 2 replaced, 1
    // added, 0 item not found.
    static int WriteItemEffect(UInventoryManagerComponent inventory, ulong uid, FItemEffect effect)
    {
        var tag = effect.TypeTag.TagName.ToString();
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
                int mode = 0;
                for (int b = 0; b < batches.Count; b++)
                {
                    if (batches[b].TypeTag.TagName.ToString() == EnchantmentBatch) continue;
                    var batch = batches[b];
                    var effects = batch.EffectsInThisBatch;
                    bool changed = false;
                    for (int f = 0; f < effects.Count; f++)
                        if (effects[f].TypeTag.TagName.ToString() == tag) { effects[f] = effect; changed = true; }
                    if (!changed) continue;
                    batch.EffectsInThisBatch = effects;
                    batches[b] = batch;
                    mode = 2;
                }
                if (mode == 0)
                {
                    mode = 1;
                    int at = -1;
                    for (int b = 0; b < batches.Count; b++)
                        if (batches[b].TypeTag.TagName.ToString() == RerollableBatch) at = b;
                    if (at < 0)
                    {
                        batches.Add(new FItemEffectBatch { TypeTag = new FGameplayTag { TagName = RerollableBatch }, EffectsInThisBatch = new List<FItemEffect>() });
                        at = batches.Count - 1;
                    }
                    var rerollable = batches[at];
                    var list = rerollable.EffectsInThisBatch;
                    list.Add(effect);
                    rerollable.EffectsInThisBatch = list;
                    batches[at] = rerollable;
                }
                data.Effects = batches;
                entry.ItemData = data;
                entries[e] = entry;
                slot.ItemInventoryEntries = entries;
                slots[s] = slot;
                container.Items = slots;
                inventory.ReplicatedItems = container;
                return mode;
            }
        }
        return 0;
    }

    /// <summary>Takes every effect of a type off a worn item (its enchantment stays) and re-equips it.</summary>
    public string RemoveItemEffect(string itemId, string tag)
    {
        if (!CanWrite(out var reason)) return reason;
        if (!FindEquipment(itemId, out var item)) return "Equipamento não encontrado; selecione de novo.";
        var inventory = Inventory();
        if (inventory == null) return "Inventário indisponível.";
        var uid = item.ItemData.SessionUID;
        int removed = ClearItemEffect(inventory, uid.UID, tag);
        if (removed == 0) return "O item não tem este efeito.";
        bool equipped = Reequip(inventory, item);
        bool still = ItemEffectStrength(uid, tag, out var read);
        Diag($"item effect {itemId} {tag} removed={removed} reequip={equipped} still={still}");
        if (still) return "O jogo manteve o efeito no item.";
        return $"Efeito removido do item. Reequipado: {(equipped ? "sim" : "não, equipe de novo pelo inventário")}.";
    }

    static int ClearItemEffect(UInventoryManagerComponent inventory, ulong uid, string tag)
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
                int removed = 0;
                for (int b = 0; b < batches.Count; b++)
                {
                    if (batches[b].TypeTag.TagName.ToString() == EnchantmentBatch) continue;
                    var batch = batches[b];
                    var effects = batch.EffectsInThisBatch;
                    for (int f = effects.Count - 1; f >= 0; f--)
                        if (effects[f].TypeTag.TagName.ToString() == tag) { effects.RemoveAt(f); removed++; }
                    batch.EffectsInThisBatch = effects;
                    batches[b] = batch;
                }
                if (removed == 0) return 0;
                data.Effects = batches;
                entry.ItemData = data;
                entries[e] = entry;
                slot.ItemInventoryEntries = entries;
                slots[s] = slot;
                container.Items = slots;
                inventory.ReplicatedItems = container;
                return removed;
            }
        }
        return 0;
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
                // The extra payouts the multiplier gives are capped by MaximumLootingPayouts, left alone until 0.4.2: "caem
                // poucos itens" with the multiplier at ×10. The cap is raised to the factor.
                float payouts = lootSet.MaximumLootingPayouts.BaseValue;
                if (loot > 1 && payouts < UKismetMathLibrary.FCeil(loot)
                    && !Change(Unreal.ClassOf<UATR_Loot>(), "MaximumLootingPayouts", payouts, UKismetMathLibrary.FCeil(loot))) confirmed = false;
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
        combatStatus = "Combate aplicado: o jogo confirmou os atributos.";
        return combatStatus + " Recargas que já estavam correndo terminam no tempo normal.";
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
    /// Raises the hero to a level the game's own way: XP up to each level's requirement, so its level-up runs.
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
        // The level itself is never written: the game's level-up did every raise tested (69→70, 78→100) and a level written
        // by hand never ran in a test. When XP didn't raise it, the XP goes back (a late level-up would add one more).
        if (!byXp) SetBase(asc, Unreal.ClassOf<UATR_XP>(), "XP", startXp);
        Diag($"hero level {start}->{level} target={target} max={maximum} byXp={byXp} xp={xp} next={next}");
        if (!byXp) return "O jogo não subiu o nível; nada foi gravado.";
        if (level != target) return $"O jogo subiu o herói até o nível {level}; pedido {target}.";
        return $"O jogo subiu o herói para o nível {level} pela experiência.";
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
        return now == target ? $"{VendorName(vendor)} agora no nível {now} (antes {level})."
            : $"O jogo manteve o {VendorName(vendor)} no nível {now}; pedido {target}.";
    }

    const string VillageMerchantTag = "SW.NPC.VillageMerchant";

    static float MerchantCharges(UAbilitySystemComponent asc, out float max)
    {
        max = -1;
        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_MerchantInfo info)
            {
                max = info.VillageMerchantRefreshChargesMax.CurrentValue;
                return info.VillageMerchantRefreshCharges.CurrentValue;
            }
        return -1;
    }

    static string CostText(FCurrencyBundle bundle)
    {
        string text = "";
        foreach (var entry in bundle.Currencies)
            text += (entry.Key == ECurrency.Emerald ? "esmeraldas" : entry.Key == ECurrency.SpringStone ? "fragmentos" : "pontos") + "=" + entry.Value + " ";
        return text == "" ? "grátis" : text;
    }

    /// <summary>
    /// Renews the Village Merchant's stock the game's own way (what holding R in its shop does), from anywhere. Filling
    /// the hero's refresh charges alone did nothing (0.3.6: they read 3 of 3 right after a refresh at the shop), so the
    /// charges are topped up and then the game's refresh is asked for.
    /// </summary>
    public string RefreshMerchantStock()
    {
        if (!CanWrite(out var reason)) return reason;
        var player = World.Player(context!);
        var asc = AbilitySystem();
        if (player == null || asc == null) return "Personagem indisponível.";
        float before = MerchantCharges(asc, out var max);
        if (before < 0) return "Os dados do Mercador não estão no herói (atributo ausente).";
        if (before < max) SetBase(asc, Unreal.ClassOf<UATR_MerchantInfo>(), "VillageMerchantRefreshCharges", max);
        var merchant = new FGameplayTag { TagName = VillageMerchantTag };
        int emeraldsBefore = UKismetStringLibrary.Conv_StringToInt(ResourceAmount("emeralds"));
        string cost = CostText(UGA_RefreshMerchantInventory.GetRefreshCost(player, merchant));
        string blockers = "";
        foreach (var why in UGA_RefreshMerchantInventory.CanRefresh(player, merchant))
            blockers += why == EGA_RefreshMerchantInventory_FailReason.Cost ? "faltam esmeraldas; " : "o jogo não aceitou o pedido; ";
        bool done = blockers == "" && UGA_RefreshMerchantInventory.DoRefreshMerchantInventory(player, merchant);
        float after = MerchantCharges(asc, out max);
        int emeraldsAfter = UKismetStringLibrary.Conv_StringToInt(ResourceAmount("emeralds"));
        Diag($"merchant refresh charges {before}->{after} max={max} cost={cost} blockers={blockers} done={done} emeralds {emeraldsBefore}->{emeraldsAfter}");
        if (blockers != "") return "O jogo recusou a renovação: " + blockers;
        if (!done) return "O jogo não renovou a loja do Mercador.";
        string paid = emeraldsAfter < emeraldsBefore ? $"custou {emeraldsBefore - emeraldsAfter} esmeraldas" : "de graça";
        return $"Loja do Mercador renovada ({paid}). Fale com ele para ver os itens novos.";
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
        Diag($"soul storm start area={area.TagName} seconds={SoulStormSeconds} game={StormDuration(area)}");
        BeginStormWatch(area);
        return "Tempestade de Almas pedida" + (IsTag(area) ? $" na área {area.TagName}" : "")
            + $" por {Short(SoulStormSeconds / 60)} min. Experimental: o jogo pode recusar fora das áreas que têm tempestade.";
    }

    // 0.4.1 in game: 300 s read as about 4 minutes (the storm's clock is real time and runs while the menu pauses the game).
    const float SoulStormSeconds = 900;

    public string EndSoulStorm()
    {
        if (!CanWrite(out var reason)) return reason;
        var storms = USubsystemBlueprintLibrary.GetWorldSubsystem(context!, Unreal.ClassOf<USWSoulStormScheduleSubsystem>()) as USWSoulStormScheduleSubsystem;
        var area = CurrentArea();
        if (storms == null || !IsTag(area)) return "Área atual desconhecida; não há tempestade para encerrar.";
        storms.EndSoulStorm(area);
        stormWatchUntil = 0;
        Diag($"soul storm end area={area.TagName}");
        return $"Fim da Tempestade de Almas pedido na área {area.TagName}.";
    }

    // The duration the game recorded for the area's storm, in seconds (-1 when it has none).
    float StormDuration(FGameplayTag area)
    {
        foreach (var actor in World.FindAll(context!, Unreal.ClassOf<ASWWorldActor>()))
            foreach (var data in (actor as ASWWorldActor)!.GetReplicatedSoulStormData())
                if (data.AffectedArea.TagName.ToString() == area.TagName.ToString()) return data.Duration;
        return -1;
    }

    // The storm the menu started: its area and how long the menu follows it (to note when it ends). Moving each new generator
    // near the hero (0.4.2) was taken out at the user's request: in game it "bugged" (one went from 6 to 23 m away, another
    // was lifted 5 m); IR AO GERADOR stays.
    FGameplayTag stormArea;
    double stormStarted;
    double stormWatchUntil;
    double nextStormScan;
    bool stormDurationLogged;
    bool stormOverLogged;

    void BeginStormWatch(FGameplayTag area)
    {
        stormArea = area;
        stormStarted = World.RealTime(context!);
        stormWatchUntil = stormStarted + SoulStormSeconds + 60;
        nextStormScan = stormStarted + 2;
        stormDurationLogged = false;
        stormOverLogged = false;
    }

    void TrackStorm()
    {
        var now = World.RealTime(context!);
        if (now < nextStormScan) return;
        nextStormScan = now + 2;
        if (!stormDurationLogged)
        {
            stormDurationLogged = true;
            Diag($"soul storm game duration={StormDuration(stormArea)} requested={SoulStormSeconds}");
        }
        // HasSoulStormOngoing read false 6 s into a storm that went on (0.4.2 in game); the game's own record is used instead.
        if (!stormOverLogged && IsTag(stormArea) && StormDuration(stormArea) < 0)
        {
            stormOverLogged = true;
            // The reward chest should appear about now: the chests near the hero are named so their drops can be told apart.
            string near = "";
            var player = World.Player(context!);
            if (player != null)
                foreach (var chest in World.FindAll(context!, Unreal.ClassOf<ALootActor>()))
                    if (chest.GetDistanceTo(player) <= 4000) near += $"{ChestText(chest)}={chest.GetDistanceTo(player) / 100:0}m ";
            Diag($"soul storm over after {now - stormStarted:0} s chests near: {near}");
        }
    }

    // Teleports the actor a distance from the centre, first at the given angle and then around it, nearer and then farther,
    // on the navmesh floor plus the lift: in 0.4.2 the game refused the same blocked spot three times and took one from
    // another side.
    bool TeleportAround(AActor mover, FVector center, double toward, double distance, double lift, out FVector spot, out int tries)
    {
        var extent = new FVector { X = 400, Y = 400, Z = 2000 };
        spot = center;
        tries = 0;
        for (int ring = 0; ring < 2; ring++)
            for (int step = 0; step < 8; step++)
            {
                // 0, -45, +45, -90, +90, -135, +135, 180 degrees from the given side.
                double angle = toward + (step % 2 == 0 ? 1 : -1) * ((step + 1) / 2) * 45;
                double stop = ring == 0 ? distance : distance * 1.5;
                spot = new FVector { X = center.X + UKismetMathLibrary.DegCos(angle) * stop, Y = center.Y + UKismetMathLibrary.DegSin(angle) * stop, Z = center.Z };
                if (UNavigationSystemV1.K2_ProjectPointToNavigation(context!, spot, out var floor, null!, null!, extent)) { spot = floor; spot.Z += lift; }
                tries++;
                if (mover.K2_TeleportTo(spot, mover.K2_GetActorRotation())) return true;
            }
        return false;
    }

    // The storm's generators (Storminators), known by their USWStorminatorBrainComponent, among every actor (they are not
    // pawns).
    List<AActor> StormGenerators()
    {
        var result = new List<AActor>();
        foreach (var actor in World.FindAll(context!, Unreal.ClassOf<AActor>()))
            if (actor.GetComponentByClass(Unreal.ClassOf<USWStorminatorBrainComponent>()) != null) result.Add(actor);
        return result;
    }

    /// <summary>
    /// Takes the hero to the nearest storm generator not already beside them (0.4.1 in game: the game places them far away).
    /// The game keeps one at a time and makes the next once it is defeated (0.4.2 in game: Medium, Easy, then Hard). The hero
    /// lands a few metres short of it, on the navmesh floor. It only finds the generator within about 40 m, once its health
    /// bar shows (user's test, 0.4.4).
    /// </summary>
    public string GoToGenerator()
    {
        if (!CanWrite(out var reason)) return reason;
        var player = World.Player(context!)!;
        AActor? target = null;
        float best = 0;
        int count = 0;
        string seen = "";
        foreach (var actor in StormGenerators())
        {
            if (!UKismetSystemLibrary.IsValid(actor)) continue;
            count++;
            float distance = actor.GetDistanceTo(player);
            if (count <= 6) seen += $"{UKismetSystemLibrary.GetDisplayName(actor)}={distance / 100:0}m ";
            if (distance <= GeneratorNear) continue;
            if (target == null || distance < best) { target = actor; best = distance; }
        }
        Diag($"storm generators={count} {seen}");
        if (count == 0) return "Nenhum gerador por perto. Chegue a menos de 40 m do gerador ativo, até aparecer a barra de vida dele, e tente de novo. O jogo cria um por vez: o próximo aparece alguns segundos depois que o atual é derrotado.";
        if (target == null) return "O herói já está junto do gerador ativo. Derrote-o; o próximo aparece depois.";
        var hero = player.K2_GetActorLocation();
        var at = target.K2_GetActorLocation();
        // Stop short of the generator, first on the side the hero comes from.
        bool moved = TeleportAround(player, at, UKismetMathLibrary.DegAtan2(hero.Y - at.Y, hero.X - at.X), GeneratorStop, SpawnLift, out var spot, out var tries);
        Diag($"storm generator teleport to {UKismetSystemLibrary.GetDisplayName(target)} dist={best / 100:0} m tries={tries} moved={moved} at {spot.X:0},{spot.Y:0},{spot.Z:0}");
        return moved ? $"Herói levado ao gerador ({Short(best / 100)} m)."
            : $"O jogo recusou o teleporte nos {tries} pontos em volta do gerador.";
    }

    const double GeneratorStop = 700;
    const float GeneratorNear = 2000;

    // ---------------------------------------------------------------- Storm chests (0.4.3)

    List<AActor> chestsSeen;
    double nextChestScan;
    double stormSeenUntil;

    // The Soul Storm's chests (SW.LootActor.StorminatorChest.Easy after each generator, .Final.<Low|…> at the end) ignore the
    // hero's loot options, and they aren't LootActors to FindAll (0.4.3 found none, not even beside the reward). They are
    // found by their type among all actors, during a storm and for 3 minutes after it, and each new one gets the hero's loot
    // values on its own ability system, in case its loot table reads the chest's attributes (experimental).
    void BoostStormChests()
    {
        if (!combatActive || context == null || combatAsc == null || !(On("loot") || On("drops") || On("duplicate"))) return;
        var now = World.RealTime(context);
        if (now < nextChestScan) return;
        nextChestScan = now + 2;
        if (StormActive()) stormSeenUntil = now + 180;
        if (now > stormSeenUntil) return;
        foreach (var actor in World.FindAll(context, Unreal.ClassOf<AActor>()))
        {
            var type = USWTypeComponent.GetTypeTagFromActor(actor, true).TagName.ToString();
            if (!type.Contains("StorminatorChest") || chestsSeen.Contains(actor)) continue;
            chestsSeen.Add(actor);
            var chest = actor as ALootActor;
            if (chest != null && chest.bLootUnlocked) { Diag($"storm chest {UKismetSystemLibrary.GetDisplayName(actor)}/{type} already open"); continue; }
            var asc = chest != null ? chest.ASC : UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(actor);
            string sets = "";
            bool hasLoot = false;
            if (asc != null)
                foreach (var set in asc.SpawnedAttributes)
                {
                    sets += UKismetSystemLibrary.GetClassDisplayName(UGameplayStatics.GetObjectClass(set)) + " ";
                    if (set is UATR_Loot) hasLoot = true;
                }
            string applied = "";
            if (asc != null && hasLoot)
                foreach (var name in new List<string> { "LootingMultiplier", "MaximumLootingPayouts", "DropChanceIncrease", "DropDuplicationChance" })
                {
                    var attribute = USWCoreGASUtil.GetAttributeChecked(Unreal.ClassOf<UATR_Loot>(), name);
                    float hero = UAbilitySystemBlueprintLibrary.GetFloatAttributeBaseFromAbilitySystemComponent(combatAsc, attribute, out var heroFound);
                    if (!heroFound) continue;
                    USWCoreGASUtil.SetNumericAttributeBase(asc, attribute, hero);
                    float read = UAbilitySystemBlueprintLibrary.GetFloatAttributeBaseFromAbilitySystemComponent(asc, attribute, out var readFound);
                    applied += $"{name}={(readFound ? read : -1)} ";
                }
            Diag($"storm chest {UKismetSystemLibrary.GetDisplayName(actor)}/{type} class={GameUI.ClassPath(actor)} lootactor={(chest != null)} asc={(asc != null)} sets={sets}loot={(hasLoot ? "sim" : "não")} applied={applied}");
        }
        for (int i = chestsSeen.Count - 1; i >= 0; i--)
            if (!UKismetSystemLibrary.IsValid(chestsSeen[i])) chestsSeen.RemoveAt(i);
    }

    // A Soul Storm is going on in the area (the world actor replicates its data while it lasts).
    bool StormActive()
    {
        foreach (var actor in World.FindAll(context!, Unreal.ClassOf<ASWWorldActor>()))
            foreach (var data in (actor as ASWWorldActor)!.GetReplicatedSoulStormData())
                return true;
        return false;
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

    /// <summary>A station's name as the game's map shows it, in the game's language; empty when the game has none.</summary>
    public static string StationName(string tag) =>
        UKismetTextLibrary.Conv_TextToString(USWUIConfigLibrary.GetMinecartStationName(GameUI.Tag(tag)));

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
        // Each change applies every option again, and the 300 log lines went in a few clicks (1.0.0 test): a line already
        // logged isn't logged again.
        var line = $"tweak {name} {before}->{actual} requested={value} found={found}";
        if (!tweaksLogged.Contains(line))
        {
            tweaksLogged.Add(line);
            Diag(line);
        }
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
        BoostStormChests();
        CheckCombatDrift();
        ReapplyRemembered();
        // A new level starts at the normal speed.
        if (gameSpeed != 1 && (context == null || World.LevelName(context) != slowLevel)) gameSpeed = 1;
        if (pendingMobs > 0 && context != null) TrackRequestedMobs();
        if (context != null && World.RealTime(context) < watchUntil) WatchSpawnedMobs();
        if (context != null && World.RealTime(context) < stormWatchUntil) TrackStorm();
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
        // The ground scan also counts drops for the other loot options; raising the rarity needs Raridade máxima.
        if (!combatActive || !(On("rarity") || On("loot") || On("drops") || On("duplicate")) || context == null)
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
        if (!On("rarity")) return;
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
        int known = groundKnown.Count, fresh = 0, gear = 0, fromChest = 0;
        string chestName = "";
        // Storm chest items to copy into the inventory (type and rarity), once the scan is done.
        var copyTags = new List<string>();
        var copyRarities = new List<string>();
        foreach (var actor in actors)
        {
            if (groundKnown.Contains(actor)) continue;
            groundKnown.Add(actor);
            fresh++;
            var component = (actor as ASWItemActor)?.GASItemComponent;
            if (component == null) continue;
            var data = component.ItemData;
            // The item names its source: the Soul Storm's reward chest is SW.LootActor.StorminatorChest.<Easy|...>.
            var source = data.DropSource.TagName.ToString();
            bool chestDrop = known > 0 && source.Contains("StorminatorChest");
            if (chestDrop)
            {
                fromChest++;
                chestName = source;
            }
            var rarity = data.RarityTag.TagName.ToString();
            var slot = UInventoryHelperLibrary.GetItemSlotTag(player, data.TypeTag).TagName.ToString();
            if (slot.Contains("Equipment")) gear++;
            if (chestDrop && Rules.CopiesChestDrop(slot))
            {
                copyTags.Add(data.TypeTag.TagName.ToString());
                copyRarities.Add(rarity);
            }
            if (!On("rarity") || !slot.Contains("Equipment") || (rarity != "SW.Rarity.Common" && rarity != "SW.Rarity.Rare")) continue;
            data.RarityTag = new FGameplayTag { TagName = TopRegularRarity };
            component.ItemData = data;
            raisedGround++;
            if (raisedGround <= 5) Diag($"ground loot {data.TypeTag.TagName} {rarity}->{component.ItemData.RarityTag.TagName} actor={UKismetSystemLibrary.GetDisplayName(actor)}");
        }
        // How many items fall at once (equipment among them, and how many from a chest), to compare the loot options on and
        // off ("caem poucos itens", 0.4.2); the first look counts what was already lying around.
        CopyChestLoot(copyTags, copyRarities);
        if (fresh > 0 && known > 0)
            Diag($"ground drops +{fresh} equip={gear}" + (fromChest > 0 ? $" chest={fromChest} {chestName}" : "") + $" options={LootOptionsText()}");
        // Forget the drops that were picked up or despawned.
        for (int i = groundKnown.Count - 1; i >= 0; i--)
            if (!UKismetSystemLibrary.IsValid(groundKnown[i])) groundKnown.RemoveAt(i);
    }

    // The storm chests have no loot attributes of their own (0.4.3: no attribute set on their ability system), so Saque ×N
    // can't reach them: each item a storm chest drops gets N−1 more of the same type and rarity straight in the inventory,
    // through the menu's own item creation (catalog, capacity and rarity checks). Their effects are rolled anew.
    int chestCopies;

    void CopyChestLoot(List<string> tags, List<string> rarities)
    {
        if (tags.Count == 0 || !On("loot")) return;
        int copies = Rules.ChestCopies(combatOptions[Rules.CombatKeys().IndexOf("loot")]);
        if (copies < 1) return;
        for (int i = 0; i < tags.Count; i++)
        {
            var result = SpawnItem(tags[i], copies, rarities[i]);
            Diag($"chest copies {tags[i]} {rarities[i]} x{copies} -> {result}");
            if (result.StartsWith("Inventário confirmou")) chestCopies += copies;
        }
        combatStatus = $"Saque do baú da tempestade: {chestCopies} cópia(s) criadas no inventário.";
    }

    static string ChestText(AActor chest) => $"{UKismetSystemLibrary.GetDisplayName(chest)}/{USWTypeComponent.GetTypeTagFromActor(chest, true).TagName}";

    // The loot options in force, for the drop counts: loot factor, then d(rops) and x (duplicate) when on.
    string LootOptionsText()
    {
        var keys = Rules.CombatKeys();
        return $"x{Short(combatOptions[keys.IndexOf("loot")])}" + (On("drops") ? "+d" : "") + (On("duplicate") ? "+x" : "");
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
