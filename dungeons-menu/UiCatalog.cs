using System.Collections.Generic;
using NeoRune;
using UE.AssetRegistry;
using UE.Engine;

namespace DungeonsMenu;

/// <summary>Storage for <see cref="UiCatalog"/>: SaveGames/NeoRune_DungeonsMenu_UiCatalog.sav.</summary>
public class UiCatalogData : USaveGame
{
    public List<string> Entries;
}

/// <summary>
/// The game's UI assets, listed once from the asset registry: the .pak is encrypted, so this is how the menu finds the
/// game's own frames, panels, tab art and icons by path. Only metadata is read; nothing is loaded. Read the list with
/// work/read-ui-catalog.py.
/// </summary>
public static class UiCatalog
{
    const string Slot = "NeoRune_DungeonsMenu_UiCatalog";

    public static void DumpOnce()
    {
        if (UGameplayStatics.DoesSaveGameExist(Slot, 0)) return;
        var registry = UAssetRegistryHelpers.GetAssetRegistry();
        var data = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<UiCatalogData>()) as UiCatalogData;
        if (registry == null || data == null) return;
        var lines = new List<string>();
        string counts = "";
        foreach (var root in new List<string> { "/OreUI", "/SpicewoodUI", "/Game/Spicewood/UI" })
        {
            int count = 0;
            if (registry.GetAssetsByPath(root, out var assets, true, true))
                foreach (var asset in assets)
                {
                    lines.Add(asset.PackagePath.ToString() + "/" + asset.AssetName.ToString() + " (" + asset.AssetClassPath.AssetName.ToString() + ")");
                    count++;
                }
            counts += root + "=" + count + " ";
        }
        data.Entries = lines;
        UGameplayStatics.SaveGameToSlot(data, Slot, 0);
        MenuLog.Write("[diag] ui catalog " + counts);
    }
}
