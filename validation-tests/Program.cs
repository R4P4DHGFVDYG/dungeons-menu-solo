using DungeonsMenu;

var checks = new (string Name, bool Pass)[] {
    ("Moeda negativa", !Rules.Amount(-1)),
    ("Moeda zero", Rules.Amount(0)),
    ("Moeda máxima", Rules.Amount(1000000)),
    ("Overflow de moeda", !Rules.Amount(int.MaxValue)),
    ("Quantidade vazia", !Rules.Quantity(0)),
    ("Lote máximo", Rules.Quantity(20)),
    ("Lote excessivo", !Rules.Quantity(21)),
    ("Tier inválido", !Rules.Tier(0)),
    ("Tier limitado", Rules.Tier(100) && !Rules.Tier(101)),
    ("Multiplicadores normais", Rules.Multipliers(1, 1)),
    ("Multiplicadores inválidos", !Rules.Multipliers(0, 1) && !Rules.Multipliers(1, 6)),
    ("NaN rejeitado", !Rules.Multipliers(float.NaN, 1) && !Rules.Multipliers(1, float.NaN)),
    ("Infinito rejeitado", !Rules.Multipliers(float.PositiveInfinity, 1)),
    ("Opções de combate desligadas são válidas", Rules.CombatKeys().TrueForAll(key => Rules.CombatValue(key, Rules.Neutral(key)))),
    ("Chave ligada aceita 1", Rules.CombatValue("ammo", 1) && Rules.CombatValue("god", 1)),
    ("Chave não aceita valor parcial", !Rules.CombatValue("souls", 0.5f) && !Rules.CombatValue("crit", 2)),
    ("Velocidade de ataque limitada", Rules.CombatValue("attack", 4) && !Rules.CombatValue("attack", 4.25f) && !Rules.CombatValue("attack", 0.2f)),
    ("Pulo limitado", Rules.CombatValue("jump", 0.5f) && !Rules.CombatValue("jump", 0.25f) && !Rules.CombatValue("jump", 6)),
    ("Dano crítico limitado", Rules.CombatValue("critdamage", 20) && !Rules.CombatValue("critdamage", 0.5f)),
    ("Opção desconhecida rejeitada", !Rules.CombatValue("fly", 1)),
    ("NaN rejeitado nas opções", !Rules.CombatValue("damage", float.NaN) && !Rules.CombatValue("god", float.NaN)),
    ("Infinito rejeitado nas opções", !Rules.CombatValue("speed", float.PositiveInfinity)),
    ("Opções antigas ficam no começo", Rules.CombatKeys().IndexOf("jump") == 10 && Rules.CombatKeys().IndexOf("xp") == 11),
    ("Saque e XP limitados", Rules.CombatValue("loot", 10) && !Rules.CombatValue("loot", 11) && Rules.CombatValue("xp", 20) && !Rules.CombatValue("xp", 0.5f)),
    ("Raridade é chave", Rules.CombatValue("rarity", 1) && !Rules.CombatValue("rarity", 3)),
    ("Nível do herói até o máximo", Rules.HeroLevel(1, 30) && Rules.HeroLevel(30, 30) && !Rules.HeroLevel(31, 30) && !Rules.HeroLevel(0, 30)),
    ("Nível sem máximo conhecido", !Rules.HeroLevel(1, 0)),
    ("Vendedor de 0 ao máximo", Rules.VendorLevel(0, 5) && Rules.VendorLevel(5, 5) && !Rules.VendorLevel(6, 5) && !Rules.VendorLevel(-1, 5)),
    ("Hora dentro do dia", Rules.Hour(0, 24) && Rules.Hour(24, 24) && !Rules.Hour(25, 24) && !Rules.Hour(-1, 24)),
    ("Hora sem ciclo de dia", !Rules.Hour(6, 0)),
    ("Velocidade do jogo limitada", Rules.GameSpeed(0.25f) && Rules.GameSpeed(2) && !Rules.GameSpeed(0) && !Rules.GameSpeed(3) && !Rules.GameSpeed(float.NaN)),
    ("Raio limitado", Rules.Radius(5) && Rules.Radius(50) && !Rules.Radius(4) && !Rules.Radius(51)),
    ("Personagem vivo", Rules.LivingHealth(1, 100)),
    ("Personagem morto", !Rules.LivingHealth(0, 100) && !Rules.LivingHealth(-1, 100)),
    ("Saúde não inicializada", !Rules.LivingHealth(1, 0)),
    ("Saúde inválida", !Rules.LivingHealth(float.NaN, 100) && !Rules.LivingHealth(float.PositiveInfinity, float.PositiveInfinity)),
    ("Reversão já confirmada dispensa escrita", CombatRecoveryRules.Restored(true, 2, 2)),
    ("Releitura ausente não confirma reversão", !CombatRecoveryRules.Restored(false, 2, 2)),
    ("Valor residual não confirma reversão", !CombatRecoveryRules.Restored(true, 6, 2)),
    ("Base aplicada permite reversão", CombatRecoveryRules.CanRestore(true, 6, 6)),
    ("Mudança externa impede sobrescrita", !CombatRecoveryRules.CanRestore(true, 8, 6)),
    ("Leitura ausente impede sobrescrita", !CombatRecoveryRules.CanRestore(false, 6, 6)),
    ("NaN impede sobrescrita", !CombatRecoveryRules.CanRestore(true, float.NaN, float.NaN)),
    ("Mesma sessão mantém bloqueio", !CombatRecoveryRules.CanReleaseLock(true, true, true, true)),
    ("Mesmo pawn mantém bloqueio", !CombatRecoveryRules.CanReleaseLock(true, true, true, false)),
    ("Mesmo ASC mantém bloqueio", !CombatRecoveryRules.CanReleaseLock(true, true, false, true)),
    ("Transição sem pawn mantém bloqueio", !CombatRecoveryRules.CanReleaseLock(false, true, false, false)),
    ("Transição sem ASC mantém bloqueio", !CombatRecoveryRules.CanReleaseLock(true, false, false, false)),
    ("Recarga completa libera bloqueio", CombatRecoveryRules.CanReleaseLock(true, true, false, false)),
    ("Categoria corpo a corpo", Rules.ItemCategory("SW.ItemSlot.Equipment.MeleeWeapon") == "melee"),
    ("Categoria à distância", Rules.ItemCategory("SW.ItemSlot.Equipment.RangedWeapon") == "ranged"),
    ("Categoria armadura", Rules.ItemCategory("SW.ItemSlot.Equipment.Armor.Chest") == "armor"),
    ("Categoria artefato", Rules.ItemCategory("SW.ItemSlot.Equipment.Artifact.Slot1") == "artifact"),
    ("Categoria talismã", Rules.ItemCategory("SW.ItemSlot.Talisman") == "talisman"),
    ("Categoria livro", Rules.ItemCategory("SW.ItemSlot.EnchantmentBook") == "book"),
    ("Outra categoria", Rules.ItemCategory("SW.ItemSlot.Consumable") == "other"),
    ("Baú sem saque extra não copia", Rules.ChestCopies(1) == 0),
    ("Baú com saque ×4 copia 3", Rules.ChestCopies(4) == 3),
    ("Baú com saque ×9 copia 8", Rules.ChestCopies(9) == 8),
    ("Saque máximo copia 9", Rules.ChestCopies(10) == 9),
    ("Saque inválido não copia", Rules.ChestCopies(float.NaN) == 0 && Rules.ChestCopies(11) == 0 && Rules.ChestCopies(0.5f) == 0),
    ("Só equipamento do baú é copiado", Rules.CopiesChestDrop("SW.ItemSlot.Equipment.MeleeWeapon") && !Rules.CopiesChestDrop("SW.ItemSlot.Food")),
    ("Efeito de 1 a 10", Rules.EffectLevel(1) && Rules.EffectLevel(10) && !Rules.EffectLevel(0) && !Rules.EffectLevel(11)),
    ("Talismã dentro dos níveis do jogo", Rules.TalismanLevel(1, 3) && Rules.TalismanLevel(3, 3) && !Rules.TalismanLevel(4, 3) && !Rules.TalismanLevel(0, 3)),
    ("Encantamento livre até 20", Rules.FreeEnchantLevel(1) && Rules.FreeEnchantLevel(20) && !Rules.FreeEnchantLevel(21) && !Rules.FreeEnchantLevel(0)),
    ("Versão do menu igual à do csproj", ModVersion.Version == ProjectVersion()),
};
foreach (var check in checks) if (!check.Pass) throw new Exception(check.Name);
Console.WriteLine($"{checks.Length} verificações de limites passaram.");

// The <Version> of dungeons-menu/DungeonsMenu.csproj, found from the build folder up (work/, the repository or the ZIP's source/).
static string ProjectVersion()
{
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
    {
        var path = Path.Combine(folder.FullName, "dungeons-menu", "DungeonsMenu.csproj");
        if (!File.Exists(path)) continue;
        var text = File.ReadAllText(path);
        int start = text.IndexOf("<Version>") + "<Version>".Length, end = text.IndexOf("</Version>");
        return start >= "<Version>".Length && end > start ? text.Substring(start, end - start) : "";
    }
    return "";
}
