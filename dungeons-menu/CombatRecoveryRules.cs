namespace DungeonsMenu;

public static class CombatRecoveryRules
{
    public static bool Restored(bool found, float actual, float saved) => found && actual == saved;
    public static bool CanRestore(bool found, float actual, float applied) => found && actual == applied;
    public static bool CanReleaseLock(bool hasPlayer, bool hasAsc, bool samePlayer, bool sameAsc)
        => hasPlayer && hasAsc && !samePlayer && !sameAsc;
}
