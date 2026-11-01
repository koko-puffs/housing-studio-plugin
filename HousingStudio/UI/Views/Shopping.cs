using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Shopping (from Pieces' foot, Ready's See the list and Done): what's still to get, by where it comes
/// from: your retainers (free), the shops grouped by the place you buy at (the NPC, where, one flag on
/// the map, each row its fixed price), the currency vendors the same way, the market board (an
/// estimate), crafting (Open in Teamcraft) and what's not known. The gil in all sits under the title;
/// the footer has Get everything for me (brass) and Copy list.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Shopping's brass button: opens the errands' estimate (the trips and the gil), as Ready's Get them for me first does.</summary>
    private const string ShopGetAll = "Get everything for me";

    private const string ShopGetAllHint = "Shows the trips and the gil first. Nothing is bought until you set out.";

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
        var view = ShopViewOf(shop);

        Frame.BeginHeader(20);
        if (ScreenHeader(shoppingFrom == Sub.Pieces ? "Back to Pieces" : $"Back to {session.Title}", $"{session.ShortTitle} · shopping", view.Title))
            sub = shoppingFrom;
        // The gil in all, under the title (it was cut off in the footer).
        if (known && view.Total is { } total)
        {
            Kit.Gap(6);
            var at = Kit.At;
            var w = Kit.Width;
            var tx = at.X + S(40) + S(12);
            Txt.Line(Kit.Dl, new Vector2(tx, at.Y), Txt.Fit(total, Face.Sans, 13, at.X + w - tx), Face.Sans, 13, Palette.Ash, 19.5f);
            Kit.Reserve(at, w, S(19.5f));
        }
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
        for (var g = 0; g < view.Sections.Count; g++)
        {
            var (section, places) = view.Sections[g];
            if (g > 0) Kit.Gap(18);
            ShopLabel(section.Title, section.Aside, section.Priced);
            Kit.Gap(10);
            if (places.Count > 0)
            {
                // The shops: a panel per place you buy at (its NPC and where, with one flag), then what's got there.
                for (var p = 0; p < places.Count; p++)
                {
                    if (p > 0) Kit.Gap(10);
                    var (place, placeLine) = places[p];
                    Kit.BeginPanel($"##shop-{section.Source}-{p}", 0, 0, 0);
                    ShopPlaceHeader($"{section.Source}-{p}", place, placeLine);
                    for (var i = 0; i < place.Lines.Count; i++) ShopRow(i + 1, place.Lines[i], true);
                    Kit.EndPanel();
                }
                continue;
            }
            Kit.BeginPanel($"##shop-{section.Source}", 0, 0, 0);
            for (var i = 0; i < section.Lines.Count; i++) ShopRow(i, section.Lines[i], false);
            if (section.Source == SourceKind.Craft) TeamcraftRow(section.Lines);
            Kit.EndPanel();
        }
        Frame.EndBody();

        DrawShoppingFooter(shop, known);
    }

    /// <summary>What Shopping draws for a plan: the title, the gil in all, the sections and each place's line.</summary>
    private sealed record ShopView(string Title, string? Total, List<(ShopSection Section, List<(ShopPlace Place, string Line)> Places)> Sections);

    private (ShoppingPlan? For, string? District, int? Ward, ShopView? View) shopView;

    /// <summary>The words and groups to draw: worked out again only when the plan (or the home you're in) changes.</summary>
    private ShopView ShopViewOf(ShoppingPlan shop)
    {
        var (district, ward) = HomeWard();
        var c = shopView;
        if (c.View != null && ReferenceEquals(c.For, shop) && c.District == district && c.Ward == ward) return c.View;
        var sections = new List<(ShopSection Section, List<(ShopPlace Place, string Line)> Places)>();
        foreach (var section in ShoppingScreen.Sections(shop, data.AnyItemName))
        {
            var places = new List<(ShopPlace Place, string Line)>();
            foreach (var p in section.Places)
                places.Add((p, ShoppingScreen.PlaceLine(p.Vendor, p.Things, ShoppingScreen.WardFor(p.Vendor, district, ward))));
            sections.Add((section, places));
        }
        var view = new ShopView(ShoppingScreen.Title(shop), ShoppingScreen.Total(shop), sections);
        shopView = (shop, district, ward, view);
        return view;
    }

    private (HousingContext? Ctx, string? District, int? Ward) homeWard;

    /// <summary>
    /// The district and ward (1 on) of the home you're in, to name the ward a housing vendor in that
    /// district is walked to in (a house's or a company's, not an apartment's); nulls when not known.
    /// Read again only when where you are changes.
    /// </summary>
    private (string? District, int? Ward) HomeWard()
    {
        var ctx = session.Context;
        if (homeWard.Ctx is { } had && had.Equals(ctx)) return (homeWard.District, homeWard.Ward);
        string? district = null;
        int? ward = null;
        try
        {
            if (ErrandGame.HomeHere(ctx) is { } here && here.Kind != HomeKind.Apartment && here.District != null && here.Ward >= 0)
            {
                district = here.District;
                ward = here.Ward + 1;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't read the ward of the home you're in");
        }
        homeWard = (ctx, district, ward);
        return (district, ward);
    }

    /// <summary>A section's label (mono, dim) with what's beside it on the right ("149,000 gil · 30", in brass when it's a price).</summary>
    private static void ShopLabel(string title, string aside, bool priced)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var h = Txt.Natural(Face.Mono, 11);
        var lw = Txt.Spaced(dl, at, title.ToUpperInvariant(), Face.Mono, 11, Palette.Dim, 0.14f);
        if (aside.Length > 0)
        {
            var color = priced ? Palette.Brass : Palette.Dim;
            var text = aside.ToUpperInvariant();
            var room = w - lw - S(12);
            var rw = Txt.SpacedWidth(text, Face.Mono, 11, 0.14f);
            if (rw <= room) Txt.Spaced(dl, new Vector2(at.X + w - rw, at.Y), text, Face.Mono, 11, color, 0.14f);
            else if (room > S(20))
            {
                var fit = Txt.Fit(text, Face.Mono, 11, room);
                Txt.Line(dl, new Vector2(at.X + w - Txt.Measure(fit, Face.Mono, 11).X, at.Y), fit, Face.Mono, 11, color);
            }
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>A place's header, at the top of its panel: the NPC, where (your ward when it's in your district) and how many things, and one flag on the map.</summary>
    private void ShopPlaceHeader(string id, ShopPlace place, string placeLine)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var v = place.Vendor;
        var nameH = Txt.Natural(Face.SansMedium, 14);
        var textH = nameH + S(2) + S(19.5f);
        var h = S(11) * 2 + MathF.Max(S(34), textH);
        var dl = Kit.Dl;
        var mid = at.Y + h / 2;
        var x = at.X + S(18);
        var flagX = at.X + w - S(18) - S(34);
        var textW = flagX - S(12) - x;
        var top = mid - textH / 2;
        var npc = v.NpcName.Length > 0 ? v.NpcName : "A vendor";
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(npc, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ivory);
        Txt.Line(dl, new Vector2(x, top + nameH + S(2)), Txt.Fit(placeLine, Face.Sans, 13, textW), Face.Sans, 13, Palette.Ash, 19.5f);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + textW, top + textH))
            && (Txt.Measure(placeLine, Face.Sans, 13).X > textW || Txt.Measure(npc, Face.SansMedium, 14).X > textW))
            Kit.Tooltip($"{npc}\n{placeLine}");
        var tip = v.Located ? $"Flag {npc} on the map" : "Where this vendor stands isn't known";
        if (Kit.IconButton($"##flag-{id}", new Vector2(flagX, mid - S(17)), Icon.MapPin, tip, 34, Btn.Glass, !v.Located))
            FlagOnMap(v);
        Kit.Reserve(at, w, h);
    }

    /// <summary>
    /// A row: the item's icon (a dye's swatch), its name and count, a second line when there's
    /// something to say, and on the right what it costs ("free", a shop's fixed price, "≈" the board's
    /// estimate, a currency). Under a place, the place says where, so the row doesn't.
    /// </summary>
    private void ShopRow(int index, ShoppingLine line, bool underPlace)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var nameH = Txt.Natural(Face.Sans, 14);
        var note = underPlace ? ShoppingScreen.PlaceRowNote(line) : ShopNote(line);
        var textH = note != null ? nameH + S(2) + S(19.5f) : nameH;
        var h = S(10) * 2 + MathF.Max(S(34), textH);
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

        var price = ShoppingScreen.RowPrice(line);
        var priceW = price.Length > 0 ? Txt.Measure(price, Face.Mono, 12).X + S(12) : 0;
        var right = at.X + w - S(18);
        var textW = right - priceW - x;

        var name = data.AnyItemName(line.ItemId) + (line.Count > 1 ? $" ×{line.Count}" : string.Empty);
        var top = mid - textH / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.Sans, 14, textW), Face.Sans, 14, Palette.Ivory);
        if (note != null)
        {
            Txt.Line(dl, new Vector2(x, top + nameH + S(2)), Txt.Fit(note, Face.Sans, 13, textW), Face.Sans, 13, Palette.Ash, 19.5f);
            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + textW, top + textH))
                && Txt.Measure(note, Face.Sans, 13).X > textW)
                Kit.Tooltip(note);
        }

        if (price.Length > 0)
            Txt.Line(dl, new Vector2(right - priceW + S(12), mid - Txt.Natural(Face.Mono, 12) / 2), price, Face.Mono, 12, Palette.Text);
        Kit.Reserve(at, w, h);
    }

    /// <summary>A row's second line outside a place: where it comes from, in words.</summary>
    private string ShopNote(ShoppingLine line)
    {
        var dye = line.Need.Kind switch { NeedKind.Dye => "Dye · ", NeedKind.Fixture => "Fixture · ", _ => string.Empty };
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
            case SourceKind.Exchange:
                var x = line.Exchange!;
                return dye + $"{x.Vendor.Where()} · {x.Cost:N0} {x.CurrencyName}" + (x.Gives > 1 ? $" for {x.Gives}" : " each");
            case SourceKind.Market:
                return ShoppingScreen.MarketNote(line);
            case SourceKind.Craft or SourceKind.Unknown when line.Exchangeable is { } ex:
                return dye + $"{ex.Vendor.Where()} · {ex.Cost:N0} {ex.CurrencyName}" + (ex.Gives > 1 ? $" for {ex.Gives}" : " each")
                       + (config.ErrandsCurrencies.Contains(ex.Currency)
                           ? $" · you haven't enough {ex.CurrencyName}"
                           : $" · allow {ex.CurrencyName} in Settings → Getting → Currencies");
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
    private void TeamcraftRow(IReadOnlyList<ShoppingLine> lines)
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

    /// <summary>
    /// The footer (shared rule 2): the brass Get everything for me at the bottom left (when the errands
    /// are offered), Copy list beside it, and under them what the brass button does.
    /// </summary>
    private void DrawShoppingFooter(ShoppingPlan shop, bool known)
    {
        Frame.BeginFooter("shopping-foot", 16, 20);
        var at = Kit.At;
        var w = Kit.Width;
        const string copy = "Copy list";
        var offered = ErrandsOffered(shop);
        var cw = Kit.ButtonWidth(copy, BtnSize.Lg, Icon.Clipboard);
        var gw = offered ? MathF.Min(w, Kit.ButtonWidth(ShopGetAll, BtnSize.Lg, Icon.Package, Btn.Primary)) : 0;
        // Side by side when both fit; else Copy list goes under it.
        var beside = !offered || gw + S(10) + cw <= w;
        if (offered && Kit.Button("##shop-errands", at, ShopGetAll, Btn.Primary, BtnSize.Lg, Icon.Package, gw, false,
                "Fetches what's missing from your retainers, vendors and the market board, and places it. You see the gil first."))
        {
            errandsRehearse = false;
            errandsOpen = true;
        }
        var copyAt = !offered ? at : beside ? new Vector2(at.X + gw + S(10), at.Y) : new Vector2(at.X, at.Y + S(46) + S(10));
        if (Kit.Button("##shop-copy", copyAt, copy, Btn.Glass, BtnSize.Lg, Icon.Clipboard, 0, !known, known ? null : "Go to your home to see what you have"))
            CopyShoppingList();
        Kit.Reserve(at, w, beside ? S(46) : S(46) * 2 + S(10));
        var hint = offered ? ShopGetAllHint : !known ? "Go to your home to see what you have." : null;
        if (hint != null)
        {
            Kit.Gap(10);
            Txt.Note(hint);
        }
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
