using System.Collections.Generic;

namespace DungeonsMenu;

public static class Rules
{
    public const string ExecutableHash = "3842102bb05fc532b22e97992d61c2248d115be80ff752ff9b6843bf4acd7005";
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

    public static bool CombatValue(string key, float value)
    {
        // NaN fails every comparison below.
        if (!CombatKeys().Contains(key)) return false;
        if (IsSwitch(key)) return value == 0 || value == 1;
        return value >= CombatMin(key) && value <= CombatMax(key);
    }
}
