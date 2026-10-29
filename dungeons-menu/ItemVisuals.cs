using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.GameplayTags;
using UE.InventorySystem;
using UE.Minimap;
using UE.SlateCore;
using UE.SpicewoodUI;
using UE.UMG;

namespace DungeonsMenu;

/// <summary>
/// The menu's look, taken from the game's own assets, as on its settings and inventory screens: the menu background and
/// tab bar, the CommonUI button styles (tabs, list entries, secondary and confirm buttons, item slots), the settings rows,
/// boxes and switch, the scroll bar, the text styles' fonts and the icons. Only looks are used: the game's own widgets are
/// never made, since their code expects game data behind them. Assets are loaded here, before widgets exist, because
/// loading can let the garbage collector run; everything loaded stays referenced from this object.
/// </summary>
public class ItemVisuals : UObject
{
    const string TextStyles = "/OreUI/UI/Typography/TextStyles/";
    const string SettingsArt = "/OreUI/UI/Button/Role/Settings/";
    const string Icons = "/OreUI/UI/Icons/";
    const string TabStyle = "/OreUI/UI/TabNavigation/ButtonStyle_Tab.ButtonStyle_Tab_C";
    const string ListStyle = "/OreUI/UI/Button/Role/ListEntry/ButtonStyle_ListEntry.ButtonStyle_ListEntry_C";
    const string SecondaryStyle = "/OreUI/UI/Button/Role/Secondary/ButtonStyle_Secondary_KBM.ButtonStyle_Secondary_KBM_C";
    const string PrimaryStyle = "/OreUI/UI/Button/Role/Progressive/ButtonStyle_Progressive_KBM.ButtonStyle_Progressive_KBM_C";
    const string SlotStyle = "/OreUI/UI/Button/SlotFrame/GearSelection/ButtonStyle_GearSlot.ButtonStyle_GearSlot_C";
    const string ScrollBarStyle = "/OreUI/UI/Widget/ScrollBox/BP_SWScrollBarStyle.BP_SWScrollBarStyle_C";

    // Font roles for Label: names and sentences, pixel text for buttons and values, big titles, tabs, section headings.
    public const int Readable = 0;
    public const int PixelUi = 1;
    public const int PixelTitle = 2;
    public const int Nav = 3;
    public const int Section = 4;

    // Button looks: the tab bar's tabs, list entries, secondary buttons (Cancel), confirm buttons (green), item slots.
    public const int LookTab = 0;
    public const int LookList = 1;
    public const int LookSecondary = 2;
    public const int LookPrimary = 3;
    public const int LookSlot = 4;

    UObject? owner;
    bool preloaded;
    List<UObject> loaded;
    ButtonLook? tabLook;
    ButtonLook? listLook;
    ButtonLook? secondaryLook;
    ButtonLook? primaryLook;
    ButtonLook? slotLook;
    bool hasRow;
    FSlateBrush row;
    bool hasRowLit;
    FSlateBrush rowLit;
    bool hasInner;
    FSlateBrush inner;
    FSlateBrush toggleOn;
    FSlateBrush toggleOff;
    FSlateBrush thumb;
    public bool HasToggleArt;
    FSlateBrush backdrop;
    public bool HasBackdrop;
    FSlateBrush navBar;
    public bool HasNavBar;
    public FScrollBarStyle ScrollBar;
    public bool HasScrollBar;
    bool hasTitleFont;
    FSlateFontInfo titleFont;
    bool hasUiFont;
    FSlateFontInfo uiFont;
    bool hasBodyFont;
    FSlateFontInfo bodyFont;
    bool hasNavFont;
    FSlateFontInfo navFont;
    bool hasSectionFont;
    FSlateFontInfo sectionFont;
    public UTexture2D? Corner;
    public UTexture2D? EmeraldIcon;
    public UTexture2D? EchoIcon;
    public UTexture2D? PointsIcon;
    public UTexture2D? SkullIcon;
    List<string> categoryKeys;
    List<UTexture2D> categoryIcons;
    List<string> iconTags;
    List<UTexture2D> iconTextures;
    List<string> missingIcons;
    List<string> titleTags;
    List<string> titleTexts;

