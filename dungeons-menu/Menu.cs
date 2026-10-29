using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.GameplayTags;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace DungeonsMenu;

/// <summary>
/// The menu, drawn like one of the game's own screens (its settings and inventory): the menu background, a tab bar with
/// the hero's currencies, settings-style rows and switches, list entries in the game's item slots, and a framed panel with
/// the chosen thing's details. It's laid out on a canvas 1080 units high with the screen's shape and scaled to fit the
/// screen, so it fills it at any resolution and UI scale; the footer's − / + zoom it (saved).
/// </summary>
public class Menu : ModMenu
{
    // PickTile groups: what a clicked box stands for.
    public const int PickItem = 0;
    public const int PickCategory = 1;
    public const int PickRarity = 2;
    public const int PickEquipment = 3;
    public const int PickEnchantment = 4;
    public const int PickResource = 5;
    public const int PickMob = 6;
    public const int PickStep = 7;
    public const int PickToggle = 8;
    public const int PickTab = 9;
    public const int PickAmount = 10;
    public const int PickPage = 11;
    public const int PickZoom = 12;
    public const int PickMode = 13;
    public const int PickEffect = 14;
    public const int PickEffectRemove = 15;
    public const int PickTalismanLevel = 16;
    // Canvas units: the canvas is 1080 high at 100% zoom.
    const float NavHeight = 92;
    const float FooterHeight = 78;
    const float SideMargin = 44;
    const float ContentTop = 26;
    const float ContentBottom = 6;
    const float Gap = 24;
    const float EntryHeight = 86;
    const float StepHeight = 60;
    const int DefaultZoom = 100;
    const int MinZoom = 80;
    const int MaxZoom = 130;
    const int ZoomStep = 10;
    const string Version = "0.4.4";
    const int TabCount = 7;
    const int TabResources = 0;
    const int TabHero = 1;
    const int TabItems = 2;
    const int TabMobs = 3;
    const int TabEnchants = 4;
    const int TabCombat = 5;
    const int TabWorld = 6;

    GameAdapter? adapter;
    ItemVisuals? visuals;
    UWidgetTree? tree;
    UBorder? backdrop;
    UBorder? navBar;
    UHorizontalBox? navRow;
    UHorizontalBox? footerRow;
    USizeBox? canvas;
    USizeBox? bodyBox;
    UVerticalBox? body;
    UTextBlock? status;
    UEditableTextBox? query;
    USpinBox? value;
    USpinBox? powerSpin;
    // Hero tab: the level box and the village vendors' boxes, and what was typed in them (-1: show the game's value).
    USpinBox? levelSpin;
    USpinBox? enchantsmithSpin;
    USpinBox? blacksmithSpin;
    USpinBox? merchantSpin;
    int levelInput;
    int enchantsmithInput;
    int blacksmithInput;
    int merchantInput;
    // World tab: the game speed and radius boxes and what was typed in them (0: the default).
    USpinBox? gameSpeedSpin;
    USpinBox? radiusSpin;
    float gameSpeedInput;
    float radiusInput;
    // Every number box on the page (typing in one keeps Q and E from switching tabs).
    List<USpinBox> spins;
    // The page's scroll boxes in the order they were made, and where they stood before the page was rebuilt: a click
    // rebuilds the page, which sent its lists back to the top (reported on 0.4.0). Set again for a few frames, since a
    // new scroll box only knows how tall its content is once it has been laid out.
    List<UScrollBox> scrolls;
    List<float> scrollOffsets;
    int builtTab;
    int restoreFrames;
    string toggleKey;
    int tab;
    bool freeMode;
    // Combat tab: the options in force (in Rules.CombatKeys order, applied as soon as one changes) and the page's factor
    // boxes.
    List<float> combat;
    List<USpinBox> combatSpins;
    List<string> combatSpinKeys;
    int powerInput;
    int resourceAmount;
    string selectedResource;
    // Canvas size (the screen's shape, 1080 / zoom high) and what the pages get of it.
    int zoom;
    float aspect;
    float canvasWidth;
    float canvasHeight;
    float contentWidth;
    float contentHeight;
    float detailWidth;
    int perPage;

    // Items tab: the whole catalog with each item's slot, what the filters leave, and the choices on screen.
    List<string> catalogTags;
    List<string> catalogSlots;
    List<string> shownTags;
    string itemQuery;
    string category;
    int itemPage;
    string selectedItem;
    int itemRarity;
    int itemQuantity;
    // The level a new talisman is created at.
    int talismanLevel;

    // Mobs tab.
    List<string> mobTags;
    string mobQuery;
    int mobPage;
    string selectedMob;
    int mobQuantity;

    // Enchantments tab.
    List<string> equipIds;
    List<string> enchantTags;
    string selectedEquip;
    string selectedEnchant;
    int enchantPage;
    int enchantLevel;
    // The same tab's effect editor (the middle column switches between enchantments and effects).
    bool effectsMode;
    List<string> effectTags;
    string selectedEffect;
    int effectPage;
    int effectLevel;
    USpinBox? effectLevelSpin;

    public void Setup(GameAdapter game, string key)
    {
        adapter = game;
        visuals = game.Visuals;
        catalogTags = new List<string>();
        catalogSlots = new List<string>();
        shownTags = new List<string>();
        mobTags = new List<string>();
        equipIds = new List<string>();
        enchantTags = new List<string>();
        effectTags = new List<string>();
        selectedEffect = "";
        effectLevel = 3;
        talismanLevel = 1;
        itemQuery = "";
        mobQuery = "";
        category = "all";
        selectedItem = "";
        selectedMob = "";
        selectedEquip = "";
        selectedEnchant = "";
        selectedResource = "emeralds";
        resourceAmount = 1000;
        itemQuantity = 1;
        mobQuantity = 1;
        enchantLevel = 1;
        levelInput = -1;
        enchantsmithInput = -1;
        blacksmithInput = -1;
        merchantInput = -1;
        gameSpeedInput = 0;
        radiusInput = 20;
        toggleKey = key;
        spins = new List<USpinBox>();
        scrolls = new List<UScrollBox>();
        scrollOffsets = new List<float>();
        builtTab = -1;
        combatSpins = new List<USpinBox>();
        combatSpinKeys = new List<string>();
        combat = new List<float>();
        foreach (var option in Rules.CombatKeys()) combat.Add(game.Option(option));
        ApplyTheme();
        BuildFooter();
        Page();
        Report(game.Status());
    }

