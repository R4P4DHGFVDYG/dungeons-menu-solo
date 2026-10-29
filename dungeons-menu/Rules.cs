using System.Collections.Generic;

namespace DungeonsMenu;

public static class Rules
{
    public static bool Amount(int value) => value >= 0 && value <= 1000000;
    public static bool Quantity(int value) => value >= 1 && value <= 20;
    public static bool Tier(int value) => value >= 1 && value <= 100;
    public static bool LivingHealth(float health, float maximum) => health > 0 && maximum > 0 && health <= maximum && maximum <= float.MaxValue;
    public static bool Multipliers(float damage, float speed) => CombatValue("damage", damage) && CombatValue("speed", speed);

    /// <summary>
    /// The temporary options (combat, then the hero tab's XP and loot), in the order the menu saves them: new ones go at
    /// the end so saved choices keep their meaning. Switches are 0 or 1; the others are factors on the hero's own value,
    /// 1 meaning unchanged.
    /// </summary>
    public static List<string> CombatKeys() => new List<string> { "god", "cooldown", "ammo", "souls", "rolls", "crit", "damage", "attack", "critdamage", "speed", "jump", "xp", "loot", "rarity", "drops", "duplicate" };

    public static bool IsSwitch(string key) => key == "god" || key == "cooldown" || key == "ammo" || key == "souls" || key == "rolls" || key == "crit"
        || key == "rarity" || key == "drops" || key == "duplicate";

    /// <summary>The value that leaves the game alone: switches off, factors at 1.</summary>
    public static float Neutral(string key) => IsSwitch(key) ? 0 : 1;

    public static float CombatMin(string key) => IsSwitch(key) ? 0 : key == "speed" || key == "attack" ? 0.25f : key == "jump" ? 0.5f : 1;

    public static float CombatMax(string key) => IsSwitch(key) ? 1 : key == "damage" ? 100 : key == "speed" || key == "jump" ? 5 : key == "attack" ? 4
        : key == "critdamage" || key == "xp" ? 20 : key == "loot" ? 10 : 1;

    /// <summary>A hero level the menu may set: 1 up to the game's own maximum.</summary>
    public static bool HeroLevel(int level, int maximum) => level >= 1 && maximum >= 1 && level <= maximum;

    /// <summary>An hour of the game's day (0 up to the length of its day).</summary>
    public static bool Hour(float hour, float day) => day > 0 && hour >= 0 && hour <= day;

    /// <summary>The whole game's speed: a quarter to double.</summary>
    public static bool GameSpeed(float factor) => factor >= 0.25f && factor <= 2;

    /// <summary>The radius, in meters, of the World tab's actions on nearby mobs.</summary>
    public static bool Radius(float meters) => meters >= 5 && meters <= 50;

    /// <summary>A village vendor level: 0 up to the game's maximum for that vendor.</summary>
    public static bool VendorLevel(int level, int maximum) => level >= 0 && maximum >= 0 && level <= maximum;

    /// <summary>The highest enchantment level the free mode writes, past the game's normal maximum.</summary>
    public const int MaxFreeLevel = 20;

    /// <summary>The highest effect level the menu writes on an item (the game's own rolls go up to 3).</summary>
    public const int MaxEffectLevel = 10;

    /// <summary>A free-mode enchantment level: 1 up to <see cref="MaxFreeLevel"/>.</summary>
    public static bool FreeEnchantLevel(int level) => level >= 1 && level <= MaxFreeLevel;

    /// <summary>An effect level the menu writes: 1 up to <see cref="MaxEffectLevel"/>.</summary>
    public static bool EffectLevel(int level) => level >= 1 && level <= MaxEffectLevel;

    /// <summary>A talisman level: 1 up to the levels the game gives that talisman.</summary>
    public static bool TalismanLevel(int level, int levels) => level >= 1 && level <= levels;

    /// <summary>
    /// How many more of each item a Soul Storm chest drops go straight to the inventory with Saque ×N: N−1 for the nearest
    /// whole factor, none for a factor the menu doesn't accept.
    /// </summary>
    public static int ChestCopies(float loot) => CombatValue("loot", loot) ? (int)(loot + 0.5f) - 1 : 0;

    /// <summary>Only equipment from a storm chest is copied: its food (apple, melon) isn't in the item catalog.</summary>
    public static bool CopiesChestDrop(string slot) => slot.Contains("Equipment");

    /// <summary>The Items tab's category of an item slot tag.</summary>
    public static string ItemCategory(string slot) =>
        slot.Contains("Melee") ? "melee" : slot.Contains("Ranged") ? "ranged" : slot.Contains("Armor") ? "armor"
        : slot.Contains("Artifact") ? "artifact" : slot.Contains("Talisman") ? "talisman" : slot.Contains("EnchantmentBook") ? "book" : "other";

    public static bool CombatValue(string key, float value)
    {
        // NaN fails every comparison below.
        if (!CombatKeys().Contains(key)) return false;
        if (IsSwitch(key)) return value == 0 || value == 1;
        return value >= CombatMin(key) && value <= CombatMax(key);
    }
}
