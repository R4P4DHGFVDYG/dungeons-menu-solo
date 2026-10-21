using NeoRune;
using UE.Engine;
using UE.InputCore;

namespace DungeonsMenu;

[ModSetting.Keybind("menu_key", "Abrir menu", Default = "Insert")]
[ModSetting.Keybind("hotkey_god", "Atalho: invencibilidade", Default = "F5")]
[ModSetting.Keybind("hotkey_cooldown", "Atalho: sem tempo de recarga", Default = "F6")]
[ModSetting.Keybind("hotkey_slow", "Atalho: câmera lenta", Default = "F7")]
[ModSetting.Keybind("hotkey_off", "Atalho: desligar tudo", Default = "F8")]
[ModSetting.Text("Use o iniciador para verificar a versão e criar backup. Apenas solo/offline.")]
public class ModActor : AActor, IModSettings
{
    GameAdapter? adapter;
    KeyListener? listener;
    KeyListener? godListener;
    KeyListener? cooldownListener;
    KeyListener? slowListener;
    KeyListener? offListener;
    Menu? menu;
    string toggleKey = "Insert";
    string godKey = "F5";
    string cooldownKey = "F6";
    string slowKey = "F7";
    string offKey = "F8";
    double nextBind;

    protected override void ReceiveBeginPlay()
    {
        SetTickableWhenPaused(true);
        adapter = UGameplayStatics.SpawnObject(Unreal.ClassOf<GameAdapter>(), this) as GameAdapter;
        if (adapter != null) adapter.Initialize(this);
        Bind();
    }

    void Bind()
    {
        if (listener != null && UKismetSystemLibrary.IsValid(listener)) listener.Stop();
        listener = Input.OnKey(this, toggleKey);
        if (listener != null) listener.Pressed += Toggle;
        BindHotkeys();
    }

    // The hotkeys work with the menu closed (keys don't reach mods while a menu has the keyboard).
    void BindHotkeys()
    {
        StopListener(godListener);
        StopListener(cooldownListener);
        StopListener(slowListener);
        StopListener(offListener);
        godListener = Input.OnKey(this, godKey);
        if (godListener != null) godListener.Pressed += HotGod;
        cooldownListener = Input.OnKey(this, cooldownKey);
        if (cooldownListener != null) cooldownListener.Pressed += HotCooldown;
        slowListener = Input.OnKey(this, slowKey);
        if (slowListener != null) slowListener.Pressed += HotSlow;
        offListener = Input.OnKey(this, offKey);
        if (offListener != null) offListener.Pressed += HotOff;
        MenuLog.Write($"[diag] hotkeys bound {godKey}={godListener != null} {cooldownKey}={cooldownListener != null} {slowKey}={slowListener != null} {offKey}={offListener != null}");
    }

    static void StopListener(KeyListener? key)
    {
        if (key != null && UKismetSystemLibrary.IsValid(key)) key.Stop();
    }

    void HotGod() { if (adapter != null) Notice(adapter.QuickToggle("god")); }
    void HotCooldown() { if (adapter != null) Notice(adapter.QuickToggle("cooldown")); }
    void HotSlow() { if (adapter != null) Notice(adapter.ToggleSlowMotion()); }
    void HotOff() { if (adapter != null) Notice(adapter.StopAll()); }

    void Notice(string text)
    {
        MenuLog.Write("[hotkey] " + text);
        GameUI.Toast(this, "Menu Solo: " + text, 2.5f);
    }

    void Toggle()
    {
        if (adapter == null) return;
        if (menu != null && UKismetSystemLibrary.IsValid(menu) && menu.IsActivated())
        {
            menu.Close();
            return;
        }
        // The game's slot boxes and fonts are loaded before the menu's widgets exist (loading can run the garbage collector).
        if (adapter.Visuals != null) adapter.Visuals.Preload();
        UiCatalog.DumpOnce();
        var layer = World.Player(this) == null ? GameLayers.Menu : GameLayers.InGameMenu;
        menu = GameUI.Open(this, layer, Unreal.ClassOf<Menu>()) as Menu;
        if (menu != null) menu.Setup(adapter, toggleKey);
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        if (adapter != null) adapter.Tick();
        var now = World.RealTime(this);
        if (now < nextBind) return;
        nextBind = now + 1;
        if ((listener == null || !UKismetSystemLibrary.IsValid(listener)) && World.PlayerController(this) != null) Bind();
    }

    protected override void ReceiveEndPlay(EEndPlayReason reason)
    {
        if (adapter != null) adapter.Shutdown();
        if (menu != null && UKismetSystemLibrary.IsValid(menu)) menu.Close();
        StopListener(listener);
        StopListener(godListener);
        StopListener(cooldownListener);
        StopListener(slowListener);
        StopListener(offListener);
    }

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey)
    {
        var name = key.KeyName.ToString();
        if (id == "menu_key") toggleKey = name;
        else if (id == "hotkey_god") godKey = name;
        else if (id == "hotkey_cooldown") cooldownKey = name;
        else if (id == "hotkey_slow") slowKey = name;
        else if (id == "hotkey_off") offKey = name;
        else return;
        Bind();
    }
    public void OnSettingsReset()
    {
        toggleKey = "Insert";
        godKey = "F5";
        cooldownKey = "F6";
        slowKey = "F7";
        offKey = "F8";
        Bind();
    }
    public void OnButtonPressed(string id) { }
    public void OnSettingChanged(string id, string value) { }
}