    public void Initialize(UObject context)
    {
        owner = context;
        loaded = new List<UObject>();
        categoryKeys = new List<string>();
        categoryIcons = new List<UTexture2D>();
        iconTags = new List<string>();
        iconTextures = new List<UTexture2D>();
        missingIcons = new List<string>();
        titleTags = new List<string>();
        titleTexts = new List<string>();
    }

    /// <summary>The game's art, fonts and icons, once, before the menu opens. Each one is optional.</summary>
    public void Preload()
    {
        if (preloaded) return;
        preloaded = true;
        tabLook = Look(TabStyle);
        listLook = Look(ListStyle);
        secondaryLook = Look(SecondaryStyle);
        primaryLook = Look(PrimaryStyle);
        slotLook = Look(SlotStyle);
        hasRow = Material(SettingsArt, "MI_UI_Settings_EntryBG", out row);
        hasRowLit = Material(SettingsArt, "MI_UI_Settings_EntryBG_Highlighted", out rowLit);
        hasInner = Material(SettingsArt, "MI_UI_Settings_EntryBG_Internal", out inner);
        bool on = Material(SettingsArt, "MI_UI_Settings_On", out toggleOn);
        bool off = Material(SettingsArt, "MI_UI_Settings_Off", out toggleOff);
        bool knob = Material(SettingsArt, "MI_UI_SliderThumb", out thumb);
        HasToggleArt = on && off && knob;
        HasBackdrop = Material("/OreUI/UI/Backgrounds/", "MI_MenuBackground", out backdrop);
        HasNavBar = Material("/OreUI/UI/TabNavigation/", "MI_UI_Navbar_BG_Opaque", out navBar);
        var barClass = Unreal.LoadClass<USWScrollBarStyle>(ScrollBarStyle);
        var bar = barClass != null ? UGameplayStatics.SpawnObject(barClass, this) as USWScrollBarStyle : null;
        if (bar != null)
        {
            loaded.Add(bar);
            ScrollBar = bar.Style;
            HasScrollBar = true;
        }
        hasTitleFont = Font("Style_Header3_Text", out titleFont) || Font("Style_Header4_Text", out titleFont);
        hasUiFont = Font("Style_Button_Text", out uiFont) || Font("Style_Header4_Text", out uiFont);
        hasBodyFont = Font("Style_Body_Text", out bodyFont);
        hasSectionFont = Font("Style_SectionHeader1_Text", out sectionFont);
        if (tabLook != null && tabLook.HasFont)
        {
            navFont = tabLook.Font;
            hasNavFont = true;
        }
        else hasNavFont = Font("Style_NavBarHeader_Text", out navFont);
        Corner = Texture("/SpicewoodUI/Spicewood/UI/Widget/Inventory/PlayerInventory/Slot/T_UI_Slot_Equipped.T_UI_Slot_Equipped");
        EmeraldIcon = Texture(Icons + "Currency/T_Emerald_UI.T_Emerald_UI");
        EchoIcon = Texture(Icons + "Currency/T_UI_SpringStone.T_UI_SpringStone");
        PointsIcon = Texture(Icons + "Currency/T_UI_EnchantmentPoints.T_UI_EnchantmentPoints");
        SkullIcon = Texture(Icons + "Threat/T_UI_Icon_Skull_Threat_Standard.T_UI_Icon_Skull_Threat_Standard");
        Category("all", "T_UI_Icon_Category_All");
        Category("melee", "T_UI_Icon_Category_Melee");
        Category("ranged", "T_UI_Icon_Category_Ranged");
        Category("armor", "T_UI_Icon_Category_Chest");
        Category("artifact", "T_UI_Icon_Category_Potion");
        Category("talisman", "T_UI_Icon_Category_Talisman");
        Category("effects", "T_UI_Icon_Category_Effects");
        Category("book", "T_UI_Icon_Category_Enchantment");
        Category("other", "T_UI_Icon_Category_Stats");
        Category("equipped", "T_UI_Icon_Category_Equipped");
        MenuLog.Write($"[diag] visuals looks tab={Has(tabLook)} list={Has(listLook)} secondary={Has(secondaryLook)} primary={Has(primaryLook)} slot={Has(slotLook)} row={hasRow}/{hasRowLit}/{hasInner} toggle={HasToggleArt} backdrop={HasBackdrop} navbar={HasNavBar} scrollbar={HasScrollBar} fonts={hasTitleFont}/{hasUiFont}/{hasBodyFont}/{hasNavFont}/{hasSectionFont} icons={(EmeraldIcon != null)}/{(EchoIcon != null)}/{(PointsIcon != null)}/{(SkullIcon != null)} categories={categoryIcons.Count} corner={(Corner != null)}");
    }

