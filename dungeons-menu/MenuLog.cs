using NeoRune;
using UE.Engine;

namespace DungeonsMenu;

// Same format as NeoRune's Log (read by work/read-menu-log.py), but it keeps only the newest lines (Log.Write appends
// forever and every write re-saves the whole file), and in its own slot: Better Blueprint Loader deletes every
// "NeoRune_<Mod>" slot each time it starts the mods in a level, which wiped the log on every trip to the main menu (0.3.2).
public static class MenuLog
{
    const int Keep = 300;
    const string Slot = "DungeonsMenu_Log";

    public static void Write(string line)
    {
        var data = UGameplayStatics.LoadGameFromSlot(Slot, 0) as NeoRuneLogData;
        if (data == null) data = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<NeoRuneLogData>()) as NeoRuneLogData;
        if (data == null) return;
        var lines = data.Lines;
        while (lines.Count >= Keep) lines.RemoveAt(0);
        lines.Add(line);
        data.Lines = lines;
        UGameplayStatics.SaveGameToSlot(data, Slot, 0);
    }
}