    protected override UWidget? Build(UWidgetTree widgetTree)
    {
        tree = widgetTree;
        // ModMenu opens and closes with the pause menu's sounds, which also muffle the game's sound (reported on 0.4.0).
        // Build runs before the screen is activated, so the click plays instead and closing is silent.
        ActivatedAudioTag = GameUI.Tag(Sounds.ClickTag);
        DeactivatedAudioTag = new FGameplayTag();
        var viewport = UWidgetLayoutLibrary.GetViewportSize(this);
        aspect = viewport.Y > 100 ? (float)(viewport.X / viewport.Y) : 16f / 9f;
        aspect = Fit(aspect, 1.25f, 2.4f);
        zoom = Settings.GetInt("ui_zoom", DefaultZoom);
        if (zoom < MinZoom || zoom > MaxZoom) zoom = DefaultZoom;
        Layout();
        var root = Overlay();
        if (root == null) return null;
        // The whole screen: the game's menu background (set in ApplyTheme) behind the scaled canvas.
        backdrop = Border();
        backdrop?.SetBrushColor(Ui.Color(0.03f, 0.1f, 0.12f, 0.97f));
        Layer(root, backdrop);
        var frame = Ui.Column(tree);
        if (frame == null) return root;
        navBar = Border();
        navRow = Ui.Row(tree);
        if (navBar != null && navRow != null)
        {
            navBar.SetBrushColor(Ui.Color(0.02f, 0.02f, 0.025f, 0.95f));
            navBar.SetPadding(new FMargin { Left = SideMargin - 8, Right = SideMargin });
            navBar.AddChild(navRow);
        }
        AddV(frame, Sized(navBar, 0, NavHeight), 0);
        body = Ui.Column(tree);
        bodyBox = Sized(body, contentWidth, contentHeight);
        bodyBox?.SetClipping(EWidgetClipping.ClipToBounds);
        frame.AddChildToVerticalBox(bodyBox)?.SetPadding(new FMargin { Left = SideMargin, Top = ContentTop, Right = SideMargin, Bottom = ContentBottom });
        footerRow = Ui.Row(tree);
        frame.AddChildToVerticalBox(Sized(footerRow, 0, FooterHeight))?.SetPadding(new FMargin { Left = SideMargin, Right = SideMargin });
        canvas = Sized(frame, canvasWidth, canvasHeight);
        var scaler = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScaleBox>(), tree) as UScaleBox;
        if (scaler == null)
        {
            Layer(root, canvas);
            return root;
        }
        scaler.SetStretch(EStretch.ScaleToFit);
        scaler.SetStretchDirection(EStretchDirection.Both);
        scaler.AddChild(canvas);
        Layer(root, scaler);
        return root;
    }

    // The game's menu background and tab bar, once the game's art is known (Build runs before Setup).
    void ApplyTheme()
    {
        if (visuals == null) return;
        if (backdrop != null && visuals.HasBackdrop)
        {
            backdrop.SetBrush(visuals.Backdrop());
            backdrop.SetBrushColor(Ui.Color(1, 1, 1, 1));
        }
        if (navBar != null && visuals.HasNavBar)
        {
            navBar.SetBrush(visuals.NavBar());
            navBar.SetBrushColor(Ui.Color(1, 1, 1, 1));
        }
    }

    // Canvas and page sizes, in canvas units: 1080 / zoom high, as wide as the screen's shape.
    void Layout()
    {
        canvasHeight = 1080f * 100f / zoom;
        canvasWidth = canvasHeight * aspect;
        contentWidth = canvasWidth - SideMargin * 2;
        contentHeight = canvasHeight - NavHeight - FooterHeight - ContentTop - ContentBottom;
        detailWidth = Fit(contentWidth * 0.31f, 470, 620);
    }

    void Rezoom(int step)
    {
        Capture();
        int next = zoom + step;
        if (next < MinZoom || next > MaxZoom) return;
        zoom = next;
        Settings.Set("ui_zoom", $"{zoom}");
        Layout();
        canvas?.SetWidthOverride(canvasWidth);
        canvas?.SetHeightOverride(canvasHeight);
        bodyBox?.SetWidthOverride(contentWidth);
        bodyBox?.SetHeightOverride(contentHeight);
        BuildFooter();
        Page();
        Report($"Tamanho do menu: {zoom}%.");
    }

    static float Fit(float value, float min, float max) => value < min ? min : value > max ? max : value;

    // Inside a detail panel: its width less the frame, the padding and the scroll bar.
    float DetailInner => detailWidth - 60;

    // ---------------------------------------------------------------- Building blocks

    static FLinearColor White() => Ui.Color(0.96f, 0.96f, 0.96f, 1);
    static FLinearColor Muted() => Ui.Color(0.66f, 0.71f, 0.76f, 1);
    static FLinearColor Light() => Ui.Color(0.84f, 0.88f, 0.9f, 1);
    static FLinearColor Gold() => Ui.Color(1, 0.8f, 0.32f, 1);
    static FLinearColor Accent() => Ui.Color(0.64f, 0.52f, 1, 1);
    static FLinearColor SectionColor() => Ui.Color(0.74f, 0.78f, 0.8f, 1);

    static FSlateFontInfo DefaultFont(int size)
    {
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        font.Size = size;
        return font;
    }

    /// <summary>Text at an exact size in one of the game's fonts (ItemVisuals' roles).</summary>
    UTextBlock? Txt(string text, int role, int size, FLinearColor color)
    {
        if (tree == null) return null;
        return visuals != null ? visuals.Label(tree, text, role, size, color) : Ui.Text(tree, text, size, color);
    }

    UTextBlock? Wrapped(string text, int size, FLinearColor color)
    {
        var label = Txt(text, ItemVisuals.Readable, size, color);
        label?.SetAutoWrapText(true);
        return label;
    }

    // A heading like the settings screen's (IDIOMA, DIFICULDADE).
    void Section(UVerticalBox? column, string text, float top) { AddV(column, Txt(text, ItemVisuals.Section, 22, SectionColor()), top); }

    FLinearColor LookColor(int look, bool selected) => visuals != null ? visuals.LookText(look, selected) : White();

    FSlateBrush Box(bool lit) => visuals != null ? visuals.BoxBrush(lit)
        : ItemVisuals.Rounded(Ui.Color(0.03f, 0.05f, 0.07f, 0.95f), Ui.Color(1, 1, 1, lit ? 0.85f : 0.22f), lit ? 2 : 1, 3);

    UBorder? Border() => UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;

    UOverlay? Overlay() => UGameplayStatics.SpawnObject(Unreal.ClassOf<UOverlay>(), tree) as UOverlay;

    void Layer(UOverlay? overlay, UWidget? child)
    {
        if (overlay == null || child == null) return;
        var slot = overlay.AddChildToOverlay(child);
        slot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
    }

    void AddV(UVerticalBox? column, UWidget? child, float top)
    {
        if (column == null || child == null) return;
        column.AddChildToVerticalBox(child)?.SetPadding(new FMargin { Top = top });
    }

    void AddH(UHorizontalBox? row, UWidget? child, float left, bool fill, bool top)
    {
        if (row == null || child == null) return;
        var slot = row.AddChildToHorizontalBox(child);
        if (slot == null) return;
        slot.SetPadding(new FMargin { Left = left });
        slot.SetVerticalAlignment(top ? EVerticalAlignment.VAlign_Top : EVerticalAlignment.VAlign_Center);
        if (fill) slot.SetSize(new FSlateChildSize { SizeRule = ESlateSizeRule.Fill, Value = 1 });
    }

    USizeBox? Sized(UWidget? child, float width, float height)
    {
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        if (box == null) return null;
        if (width > 0) box.SetWidthOverride(width);
        if (height > 0) box.SetHeightOverride(height);
        if (child != null) box.AddChild(child);
        return box;
    }

    void StyleButton(UButton? button, int look, bool selected, float padX, float padY)
    {
        if (button == null) return;
        var padding = new FMargin { Left = padX, Top = padY, Right = padX, Bottom = padY };
        if (visuals != null) button.SetStyle(visuals.Button(look, selected, padding));
        else button.SetBackgroundColor(Ui.Color(0.2f, 0.26f, 0.31f, 1));
    }

    void Fill(UButton? button, UWidget? content)
    {
        if (button == null || content == null) return;
        var slot = button.AddChild(content) as UButtonSlot;
        slot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
    }

    void Center(UButton? button, UWidget? content)
    {
        if (button == null || content == null) return;
        var slot = button.AddChild(content) as UButtonSlot;
        slot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
    }

    /// <summary>A button in one of the game's looks (ItemVisuals.Look…), with its label in the pixel font.</summary>
    UButton? Btn(string label, int look, int size)
    {
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<UButton>(), tree) as UButton;
        if (button == null) return null;
        WithSounds(button);
        StyleButton(button, look, false, 20, 8);
        Center(button, Txt(label, ItemVisuals.PixelUi, size, LookColor(look, false)));
        return button;
    }

    /// <summary>A clickable box that hands its group and id to Pick, with the game's click and hover sounds.</summary>
    PickTile? Tile(int group, string id)
    {
        var tile = UGameplayStatics.SpawnObject(Unreal.ClassOf<PickTile>(), tree) as PickTile;
        if (tile == null) return null;
        WithSounds(tile);
        tile.Setup(this, group, id);
        return tile;
    }

    // The chosen box gets a gold frame on top (it doesn't take clicks).
    USizeBox? Selectable(UWidget? content, float width, float height, bool chosen)
    {
        if (!chosen) return Sized(content, width, height);
        var layers = Overlay();
        var outline = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        if (layers == null || outline == null) return Sized(content, width, height);
        Layer(layers, content);
        outline.SetBrush(ItemVisuals.Rounded(Ui.Color(1, 0.78f, 0.3f, 0.07f), Gold(), 3, 3));
        outline.SetVisibility(ESlateVisibility.HitTestInvisible);
        Layer(layers, outline);
        return Sized(layers, width, height);
    }

    // A tinted picture (the game's category icons are white).
    USizeBox? Picture(UTexture2D? texture, float size, FLinearColor tint)
    {
        var box = Ui.Image(tree, texture, size, size);
        var image = box != null ? box.GetChildAt(0) as UImage : null;
        image?.SetColorAndOpacity(tint);
        return box;
    }

    // A keyboard key, like the game's hints (ESC, Q, E).
    UWidget? KeyCap(string key)
    {
        var cap = Border();
        var label = Txt(key, ItemVisuals.PixelUi, 16, Ui.Color(0.08f, 0.08f, 0.09f, 1));
        if (cap == null || label == null) return label;
        label.SetShadowColorAndOpacity(Ui.Color(0, 0, 0, 0));
        cap.SetBrush(ItemVisuals.Rounded(Ui.Color(0.9f, 0.9f, 0.9f, 1), Ui.Color(0, 0, 0, 0.5f), 1, 4));
        cap.SetPadding(new FMargin { Left = 7, Top = 2, Right = 7, Bottom = 2 });
        cap.AddChild(label);
        return cap;
    }

    // A rarity tag like the inventory's (ÚNICO on orange).
    UWidget? Badge(string text, FLinearColor color)
    {
        var badge = Border();
        var label = Txt(text.ToUpper(), ItemVisuals.PixelUi, 19, Ui.Color(0.08f, 0.06f, 0.05f, 1));
        if (badge == null || label == null) return label;
        label.SetShadowColorAndOpacity(Ui.Color(0, 0, 0, 0));
        badge.SetBrush(ItemVisuals.Rounded(color, Ui.Color(0, 0, 0, 0.35f), 1, 3));
        badge.SetPadding(new FMargin { Left = 14, Top = 4, Right = 14, Bottom = 4 });
        badge.AddChild(label);
        return badge;
    }

    void Divider(UVerticalBox? column, float top)
    {
        var line = Border();
        line?.SetBrushColor(Ui.Color(1, 1, 1, 0.14f));
        AddV(column, Sized(line, 0, 2), top);
    }

    UScrollBox? Scroll()
    {
        var scroll = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScrollBox>(), tree) as UScrollBox;
        if (scroll == null) return null;
        if (visuals != null && visuals.HasScrollBar) scroll.WidgetBarStyle = visuals.ScrollBar;
        scroll.SetScrollBarPadding(new FMargin { Left = 8 });
        scrolls.Add(scroll);
        return scroll;
    }

    static FMargin RowPadding() => new FMargin { Left = 26, Top = 16, Right = 26, Bottom = 16 };

    // A settings row's band (not clickable) around a row of controls.
    UHorizontalBox? Band(UVerticalBox? column, float top)
    {
        var border = Border();
        var row = Ui.Row(tree);
        if (border == null || row == null) return null;
        border.SetBrush(visuals != null ? visuals.RowBrush() : ItemVisuals.Rounded(Ui.Color(0, 0, 0, 0.28f), Ui.Color(0, 0, 0, 0), 0, 2));
        border.SetPadding(RowPadding());
        border.AddChild(row);
        AddV(column, border, top);
        return row;
    }

    /// <summary>
    /// A framed panel the page's height, like the game's info panels: a header strip, then its content in a scroll box
    /// (returned).
    /// </summary>
    UVerticalBox? DetailPanel(UHorizontalBox? parent, string header, float width, float left)
    {
        var frame = Border();
        var outer = Ui.Column(tree);
        var scroll = Scroll();
        var column = Ui.Column(tree);
        var strip = Border();
        if (frame == null || outer == null || scroll == null || column == null || strip == null) return null;
        frame.SetBrush(ItemVisuals.Frame());
        frame.SetPadding(Ui.Margin(4));
        frame.AddChild(outer);
        AddH(parent, Sized(frame, width, contentHeight), left, false, true);
        strip.SetBrush(ItemVisuals.Rounded(Ui.Color(0.2f, 0.29f, 0.33f, 0.96f), Ui.Color(1, 1, 1, 0.12f), 1, 2));
        strip.SetPadding(new FMargin { Top = 10, Bottom = 10 });
        strip.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        strip.AddChild(Txt(header, ItemVisuals.PixelUi, 20, Ui.Color(0.88f, 0.93f, 0.95f, 1)));
        AddV(outer, strip, 0);
        scroll.AddChild(column);
        var slot = outer.AddChildToVerticalBox(scroll);
        slot?.SetPadding(new FMargin { Left = 22, Top = 20, Right = 10, Bottom = 14 });
        slot?.SetSize(new FSlateChildSize { SizeRule = ESlateSizeRule.Fill, Value = 1 });
        return column;
    }

    // ---------------------------------------------------------------- Tab bar and footer

    void BuildNav()
    {
        if (navRow == null) return;
        navRow.ClearChildren();
        // The menu's name and the Q / E hints only when the bar has room: with seven tabs they pushed the third currency off a
        // 1920-wide bar (seen on 0.3.5).
        bool roomy = canvasWidth >= 2200;
        if (roomy) AddH(navRow, Txt("MENU SOLO", ItemVisuals.PixelUi, 20, Gold()), 0, false, false);
        if (roomy) AddH(navRow, KeyCap("Q"), 26, false, false);
        Tab("RECURSOS", TabResources);
        Tab("HERÓI", TabHero);
        Tab("ITENS", TabItems);
        Tab("MOBS", TabMobs);
        Tab("ENCANTAMENTOS", TabEnchants);
        Tab("COMBATE", TabCombat);
        Tab("MUNDO", TabWorld);
        if (roomy) AddH(navRow, KeyCap("E"), 10, false, false);
        AddH(navRow, UGameplayStatics.SpawnObject(Unreal.ClassOf<USpacer>(), tree) as USpacer, 0, true, false);
        Currency(visuals != null ? visuals.EchoIcon : null, "echo_shards", 0);
        Currency(visuals != null ? visuals.EmeraldIcon : null, "emeralds", 28);
        Currency(visuals != null ? visuals.PointsIcon : null, "enchantment_points", 28);
    }

    // A tab in the game's tab look: grey text, white with the orange line under it when open.
    void Tab(string label, int index)
    {
        var button = Tile(PickTab, $"{index}");
        if (button == null || navRow == null) return;
        bool open = tab == index;
        StyleButton(button, ItemVisuals.LookTab, open, 20, 4);
        Center(button, Txt(label, ItemVisuals.Nav, 25, LookColor(ItemVisuals.LookTab, open)));
        var slot = navRow.AddChildToHorizontalBox(button);
        slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
        slot?.SetPadding(new FMargin { Left = 6 });
    }

    // The hero's currency in the tab bar, like the game's.
    void Currency(UTexture2D? icon, string key, float left)
    {
        var row = Ui.Row(tree);
        if (row == null) return;
        if (icon != null) AddH(row, Ui.Image(tree, icon, 36, 36), 0, false, false);
        AddH(row, Txt(Group(adapter != null ? adapter.ResourceAmount(key) : "—"), ItemVisuals.PixelUi, 25, White()), 10, false, false);
        AddH(navRow, row, left, false, false);
    }

    void BuildFooter()
    {
        if (footerRow == null) return;
        footerRow.ClearChildren();
        var back = UGameplayStatics.SpawnObject(Unreal.ClassOf<UButton>(), tree) as UButton;
        var backRow = Ui.Row(tree);
        if (back != null && backRow != null)
        {
            var clear = ItemVisuals.Rounded(Ui.Color(0, 0, 0, 0.01f), Ui.Color(0, 0, 0, 0), 0, 3);
            var lit = ItemVisuals.Rounded(Ui.Color(1, 1, 1, 0.08f), Ui.Color(1, 1, 1, 0.3f), 1, 3);
            var padding = new FMargin { Left = 10, Top = 6, Right = 12, Bottom = 6 };
            back.SetStyle(new FButtonStyle { Normal = clear, Hovered = lit, Pressed = lit, Disabled = clear, NormalPadding = padding, PressedPadding = padding });
            AddH(backRow, KeyCap("ESC"), 0, false, false);
            AddH(backRow, Txt("Voltar", ItemVisuals.Readable, 23, White()), 10, false, false);
            back.AddChild(backRow);
            back.OnClicked += Close;
            WithSounds(back);
            AddH(footerRow, back, 0, false, false);
        }
        status = Txt("Preparando menu...", ItemVisuals.Readable, 21, Ui.Color(0.82f, 0.9f, 0.95f, 1));
        status?.SetAutoWrapText(true);
        AddH(footerRow, status, 28, true, false);
        AddH(footerRow, Txt("TAMANHO", ItemVisuals.Section, 18, Muted()), 24, false, false);
        AddH(footerRow, Small("-", PickZoom, "-"), 12, false, false);
        AddH(footerRow, Txt($"{zoom}%", ItemVisuals.PixelUi, 20, White()), 10, false, false);
        AddH(footerRow, Small("+", PickZoom, "+"), 10, false, false);
        AddH(footerRow, Txt("v" + Version, ItemVisuals.Readable, 18, Muted()), 22, false, false);
    }

    UWidget? Small(string label, int group, string id)
    {
        var button = Tile(group, id);
        if (button == null) return null;
        StyleButton(button, ItemVisuals.LookSecondary, false, 4, 2);
        Center(button, Txt(label, ItemVisuals.PixelUi, 24, LookColor(ItemVisuals.LookSecondary, false)));
        return Sized(button, 52, 46);
    }

    void Report(string text)
    {
        status?.SetText(text);
        MenuLog.Write("[menu] " + text);
    }

    // Keeps what the player typed before a page is rebuilt.
    void Capture()
    {
        if (query != null)
        {
            var text = query.GetText().ToString();
            if (tab == TabItems) itemQuery = text;
            else if (tab == TabMobs) mobQuery = text;
        }
        if (value != null)
        {
            int number = (int)value.GetValue();
            if (tab == TabResources) resourceAmount = number;
            else if (tab == TabItems) itemQuantity = number;
            else if (tab == TabMobs) mobQuantity = number;
            else if (tab == TabEnchants) enchantLevel = number;
        }
        if (powerSpin != null && tab == TabEnchants) powerInput = (int)powerSpin.GetValue();
        if (effectLevelSpin != null) effectLevel = (int)effectLevelSpin.GetValue();
        if (levelSpin != null) levelInput = (int)levelSpin.GetValue();
        if (enchantsmithSpin != null) enchantsmithInput = (int)enchantsmithSpin.GetValue();
        if (blacksmithSpin != null) blacksmithInput = (int)blacksmithSpin.GetValue();
        if (merchantSpin != null) merchantInput = (int)merchantSpin.GetValue();
        if (gameSpeedSpin != null) gameSpeedInput = gameSpeedSpin.GetValue();
        if (radiusSpin != null) radiusInput = radiusSpin.GetValue();
        for (int i = 0; i < combatSpins.Count; i++)
        {
            int at = Rules.CombatKeys().IndexOf(combatSpinKeys[i]);
            if (at >= 0) combat[at] = combatSpins[i].GetValue();
        }
    }

    void Page()
    {
        if (body == null || tree == null) return;
        // The same tab comes back where it was scrolled to; another tab starts at the top.
        scrollOffsets.Clear();
        if (tab == builtTab) foreach (var scroll in scrolls) scrollOffsets.Add(scroll.GetScrollOffset());
        scrolls.Clear();
        builtTab = tab;
        BuildNav();
        body.ClearChildren();
        query = null; value = null; powerSpin = null; effectLevelSpin = null;
        levelSpin = null; enchantsmithSpin = null; blacksmithSpin = null; merchantSpin = null; gameSpeedSpin = null; radiusSpin = null;
        spins.Clear(); combatSpins.Clear(); combatSpinKeys.Clear();
        if (tab == TabResources) ResourcesPage();
        else if (tab == TabHero) HeroPage();
        else if (tab == TabItems) ItemsPage();
        else if (tab == TabMobs) MobsPage();
        else if (tab == TabEnchants) EnchantsPage();
        else if (tab == TabWorld) WorldPage();
        else CombatPage();
        RestoreScroll();
        restoreFrames = 3;
    }

    void RestoreScroll()
    {
        for (int i = 0; i < scrolls.Count && i < scrollOffsets.Count; i++) scrolls[i].SetScrollOffset(scrollOffsets[i]);
    }

    public override void Tick(FGeometry geometry, float deltaTime)
    {
        if (restoreFrames <= 0) return;
        restoreFrames--;
        RestoreScroll();
    }

    void OpenTab(int next)
    {
        if (next < 0 || next >= TabCount) return;
        if (next == TabEnchants) { equipIds.Clear(); enchantTags.Clear(); }
        if (next == TabMobs) mobTags.Clear();
        tab = next;
    }

    // ---------------------------------------------------------------- Inputs

    // − [number] +: the number sits in the game's settings box (click it to type), the buttons step it.
    USpinBox? Stepper(UHorizontalBox? row, string field, float min, float max, float initial, int decimals, float width, float left)
    {
        if (row == null) return null;
        AddH(row, StepButton(field, false), left, false, false);
        var spin = Number(min, max, initial, decimals);
        if (spin != null) spins.Add(spin);
        AddH(row, Sized(spin, width, StepHeight), 8, false, false);
        AddH(row, StepButton(field, true), 8, false, false);
        return spin;
    }

    UWidget? StepButton(string field, bool up)
    {
        var button = Tile(PickStep, field + (up ? "+" : "-"));
        if (button == null) return null;
        StyleButton(button, ItemVisuals.LookSecondary, false, 2, 2);
        Center(button, Txt(up ? "+" : "-", ItemVisuals.PixelUi, 30, LookColor(ItemVisuals.LookSecondary, false)));
        return Sized(button, StepHeight, StepHeight);
    }

    void Step(string id)
    {
        bool up = id.EndsWith("+");
        var field = id.Substring(0, id.Length - 1);
        // Combat factors are "c:<option>".
        int combatAt = field.StartsWith("c:") ? combatSpinKeys.IndexOf(field.Substring(2)) : -1;
        var target = field == "power" ? powerSpin : field == "level" ? levelSpin : field == "v:enchantsmith" ? enchantsmithSpin
            : field == "v:blacksmith" ? blacksmithSpin : field == "v:merchant" ? merchantSpin
            : field == "gamespeed" ? gameSpeedSpin : field == "radius" ? radiusSpin : field == "elevel" ? effectLevelSpin
            : combatAt >= 0 ? combatSpins[combatAt] : value;
        if (target == null) return;
        float step = combatAt >= 0 ? CombatStep(combatSpinKeys[combatAt]) : field == "gamespeed" ? 0.25f : field == "radius" ? 5 : field == "value" && tab == TabResources ? 100 : 1;
        float before = target.GetValue();
        float next = before + (up ? step : -step);
        float min = target.GetMinValue();
        float max = target.GetMaxValue();
        next = next < min ? min : next > max ? max : next;
        if (next == before) return;
        target.SetValue(next);
        // Combat and hero options, vendor levels and the game speed take effect at once; the other boxes wait for their
        // button (creating, enchanting, raising the level).
        if (combatAt >= 0) ApplyCombat(combatSpinKeys[combatAt]);
        else if (field.StartsWith("v:")) ApplyVendor(field.Substring(2), target);
        else if (field == "gamespeed") ApplyGameSpeed();
        // The effect's level redraws the panel so its description follows.
        else if (field == "elevel")
        {
            effectLevel = (int)next;
            Page();
        }
    }

    USpinBox? Number(float min, float max, float initial, int decimals)
    {
        var spin = UGameplayStatics.SpawnObject(Unreal.ClassOf<USpinBox>(), tree) as USpinBox;
        if (spin == null) return null;
        var none = new FSlateBrush { DrawAs = ESlateBrushDrawType.NoDrawType };
        var style = spin.WidgetStyle;
        style.BackgroundBrush = Box(false);
        style.HoveredBackgroundBrush = Box(true);
        style.ActiveBackgroundBrush = Box(true);
        style.ActiveFillBrush = none;
        style.HoveredFillBrush = none;
        style.InactiveFillBrush = none;
        style.ArrowsImage = none;
        style.ForegroundColor = Ui.SlateColor(White());
        style.TextPadding = new FMargin { Left = 12, Top = 8, Right = 12, Bottom = 8 };
        spin.WidgetStyle = style;
        spin.ForegroundColor = Ui.SlateColor(White());
        spin.Font = visuals != null ? visuals.FontFor(ItemVisuals.PixelUi, 28) : DefaultFont(28);
        spin.Justification = ETextJustify.Center;
        spin.bEnableSlider = false;
        spin.SetMinValue(min); spin.SetMaxValue(max); spin.SetMinSliderValue(min); spin.SetMaxSliderValue(max);
        // The spin box shows one decimal by default (0.2.7 showed 100,0 for a power of 100).
        spin.SetMinFractionalDigits(0); spin.SetMaxFractionalDigits(decimals);
        spin.SetDelta(decimals == 0 ? 1 : 0.25f); spin.SetValue(initial);
        return spin;
    }

    UEditableTextBox? Search(string text, string hint)
    {
        var input = UGameplayStatics.SpawnObject(Unreal.ClassOf<UEditableTextBox>(), tree) as UEditableTextBox;
        if (input == null) return null;
        var font = visuals != null ? visuals.FontFor(ItemVisuals.Readable, 24) : DefaultFont(24);
        input.WidgetStyle = new FEditableTextBoxStyle
        {
            BackgroundImageNormal = Box(false),
            BackgroundImageHovered = Box(true),
            BackgroundImageFocused = Box(true),
            BackgroundImageReadOnly = Box(false),
            Padding = new FMargin { Left = 18, Top = 12, Right = 18, Bottom = 12 },
            TextStyle = new FTextBlockStyle
            {
                Font = font,
                ColorAndOpacity = Ui.SlateColor(White()),
                ShadowColorAndOpacity = Ui.Color(0, 0, 0, 0),
                SelectedBackgroundColor = Ui.SlateColor(Ui.Color(0.25f, 0.5f, 0.9f, 0.7f)),
            },
            ForegroundColor = Ui.SlateColor(White()),
            BackgroundColor = Ui.SlateColor(Ui.Color(1, 1, 1, 1)),
            ReadOnlyForegroundColor = Ui.SlateColor(Muted()),
            FocusedForegroundColor = Ui.SlateColor(White()),
        };
        input.SetHintText(hint);
        if (text != "") input.SetText(text);
        input.OnTextCommitted += SearchCommitted;
        return input;
    }

    void SearchBar(UVerticalBox? column, string text, string hint, float width)
    {
        var bar = Ui.Row(tree);
        AddV(column, bar, 0);
        query = Search(text, hint);
        AddH(bar, Sized(query, width - 196, 62), 0, false, false);
        var go = Btn("BUSCAR", ItemVisuals.LookSecondary, 22);
        if (go != null) { go.OnClicked += SearchClicked; AddH(bar, Sized(go, 180, 62), 16, false, false); }
    }

    // Enter in the search box searches, like the button.
    void SearchCommitted(FText text, ETextCommit method)
    {
        if (method == ETextCommit.OnEnter) SearchClicked();
    }

    void SearchClicked()
    {
        Capture();
        if (tab == TabItems) itemPage = 0;
        else if (tab == TabMobs)
        {
            mobPage = 0;
            mobTags.Clear();
        }
        Page();
    }

    // A settings row that switches something on or off (the whole row takes the click), with the game's switch.
    void ToggleRow(UVerticalBox? column, string id, string title, string detail, bool on, float width, float top)
    {
        var button = Tile(PickToggle, id);
        var row = Ui.Row(tree);
        var texts = Ui.Column(tree);
        if (button == null || row == null || texts == null) return;
        if (visuals != null) button.SetStyle(visuals.RowButton(RowPadding()));
        AddV(texts, Txt(title, ItemVisuals.Readable, 25, White()), 0);
        if (detail != "") AddV(texts, Wrapped(detail, 19, Muted()), 4);
        AddH(row, texts, 0, true, false);
        AddH(row, Switch(on), 24, false, false);
        Fill(button, row);
        AddV(column, Sized(button, width, 0), top);
    }

    // ATIVADO / DESATIVADO and the settings screen's switch, its knob at the right when on.
    UWidget? Switch(bool on)
    {
        var row = Ui.Row(tree);
        var track = Border();
        var knob = Border();
        if (row == null || track == null || knob == null) return null;
        AddH(row, Txt(on ? "ATIVADO" : "DESATIVADO", ItemVisuals.PixelUi, 20, on ? Ui.Color(0.55f, 0.95f, 0.55f, 1) : White()), 0, false, false);
        bool art = visuals != null && visuals.HasToggleArt;
        if (art)
        {
            track.SetBrush(visuals!.ToggleTrack(on));
            knob.SetBrush(visuals!.ToggleKnob());
        }
        else
        {
            track.SetBrush(ItemVisuals.Rounded(on ? Ui.Color(0.2f, 0.72f, 0.33f, 1) : Ui.Color(0.3f, 0.3f, 0.33f, 1), Ui.Color(0, 0, 0, 0.5f), 2, 3));
            knob.SetBrush(ItemVisuals.Rounded(Ui.Color(0.92f, 0.92f, 0.92f, 1), Ui.Color(0, 0, 0, 0.35f), 1, 2));
        }
        track.SetPadding(Ui.Margin(3));
        track.SetHorizontalAlignment(on ? EHorizontalAlignment.HAlign_Right : EHorizontalAlignment.HAlign_Left);
        track.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
        track.AddChild(Sized(knob, art ? 16 : 26, 0));
        var trackBox = Sized(track, 68, 36);
        trackBox?.SetVisibility(ESlateVisibility.HitTestInvisible);
        AddH(row, trackBox, 16, false, false);
        return row;
    }

    // A settings row with a name, a line under it and a − [number] + on the right.
    USpinBox? StepRow(UVerticalBox? column, string field, string title, string detail, float min, float max, float initial, int decimals, float top)
    {
        var band = Band(column, top);
        var texts = Ui.Column(tree);
        if (band == null || texts == null) return null;
        AddV(texts, Txt(title, ItemVisuals.Readable, 25, White()), 0);
        AddV(texts, Wrapped(detail, 19, Muted()), 4);
        AddH(band, texts, 0, true, false);
        return Stepper(band, field, min, max, initial, decimals, 220, 24);
    }

    // ---------------------------------------------------------------- Resources

    void ResourcesPage()
    {
        Section(body, "RECURSOS DO HERÓI", 0);
        var cards = Ui.Row(tree);
        AddV(body, cards, 14);
        float cardWidth = (contentWidth - Gap * 2) / 3;
        ResourceCard(cards, "emeralds", visuals != null ? visuals.EmeraldIcon : null, cardWidth, 0);
        ResourceCard(cards, "echo_shards", visuals != null ? visuals.EchoIcon : null, cardWidth, Gap);
        ResourceCard(cards, "enchantment_points", visuals != null ? visuals.PointsIcon : null, cardWidth, Gap);
        Section(body, "QUANTIDADE  •  " + ResourceName(selectedResource).ToUpper(), 38);
        var band = Band(body, 12);
        if (band != null)
        {
            AddH(band, Txt("Quantidade", ItemVisuals.Readable, 25, White()), 0, false, false);
            value = Stepper(band, "value", 0, 1000000, resourceAmount, 0, 300, 28);
            AddH(band, Txt("ATALHOS", ItemVisuals.Section, 18, Muted()), 40, false, false);
            AddH(band, Amount("100", "100"), 14, false, false);
            AddH(band, Amount("1.000", "1000"), 10, false, false);
            AddH(band, Amount("10.000", "10000"), 10, false, false);
            AddH(band, Amount("100.000", "100000"), 10, false, false);
        }
        var actions = Ui.Row(tree);
        AddV(body, actions, 26);
        var add = Btn("ADICIONAR AO SALDO", ItemVisuals.LookPrimary, 24);
        if (add != null) { add.OnClicked += AddResource; AddH(actions, Sized(add, 420, 74), 0, false, false); }
        var set = Btn("DEFINIR SALDO", ItemVisuals.LookSecondary, 24);
        if (set != null) { set.OnClicked += SetResource; AddH(actions, Sized(set, 330, 74), 18, false, false); }
        AddV(body, Wrapped("Adicionar soma a quantidade ao saldo; definir troca o saldo por ela (0 a 1.000.000). Recursos, itens e encantamentos ficam salvos no herói.", 20, Muted()), 22);
    }

    // A resource as a big card: the game's currency icon in a slot, its name and balance; the chosen one is framed.
    void ResourceCard(UHorizontalBox? row, string key, UTexture2D? icon, float width, float left)
    {
        bool chosen = key == selectedResource;
        var button = Tile(PickResource, key);
        var content = Ui.Row(tree);
        var texts = Ui.Column(tree);
        if (button == null || content == null || texts == null) return;
        StyleButton(button, ItemVisuals.LookList, false, 22, 14);
        AddH(content, IconFrame(icon, 112, chosen, false), 0, false, false);
        AddV(texts, Txt(ResourceName(key), ItemVisuals.Readable, 25, chosen ? Gold() : Muted()), 0);
        AddV(texts, Txt(Group(adapter != null ? adapter.ResourceAmount(key) : "—"), ItemVisuals.PixelTitle, 50, White()), 4);
        AddV(texts, Txt(chosen ? "SELECIONADO" : "CLIQUE PARA ESCOLHER", ItemVisuals.PixelUi, 16, chosen ? Gold() : Muted()), 6);
        AddH(content, texts, 22, true, false);
        Fill(button, content);
        AddH(row, Selectable(button, width, 172, chosen), left, false, false);
    }

    static string ResourceName(string key) => key == "echo_shards" ? "Fragmentos de eco" : key == "enchantment_points" ? "Pontos de encantamento" : "Esmeraldas";

    // A quick amount: puts it in the quantity box.
    UWidget? Amount(string label, string amount)
    {
        var button = Tile(PickAmount, amount);
        if (button == null) return null;
        StyleButton(button, ItemVisuals.LookSecondary, false, 18, 8);
        Center(button, Txt(label, ItemVisuals.PixelUi, 20, LookColor(ItemVisuals.LookSecondary, false)));
        return Sized(button, 0, StepHeight);
    }

    // ---------------------------------------------------------------- Hero

    void HeroPage()
    {
        if (adapter == null) return;
        float leftWidth = contentWidth - detailWidth - Gap;
        var main = Ui.Row(tree);
        AddV(body, main, 0);
        var left = Ui.Column(tree);
        var leftScroll = Scroll();
        if (leftScroll == null || left == null) return;
        leftScroll.AddChild(left);
        AddH(main, Sized(leftScroll, leftWidth, contentHeight), 0, false, true);

        adapter.HeroLevelInfo(out var level, out var maxLevel, out var xp, out var next);
        Section(left, "NÍVEL DO HERÓI", 0);
        if (level < 0)
            AddV(left, Wrapped("Entre em uma partida para ver o nível do herói.", 21, Muted()), 12);
        else
        {
            var band = Band(left, 12);
            var texts = Ui.Column(tree);
            if (band != null && texts != null)
            {
                AddV(texts, Txt($"Nível {level} de {maxLevel}", ItemVisuals.Readable, 25, White()), 0);
                AddV(texts, Wrapped($"Experiência: {Num(xp)} • próximo nível em {Num(next)}", 19, Muted()), 4);
                AddH(band, texts, 0, true, false);
                int start = levelInput >= 1 ? levelInput : level < maxLevel ? level + 1 : level;
                levelSpin = Stepper(band, "level", 1, maxLevel > 0 ? maxLevel : 1, start, 0, 150, 24);
                var up = Btn("SUBIR", ItemVisuals.LookPrimary, 22);
                if (up != null) { up.OnClicked += SetLevel; AddH(band, Sized(up, 150, StepHeight), 12, false, false); }
            }
        }

        Section(left, "VILA", 30);
        VendorRow(left, "enchantsmith", enchantsmithInput, 12);
        VendorRow(left, "blacksmith", blacksmithInput, 10);
        VendorRow(left, "merchant", merchantInput, 10);
        var refill = Btn("RENOVAR LOJA DO MERCADOR", ItemVisuals.LookSecondary, 21);
        if (refill != null) { refill.OnClicked += RefillRefreshes; AddV(left, Sized(refill, 460, 64), 14); }

        Section(left, "EXPERIÊNCIA E SAQUE", 30);
        CombatFactor(left, "xp", "Vezes a experiência ganha.", 1, 12);
        CombatFactor(left, "loot", "Vezes o saque dos inimigos e baús.", 1, 10);
        CombatSwitch(left, "rarity", "Equipamentos que entram no inventário viram Especiais (a raridade mais alta dos itens comuns); únicos continuam únicos. Os efeitos sorteados no drop não mudam.", 10);
        CombatSwitch(left, "drops", "Os inimigos sempre deixam algo.", 10);
        CombatSwitch(left, "duplicate", "O saque sai em dobro.", 10);
        var reset = Btn("RESTAURAR ORIGINAIS", ItemVisuals.LookSecondary, 22);
        if (reset != null) { reset.OnClicked += Restore; AddV(left, Sized(reset, 380, 74), 22); }

        var info = DetailPanel(main, "HERÓI", detailWidth, Gap);
        if (info == null) return;
        AddV(info, Txt("NÍVEL", ItemVisuals.PixelUi, 22, White()), 4);
        AddV(info, Wrapped("Subir usa a experiência do próprio jogo; se o jogo não subir na hora, o nível é gravado direto (experimental). Baixar o nível não é possível.", 20, Light()), 10);
        AddV(info, Txt("VILA", ItemVisuals.PixelUi, 22, White()), 24);
        AddV(info, Wrapped("Cada − / + muda o nível na hora. O nível do Encantador libera as melhorias de encantamento que o jogo bloqueia. Fica salvo no herói; confira depois de recarregar.", 20, Light()), 10);
        AddV(info, Txt("EXPERIÊNCIA E SAQUE", ItemVisuals.PixelUi, 22, White()), 24);
        AddV(info, Wrapped("Valem como as opções de combate: na hora em que você muda, ficam lembradas e Restaurar originais desliga tudo, inclusive o combate.", 20, Light()), 10);
    }

    // A vendor's name and level and a − [level] + box that sets the level at once (what was typed is kept in the vendor's
    // field).
    void VendorRow(UVerticalBox? column, string vendor, int input, float top)
    {
        if (adapter == null) return;
        adapter.VendorLevelInfo(vendor, out var level, out var maximum);
        var band = Band(column, top);
        var texts = Ui.Column(tree);
        if (band == null || texts == null) return;
        AddV(texts, Txt(GameAdapter.VendorName(vendor), ItemVisuals.Readable, 25, White()), 0);
        AddV(texts, Wrapped(level < 0 ? "Indisponível neste herói." : $"Nível {level} de {maximum}", 19, Muted()), 4);
        AddH(band, texts, 0, true, false);
        if (level < 0) return;
        var spin = Stepper(band, "v:" + vendor, 0, maximum, input >= 0 ? input : level, 0, 120, 24);
        if (spin == null) return;
        if (vendor == "enchantsmith") { enchantsmithSpin = spin; spin.OnValueCommitted += EnchantsmithTyped; }
        else if (vendor == "blacksmith") { blacksmithSpin = spin; spin.OnValueCommitted += BlacksmithTyped; }
        else { merchantSpin = spin; spin.OnValueCommitted += MerchantTyped; }
    }

    // ---------------------------------------------------------------- Items

    void ItemsPage()
    {
        if (adapter != null) adapter.ProbeSoulStorm();
        if (adapter != null) adapter.DumpNamesOnce();
        if (adapter != null) adapter.AuditOnce();
        if (catalogTags.Count == 0 && adapter != null) adapter.ItemCatalogSlots(catalogTags, catalogSlots);
        FilterItems();
        float sideWidth = Fit(contentWidth * 0.17f, 270, 320);
        float listWidth = contentWidth - sideWidth - detailWidth - Gap * 2;
        int columns = Columns(listWidth, 340);
        perPage = columns * Rows(contentHeight - 76 - 70);
        itemPage = Clamp(itemPage, Pages(shownTags.Count));
        int first = itemPage * perPage;
        int last = first + perPage > shownTags.Count ? shownTags.Count : first + perPage;
        // Icons and names first: loading can run the garbage collector, so no widget is made until they are in memory.
        var icons = new List<UTexture2D>();
        var titles = new List<string>();
        for (int i = first; i < last; i++)
        {
            icons.Add(IconFor(PickItem, shownTags[i])!);
            titles.Add(TitleFor(PickItem, shownTags[i]));
        }
        UTexture2D? heroIcon = selectedItem != "" ? IconFor(PickItem, selectedItem) : null;
        string heroTitle = selectedItem != "" ? TitleFor(PickItem, selectedItem) : "";

        var main = Ui.Row(tree);
        AddV(body, main, 0);
        var side = Ui.Column(tree);
        AddH(main, Sized(side, sideWidth, contentHeight), 0, false, true);
        Section(side, "CATEGORIAS", 0);
        foreach (var key in new List<string> { "all", "melee", "ranged", "armor", "artifact", "talisman", "book", "other" })
            CategoryButton(side, key, sideWidth);

        var center = Ui.Column(tree);
        AddH(main, Sized(center, listWidth, contentHeight), Gap, false, true);
        SearchBar(center, itemQuery, "Buscar pelo nome (ex.: espada, arco, única)", listWidth);
        Grid(center, PickItem, shownTags, first, last, icons, titles, selectedItem, listWidth, columns);
        Pager(center, itemPage, shownTags.Count, "itens");

        var panel = DetailPanel(main, "CRIAR ITEM", detailWidth, Gap);
        if (panel == null) return;
        if (selectedItem == "")
        {
            AddV(panel, Txt("NENHUM ITEM ESCOLHIDO", ItemVisuals.PixelUi, 22, White()), 4);
            AddV(panel, Wrapped("Escolha um item na lista para ver a foto, escolher a raridade e criar.", 21, Muted()), 12);
            return;
        }
        if (GameAdapter.IsTalisman(selectedItem))
        {
            TalismanPanel(panel, heroIcon, heroTitle);
            return;
        }
        HeroTall(panel, heroIcon, heroTitle, ItemVisuals.RarityName(itemRarity), ItemVisuals.RarityColor(itemRarity), 156);
        Section(panel, "RARIDADE", 22);
        RarityGrid(panel, itemRarity, DetailInner);
        AddV(panel, Wrapped("Muda a moldura e o que o jogo salva; os efeitos sorteados na criação não mudam.", 18, Muted()), 8);
        Section(panel, "QUANTIDADE  •  1 A 20", 20);
        var row = Ui.Row(tree);
        AddV(panel, row, 10);
        value = Stepper(row, "value", 1, 20, itemQuantity, 0, DetailInner - 2 * (StepHeight + 8), 0);
        var create = Btn("CRIAR  •  " + ItemVisuals.RarityName(itemRarity).ToUpper(), ItemVisuals.LookPrimary, 26);
        if (create != null) { create.OnClicked += Spawn; AddV(panel, Sized(create, DetailInner, 76), 22); }
    }

    // A talisman instead of rarity and quantity: the hero's own one changes level at a click (talismans level up from
    // 1 to 3 as they gain XP); one the hero lacks is created at the chosen level.
    void TalismanPanel(UVerticalBox? panel, UTexture2D? icon, string title)
    {
        if (adapter == null) return;
        int owned = adapter.TalismanLevel(selectedItem, out var levels);
        if (levels <= 0) levels = 3;
        HeroTall(panel, icon, title, "Talismã", Gold(), 156);
        Section(panel, owned > 0 ? $"NÍVEL  •  O HERÓI TEM NO NÍVEL {owned}" : "NÍVEL AO CRIAR", 22);
        LevelRow(panel, owned > 0 ? owned : talismanLevel, levels, DetailInner);
        if (owned > 0)
        {
            AddV(panel, Wrapped("Clique num nível para mudar o talismã do herói na hora.", 18, Muted()), 8);
            var effects = adapter.TalismanEffects(selectedItem);
            if (effects.Count > 0) Section(panel, "EFEITO NESTE NÍVEL", 20);
            foreach (var text in effects)
                if (text != "") AddV(panel, Wrapped(text, 20, Light()), 8);
            return;
        }
        var create = Btn($"CRIAR  •  NÍVEL {talismanLevel}", ItemVisuals.LookPrimary, 26);
        if (create != null) { create.OnClicked += SpawnTalisman; AddV(panel, Sized(create, DetailInner, 76), 22); }
        AddV(panel, Wrapped("O herói fica com um de cada talismã; depois de criado, o nível muda aqui mesmo.", 18, Muted()), 12);
    }

    // A talisman's levels side by side; the chosen one framed in gold.
    void LevelRow(UVerticalBox? column, int selected, int levels, float width)
    {
        var row = Ui.Row(tree);
        if (column == null || row == null) return;
        AddV(column, row, 10);
        float cell = (width - 8 * (levels - 1)) / levels;
        for (int i = 1; i <= levels; i++)
        {
            var button = Tile(PickTalismanLevel, $"{i}");
            if (button == null) continue;
            StyleButton(button, ItemVisuals.LookList, false, 10, 8);
            Center(button, Txt($"NÍVEL {i}", ItemVisuals.PixelUi, 19, i == selected ? Gold() : White()));
            AddH(row, Selectable(button, cell, 56, i == selected), i == 1 ? 0 : 8, false, false);
        }
    }

    // A category in the side list: the game's category icon and its name; the open one is framed in gold.
    void CategoryButton(UVerticalBox? column, string key, float width)
    {
        bool active = category == key;
        var button = Tile(PickCategory, key);
        var row = Ui.Row(tree);
        if (button == null || row == null) return;
        StyleButton(button, ItemVisuals.LookList, false, 14, 8);
        var icon = visuals != null ? visuals.CategoryIcon(key) : null;
        if (icon != null) AddH(row, Picture(icon, 34, active ? Gold() : White()), 0, false, false);
        var label = Txt(CategoryName(key), ItemVisuals.PixelUi, 18, active ? Gold() : White());
        label?.SetTextOverflowPolicy(ETextOverflowPolicy.Ellipsis);
        AddH(row, label, icon != null ? 12 : 0, true, false);
        Fill(button, row);
        AddV(column, Selectable(button, width, 62, active), 10);
    }

    void FilterItems()
    {
        shownTags.Clear();
        var text = itemQuery.ToLower();
        for (int i = 0; i < catalogTags.Count; i++)
        {
            if (category != "all" && CategoryOf(catalogSlots[i]) != category) continue;
            var tag = catalogTags[i];
            if (text != "" && !tag.ToLower().Contains(text) && !TitleFor(PickItem, tag).ToLower().Contains(text)) continue;
            shownTags.Add(tag);
        }
    }

    static string CategoryOf(string slot) =>
        slot.Contains("Melee") ? "melee" : slot.Contains("Ranged") ? "ranged" : slot.Contains("Armor") ? "armor"
        : slot.Contains("Artifact") ? "artifact" : slot.Contains("Talisman") ? "talisman" : slot.Contains("EnchantmentBook") ? "book" : "other";

    static string CategoryName(string key) =>
        key == "melee" ? "CORPO A CORPO" : key == "ranged" ? "À DISTÂNCIA" : key == "armor" ? "ARMADURAS"
        : key == "artifact" ? "ARTEFATOS" : key == "talisman" ? "TALISMÃS" : key == "book" ? "LIVROS" : key == "other" ? "OUTROS" : "TODOS";

    // ---------------------------------------------------------------- Mobs

    void MobsPage()
    {
        if (adapter == null) return;
        if (mobTags.Count == 0)
        {
            var all = adapter.MobCatalog("");
            var text = mobQuery.ToLower();
            foreach (var tag in all)
                if (text == "" || tag.ToLower().Contains(text) || MobTitle(tag).ToLower().Contains(text)) mobTags.Add(tag);
            Report($"Catálogo: {all.Count} tipo(s) de mob, {mobTags.Count} na lista. Mobs vistos perto do herói entram no catálogo; busque de novo depois de encontrá-los.");
        }
        float listWidth = contentWidth - detailWidth - Gap;
        int columns = Columns(listWidth, 330);
        perPage = columns * Rows(contentHeight - 76 - 70);
        mobPage = Clamp(mobPage, Pages(mobTags.Count));
        int first = mobPage * perPage;
        int last = first + perPage > mobTags.Count ? mobTags.Count : first + perPage;
        var icons = new List<UTexture2D>();
        var titles = new List<string>();
        for (int i = first; i < last; i++)
        {
            icons.Add(IconFor(PickMob, mobTags[i])!);
            titles.Add(MobTitle(mobTags[i]));
        }
        UTexture2D? heroIcon = selectedMob != "" ? IconFor(PickMob, selectedMob) : null;

        var main = Ui.Row(tree);
        AddV(body, main, 0);
        var left = Ui.Column(tree);
        AddH(main, Sized(left, listWidth, contentHeight), 0, false, true);
        SearchBar(left, mobQuery, "Buscar mob (ex.: zumbi, esqueleto, creeper)", listWidth);
        Grid(left, PickMob, mobTags, first, last, icons, titles, selectedMob, listWidth, columns);
        Pager(left, mobPage, mobTags.Count, "mobs");

        var panel = DetailPanel(main, "CRIAR MOB", detailWidth, Gap);
        if (panel == null) return;
        if (selectedMob == "")
        {
            AddV(panel, Txt("NENHUM MOB ESCOLHIDO", ItemVisuals.PixelUi, 22, White()), 4);
            AddV(panel, Wrapped("Escolha um mob na lista; ele aparece perto do herói.", 21, Muted()), 12);
        }
        else
        {
            HeroTall(panel, heroIcon, MobTitle(selectedMob), "", Gold(), 140);
            var tagLabel = Txt(selectedMob, ItemVisuals.Readable, 17, Muted());
            tagLabel?.SetJustification(ETextJustify.Center);
            AddV(panel, tagLabel, 8);
        }
        Section(panel, "QUANTIDADE  •  1 A 20", 22);
        var row = Ui.Row(tree);
        AddV(panel, row, 10);
        value = Stepper(row, "value", 1, 20, mobQuantity, 0, DetailInner - 2 * (StepHeight + 8), 0);
        var create = Btn("CRIAR MOB", ItemVisuals.LookPrimary, 26);
        if (create != null) { create.OnClicked += Spawn; AddV(panel, Sized(create, DetailInner, 76), 22); }
        var remove = Btn("REMOVER MOBS CRIADOS", ItemVisuals.LookSecondary, 21);
        if (remove != null) { remove.OnClicked += RemoveMobs; AddV(panel, Sized(remove, DetailInner, 64), 12); }
        AddV(panel, Wrapped("Os mobs ficam parados enquanto o menu está aberto; feche-o para eles agirem. Ligue a invencibilidade antes de criar muitos.", 19, Muted()), 18);
    }

    // A mob's name: the game's, or its tag made readable (SW.Mob.BabySentinel: Baby Sentinel).
    string MobTitle(string tag)
    {
        var title = visuals != null ? visuals.Title(tag) : tag;
        return title != tag ? title : Pretty(tag);
    }

    static string Pretty(string tag)
    {
        var name = tag.StartsWith("SW.Mob.") ? tag.Substring(7) : tag;
        name = name.Replace(".", " · ").Replace("_", " ");
        string result = "";
        for (int i = 0; i < name.Length; i++)
        {
            var letter = name.Substring(i, 1);
            if (i > 0 && letter != letter.ToLower())
            {
                var previous = name.Substring(i - 1, 1);
                if (previous != " " && previous == previous.ToLower()) result += " ";
            }
            result += letter;
        }
        return result;
    }

    // ---------------------------------------------------------------- Enchantments

    void EnchantsPage()
    {
        if (adapter == null) return;
        if (equipIds.Count == 0) equipIds = adapter.EquipmentCatalog(effectsMode);
        if ((selectedEquip == "" || !equipIds.Contains(selectedEquip)) && equipIds.Count > 0) { selectedEquip = equipIds[0]; selectedEnchant = ""; enchantTags.Clear(); }
        if (enchantTags.Count == 0 && selectedEquip != "") enchantTags = freeMode ? adapter.AllEnchantments() : adapter.EnchantmentCatalog(selectedEquip, "");
        if (effectTags.Count == 0) effectTags = adapter.EffectCatalog();
        // The middle column lists enchantments or effects; the same paging serves both.
        var listed = effectsMode ? effectTags : enchantTags;
        int listGroup = effectsMode ? PickEffect : PickEnchantment;
        float equipWidth = Fit(contentWidth * 0.23f, 330, 430);
        float listWidth = contentWidth - equipWidth - detailWidth - Gap * 2;
        int columns = Columns(listWidth, 320);
        perPage = columns * Rows(contentHeight - 68 - (effectsMode ? 0 : 106) - 70);
        int listPage = Clamp(effectsMode ? effectPage : enchantPage, Pages(listed.Count));
        if (effectsMode) effectPage = listPage;
        else enchantPage = listPage;
        int first = listPage * perPage;
        int last = first + perPage > listed.Count ? listed.Count : first + perPage;
        // Icons and names first: loading can run the garbage collector, so no widget is made until they are in memory.
        var equipIcons = new List<UTexture2D>();
        var equipTitles = new List<string>();
        foreach (var id in equipIds)
        {
            equipIcons.Add(IconFor(PickEquipment, id)!);
            equipTitles.Add(TitleFor(PickEquipment, id));
        }
        var icons = new List<UTexture2D>();
        var titles = new List<string>();
        for (int i = first; i < last; i++)
        {
            icons.Add(IconFor(listGroup, listed[i])!);
            titles.Add(TitleFor(listGroup, listed[i]));
        }
        UTexture2D? equipIcon = selectedEquip != "" ? IconFor(PickEquipment, selectedEquip) : null;
        UTexture2D? enchantIcon = selectedEnchant != "" ? IconFor(PickEnchantment, selectedEnchant) : null;
        if (effectsMode && visuals != null && selectedEquip != "")
        {
            // The effect panel's icons too (the item's effects and the chosen one), before any widget.
            var worn = new List<string>();
            adapter.ItemEffects(selectedEquip, worn, new List<string>());
            foreach (var tag in worn) IconFor(PickEffect, tag);
            if (selectedEffect != "") IconFor(PickEffect, selectedEffect);
        }

        var main = Ui.Row(tree);
        AddV(body, main, 0);
        var equipColumn = Ui.Column(tree);
        AddH(main, Sized(equipColumn, equipWidth, contentHeight), 0, false, true);
        Section(equipColumn, "EQUIPADO", 0);
        for (int i = 0; i < equipIds.Count; i++)
        {
            var id = equipIds[i];
            var rarity = ItemVisuals.RarityColor(ItemVisuals.RarityIndex(adapter.EquippedRarity(id)));
            AddV(equipColumn, Entry(PickEquipment, id, equipTitles[i], equipIcons[i], equipWidth, id == selectedEquip, rarity, true), 10);
        }

        var enchantColumn = Ui.Column(tree);
        AddH(main, Sized(enchantColumn, listWidth, contentHeight), Gap, false, true);
        var modes = Ui.Row(tree);
        AddV(enchantColumn, modes, 0);
        ModeTab(modes, "enchant", $"ENCANTAMENTOS  •  {enchantTags.Count}", !effectsMode, 0);
        ModeTab(modes, "effects", $"EFEITOS  •  {effectTags.Count}", effectsMode, 8);
        if (!effectsMode)
            ToggleRow(enchantColumn, "free", "Modo livre", freeMode ? "Qualquer encantamento em qualquer item, até o nível 20." : "Só os que o jogo aceita neste item.", freeMode, 0, 12);
        Grid(enchantColumn, listGroup, listed, first, last, icons, titles, effectsMode ? selectedEffect : selectedEnchant, listWidth, columns);
        Pager(enchantColumn, listPage, listed.Count, effectsMode ? "efeitos" : "encantamentos");

        var panel = DetailPanel(main, "ITEM EQUIPADO", detailWidth, Gap);
        if (panel == null || selectedEquip == "") return;
        int equipRarity = ItemVisuals.RarityIndex(adapter.EquippedRarity(selectedEquip));
        int currentPower = adapter.ItemPower(selectedEquip);
        HeroWide(panel, equipIcon, TitleFor(PickEquipment, selectedEquip), ItemVisuals.RarityName(equipRarity), ItemVisuals.RarityColor(equipRarity), $"Poder atual: {currentPower}", 124);
        if (effectsMode)
        {
            EffectsPanel(panel);
            return;
        }
        Section(panel, "RARIDADE", 20);
        RarityGrid(panel, equipRarity, DetailInner);
        Section(panel, "PODER DO ITEM", 20);
        var powerRow = Ui.Row(tree);
        AddV(panel, powerRow, 10);
        powerSpin = Stepper(powerRow, "power", 1, 999, powerInput > 0 ? powerInput : currentPower, 0, DetailInner - 2 * (StepHeight + 8) - 150, 0);
        var setPower = Btn("APLICAR", ItemVisuals.LookSecondary, 21);
        if (setPower != null) { setPower.OnClicked += ApplyPower; AddH(powerRow, Sized(setPower, 138, StepHeight), 12, false, false); }
        if (adapter.IsEnchanted(selectedEquip))
        {
            Section(panel, "ENCANTAMENTO ATUAL", 20);
            var clear = Btn("REMOVER ENCANTAMENTO", ItemVisuals.LookSecondary, 21);
            if (clear != null) { clear.OnClicked += ClearEnchant; AddV(panel, Sized(clear, DetailInner, 64), 10); }
            AddV(panel, Wrapped("Usa o desencantar do jogo; se ele recusar, tira direto do item. Em itens únicos remove também o encantamento próprio do item.", 18, Muted()), 8);
        }
        Divider(panel, 22);
        if (selectedEnchant == "")
        {
            AddV(panel, Wrapped("Escolha um encantamento na lista do meio.", 21, Muted()), 16);
            return;
        }
        HeroWide(panel, enchantIcon, TitleFor(PickEnchantment, selectedEnchant), "", Accent(), "", 92);
        var levelRow = Ui.Row(tree);
        if (freeMode)
        {
            Section(panel, $"NÍVEL  •  NORMAL ATÉ {adapter.NormalMaxLevel(selectedEnchant)}, LIVRE ATÉ {GameAdapter.MaxFreeLevel}", 18);
            AddV(panel, levelRow, 10);
            value = Stepper(levelRow, "value", 1, GameAdapter.MaxFreeLevel, enchantLevel, 0, DetailInner - 2 * (StepHeight + 8), 0);
            var write = Btn("GRAVAR NO ITEM", ItemVisuals.LookPrimary, 26);
            if (write != null) { write.OnClicked += WriteEnchant; AddV(panel, Sized(write, DetailInner, 76), 20); }
            AddV(panel, Wrapped("Grava direto no item, ignorando o slot e o nível máximo; substitui o encantamento atual. Acima do máximo é experimental.", 18, Muted()), 12);
            return;
        }
        Section(panel, "NÍVEL", 18);
        AddV(panel, levelRow, 10);
        value = Stepper(levelRow, "value", 1, 100, enchantLevel, 0, DetailInner - 2 * (StepHeight + 8), 0);
        var apply = Btn("APLICAR ENCANTAMENTO", ItemVisuals.LookPrimary, 26);
        if (apply != null) { apply.OnClicked += Enchant; AddV(panel, Sized(apply, DetailInner, 76), 20); }
        AddV(panel, Wrapped("Pontos de encantamento ficam na aba Recursos; o livro que faltar é criado. Para qualquer slot ou nível, ligue o modo livre.", 18, Muted()), 12);
    }

    // One side of the middle column's switch, in the game's tab look.
    void ModeTab(UHorizontalBox? row, string id, string label, bool open, float left)
    {
        var button = Tile(PickMode, id);
        if (button == null) return;
        StyleButton(button, ItemVisuals.LookTab, open, 14, 4);
        Center(button, Txt(label, ItemVisuals.Nav, 21, LookColor(ItemVisuals.LookTab, open)));
        AddH(row, Sized(button, 0, 56), left, false, false);
    }

    // The worn item's effects (each with REMOVER), then the effect chosen in the middle at a level, with the game's own
    // description of what it will do.
    void EffectsPanel(UVerticalBox? panel)
    {
        if (adapter == null) return;
        var tags = new List<string>();
        var texts = new List<string>();
        adapter.ItemEffects(selectedEquip, tags, texts);
        Section(panel, "EFEITOS DO ITEM", 20);
        if (tags.Count == 0) AddV(panel, Wrapped("Este item não tem efeitos.", 19, Muted()), 10);
        for (int i = 0; i < tags.Count; i++) EffectRow(panel, tags[i], texts[i]);
        Divider(panel, 22);
        if (selectedEffect == "")
        {
            AddV(panel, Wrapped("Escolha um efeito na lista do meio para pôr no item.", 21, Muted()), 16);
            return;
        }
        bool has = tags.Contains(selectedEffect);
        Section(panel, has ? "TROCAR EFEITO" : "NOVO EFEITO", 18);
        var preview = adapter.EffectPreview(selectedEffect, effectLevel);
        var chosen = Ui.Row(tree);
        AddV(panel, chosen, 10);
        AddH(chosen, IconFrame(IconFor(PickEffect, selectedEffect), 92, true, false), 0, false, false);
        var chosenTexts = Ui.Column(tree);
        AddH(chosen, chosenTexts, 16, true, false);
        AddV(chosenTexts, Txt(EffectTitle(selectedEffect).ToUpper(), ItemVisuals.PixelTitle, 26, White()), 0);
        if (preview != "") AddV(chosenTexts, Wrapped(preview, 20, Gold()), 8);
        Section(panel, $"NÍVEL  •  NORMAL ATÉ 3, LIVRE ATÉ {GameAdapter.MaxEffectLevel}", 18);
        var levelRow = Ui.Row(tree);
        AddV(panel, levelRow, 10);
        effectLevelSpin = Stepper(levelRow, "elevel", 1, GameAdapter.MaxEffectLevel, effectLevel, 0, DetailInner - 2 * (StepHeight + 8), 0);
        if (effectLevelSpin != null) effectLevelSpin.OnValueCommitted += EffectLevelTyped;
        var write = Btn(has ? "TROCAR NO ITEM" : "ADICIONAR AO ITEM", ItemVisuals.LookPrimary, 26);
        if (write != null) { write.OnClicked += AddEffect; AddV(panel, Sized(write, DetailInner, 76), 20); }
        AddV(panel, Wrapped("Grava direto no item e o reequipa. Trocar muda a força do efeito que o item já tem. Alguns efeitos só agem no tipo de item certo (tiro múltiplo num arco, por exemplo); acima do nível 3 é experimental.", 18, Muted()), 12);
    }

    // An effect on the item: its icon, what it does and a button that takes it off.
    void EffectRow(UVerticalBox? column, string tag, string text)
    {
        var band = Band(column, 10);
        if (band == null) return;
        var texts = Ui.Column(tree);
        AddH(band, IconFrame(IconFor(PickEffect, tag), 56, false, false), 0, false, false);
        AddH(band, texts, 14, true, false);
        AddV(texts, Txt(EffectTitle(tag), ItemVisuals.Readable, 22, White()), 0);
        if (text != "") AddV(texts, Wrapped(text, 18, Muted()), 4);
        var remove = Tile(PickEffectRemove, tag);
        if (remove == null) return;
        StyleButton(remove, ItemVisuals.LookSecondary, false, 8, 4);
        Center(remove, Txt("REMOVER", ItemVisuals.PixelUi, 16, LookColor(ItemVisuals.LookSecondary, false)));
        AddH(band, Sized(remove, 118, 48), 12, false, false);
    }

    // An effect's short name: the menu's Portuguese one; the game's "titles" for effects are internal names
    // (SoulGather_TalismanEffect, seen on 0.4.1), so one is used only when it reads like a name.
    string EffectTitle(string tag)
    {
        var name = GameAdapter.EffectName(tag);
        if (name != "") return name;
        var title = visuals != null ? visuals.Title(tag) : tag;
        return title != tag && !title.Contains("_") ? title : Pretty(tag.Replace("SW.Effect.", "").Replace("SW.Enchantment.", ""));
    }

    // ---------------------------------------------------------------- Combat

    void CombatPage()
    {
        float leftWidth = contentWidth - detailWidth - Gap;
        var main = Ui.Row(tree);
        AddV(body, main, 0);
        // In a scroll box: the rows are taller than the page on most screens.
        var left = Ui.Column(tree);
        var leftScroll = Scroll();
        if (leftScroll == null || left == null) return;
        leftScroll.AddChild(left);
        AddH(main, Sized(leftScroll, leftWidth, contentHeight), 0, false, true);
        var reset = Btn("RESTAURAR ORIGINAIS", ItemVisuals.LookSecondary, 22);
        if (reset != null) { reset.OnClicked += Restore; AddV(left, Sized(reset, 380, 74), 0); }
        Section(left, "PROTEÇÃO", 26);
        CombatSwitch(left, "god", "A vida não cai: o mínimo vira o máximo e ela é recarregada o tempo todo.", 12);
        CombatSwitch(left, "cooldown", "Habilidades, artefatos e poções prontos de novo na hora, inclusive as cargas da Poção Sem Fundo.", 10);
        Section(left, "MUNIÇÃO E RECURSOS", 30);
        CombatSwitch(left, "ammo", "A aljava volta a ficar cheia depois de cada disparo.", 12);
        CombatSwitch(left, "souls", "As almas ficam sempre no máximo para os artefatos de alma.", 10);
        CombatSwitch(left, "rolls", "As cargas de rolagem voltam na hora.", 10);
        Section(left, "ATAQUE", 30);
        CombatSwitch(left, "crit", "Todo golpe é crítico.", 12);
        CombatFactor(left, "damage", "Vezes o dano normal.", 1, 12);
        CombatFactor(left, "attack", "Vezes a velocidade dos golpes e dos disparos.", 2, 10);
        CombatFactor(left, "critdamage", "Vezes o dano dos golpes críticos.", 2, 10);
        Section(left, "MOVIMENTO", 30);
        CombatFactor(left, "speed", "Vezes a velocidade de movimento.", 2, 12);
        CombatFactor(left, "jump", "Vezes a altura do pulo.", 2, 10);

        var info = DetailPanel(main, "COMBATE", detailWidth, Gap);
        if (info == null || adapter == null) return;
        AddV(info, Txt("COMO FUNCIONA", ItemVisuals.PixelUi, 22, White()), 4);
        AddV(info, Wrapped("As chaves e os multiplicadores valem na hora em que você muda (um número digitado vale ao apertar Enter ou sair da caixa). O que estiver ligado fica lembrado e volta sozinho ao trocar de fase ou abrir o jogo de novo.", 21, Light()), 12);
        AddV(info, Wrapped("Restaurar originais desliga tudo e esquece a escolha.", 21, Light()), 12);
        AddV(info, Txt("EM USO AGORA", ItemVisuals.PixelUi, 22, White()), 28);
        foreach (var option in Rules.CombatKeys())
            if (adapter.On(option)) InfoLine(info, GameAdapter.CombatName(option), Rules.IsSwitch(option) ? "ligado" : "× " + Num(adapter.Option(option)));
        if (adapter.ActiveNames() == "nada ligado") InfoLine(info, "Combate", "normal");
        if (adapter.SlowMotion) InfoLine(info, "Câmera lenta", "ligada");
        InfoLine(info, "Lembrado", adapter.Remembers ? "sim" : "não");
        AddV(info, Txt("ATALHOS", ItemVisuals.PixelUi, 22, White()), 28);
        AddV(info, Wrapped("F5 invencibilidade, F6 sem recarga, F7 câmera lenta, F8 desliga tudo. Funcionam com o menu fechado; troque as teclas nas configurações do mod.", 19, Muted()), 10);
    }

    void CombatSwitch(UVerticalBox? column, string option, string detail, float top)
    {
        int at = Rules.CombatKeys().IndexOf(option);
        ToggleRow(column, option, GameAdapter.CombatName(option), detail, at >= 0 && combat[at] != 0, 0, top);
    }

    void CombatFactor(UVerticalBox? column, string option, string detail, int decimals, float top)
    {
        int at = Rules.CombatKeys().IndexOf(option);
        if (at < 0) return;
        var range = $" De {Num(Rules.CombatMin(option))} a {Num(Rules.CombatMax(option))}.";
        var spin = StepRow(column, "c:" + option, GameAdapter.CombatName(option), detail + range, Rules.CombatMin(option), Rules.CombatMax(option), combat[at], decimals, top);
        if (spin == null) return;
        spin.OnValueCommitted += CombatTyped;
        combatSpins.Add(spin);
        combatSpinKeys.Add(option);
    }

    static float CombatStep(string option) => option == "damage" ? 1 : option == "critdamage" ? 0.5f : 0.25f;

    void InfoLine(UVerticalBox? column, string name, string state)
    {
        var row = Ui.Row(tree);
        AddV(column, row, 10);
        AddH(row, Txt(name, ItemVisuals.Readable, 21, Muted()), 0, true, false);
        AddH(row, Txt(state.ToUpper(), ItemVisuals.PixelUi, 20, White()), 12, false, false);
    }

    // ---------------------------------------------------------------- World

    void WorldPage()
    {
        if (adapter == null) return;
        float leftWidth = contentWidth - detailWidth - Gap;
        var main = Ui.Row(tree);
        AddV(body, main, 0);
        var left = Ui.Column(tree);
        var leftScroll = Scroll();
        if (leftScroll == null || left == null) return;
        leftScroll.AddChild(left);
        AddH(main, Sized(leftScroll, leftWidth, contentHeight), 0, false, true);

        Section(left, "HORA DO DIA", 0);
        var time = adapter.TimeText();
        AddV(left, Wrapped(time == "" ? "Esta fase não tem ciclo de dia e noite." : "Agora: " + time, 21, Muted()), 10);
        if (time != "")
        {
            var hours = Ui.Row(tree);
            AddV(left, hours, 12);
            var dawn = WorldButton(hours, "AMANHECER", 0);
            if (dawn != null) dawn.OnClicked += Dawn;
            var noon = WorldButton(hours, "MEIO-DIA", 14);
            if (noon != null) noon.OnClicked += Noon;
            var dusk = WorldButton(hours, "ANOITECER", 14);
            if (dusk != null) dusk.OnClicked += Dusk;
            var midnight = WorldButton(hours, "MEIA-NOITE", 14);
            if (midnight != null) midnight.OnClicked += Midnight;
        }
        Section(left, "VELOCIDADE DO JOGO", 30);
        gameSpeedSpin = StepRow(left, "gamespeed", "Velocidade", "De 0,25 (câmera lenta) a 2 vezes a velocidade normal; muda na hora. Volta ao normal em outra fase.", 0.25f, 2, gameSpeedInput > 0 ? gameSpeedInput : adapter.GameSpeed, 2, 12);
        if (gameSpeedSpin != null) gameSpeedSpin.OnValueCommitted += GameSpeedTyped;
        var speedRow = Ui.Row(tree);
        AddV(left, speedRow, 14);
        var normal = WorldButton(speedRow, "NORMAL", 0);
        if (normal != null) normal.OnClicked += NormalGameSpeed;

        Section(left, "TEMPESTADE DE ALMAS", 30);
        var stormRow = Ui.Row(tree);
        AddV(left, stormRow, 12);
        var start = WorldButton(stormRow, "COMEÇAR AQUI", 0);
        if (start != null) start.OnClicked += StartStorm;
        var end = WorldButton(stormRow, "ENCERRAR", 14);
        if (end != null) end.OnClicked += EndStorm;
        var toGenerator = WorldButton(stormRow, "IR AO GERADOR", 14);
        if (toGenerator != null) toGenerator.OnClicked += GoGenerator;
        AddV(left, Wrapped("Experimental: dura 15 min (o tempo corre com o menu aberto). O jogo cria um gerador por vez; IR AO GERADOR leva ao que estiver ativo. A tempestade é da área onde o herói está; o jogo pode recusar fora das áreas que têm tempestade.", 18, Muted()), 8);

        Section(left, "INIMIGOS POR PERTO", 30);
        radiusSpin = StepRow(left, "radius", "Raio", "Em metros, ao redor do herói. Só inimigos: pets, invocações e aldeões ficam de fora.", 5, 50, radiusInput > 0 ? radiusInput : 20, 0, 12);
        var mobRow = Ui.Row(tree);
        AddV(left, mobRow, 14);
        var kill = WorldButton(mobRow, "ABATER", 0);
        if (kill != null) kill.OnClicked += KillNearby;
        var freeze = WorldButton(mobRow, adapter.HasFrozen ? "LIBERAR" : "CONGELAR", 14);
        if (freeze != null) freeze.OnClicked += FreezeNearby;

        Section(left, "POSIÇÕES", 30);
        PlaceRow(left, 0, 12);
        PlaceRow(left, 1, 10);
        PlaceRow(left, 2, 10);

        var info = DetailPanel(main, "MUNDO", detailWidth, Gap);
        if (info == null) return;
        AddV(info, Txt("COMO FUNCIONA", ItemVisuals.PixelUi, 22, White()), 4);
        AddV(info, Wrapped("A hora e a tempestade valem para a fase atual. As posições ficam guardadas até fechar o jogo e só servem na fase em que foram salvas.", 20, Light()), 10);
        AddV(info, Wrapped("F7 liga e desliga a câmera lenta; F8 também volta a velocidade ao normal e libera os inimigos congelados.", 20, Light()), 10);
        var stations = adapter.UnlockedStations();
        AddV(info, Txt($"VAGONETAS LIBERADAS  •  {stations.Count}", ItemVisuals.PixelUi, 22, White()), 24);
        foreach (var station in stations) AddV(info, Wrapped(Pretty(station.Replace("SW.MinecartStation.", "")), 18, Muted()), 6);
    }

    UButton? WorldButton(UHorizontalBox? row, string label, float left)
    {
        var button = Btn(label, ItemVisuals.LookSecondary, 20);
        if (button != null) AddH(row, Sized(button, 0, StepHeight), left, false, false);
        return button;
    }

    // A saved place: its state and the Salvar / Ir buttons.
    void PlaceRow(UVerticalBox? column, int index, float top)
    {
        if (adapter == null) return;
        var band = Band(column, top);
        if (band == null) return;
        AddH(band, Txt($"Posição {index + 1}  •  {(adapter.HasPlace(index) ? "salva" : "vazia")}", ItemVisuals.Readable, 24, White()), 0, true, false);
        var save = WorldButton(band, "SALVAR AQUI", 12);
        var go = WorldButton(band, "IR", 12);
        if (index == 0) { if (save != null) save.OnClicked += SavePlace1; if (go != null) go.OnClicked += GoPlace1; }
        else if (index == 1) { if (save != null) save.OnClicked += SavePlace2; if (go != null) go.OnClicked += GoPlace2; }
        else { if (save != null) save.OnClicked += SavePlace3; if (go != null) go.OnClicked += GoPlace3; }
    }

    // ---------------------------------------------------------------- Lists and details

    static int Columns(float width, float minEntry) => (int)Fit((width + 10) / (minEntry + 10), 1, 4);
    static int Rows(float height) => (int)Fit((height + 10) / (EntryHeight + 10), 1, 12);
    int Pages(int count) => count <= 0 || perPage <= 0 ? 1 : (count + perPage - 1) / perPage;
    static int Clamp(int page, int pages) => page < 0 ? 0 : page >= pages ? pages - 1 : page;

    UTexture2D? IconFor(int group, string id)
    {
        if (visuals == null) return null;
        if (group == PickEquipment) return visuals.Icon(GameAdapter.EquipmentTag(id));
        // Unique items keep their own enchantment among their effects: it shows its enchantment's icon.
        if (group == PickEffect) return id.StartsWith("SW.Enchantment.") ? IconFor(PickEnchantment, id) : visuals.EffectIcon(id);
        if (group == PickMob)
        {
            var mob = visuals.Icon(id);
            return mob != null ? mob : visuals.SkullIcon;
        }
        if (group != PickEnchantment) return visuals.Icon(id);
        // Enchantments without an icon of their own show their book.
        var icon = visuals.Icon(id);
        return icon != null ? icon : visuals.Icon("SW.Item.EnchantmentBook." + id.Substring(id.LastIndexOf(".") + 1));
    }

    string TitleFor(int group, string id)
    {
        if (visuals == null) return id;
        if (group == PickEquipment) return visuals.Title(GameAdapter.EquipmentTag(id));
        if (group == PickEffect) return EffectTitle(id);
        var title = visuals.Title(id);
        if (group != PickEnchantment || title != id) return title;
        var book = "SW.Item.EnchantmentBook." + id.Substring(id.LastIndexOf(".") + 1);
        var bookTitle = visuals.Title(book);
        return bookTitle != book ? bookTitle : id;
    }

    // One page of entries in a grid; icons and names were loaded by the page (first..last).
    void Grid(UVerticalBox? column, int group, List<string> ids, int first, int last, List<UTexture2D> icons, List<string> titles, string selected, float width, int columns)
    {
        if (column == null || tree == null) return;
        if (ids.Count == 0)
        {
            AddV(column, Wrapped("Nada encontrado com este filtro.", 21, Muted()), 18);
            return;
        }
        var grid = UGameplayStatics.SpawnObject(Unreal.ClassOf<UUniformGridPanel>(), tree) as UUniformGridPanel;
        if (grid == null) return;
        grid.SetSlotPadding(Ui.Margin(5));
        AddV(column, grid, 12);
        float entryWidth = width / columns - 10;
        for (int i = first; i < last; i++)
        {
            var id = ids[i];
            bool chosen = id == selected;
            grid.AddChildToUniformGrid(Entry(group, id, titles[i - first], icons[i - first], entryWidth, chosen, StripColor(group, id, chosen), false), (i - first) / columns, (i - first) % columns);
        }
    }

    FLinearColor StripColor(int group, string id, bool chosen)
    {
        if (group == PickEnchantment || group == PickEffect) return Accent();
        if (group == PickMob) return Ui.Color(0.85f, 0.32f, 0.3f, 1);
        if (group == PickItem && GameAdapter.IsTalisman(id)) return Gold();
        if (chosen && group == PickItem) return ItemVisuals.RarityColor(itemRarity);
        return id.ToLower().Contains("unique") ? ItemVisuals.RarityColor(3) : Ui.Color(0.4f, 0.44f, 0.5f, 1);
    }

    // One clickable line in the game's list-entry look: a colour strip, the icon in the game's item slot (its selected
    // frame when chosen, the equipped corner for worn gear) and the name, cut with "…" when too long.
    UWidget? Entry(int group, string id, string title, UTexture2D? icon, float width, bool selected, FLinearColor strip, bool equipped)
    {
        var button = Tile(group, id);
        var row = Ui.Row(tree);
        var bar = Border();
        if (button == null || row == null || bar == null) return null;
        StyleButton(button, ItemVisuals.LookList, false, 10, 6);
        bar.SetBrush(ItemVisuals.Rounded(strip, Ui.Color(0, 0, 0, 0), 0, 1));
        AddH(row, Sized(bar, 6, EntryHeight - 26), 0, false, false);
        AddH(row, IconFrame(icon, EntryHeight - 16, selected, equipped), 10, false, false);
        var name = Txt(title, ItemVisuals.Readable, 23, selected ? Gold() : White());
        name?.SetTextOverflowPolicy(ETextOverflowPolicy.Ellipsis);
        AddH(row, name, 14, true, false);
        Fill(button, row);
        var box = Selectable(button, width, EntryHeight, selected);
        box?.SetClipping(EWidgetClipping.ClipToBounds);
        return box;
    }

    // The icon centred in the game's item slot (its selected frame when chosen); the equipped corner on worn gear.
    UWidget? IconFrame(UTexture2D? icon, float size, bool selected, bool equipped)
    {
        var layers = Overlay();
        var frame = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        if (layers == null || frame == null) return null;
        frame.SetBrush(visuals != null ? visuals.SlotBrush(selected) : ItemVisuals.Rounded(Ui.Color(0.03f, 0.04f, 0.06f, 1), Ui.Color(1, 1, 1, 0.2f), 1, 3));
        Layer(layers, frame);
        UWidget? picture = icon != null ? Ui.Image(tree, icon, size * 0.8f, size * 0.8f) : Txt("?", ItemVisuals.PixelUi, (int)(size * 0.4f), Muted());
        var pictureSlot = layers.AddChildToOverlay(picture);
        pictureSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        pictureSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        if (equipped && visuals != null && visuals.Corner != null)
        {
            var cornerSlot = layers.AddChildToOverlay(Ui.Image(tree, visuals.Corner, size * 0.42f, size * 0.42f));
            cornerSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Right);
            cornerSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Top);
        }
        return Sized(layers, size, size);
    }

    // A detail panel's top, stacked: the big icon, the name in the title font and the rarity tag, centred.
    void HeroTall(UVerticalBox? column, UTexture2D? icon, string title, string badge, FLinearColor color, float size)
    {
        if (column == null) return;
        column.AddChildToVerticalBox(IconFrame(icon, size, true, false))?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        var name = Txt(title.ToUpper(), ItemVisuals.PixelTitle, 32, White());
        name?.SetAutoWrapText(true);
        name?.SetJustification(ETextJustify.Center);
        column.AddChildToVerticalBox(name)?.SetPadding(new FMargin { Top = 16 });
        if (badge == "") return;
        var badgeSlot = column.AddChildToVerticalBox(Badge(badge, color));
        badgeSlot?.SetPadding(new FMargin { Top = 12 });
        badgeSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
    }

    // A detail panel's top, side by side: the icon, then the rarity tag, the name and a line under it.
    void HeroWide(UVerticalBox? column, UTexture2D? icon, string title, string badge, FLinearColor color, string subtitle, float size)
    {
        var row = Ui.Row(tree);
        var texts = Ui.Column(tree);
        if (row == null || texts == null) return;
        AddV(column, row, 0);
        AddH(row, IconFrame(icon, size, true, false), 0, false, false);
        AddH(row, Sized(texts, DetailInner - size - 18, 0), 18, false, false);
        if (badge != "") texts.AddChildToVerticalBox(Badge(badge, color))?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Left);
        var name = Txt(title.ToUpper(), ItemVisuals.PixelTitle, 28, White());
        name?.SetAutoWrapText(true);
        AddV(texts, name, badge != "" ? 10 : 0);
        if (subtitle != "") AddV(texts, Wrapped(subtitle, 19, Muted()), 6);
    }

    // The four rarities, two by two, each with a strip in its colour; the chosen one framed in gold.
    void RarityGrid(UVerticalBox? column, int selected, float width)
    {
        var grid = UGameplayStatics.SpawnObject(Unreal.ClassOf<UUniformGridPanel>(), tree) as UUniformGridPanel;
        if (column == null || grid == null) return;
        grid.SetSlotPadding(Ui.Margin(4));
        AddV(column, grid, 10);
        float cell = (width - 16) / 2;
        for (int i = 0; i < ItemVisuals.RarityCount; i++)
        {
            var button = Tile(PickRarity, RarityId(i));
            var content = Ui.Row(tree);
            var bar = Border();
            if (button == null || content == null || bar == null) continue;
            StyleButton(button, ItemVisuals.LookList, false, 14, 8);
            var color = ItemVisuals.RarityColor(i);
            bar.SetBrush(ItemVisuals.Rounded(color, Ui.Color(0, 0, 0, 0), 0, 1));
            AddH(content, Sized(bar, 8, 30), 0, false, false);
            AddH(content, Txt(ItemVisuals.RarityName(i).ToUpper(), ItemVisuals.PixelUi, 19, i == selected ? color : White()), 12, false, false);
            Fill(button, content);
            grid.AddChildToUniformGrid(Selectable(button, cell, 56, i == selected), i / 2, i % 2);
        }
    }

    static string RarityId(int index) => index == 1 ? "1" : index == 2 ? "2" : index == 3 ? "3" : "0";
    static int RarityFromId(string id) => id == "1" ? 1 : id == "2" ? 2 : id == "3" ? 3 : 0;

    void Pager(UVerticalBox? column, int page, int count, string noun)
    {
        if (column == null) return;
        var row = Ui.Row(tree);
        if (row == null) return;
        var slot = column.AddChildToVerticalBox(row);
        slot?.SetPadding(new FMargin { Top = 14 });
        slot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        int pages = Pages(count);
        if (pages <= 1)
        {
            AddH(row, Txt($"{count} {noun}", ItemVisuals.Readable, 19, Muted()), 0, false, false);
            return;
        }
        AddH(row, PageButton(false), 0, false, false);
        AddH(row, Txt($"PÁGINA {page + 1} DE {pages}", ItemVisuals.PixelUi, 20, White()), 18, false, false);
        AddH(row, Txt($"{count} {noun}", ItemVisuals.Readable, 19, Muted()), 14, false, false);
        AddH(row, PageButton(true), 18, false, false);
    }

    UWidget? PageButton(bool next)
    {
        var button = Tile(PickPage, next ? "+" : "-");
        if (button == null) return null;
        StyleButton(button, ItemVisuals.LookSecondary, false, 2, 2);
        Center(button, Txt(next ? ">" : "<", ItemVisuals.PixelUi, 26, LookColor(ItemVisuals.LookSecondary, false)));
        return Sized(button, 58, 50);
    }

    // "14891" as the game writes it: 14.891.
    static string Group(string digits)
    {
        if (digits.Length <= 3 || !"0123456789".Contains(digits.Substring(0, 1))) return digits;
        int first = digits.Length % 3;
        if (first == 0) first = 3;
        string result = digits.Substring(0, first);
        for (int i = first; i < digits.Length; i += 3) result += "." + digits.Substring(i, 3);
        return result;
    }

    // A multiplier with up to two decimals and a comma: 1, 2,5, 0,25.
    static string Num(float number)
    {
        int whole = (int)number;
        int hundredths = (int)((number - whole) * 100 + 0.5f);
        if (hundredths >= 100) return $"{whole + 1}";
        if (hundredths <= 0) return $"{whole}";
        if (hundredths % 10 == 0) return $"{whole},{hundredths / 10}";
        return hundredths < 10 ? $"{whole},0{hundredths}" : $"{whole},{hundredths}";
    }

    /// <summary>A box was clicked: a tab, an entry, a filter, a rarity, a switch, a step, a page or a quick amount.</summary>
    public void Pick(int group, string id)
    {
        if (group == PickStep) { Step(id); return; }
        if (group == PickAmount)
        {
            if (value != null) value.SetValue(UKismetStringLibrary.Conv_StringToInt(id));
            return;
        }
        if (group == PickZoom) { Rezoom(id == "+" ? ZoomStep : -ZoomStep); return; }
        Capture();
        if (group == PickTab) OpenTab(UKismetStringLibrary.Conv_StringToInt(id));
        else if (group == PickPage)
        {
            int delta = id == "+" ? 1 : -1;
            if (tab == TabItems) itemPage += delta;
            else if (tab == TabMobs) mobPage += delta;
            else if (tab == TabEnchants && effectsMode) effectPage += delta;
            else if (tab == TabEnchants) enchantPage += delta;
        }
        else if (group == PickMode)
        {
            // Artifacts take effects but no enchantment: the worn list is made again for the other mode.
            if (effectsMode != (id == "effects")) equipIds.Clear();
            effectsMode = id == "effects";
        }
        else if (group == PickEffect) selectedEffect = id;
        else if (group == PickEffectRemove)
        {
            var removed = adapter != null && selectedEquip != "" ? adapter.RemoveItemEffect(selectedEquip, id) : "";
            Page();
            Report(removed);
            return;
        }
        else if (group == PickTalismanLevel)
        {
            int level = UKismetStringLibrary.Conv_StringToInt(id);
            // The hero's own talisman changes at once; otherwise this is the level to create it at.
            if (adapter != null && adapter.TalismanLevel(selectedItem, out var levels) > 0)
            {
                var result = adapter.SetTalismanLevel(selectedItem, level);
                Page();
                Report(result);
                return;
            }
            talismanLevel = level;
        }
        else if (group == PickToggle)
        {
            if (id == "free")
            {
                freeMode = !freeMode;
                enchantTags.Clear();
                enchantPage = 0;
                selectedEnchant = "";
            }
            else
            {
                int at = Rules.CombatKeys().IndexOf(id);
                if (at >= 0) combat[at] = combat[at] != 0 ? 0 : 1;
                ApplyCombat(id);
                return;
            }
        }
        else if (group == PickItem)
        {
            selectedItem = id;
            itemRarity = id.ToLower().Contains("unique") ? 3 : 0;
            talismanLevel = 1;
        }
        else if (group == PickCategory)
        {
            category = id;
            itemPage = 0;
        }
        else if (group == PickRarity)
        {
            int rarity = RarityFromId(id);
            if (tab == TabItems) itemRarity = rarity;
            else if (tab == TabEnchants && adapter != null && selectedEquip != "")
            {
                var result = adapter.SetEquippedRarity(selectedEquip, ItemVisuals.RarityTag(rarity));
                Page();
                Report(result);
                return;
            }
        }
        else if (group == PickEquipment)
        {
            selectedEquip = id;
            selectedEnchant = "";
            powerInput = 0;
            enchantPage = 0;
            enchantTags.Clear();
        }
        else if (group == PickEnchantment) selectedEnchant = id;
        else if (group == PickResource) selectedResource = id;
        else if (group == PickMob) selectedMob = id;
        Page();
    }

    // ---------------------------------------------------------------- Actions

    void AddResource() { ChangeResource(true); }
    void SetResource() { ChangeResource(false); }

    void ChangeResource(bool add)
    {
        if (adapter == null || value == null) return;
        resourceAmount = (int)value.GetValue();
        var result = adapter.SetResource(selectedResource, resourceAmount, add);
        Page();
        Report(result);
    }

    void Spawn()
    {
        if (adapter == null || value == null) return;
        if (tab == TabItems)
        {
            if (selectedItem == "") { Report("Escolha um item na lista."); return; }
            itemQuantity = (int)value.GetValue();
            Report(adapter.SpawnItem(selectedItem, itemQuantity, ItemVisuals.RarityTag(itemRarity)));
        }
        else if (tab == TabMobs)
        {
            if (selectedMob == "") { Report("Escolha um mob na lista."); return; }
            mobQuantity = (int)value.GetValue();
            Report(adapter.SpawnMob(selectedMob, mobQuantity));
        }
    }

    void RemoveMobs() { if (adapter != null) Report(adapter.RemoveSpawnedMobs()); }

    void SpawnTalisman()
    {
        if (adapter == null || selectedItem == "") return;
        var result = adapter.SpawnTalisman(selectedItem, talismanLevel);
        Page();
        Report(result);
    }

    void AddEffect()
    {
        if (adapter == null || selectedEquip == "" || selectedEffect == "") return;
        Capture();
        var result = adapter.SetItemEffect(selectedEquip, selectedEffect, effectLevel);
        Page();
        Report(result);
    }

    // A level typed in the effect's box: Enter redraws the description, leaving the box keeps it for the button.
    void EffectLevelTyped(float number, ETextCommit method)
    {
        effectLevel = (int)number;
        if (method == ETextCommit.OnEnter) Page();
    }

    void ApplyPower()
    {
        if (adapter == null || powerSpin == null || selectedEquip == "") return;
        var result = adapter.SetItemPower(selectedEquip, (int)powerSpin.GetValue());
        powerInput = 0;
        Page();
        Report(result);
    }

    void WriteEnchant()
    {
        if (adapter == null || value == null || selectedEquip == "" || selectedEnchant == "") return;
        enchantLevel = (int)value.GetValue();
        var result = adapter.WriteEnchantment(selectedEquip, selectedEnchant, enchantLevel);
        Page();
        Report(result);
    }

    void Enchant()
    {
        if (adapter == null || value == null || selectedEquip == "" || selectedEnchant == "") return;
        enchantLevel = (int)value.GetValue();
        var result = adapter.Enchant(selectedEquip, selectedEnchant, enchantLevel);
        Page();
        Report(result);
    }

    /// <summary>
    /// Applies every combat and hero option on screen at once, right after one of them (key) changed; what the game
    /// refused goes back to what is in force.
    /// </summary>
    void ApplyCombat(string key)
    {
        if (adapter == null) return;
        Capture();
        Report(SetCombat(key));
        Page();
    }

    string SetCombat(string key)
    {
        if (adapter == null) return "";
        int at = Rules.CombatKeys().IndexOf(key);
        float wanted = at >= 0 ? combat[at] : 0;
        var result = adapter.SetCombat(combat);
        combat.Clear();
        foreach (var option in Rules.CombatKeys()) combat.Add(adapter.Option(option));
        if (at < 0 || combat[at] != wanted) return result;
        // "Invencibilidade" is feminine, like the F5 notice.
        if (Rules.IsSwitch(key)) return $"{GameAdapter.CombatName(key)}: {(wanted != 0 ? "ligad" : "desligad")}{(key == "god" ? "a" : "o")}.";
        return $"{GameAdapter.CombatName(key)}: × {Num(wanted)}.";
    }

    // A factor typed in a box: Enter applies it and redraws the page; leaving the box (a click somewhere else) applies it
    // without redrawing, or that click would land on a button that was just rebuilt.
    void CombatTyped(float number, ETextCommit method)
    {
        if (adapter == null) return;
        Capture();
        for (int i = 0; i < combatSpins.Count; i++)
        {
            var key = combatSpinKeys[i];
            if (combat[Rules.CombatKeys().IndexOf(key)] == adapter.Option(key)) continue;
            Report(SetCombat(key));
            if (method == ETextCommit.OnEnter) Page();
            else for (int j = 0; j < combatSpins.Count; j++) combatSpins[j].SetValue(adapter.Option(combatSpinKeys[j]));
            return;
        }
    }

    void Restore()
    {
        if (adapter != null) adapter.Restore();
        if (adapter != null) adapter.ForgetCombat();
        combat.Clear();
        foreach (var option in Rules.CombatKeys()) combat.Add(Rules.Neutral(option));
        Page();
        Report(adapter != null ? adapter.Status() : "Sem personagem");
    }

    void SetLevel()
    {
        if (adapter == null || levelSpin == null) return;
        var result = adapter.SetHeroLevel((int)levelSpin.GetValue());
        levelInput = -1;
        Page();
        Report(result);
    }

    void EnchantsmithTyped(float number, ETextCommit method) { VendorTyped("enchantsmith", enchantsmithSpin, method); }
    void BlacksmithTyped(float number, ETextCommit method) { VendorTyped("blacksmith", blacksmithSpin, method); }
    void MerchantTyped(float number, ETextCommit method) { VendorTyped("merchant", merchantSpin, method); }

    // A level typed in a vendor's box, like CombatTyped: Enter redraws the page, leaving the box doesn't.
    void VendorTyped(string vendor, USpinBox? spin, ETextCommit method)
    {
        if (adapter == null || spin == null) return;
        adapter.VendorLevelInfo(vendor, out var level, out var maximum);
        if ((int)spin.GetValue() == level) return;
        if (method == ETextCommit.OnEnter) { ApplyVendor(vendor, spin); return; }
        var result = adapter.SetVendorLevel(vendor, (int)spin.GetValue());
        adapter.VendorLevelInfo(vendor, out level, out maximum);
        spin.SetValue(level);
        Report(result);
    }

    void ApplyVendor(string vendor, USpinBox? spin)
    {
        if (adapter == null || spin == null) return;
        var result = adapter.SetVendorLevel(vendor, (int)spin.GetValue());
        enchantsmithInput = -1;
        blacksmithInput = -1;
        merchantInput = -1;
        Page();
        Report(result);
    }

    void RefillRefreshes()
    {
        if (adapter == null) return;
        var result = adapter.RefreshMerchantStock();
        Page();
        Report(result);
    }

    void ClearEnchant()
    {
        if (adapter == null || selectedEquip == "") return;
        Capture();
        var result = adapter.ClearEnchantment(selectedEquip);
        enchantTags.Clear();
        Page();
        Report(result);
    }

    // Moments of the game's own day (its first half is day, the second night).
    void Dawn() { SetDayMoment(0); }
    void Noon() { SetDayMoment(1); }
    void Dusk() { SetDayMoment(2); }
    void Midnight() { SetDayMoment(3); }

    void SetDayMoment(int moment)
    {
        if (adapter == null) return;
        Capture();
        var result = adapter.SetDayMoment(moment);
        Page();
        Report(result);
    }

    void ApplyGameSpeed()
    {
        if (adapter == null || gameSpeedSpin == null) return;
        var result = adapter.SetGameSpeed(gameSpeedSpin.GetValue());
        gameSpeedInput = 0;
        Page();
        Report(result);
    }

    // A speed typed in the box, like CombatTyped: Enter redraws the page, leaving the box doesn't.
    void GameSpeedTyped(float number, ETextCommit method)
    {
        if (adapter == null || gameSpeedSpin == null || gameSpeedSpin.GetValue() == adapter.GameSpeed) return;
        if (method == ETextCommit.OnEnter) { ApplyGameSpeed(); return; }
        var result = adapter.SetGameSpeed(gameSpeedSpin.GetValue());
        gameSpeedInput = 0;
        gameSpeedSpin.SetValue(adapter.GameSpeed);
        Report(result);
    }

    void NormalGameSpeed()
    {
        if (adapter == null) return;
        var result = adapter.SetGameSpeed(1);
        gameSpeedInput = 0;
        Page();
        Report(result);
    }

    void StartStorm() { WorldResult(adapter != null ? adapter.StartSoulStorm() : ""); }
    void EndStorm() { WorldResult(adapter != null ? adapter.EndSoulStorm() : ""); }
    void GoGenerator() { WorldResult(adapter != null ? adapter.GoToGenerator() : ""); }
    void KillNearby() { Capture(); WorldResult(adapter != null ? adapter.KillNearbyMobs(radiusInput) : ""); }
    void FreezeNearby() { Capture(); WorldResult(adapter != null ? adapter.ToggleFreeze(radiusInput) : ""); }
    void SavePlace1() { WorldResult(adapter != null ? adapter.SavePlace(0) : ""); }
    void SavePlace2() { WorldResult(adapter != null ? adapter.SavePlace(1) : ""); }
    void SavePlace3() { WorldResult(adapter != null ? adapter.SavePlace(2) : ""); }
    void GoPlace1() { WorldResult(adapter != null ? adapter.GoToPlace(0) : ""); }
    void GoPlace2() { WorldResult(adapter != null ? adapter.GoToPlace(1) : ""); }
    void GoPlace3() { WorldResult(adapter != null ? adapter.GoToPlace(2) : ""); }

    void WorldResult(string result)
    {
        if (adapter == null) return;
        Page();
        Report(result);
    }

    // Typing in a box: Q and E are letters there, not tab keys.
    bool Typing()
    {
        if (Focused(query)) return true;
        foreach (var spin in spins) if (Focused(spin)) return true;
        return false;
    }

    static bool Focused(UWidget? widget) => widget != null && (widget.HasKeyboardFocus() || widget.HasFocusedDescendants());

    public override FEventReply OnKeyDown(FGeometry geometry, FKeyEvent keyEvent)
    {
        var key = UKismetInputLibrary.GetKey(keyEvent).KeyName;
        if (key == toggleKey || key == "Escape" || key == "Gamepad_FaceButton_Right")
        {
            Close();
            return UWidgetBlueprintLibrary.Handled();
        }
        bool back = key == "Q" || key == "Gamepad_LeftShoulder";
        bool forward = key == "E" || key == "Gamepad_RightShoulder";
        if ((back || forward) && !Typing())
        {
            Capture();
            OpenTab(forward ? (tab + 1) % TabCount : (tab + TabCount - 1) % TabCount);
            Page();
            return UWidgetBlueprintLibrary.Handled();
        }
        return UWidgetBlueprintLibrary.Unhandled();
    }
}
