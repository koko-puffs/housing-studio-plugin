# Housing Studio (FFXIV plugin)

A Dalamud plugin that furnishes your house for you from a design on
[Housing Studio](https://kokos-tools.com/housing-studio).

Load a design (a `.build` from Housing Studio, or a MakePlace `.json`) and the plugin shows
what you already have: placed, in your bags, in your storeroom, or still to buy. Press
**Start placing** and it moves what's placed, places the rest at their exact spots, and dyes
everything to match. It saves a backup first, so **Put them back** can undo a run.

It also works the other way: **Save this house as a build** saves the home you're in (furniture,
dyes, fixtures, the outside and wallpapers) as a file you can upload to the site.

Linked to your account on the site, it opens builds and designs you **send to the game**, can
**verify** your home as your design and send **shots** of it to its page, and shows when a home
you visit is a design on Housing Studio (like, save or stamp it from there).

Ready can also **clear out** what isn't in the design first, or **swap** designs and swap back
later. The **shopping list** says where each missing piece comes from (your retainers, a vendor,
the market board, or crafting). **Get everything for me** fetches what's missing from your
retainers, vendors and the market board (and the fixtures), then places it; it needs the vnavmesh
and Lifestream plugins.

## Installing

1. In the game, open `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.
2. Add `https://www.kokos-tools.com/dalamud/repo.json`, tick it, and **Save and Close**.
3. Open `/xlplugins`, search for **Housing Studio** and install it.
4. Type `/hstudio` to open it.

## Using it

1. Open a design: drop it on the window, or use **Choose a file**.
2. Go into your house (or onto your plot for the garden). You need permission to decorate.
3. Open Housing → **Indoor Furnishings** (or Outdoor).
4. Ready shows what to get, what Start does and the fixtures left, in three steps. Press the brass
   button (**Start placing**, or whatever it says it will do), or **Preview** to see what it will do
   without touching anything.

If something goes wrong, Settings → **Help** has a step-by-step mode and a
recorder whose output you can send along with your report.

**Please note:** plugins that act in the game for you are against Square Enix's terms of
service. This one only touches your own furniture, in a home you may decorate, through the
game's own windows. Whether to use it is your call.

## Building it

You need the .NET 10 SDK and Dalamud (installed through XIVLauncher, or unpacked with
`DALAMUD_HOME` pointing at it).

```sh
dotnet test HousingStudio.Tests
dotnet build HousingStudio.sln -c Release
```

Releasing is in [docs/PUBLISHING.md](docs/PUBLISHING.md).

## More detail

- [DETAILS.md](DETAILS.md): every view and setting, pacing, diagnostics, how the code is laid out.
- [docs/AUTOMATION.md](docs/AUTOMATION.md): how the plugin drives the game, step by step.
- [docs/UI.md](docs/UI.md): which code draws which part of the window.

## Credits and licence

Built on what the MakePlace plugin (jawslouis) and its forks RemakePlace and DisPlace found,
with ideas from Refurnish and ECommons. Fonts: Geist, Geist Mono and Cormorant Garamond (SIL
Open Font License). Icons drawn from Lucide (ISC). Licensed under the GNU AGPL-3.0
(see `LICENSE`).
