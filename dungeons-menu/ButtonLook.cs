using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.SlateCore;
using UE.UMG;

namespace DungeonsMenu;

/// <summary>
/// One of the game's button looks, read from its CommonUI button style (ButtonStyle_Tab, ButtonStyle_ListEntry, …): the
/// brush of each state and the text style's font, colour and shadow. Only the look is used: the game's own button widgets
/// are never made.
/// </summary>
public class ButtonLook : UObject
{
    public bool Ok;
    public FSlateBrush Normal;
    public FSlateBrush Hovered;
    public FSlateBrush Pressed;
    public FSlateBrush Selected;
    public FSlateBrush SelectedHovered;
    public FSlateBrush SelectedPressed;
    public bool HasFont;
    public FSlateFontInfo Font;
    public FLinearColor TextColor;
    public FLinearColor SelectedTextColor;
    public bool HasShadow;
    public FVector2D ShadowOffset;
    public FLinearColor ShadowColor;

    /// <summary>Reads a button style class ("/OreUI/…/ButtonStyle_Tab.ButtonStyle_Tab_C"); false when it isn't in the game.</summary>
    public bool Load(string classPath, List<UObject> keep)
    {
        TextColor = new FLinearColor { R = 1, G = 1, B = 1, A = 1 };
        SelectedTextColor = TextColor;
        var styleClass = Unreal.LoadClass<UCommonButtonStyle>(classPath);
        var style = styleClass != null ? UGameplayStatics.SpawnObject(styleClass, this) as UCommonButtonStyle : null;
        if (style == null) return false;
        keep.Add(style);
        style.GetNormalBaseBrush(out var normal);
        style.GetNormalHoveredBrush(out var hovered);
        style.GetNormalPressedBrush(out var pressed);
        style.GetSelectedBaseBrush(out var selected);
        style.GetSelectedHoveredBrush(out var selectedHovered);
        style.GetSelectedPressedBrush(out var selectedPressed);
        Normal = normal;
        Hovered = Drawn(hovered) ? hovered : normal;
        Pressed = Drawn(pressed) ? pressed : Hovered;
        // Styles that are never selected leave their selected brushes empty, which would draw a white box.
        Selected = Drawn(selected) ? selected : Hovered;
        SelectedHovered = Drawn(selectedHovered) ? selectedHovered : Selected;
        SelectedPressed = Drawn(selectedPressed) ? selectedPressed : Selected;
        var text = style.GetNormalTextStyle();
        if (text != null)
        {
            keep.Add(text);
            text.GetFont(out var font);
            if (font.FontObject != null)
            {
                Font = font;
                HasFont = true;
            }
            text.GetColor(out var color);
            TextColor = color;
            SelectedTextColor = color;
            HasShadow = text.bUsesDropShadow;
            text.GetShadowOffset(out var shadowOffset);
            text.GetShadowColor(out var shadowColor);
            ShadowOffset = shadowOffset;
            ShadowColor = shadowColor;
        }
        var selectedText = style.GetSelectedTextStyle();
        if (selectedText != null)
        {
            keep.Add(selectedText);
            selectedText.GetColor(out var selectedColor);
            SelectedTextColor = selectedColor;
        }
        Ok = true;
        return true;
    }

    // A brush draws nothing (on purpose), a rounded box, or a texture or material; an image brush with neither is a
    // style's empty default.
    static bool Drawn(FSlateBrush brush) =>
        brush.DrawAs == ESlateBrushDrawType.NoDrawType || brush.DrawAs == ESlateBrushDrawType.RoundedBox || brush.ResourceObject != null;

    /// <summary>The look as a UMG button style; selected uses the style's selected brushes.</summary>
    public FButtonStyle Style(bool selected, FMargin padding) => new FButtonStyle
    {
        Normal = selected ? Selected : Normal,
        Hovered = selected ? SelectedHovered : Hovered,
        Pressed = selected ? SelectedPressed : Pressed,
        Disabled = Normal,
        NormalPadding = padding,
        PressedPadding = padding,
    };
}