    static bool Has(ButtonLook? look) => look != null && look.Ok;

    ButtonLook? Look(string path)
    {
        var look = UGameplayStatics.SpawnObject(Unreal.ClassOf<ButtonLook>(), this) as ButtonLook;
        if (look == null) return null;
        loaded.Add(look);
        look.Load(path, loaded);
        return look;
    }

    bool Material(string folder, string name, out FSlateBrush brush)
    {
        brush = new FSlateBrush();
        var asset = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath($"{folder}{name}.{name}")));
        var material = asset as UMaterialInterface;
        if (material == null) return false;
        loaded.Add(material);
        brush = UWidgetBlueprintLibrary.MakeBrushFromMaterial(material, 32, 32);
        return true;
    }

    // A text style's font only: the styles come with fixed sizes, and the menu sizes every text itself.
    bool Font(string style, out FSlateFontInfo font)
    {
        font = new FSlateFontInfo();
        var styleClass = Unreal.LoadClass<UCommonTextStyle>($"{TextStyles}{style}.{style}_C");
        var instance = styleClass != null ? UGameplayStatics.SpawnObject(styleClass, this) as UCommonTextStyle : null;
        if (instance == null) return false;
        instance.GetFont(out var found);
        if (found.FontObject == null) return false;
        loaded.Add(instance);
        font = found;
        return true;
    }

    UTexture2D? Texture(string path)
    {
        var texture = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path))) as UTexture2D;
        if (texture != null) loaded.Add(texture);
        return texture;
    }

    void Category(string key, string name)
    {
        var texture = Texture($"{Icons}Categories/{name}.{name}");
        if (texture == null) return;
        categoryKeys.Add(key);
        categoryIcons.Add(texture);
    }

    /// <summary>An item category's icon (the game's inventory category icons), or null.</summary>
    public UTexture2D? CategoryIcon(string key)
    {
        int at = categoryKeys.IndexOf(key);
        return at >= 0 ? categoryIcons[at] : null;
    }

    /// <summary>A type's icon (item, book, enchantment or mob), or null; loaded once.</summary>
    public UTexture2D? Icon(string tag)
    {
        int at = iconTags.IndexOf(tag);
        if (at >= 0) return iconTextures[at];
        if (owner == null || missingIcons.Contains(tag)) return null;
        var soft = UInventoryHelperLibrary.GetTypeIcon(owner, new FGameplayTag { TagName = tag }, 0);
        var path = UKismetSystemLibrary.Conv_SoftObjectReferenceToString(soft);
        var icon = string.IsNullOrEmpty(path) ? null
            : UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path))) as UTexture2D;
        if (icon == null)
        {
            missingIcons.Add(tag);
            return null;
        }
        iconTags.Add(tag);
        iconTextures.Add(icon);
        return icon;
    }

    /// <summary>
    /// An item effect's icon (SW.Effect.X), the one the game's tooltips show: /OreUI/UI/Icons/Effects/T_UI_Icon_Effect_X,
    /// a few spelled differently; the effects category icon when the game has none. Loaded once.
    /// </summary>
    public UTexture2D? EffectIcon(string tag)
    {
        int at = iconTags.IndexOf(tag);
        if (at >= 0) return iconTextures[at];
        if (missingIcons.Contains(tag)) return CategoryIcon("effects");
        var name = tag.StartsWith("SW.Effect.") ? tag.Substring(10) : tag;
        name = name == "EagleEye" ? "Eagle_Eye" : name == "MasterMarksman" ? "Master_Marksman" : name == "MasterStrike" ? "Master_Strike"
            : name == "Jumpshot" ? "Jumpshot_unique" : name == "EmeraldsIncrease" ? "Prospector" : name == "SoulGather" ? "SoulGatherMultiply"
            : name == "Cooldown" ? "CoolDown" : name == "Sidestep" ? "SideStep" : name == "Supercharge" ? "SuperCharge"
            : name == "SpeedIncrement" ? "SpeedIncriment" : name == "StatusOnHit.Strength" ? "StrengtheningStrike" : name == "SwiftSneak" ? "Shadow_Step" : name;
        var icon = Texture($"{Icons}Effects/T_UI_Icon_Effect_{name}.T_UI_Icon_Effect_{name}");
        if (icon == null)
        {
            missingIcons.Add(tag);
            return CategoryIcon("effects");
        }
        iconTags.Add(tag);
        iconTextures.Add(icon);
        return icon;
    }

    /// <summary>A type's name in the game's language, or the tag when the game has none.</summary>
    public string Title(string tag)
    {
        int at = titleTags.IndexOf(tag);
        if (at >= 0) return titleTexts[at];
        string title = tag;
        if (owner != null)
        {
            var text = UKismetTextLibrary.Conv_TextToString(UInventoryHelperLibrary.GetTypeTitle(owner, new FGameplayTag { TagName = tag }, false));
            if (!string.IsNullOrEmpty(text)) title = text;
        }
        titleTags.Add(tag);
        titleTexts.Add(title);
        return title;
    }

    // ---------------------------------------------------------------- Text

    /// <summary>One of the game's fonts at an exact size, for a role (see the role constants).</summary>
    public FSlateFontInfo FontFor(int role, int size)
    {
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        if (role == PixelTitle && hasTitleFont) font = titleFont;
        else if (role == Nav && hasNavFont) font = navFont;
        else if (role == Section && hasSectionFont) font = sectionFont;
        else if (role != Readable && hasUiFont) font = uiFont;
        else if (role == Readable && hasBodyFont) font = bodyFont;
        font.Size = size;
        return font;
    }

    /// <summary>Text at an exact size in one of the game's fonts; pixel text gets the game's dark drop shadow.</summary>
    public UTextBlock? Label(UObject outer, string text, int role, int size, FLinearColor color)
    {
        var label = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), outer) as UTextBlock;
        if (label == null) return null;
        label.SetText(text);
        var font = FontFor(role, size);
        if (font.FontObject == null)
        {
            font = label.Font;
            font.Size = size;
        }
        label.SetFont(font);
        label.SetColorAndOpacity(Ui.SlateColor(color));
        if (role != Readable)
        {
            float shadow = size >= 30 ? 3 : 2;
            label.SetShadowOffset(new FVector2D { X = shadow, Y = shadow });
            label.SetShadowColorAndOpacity(Rgba(0, 0, 0, 0.6f));
        }
        return label;
    }

    /// <summary>A button's text in its look's text colour (the game's tab text is grey, white when it's the open tab).</summary>
    public FLinearColor LookText(int look, bool selected)
    {
        var found = LookFor(look);
        if (found == null || !found.Ok) return look == LookTab && !selected ? Rgba(0.7f, 0.72f, 0.75f, 1) : Rgba(0.96f, 0.96f, 0.96f, 1);
        var color = selected ? found.SelectedTextColor : found.TextColor;
        // A style whose text colour is unusable on this menu's dark screen falls back to white.
        return color.A < 0.2f ? Rgba(0.96f, 0.96f, 0.96f, 1) : color;
    }

    // ---------------------------------------------------------------- Brushes

    ButtonLook? LookFor(int look) =>
        look == LookTab ? tabLook : look == LookList ? listLook : look == LookSecondary ? secondaryLook : look == LookPrimary ? primaryLook : slotLook;

    /// <summary>A button in one of the game's looks; drawn by the menu when the game's look isn't there.</summary>
    public FButtonStyle Button(int look, bool selected, FMargin padding)
    {
        var found = LookFor(look);
        if (found != null && found.Ok) return found.Style(selected, padding);
        var gold = Rgba(0.95f, 0.72f, 0.25f, 1);
        var fill = look == LookPrimary ? Rgba(0.2f, 0.55f, 0.27f, 1) : look == LookTab ? Rgba(0, 0, 0, 0) : Rgba(0.21f, 0.28f, 0.33f, 0.96f);
        var idle = Rounded(fill, Rgba(1, 1, 1, look == LookTab ? 0 : 0.18f), 1, 3);
        var lit = Rounded(look == LookTab ? Rgba(1, 1, 1, 0.06f) : fill, gold, 2, 3);
        return new FButtonStyle { Normal = selected ? lit : idle, Hovered = lit, Pressed = lit, Disabled = idle, NormalPadding = padding, PressedPadding = padding };
    }

    /// <summary>The game's item slot frame, drawn behind icons; its selected frame for the chosen one.</summary>
    public FSlateBrush SlotBrush(bool selected)
    {
        if (slotLook != null && slotLook.Ok) return selected ? slotLook.Selected : slotLook.Normal;
        return Rounded(Rgba(0.03f, 0.04f, 0.06f, 1), selected ? Rgba(0.95f, 0.72f, 0.25f, 1) : Rgba(1, 1, 1, 0.2f), selected ? 3 : 1, 3);
    }

    /// <summary>A settings row: the game's row band, framed in white while hovered (as on the settings screen).</summary>
    public FButtonStyle RowButton(FMargin padding)
    {
        var idle = RowBrush();
        var lit = hasRowLit ? rowLit : Rounded(Rgba(1, 1, 1, 0.06f), Rgba(1, 1, 1, 0.9f), 2, 2);
        return new FButtonStyle { Normal = idle, Hovered = lit, Pressed = lit, Disabled = idle, NormalPadding = padding, PressedPadding = padding };
    }

    public FSlateBrush RowBrush() => hasRow ? row : Rounded(Rgba(0, 0, 0, 0.28f), Rgba(1, 1, 1, 0), 0, 2);

    /// <summary>The box inside a settings row (its dropdowns and slider), lit with the row's white frame.</summary>
    public FSlateBrush BoxBrush(bool lit)
    {
        if (lit && hasRowLit) return rowLit;
        if (hasInner) return inner;
        return Rounded(Rgba(0.03f, 0.05f, 0.07f, 0.95f), lit ? Rgba(1, 1, 1, 0.85f) : Rgba(1, 1, 1, 0.22f), lit ? 2 : 1, 3);
    }

    /// <summary>The settings switch's track (on or off) and its knob.</summary>
    public FSlateBrush ToggleTrack(bool on) => on ? toggleOn : toggleOff;
    public FSlateBrush ToggleKnob() => thumb;

    public FSlateBrush Backdrop() => backdrop;
    public FSlateBrush NavBar() => navBar;

    /// <summary>A panel like the game's info panels: dark, see-through, with a thin teal frame.</summary>
    public static FSlateBrush Frame() => Rounded(Rgba(0.015f, 0.035f, 0.05f, 0.72f), Rgba(0.42f, 0.6f, 0.64f, 0.75f), 2, 3);

    static FLinearColor Rgba(float r, float g, float b, float a) => new FLinearColor { R = r, G = g, B = b, A = a };

    public static FSlateBrush Rounded(FLinearColor fill, FLinearColor outline, float outlineWidth, float radius) => new FSlateBrush
    {
        DrawAs = ESlateBrushDrawType.RoundedBox,
        TintColor = Ui.SlateColor(fill),
        OutlineSettings = new FSlateBrushOutlineSettings
        {
            CornerRadii = new FVector4 { X = radius, Y = radius, Z = radius, W = radius },
            Color = Ui.SlateColor(outline),
            Width = outlineWidth,
            RoundingType = ESlateBrushRoundingType.FixedRadius,
        },
    };

    // Rarities the player can pick, in the game's order. Colours follow the game's inventory frames.
    public const int RarityCount = 4;

    public static string RarityTag(int index) => index == 1 ? "SW.Rarity.Rare" : index == 2 ? "SW.Rarity.Special" : index == 3 ? "SW.Rarity.Unique" : "SW.Rarity.Common";

    public static string RarityName(int index) => index == 1 ? "Rara" : index == 2 ? "Especial" : index == 3 ? "Única" : "Comum";

    public static int RarityIndex(string tag) => tag == "SW.Rarity.Rare" ? 1 : tag == "SW.Rarity.Special" ? 2 : tag == "SW.Rarity.Unique" ? 3 : 0;

    public static FLinearColor RarityColor(int index) =>
        index == 1 ? new FLinearColor { R = 0.3f, G = 0.8f, B = 0.35f, A = 1 }
        : index == 2 ? new FLinearColor { R = 0.35f, G = 0.6f, B = 1, A = 1 }
        : index == 3 ? new FLinearColor { R = 1, G = 0.62f, B = 0.18f, A = 1 }
        : new FLinearColor { R = 0.62f, G = 0.62f, B = 0.66f, A = 1 };
}
