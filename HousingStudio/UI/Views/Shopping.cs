using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Shopping (from Pieces' foot and Done): what's still to get, by where it comes from: your retainers,
/// vendors (the NPC, where, the price, a flag on the map), the market board (an estimate), crafting
/// (Open in Teamcraft) and what's not known; with the gil in all and Copy list.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly (SourceKind Source, string Title)[] ShopGroups =
    {
        (SourceKind.Retainer, "On your retainers"),
        (SourceKind.Vendor, "Buy from vendors"),
        (SourceKind.Market, "Market board"),
        (SourceKind.Craft, "Craft"),
        (SourceKind.Unknown, "Not known"),
    };

    /// <summary>Where Shopping's back arrow goes (Pieces, or the main view).</summary>
    private Sub shoppingFrom;

    /// <summary>Where everything still needed comes from (built again only when something changes).</summary>
    private ShoppingPlan ShopPlan() =>
        session.Plan == null ? ShoppingPlan.Empty : Plugin.Shop.Plan(session.Plan, vm.MissingDyes, session.Context.District ?? session.Layout?.District);

    private void OpenShopping(Sub from)
    {
        shoppingFrom = from;
        sub = Sub.Shopping;
    }

    private void DrawShopping()
    {
        var shop = ShopPlan();
        var known = session.Plan != null;

        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var eyebrowH = Txt.Natural(Face.Mono, 12);
        var titleH = S(26 * 1.1f);
        var headH = MathF.Max(S(40), eyebrowH + S(2) + titleH);
        if (BackButton(new Vector2(at.X, at.Y + (headH - S(40)) / 2), shoppingFrom == Sub.Pieces ? "Back to Pieces" : $"Back to {session.Title}"))
            sub = shoppingFrom;
        var tx = at.X + S(40) + S(10);
        var top = at.Y + (headH - (eyebrowH + S(2) + titleH)) / 2;
        Kit.Eyebrow(new Vector2(tx, top), session.Title, null, at.X + w - tx);
        var things = shop.Lines.Sum(l => l.Count);
        Title(new Vector2(tx, top + eyebrowH + S(2)), things == 0 ? "Nothing to get" : Words.Count(things, "thing") + " to get", null, at.X + w - tx, 26);
        Kit.Reserve(at, w, headH);
        Frame.EndHeader();

        Frame.BeginBody("##shopping", "shopping-foot", 16, 22);
        if (!known)
        {
            AlertNote("Go to your home to see what you have: what's still missing is worked out from your storeroom and bags there.", Palette.Dim, Icon.Info);
            Kit.Gap(14);
        }
        if (Plugin.Shop.Status() is { } status)
        {
            AlertNote(status, Palette.Dim, Icon.Info);
            Kit.Gap(14);
        }
        if (known && shop.IsEmpty)
        {
            Kit.BeginPanel("##shop-none", 18, 20, 18);
            Txt.Note("You have every piece, and the dyes for them.");
            Kit.EndPanel();
        }
        var groups = ShopGroupsOf(shop);
        for (var g = 0; g < groups.Count; g++)
        {
            var (source, head, lines) = groups[g];
            if (g > 0) Kit.Gap(18);
            Kit.EyebrowRow(head);
            Kit.Gap(10);
            Kit.BeginPanel($"##shop-{source}", 0, 0, 0);
            for (var i = 0; i < lines.Count; i++) ShopRow(i, lines[i]);
            if (source == SourceKind.Craft) TeamcraftRow(lines);
            Kit.EndPanel();
        }
        Frame.EndBody();

        DrawShoppingFooter(shop, known);
    }

    private (ShoppingPlan? For, List<(SourceKind Source, string Head, List<ShoppingLine> Lines)> Groups) shopGroups = (null, new());

    /// <summary>The groups to draw, sorted by name, with their headings: worked out again only when the plan changes.</summary>
    private List<(SourceKind Source, string Head, List<ShoppingLine> Lines)> ShopGroupsOf(ShoppingPlan shop)
    {
        if (ReferenceEquals(shopGroups.For, shop)) return shopGroups.Groups;
        var groups = new List<(SourceKind, string, List<ShoppingLine>)>();
        foreach (var (source, title) in ShopGroups)
        {
            var lines = shop.Of(source).OrderBy(l => data.AnyItemName(l.ItemId), StringComparer.OrdinalIgnoreCase).ToList();
            if (lines.Count == 0) continue;
            var gil = lines.Sum(l => l.Gil);
            var head = source switch
            {
                SourceKind.Vendor => $"{title} · {ShoppingList.Gil(gil)}",
                SourceKind.Market when gil > 0 => $"{title} · about {ShoppingList.Gil(gil)}",
                _ => title,
            };
            groups.Add((source, $"{head} · {lines.Sum(l => l.Count)}", lines));
        }
        shopGroups = (shop, groups);
        return groups;
    }

    /// <summary>A row: the item's icon (a dye's swatch), its name and count, where it comes from, the gil, and for a vendor a flag on the map.</summary>
    private void ShopRow(int index, ShoppingLine line)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var nameH = Txt.Natural(Face.Sans, 14);
        var h = S(10) * 2 + MathF.Max(S(34), nameH + S(2) + S(19.5f));
        var clipTop = ImGui.GetWindowPos().Y;
        var clipBottom = clipTop + ImGui.GetWindowHeight();
        if (at.Y > clipBottom || at.Y + h < clipTop)
        {
            Kit.Reserve(at, w, h);
            return;
        }
        var dl = Kit.Dl;
        if (index > 0) Kit.Divider(Kit.Panel.Start.X, Kit.Panel.Start.X + Kit.Panel.Width, at.Y - 0.5f);
        var mid = at.Y + h / 2;
        var x = at.X + S(18);
        if (line.Need.Kind == NeedKind.Dye)
        {
            dl.AddRectFilled(new Vector2(x, mid - S(17)), new Vector2(x + S(34), mid + S(17)), Palette.U32(Palette.Photo), S(Palette.RadiusSm));
            Kit.Swatch(dl, new Vector2(x + S(17), mid), data.DyeRgb(line.Need.Stain));
        }
        else
        {
            Kit.Thumb(new Vector2(x, mid - S(17)), S(34), S(Palette.RadiusSm), data.ItemIcon(line.ItemId));
        }
        x += S(34) + S(12);

        var vendor = line.Vendor;
        var pinW = vendor != null ? S(32) + S(12) : 0;
        var gil = line.Source switch
        {
            SourceKind.Vendor => ShoppingList.Gil(line.Gil),
            SourceKind.Market => "≈ " + ShoppingList.Gil(line.Gil),
            _ => string.Empty,
        };
        var gilW = gil.Length > 0 ? Txt.Measure(gil, Face.Mono, 12).X + S(12) : 0;
        var right = at.X + w - S(18);
        var textW = right - pinW - gilW - x;

        var name = data.AnyItemName(line.ItemId) + (line.Count > 1 ? $" ×{line.Count}" : string.Empty);
        var note = ShopNote(line);
        var top = mid - (nameH + S(2) + S(19.5f)) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.Sans, 14, textW), Face.Sans, 14, Palette.Ivory);
        Txt.Line(dl, new Vector2(x, top + nameH + S(2)), Txt.Fit(note, Face.Sans, 13, textW), Face.Sans, 13, Palette.Ash, 19.5f);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + textW, top + nameH + S(21.5f)))
            && Txt.Measure(note, Face.Sans, 13).X > textW)
            Kit.Tooltip(note);

        if (gil.Length > 0)
            Txt.Line(dl, new Vector2(right - pinW - gilW + S(12), mid - Txt.Natural(Face.Mono, 12) / 2), gil, Face.Mono, 12, Palette.Text);
        if (vendor != null)
        {
            var tip = vendor.Located ? $"Flag {vendor.NpcName} on the map" : "Where this vendor stands isn't known";
            if (Kit.IconButton($"##flag{index}-{line.ItemId}", new Vector2(right - S(32), mid - S(16)), Icon.MapPin, tip, 32, Btn.Glass, !vendor.Located))
                FlagOnMap(vendor);
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>The row's second line: where it comes from, in words.</summary>
    private string ShopNote(ShoppingLine line)
    {
        var dye = line.Need.Kind == NeedKind.Dye ? "Dye · " : string.Empty;
        switch (line.Source)
        {
            case SourceKind.Retainer:
                var r = line.Retainer!;
                return dye + (r.SeenAt is { } seen
                    ? $"On {r.Name} · seen {When.Relative(seen.ToLocalTime(), DateTime.Now, Clock)}"
                    : $"On {r.Name} · as Allagan Tools has it");
            case SourceKind.Vendor:
                var v = line.Vendor!;
                return dye + $"{v.Where()} · {ShoppingList.Gil(v.Price)} each" + (v.NeedsUnlock ? " · after a quest" : string.Empty);
            case SourceKind.Market:
                var cost = line.MarketCost;
                var each = line.Count > 0 ? line.Gil / line.Count : 0;
                var extra = cost != null && cost.Covered > line.Count ? $" · whole stacks: {cost.Covered} bought" : string.Empty;
                return dye + $"About {ShoppingList.Gil(each)} each on {line.Market?.Scope}" + extra;
            case SourceKind.Craft:
                var c = line.Craft!;
                var none = !config.LookUpMarketPrices ? "no vendor sells it" : "no vendor sells it, none listed";
                return dye + (c.Level > 0 ? $"{c.Job}, level {c.Level} · {none}" : $"{c.Job} · {none}");
            default:
                if (!Plugin.Shop.Vendors.Marketable(line.ItemId)) return dye + "Not sold by vendors, and can't be traded";
                return dye + (config.LookUpMarketPrices ? "Not sold by vendors, and none listed" : "Not sold by vendors (market prices are off)");
        }
    }

    /// <summary>The Craft group's last row: Open in Teamcraft, with the import text copied as well.</summary>
    private void TeamcraftRow(List<ShoppingLine> lines)
    {
        var at = Kit.At;
        var w = Kit.Width;
        Kit.Divider(Kit.Panel.Start.X, Kit.Panel.Start.X + Kit.Panel.Width, at.Y - 0.5f);
        const string label = "Open in Teamcraft";
        var bw = Kit.ButtonWidth(label, BtnSize.Sm);
        var h = S(12) * 2 + S(34);
        var noteW = w - S(18) * 2 - bw - S(12);
        Txt.Line(Kit.Dl, new Vector2(at.X + S(18), at.Y + h / 2 - S(19.5f) / 2), Txt.Fit("A crafting list of these, and what they're made from", Face.Sans, 13, noteW),
            Face.Sans, 13, Palette.Ash, 19.5f);
        var items = lines.Select(l => (l.ItemId, l.Count)).ToList();
        if (Kit.Button("##teamcraft", new Vector2(at.X + w - S(18) - bw, at.Y + S(12)), label, Btn.Glass, BtnSize.Sm, null, 0, false,
                "Opens ffxivteamcraft.com in your browser with these items in a new list"))
        {
            var url = Teamcraft.ImportUrl(items);
            if (url != null)
            {
                Dalamud.Utility.Util.OpenLink(url);
                Notify("Opening Teamcraft");
            }
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>Puts a flag on the map at the vendor and opens it.</summary>
    private static void FlagOnMap(VendorOffer v)
    {
        if (v.Position is not { } p || v.TerritoryId == 0) return;
        OnFramework(() =>
        {
            if (Services.GameGui.OpenMapWithMapLink(v.TerritoryId, v.MapId, p)) Notify($"{v.NpcName} is flagged on your map");
            else Notify("Couldn't open the map there");
        });
    }

    private void DrawShoppingFooter(ShoppingPlan shop, bool known)
    {
        Frame.BeginFooter("shopping-foot", 16, 20);
        var at = Kit.At;
        var w = Kit.Width;
        const string copy = "Copy list";
        var cw = Kit.ButtonWidth(copy, BtnSize.Md, Icon.Clipboard);
        if (Kit.Button("##shop-copy", at, copy, Btn.Glass, BtnSize.Md, Icon.Clipboard, 0, !known, known ? null : "Go to your home to see what you have"))
            CopyShoppingList();
        // Errands (experimental): fetch it all, on the right.
        var ew = ErrandsOffered(shop) ? ErrandsButtonWidth + S(10) : 0;
        if (ew > 0) ErrandsButton(new Vector2(at.X + w - ew + S(10), at.Y));
        if (known && shop.TotalGil > 0)
        {
            var total = Words.Capital(ShoppingList.TotalWords(shop));
            Txt.Line(Kit.Dl, new Vector2(at.X + cw + S(10), at.Y), Txt.Fit(total, Face.Sans, 13, w - cw - S(10) - ew), Face.Sans, 13, Palette.Ash, 40);
        }
        Kit.Reserve(at, w, S(40));
        Frame.EndFooter();
    }

    /// <summary>Pieces' note for a missing piece that's on a retainer: "On Momo · seen today 05.24", or null.</summary>
    private (ShoppingPlan? For, Dictionary<uint, List<ShoppingLine>> ByItem) onRetainers = (null, new());

    private string? RetainerNote(uint itemId, int count, ShoppingPlan shop)
    {
        if (!ReferenceEquals(onRetainers.For, shop))
            onRetainers = (shop, shop.Lines.Where(l => l.Source == SourceKind.Retainer && l.Need.Kind == NeedKind.Piece)
                .GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.ToList()));
        if (!onRetainers.ByItem.TryGetValue(itemId, out var on)) return null;
        var held = on.Sum(l => l.Count);
        var r = on[0].Retainer!;
        var seen = r.SeenAt is { } t ? $" (seen {When.Relative(t.ToLocalTime(), DateTime.Now, Clock)})" : string.Empty;
        var who = on.Count > 1 ? $"{r.Name} and {Words.Count(on.Count - 1, "other")}" : r.Name;
        return held >= count ? $"On {who}{seen}" : $"{held} on {who}{seen} · {count - held} to get";
    }

    /// <summary>A neutral pill ("RETAINER") where Pieces would say MISSING for a piece that's on a retainer.</summary>
    private static float RetainerPillWidth => S(8) * 2 + Txt.SpacedWidth("RETAINER", Face.Mono, 11, 0.1f);

    private static void RetainerPill(Vector2 pos)
    {
        var w = RetainerPillWidth;
        var h = Kit.StatePillHeight;
        Kit.Dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.LinePicked), h / 2);
        Txt.Spaced(Kit.Dl, pos + new Vector2(S(8), S(3)), "RETAINER", Face.Mono, 11, Palette.Text, 0.1f);
    }
}
