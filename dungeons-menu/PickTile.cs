using UE.UMG;

namespace DungeonsMenu;

/// <summary>
/// A clickable box that remembers what it stands for. NeoRune lambdas can't capture locals, so each box keeps its own
/// group and id and hands them to the menu when clicked.
/// </summary>
public class PickTile : UButton
{
    Menu? menu;
    int group;
    string id;

    public void Setup(Menu owner, int pickGroup, string pickId)
    {
        menu = owner;
        group = pickGroup;
        id = pickId;
        OnClicked += Clicked;
    }

    void Clicked()
    {
        if (menu != null) menu.Pick(group, id);
    }
}
