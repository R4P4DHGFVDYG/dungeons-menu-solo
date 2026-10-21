using NeoRune;
using UE.CoreUObject;
using UE.CrossplayOSS;
using UE.Engine;
using UE.GameplayAbilities;
using UE.SpicewoodGAS;

namespace DungeonsMenu;

public static class SessionGuard
{
    public static bool CanWrite(UObject context, out string reason)
    {
        reason = "";
        // The launcher hashes the executable on every launch. No persisted permission survives a game update.
        if (!UKismetSystemLibrary.ParseParamValue(UKismetSystemLibrary.GetCommandLine(), "DungeonsMenu.compat=", out var stamp)
            || stamp != Rules.ExecutableHash)
        {
            reason = "Somente leitura: abra pelo Iniciar-Mod.ps1 para verificar a versão e criar o backup.";
            return false;
        }
        if (UOnlineUtil.IsOnlineGameMode())
        {
            reason = "Alterações disponíveis apenas em partida solo/offline.";
            return false;
        }
        var player = World.Player(context);
        if (player == null || !UKismetSystemLibrary.IsValid(player))
        {
            reason = "Entre em uma partida com um personagem antes de alterar valores.";
            return false;
        }
        if (UGameplayStatics.GetPlayerPawn(context, 1) != null)
        {
            reason = "Cooperativo local não é suportado; use um único jogador.";
            return false;
        }
        var asc = UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(player);
        if (asc != null && World.LevelName(context) != "")
            foreach (var set in asc.SpawnedAttributes)
                if (set is UATR_Health health && Rules.LivingHealth(health.Health.CurrentValue, health.HealthMax.CurrentValue))
                    return true;
        reason = "Aguarde um personagem vivo e o mapa terminar de carregar.";
        return false;
    }
}
