# Housing Studio plugin: the details

The full reference for the plugin (it used to be the README; [README.md](README.md) is the short
version): what every view and setting does, the pace table, diagnostics, the code's layout.

Furnish your FFXIV house from a design on [Housing Studio](https://github.com/koko-puffs/kokos-tools):
load its `.build` (or any MakePlace `.json` layout), see every piece matched to what you have, and
press **Start placing**: the plugin moves what's placed, places what's in your bags and storeroom at
its exact spot, then dyes everything to match.

> **Status: 0.8.6. Placing works in the game; the newer features haven't all been tried in it yet.**
> 0.8.6: **a click done by hand while it waits for Continue.** Taking from a retainer (and exchanging
> currencies) has a click not yet seen working, so the errands hold before it for Continue. Its callout
> showed the click's own words ("Choose “Retrieve…”"), which read as something for you to do; done by
> hand, the menu closed, and Continue then stopped the errands. The callout now says what it will do
> ("Next, it will choose…") and that Continue makes the click. Done by hand anyway, the errands see the
> item come to your bags and carry on by themselves; Continue after it counts what came and never takes
> or exchanges again; a menu closed with nothing taken is opened again once.
> Also in 0.8.6, from the FC chamber's real run: **a full storeroom.** A bag piece is placed through the
> storeroom (stored, then placed from there); the chamber's storeroom was full, so each fell back to
> placing it straight from the bag, which the game refuses from the plugin ("Unable to place item.",
> as in 0.3.0), and you were asked to put each down. Now the storeroom's pieces are placed before the
> bags' (each frees a slot), the storeroom isn't asked again until it has room, and after one refused
> set-down the rest go straight to you, saying that room for one piece in the storeroom lets the
> plugin do them. **The storeroom read too late**: back in the chamber after the errands, the home was
> read before the storeroom came in, so its 46 pieces counted as missing ("0 from the storeroom");
> the errands now wait for it (15 s at most). **Put them back** did nothing with a backup of an empty
> room ("The backup has no pieces here"): an empty backup of this home now means everything placed
> since is put away, and with the storeroom full, pieces go to your bags (the editor's inventory
> button) instead of being skipped.
> 0.8.5, a scan before a real run. **Dyeing** never presses Dye unless the window shows the dye chosen on
> this piece (its preview, read in the piece's dye; when no preview shows, Dye is pressed and the dye
> checked after, as before): another dye, or the window on another piece, skips the piece instead of
> using a dye on it. "Already in its dye" is decided once the window is closed and the piece is known;
> the dye counts only after Yes (or the game closing the window itself) and once it has held for a
> second with the window closed; a piece's dye trouble skips that piece, not the run, and the clean-up
> says no and waits for the windows to close. **Placing**: a pick-up or a put-away checks the editor's
> mode at that moment (a pause or Continue let you change it, and a pick in Store mode would have put
> a piece away); a piece the run let go of badly goes back where it was (the bar's Move, as Esc) rather
> than being set down again where the game may just have refused it; a just-placed piece's object is
> waited for (2.5 s); the storeroom waits 4 s more for a slow placement before its second try (which
> could place a second copy); a store that ends badly says no to its question; a storeroom row's or a
> bag item's menu that doesn't open skips that piece, not the run. **Errands**: the board's "buy this?"
> must name the chosen listing's whole cost and quantity (a fee or a price for one equal to the chosen
> listing's no longer passes); a late question is said no to before the next listing; a purchase whose
> gil has gone is waited for, never followed by another listing; the gil a purchase took is waited for
> (1.5 s) so it's always counted; a listing too big for your bags is passed over, not the run stopped;
> a shop's other price is said yes to only for a question naming the item; the retainer's and shops'
> waits are longer (lag no longer stops the errands). **The window**: "Place what I have" and "Clear out
> first?" start what's to do when pressed, not what was when the button was drawn; the errands ask before
> a clear out or swap after them; a placing piece waiting for you during the errands can be skipped;
> Carry on isn't offered after a stopped Put them back (it would leave what it was putting away out);
> the design can't be closed or replaced while the fixtures or the errands go. **Stability**: zone
> changes and logout stop each run on its own (one failing no longer leaves another going, or the
> placement check off); a failure to read where you are no longer freezes the rest of the plugin; the
> editor's state is read only when its functions were found (after a patch that moves them, the plugin
> says so rather than reading stale offsets).
> 0.8.4, from the Mist house's second rehearsal. **Dyeing**: while the dye window is open the game
> shows its preview in the piece's own dye (the dye chosen, or a shade's first dye when the shade is
> switched), and puts it back when the window closes. The rehearsal read the preview as the piece's
> dye before, and its real dye after, and said the Icebox's dye had changed (Sunset Orange → Goobbue
> Grey) when nothing had. The dye before is now read with the window closed, and the check waits for
> the window to close and the dye to hold. A real run could have been fooled the same way (the
> preview taken for the dye done, the window then closed without Yes): it now waits for the game's
> question, and checks the dye once the window has closed. **The end of a rehearsal** no longer says
> the editor holds a piece when it's only Furnishing Color's selected piece.
> 0.8.3: **other districts' vendors.** What only another residential district's vendors sell is got
> there: Lifestream's housing address takes you through that district's city aetheryte and its
> Residential District Aethernet (Limsa Lominsa → Mist, New Gridania → the Lavender Beds, Ul'dah → the
> Goblet, Kugane → Shirogane, the Foundation → Empyreum) to ward 1, and the nearest merchant there is
> walked to. Only cities whose aetheryte you're attuned to; your own district's vendors still come
> first (in the plan, and in a trip), and the vendor chosen for an item is still your district's when
> it sells it. An apartment's own district is reached the same way.
> 0.8.2, from the Mist house's rehearsal. **Vendors**: a remodelled interior's zone ("h2i2") names no
> district, so the house's district was read as none and its Housing Merchant as out of reach: nothing
> was planned from vendors, and what only they sell was left out. The district now comes from the
> house's own id (its ward's zone), else the game's original house zone. **The market**: a listing up
> to a quarter above the planned price is bought (Universalis's prices are old by the time the board is
> read: a Hanging Planter Branch 1 gil over was left), still never past the gil agreed or the ceiling;
> the log says when. **Currencies**: something only a currency vendor has, for a currency not allowed
> (or not enough of it), is said as "for Wolf Marks" and the like (allow it in Settings → Getting →
> Currencies), not "to craft" or "not known". **Placing**: when the furnishing editor doesn't close
> (the bar's close after a let-go did nothing, so the dye step stopped the run), its list is closed and
> the bar's close pressed again, as by hand, then the bar closed outright; the Housing menu is opened if
> it doesn't come back.
> 0.8.1, from the company chambers' rehearsal and a stability scan. **The errands**: an NPC's items
> are one stop, each of their shops opened in turn (the Housing Merchant's chairs, then their
> partitions: one visit a run, not one a trip), and each item is checked against its own price (0.8.0
> kept the stop's first item's offer, so a Stage Panel was checked against an Eorzean Map's 1,000 gil
> and "learnt" nothing); a trip goes to the vendors first (it starts at home, where they are), then the
> bell, then your own world's board, then the others (0.8.0 went to a city's bell, home to the merchant,
> then back to a city); a bell in your home still comes first. The board's results get 1.5 s before
> the first pick whatever the pace (at Quick a quarter of the picks went too early and were lost),
> and a lost pick is tried again after 1.2 s, not 2.5. NPCs are walked up to 2 yalms (from 3 the game
> said "Unable to execute action from this location"). A "You:" step that's done already isn't said
> in the log; a rehearsal says "learnt" only when a price was. **Placing**: the storeroom counts as
> loaded once its pages stop coming (the first page alone made pieces on later pages read as missing);
> a set-down gets 5 s to land; a bag piece goes straight from the bag when the storeroom's picking is
> off; the furnishing list is read every 40 ms while it settles, not every frame; a garden run stops
> if you go to another plot, a garden read off every plot is "stand on your plot", and the check that
> the garden's list is this plot's reads the spawned pieces (it never caught anything); a rehearsal
> with nothing placed says so instead of "none of the 0 pieces". **Elsewhere**: the fixtures' apply
> waits for its question (a closed window no longer counts as answered) and says yes to one that
> comes late; the run's progress file is written every 3 s at most, not at each piece; retainer saves
> land in order; a plugin that fails while starting lets go of what it hooked; the recording keeps
> its first lines, cuts very long ones, and doesn't stop at an hour while a run goes (three at most).
> 0.8.0, from a full rehearsal and a scan of the whole plugin. **The errands**: a bell or board is used
> once you're free (not still at a bell or in an event) and used again every 2.5 s it hasn't opened
> (the board after a retainer said "Cannot execute at this time"); a retainer's greeting is clicked
> through at once (it waited 8 s); the board's first result is picked again when its listings don't
> open, and the search made again when the last item's results were still showing; a listing gone
> (bought by someone else) or a question about another row moves on to the next listing, three times,
> instead of stopping the run; the rows of the board's list move up after a purchase as the board's
> do; a board's empty answer is "none listed" only after 10 s; teleports, the aethernet and world
> visits that never start are asked for again (twice) instead of waiting out minutes, and their own
> 20 s of asking now apply (a step's 3 s default cut them short); the door is looked for again for 5 s
> when it hasn't loaded; a rehearsal that carries on past a failed stop stops its travel first; a new
> run starts from nothing of the last. **The way to your door needs no setup**: with none known for
> your plot, the first trip home asks you to walk in once, and that walk is remembered. **Placing**:
> furniture placed but not spawned (as read in private chambers) is read from the game's own list of
> placed furniture, and a read that finds nothing while something is placed is "not loaded" rather
> than "nothing placed" (the counts are logged when the two disagree); a storeroom piece that looks
> placed isn't placed again; clean-up never sets down a storeroom preview; a store job checks the
> piece's spot too; the last phases (dyes, putting away) wait for a read rather than being dropped;
> an older run's backup isn't offered when this run saved none; windows get 6 s, not 3. **Elsewhere**:
> turning a currency off survives a restart; dyes bought by hand are noticed within 2 s; one failing
> part of the plugin's frame no longer stops every run; a non-JSON answer from Universalis isn't
> "nothing listed"; the log keeps 5,000 lines and the recording an hour, without other plugins'
> overlays. **The window**: when the errands can't set out, Ready's button still opens their screen,
> which says why and offers "Place what I have"; the errands' summary goes on to the placing's own
> result (Put them back, the fixtures); Continue sits at the top of the running screens; toasts stay
> in the window, above its footer; "Way to your door" in Settings says it's learnt on the first walk
> home; leftover "experimental" wording is gone.
> 0.7.14: **Currency vendors.** The errands now read the game's currency shops (SpecialShop: MGP at
> the Gold Saucer, marks, seals, tomestones) and, for what no gil vendor sells, plan them before the
> market board: only currencies you allow (Settings → Getting → **Currencies**, MGP on by default),
> and only as much as what you have of each buys (the rest from the board). The estimate says what
> each currency costs against what you have ("Also spends 45,000 MGP (you have 120,000)"), and the
> brass button names it ("Set out · up to 58,920 gil + 45,000 MGP"); a run never spends more of a
> currency than was shown. At the vendor: their window (ShopExchangeCurrency or ShopExchangeItem),
> the item's row found by name, Exchange with the count, and what comes to your bags and what the
> currency went down by checked. **The Exchange click isn't confirmed by a recording yet**: it waits
> for Continue until "Trust the new clicks" is on, a rehearsal finds the row and never presses it, and
> if the game opens its own confirm window you press its button and the errands carry on. **Home**:
> the home you start in is the one the errands go home to whenever the game says it's yours (0.7.13
> let Settings' "Home is" win: from company chambers with "My house" chosen, it planned Lavender
> Beds' merchants and teleported to the house in Mist); "Home is" only asks for a home the game
> doesn't say is yours. **Paces, a step faster**: Steady is what Quick was, Quick (the default) what
> Daring was, and Daring is new: 0.02 s between pieces, 0.12 s between clicks, waits at 0.4, the
> errands' clicks 0.15 s apart and the board's results read after 0.6 s. A pace chosen before keeps
> its speed (Daring becomes Quick, the rest Steady).
> 0.7.13: **No Beta**: the errands, setting the fixtures and putting back what changed are always
> there, and the Beta tab and tags are gone; Ready's own switches (Get them for me first, Set them for
> me after placing) are on by default. **New defaults** for a new install: Daring pace, pieces in place
> marked too, a 1,000,000 gil market ceiling, other worlds on, Ul'dah for the market. **Trips by
> world**: each stop (a retainer, a vendor, a world's board) goes into as few trips as it can, the
> biggest first into the first trip with room, so a world's board isn't visited trip after trip.
> **Fixtures** for the side you're in that aren't in your bags are got too (vendors, the board,
> retainers), and set after placing. **Vendors**: a price a shop asks that the game's data has wrong is
> learnt and used in every estimate after (in a rehearsal it's a note, not a failure); a shop that
> offers an item only after a quest you haven't done isn't planned. **The way to your door** is kept
> for the plot it goes into: the errands' screen asks you to record it when your home's plot has none
> (none yet, or you've moved), and Settings says which plot's it is (a way from 0.7.12 learns its plot
> the first time it's followed in). **A rehearsal's dye**: a placed piece already dyed is a stand-in
> first (0.7.12 found none going by the item sheet alone), and when none is found it says what it
> counted. **The market ceiling** can be typed in (2500000, 2.5m, 750k), up to the gil cap. **The
> errands' limits** sit above the trips, so changing one doesn't move the row away. **"What it's
> doing"** shows as many lines as fit the window. The chambers' "You: Go up…" isn't logged when you're
> up already.
> 0.7.12: **the way to your front door** is recorded once and followed after (Settings → Getting →
> **Way to your door**: Record, walk from where the home teleport lands you to your door, go in; going
> in saves it). vnavmesh's map of a residential ward is built from the zone, which has no houses or
> fences in it, so its own way to a door runs through the gate posts and walls; no spot picked around
> the door could fix that. One recording serves the same plot in every ward and on every world (the
> plots sit in the same places). Without one, vnavmesh's way is tried; stuck (no nearer for 4 s) or
> stopped short, once more, then **you're asked to walk in** and the errands carry on once you're
> inside, rather than stopping. **The door** is used only once you've landed (not mid-jump: "Unable to
> execute command while jumping"), and again if its question doesn't come in 2.5 s (three times).
> **Market listings**: listings Universalis hasn't seen in 12 hours are chosen only when the fresh ones
> aren't enough (old listings have often been bought by the time the board is read), and a world whose
> board had none of an item isn't planned for it again in the run; a trip that only found empty boards
> doesn't stop the run. The board's own answer with nothing in it reads as "none listed" at once.
> **The furnishing editor in another mode**: it opens in the mode last used (the storeroom, say), and
> its bar comes a moment after; switching to Rotate now waits for the bar (0.7.11 stopped with
> "HousingLayout isn't open").
> 0.7.11: **the market board's listings** are read as the game receives them (Dalamud's market board
> service, `Game/BoardListings.cs`): 0.7.10 read the game's item search proxy, which never said they'd
> come, so every item waited 15 s and was left. A batch that comes later (the list sent again, after a
> purchase) is taken afresh; an item with nothing listed shows none after 6 s. **A vendor's price**
> other than the game data's ("Purchase 1 stage panel for 4,000 gil?", 1,000 in the data) is said yes
> to while the whole run stays within the gil you agreed to (and you have it), else no and the item
> left. **The front door**: of four spots 2 yalms around the door (ahead, behind, either side, by its
> yaw), the one nearest you, as the door's yaw outside didn't read as "straight out"; the log says the
> door's place and facing and the spot.
> 0.7.10: **Ul'dah's market boards** are at the Sapphire Avenue Exchange, in the Steps of Thal, not by
> the Steps of Nald's aetheryte (0.7.9 looked there and found none): the errands teleport to the
> aetheryte and take the aethernet to the Sapphire Avenue Exchange (Lifestream's AethernetTeleport, or
> "/li" and its name), as a recording of doing it by hand showed. **Doors**: the spot walked to is
> straight out in front of the door, by the way the door faces (0.7.9 went out towards you, slantwise
> from the side), and walked all the way to before the door is used. The chambers' door: 1.5 yalms out,
> stood still a moment, used; if no menu comes in 2.5 s ("Unable to execute action from this
> location"), a step closer and again. Recordings now keep the shop window's values in full (its
> prices), for the vendor prices that differ from the game data's.
> 0.7.9, the errands, from a full rehearsal from private chambers: **Market city** (Settings → Getting):
> Limsa Lominsa or Ul'dah (Steps of Nald, by the Sapphire Avenue Exchange) for the market board, the
> summoning bells and world visits; already in either with a board (or bell) in sight, no teleport (a
> world visit that lands in Ul'dah uses Ul'dah's board). **Teleports** wait until you and Lifestream are
> free and ask again every 2 s for 20 s (Lifestream refused at once just after a shop or a world visit).
> **Off the mount**, it waits until you've landed (the game won't use anything mid-jump) and talks to a
> vendor again if nothing opened. **A vendor's price** other than planned ("Purchase 1 stage panel for
> 4,000 gil?" with 1,000 planned) is said no to and that item left, not the whole run stopped; the log
> names the NPC and shop. **The market board's search**: the results' names can't be read from the
> window, so the first result is opened (searched by the full name, it's the item) and its listings
> checked to be that item's. **Doors**: walked to a spot 2 yalms in front, on your side (a door stands in
> its wall, and vnavmesh went round odd ways); the chambers' door is tried from up to 8 yalms.
> 0.7.8: a home saved from inside keeps its **lighting level**, so the site can light its 3D view of
> the house as the game does: `properties.light` in the file, a whole number 0–5 (5 the brightest,
> 0 the lights out; the site lights a room at level × 20%), beside `build` and `plot`, in **Save as
> a build**, **Send to my builds**, and the backups and snapshots a run saves. It's the game's own
> `HousingManager.GetBrightness()` (5 minus its inverted level, 0–5 with higher darker), read on the
> framework thread inside a house, apartment or chambers, and written as it is; anything outside
> 0–5, or a level that can't be read, isn't written. A garden's save, and an older file, have none.
> This house says it too ("Medium house · 312 of 450 pieces placed inside · lighting 3 of 5", or
> "lighting off" at 0). The plugin only reads the setting. Unverified in game: that the level read
> is the one Adjust Lighting shows.
> 0.7.7: **Take a shot** reads the camera again ("Couldn't read the camera" in 0.7.0 to 0.7.6). The
> game keeps the camera's view as an affine transform whose fourth column isn't filled in, so as
> it stood it couldn't be inverted; that column is now set before it's read, the camera's own place
> in the world wins when the view puts it elsewhere, and a view that still can't be read falls back
> to where the camera is and what it looks at. When it can't be read at all, /xllog says what the
> camera held.
> 0.7.6: the errands get about faster. For a walk of 35 yalms or more they take the **mount roulette**
> where the zone lets you ride (its TerritoryType row's Mount: not a city's streets, not inside), else
> **Sprint**, and Sprint again whenever it's ready; off the mount before talking to someone or using
> something (Settings → Getting → **Ride and sprint**, on by default). General actions by row: Sprint 4,
> Mount Roulette 9, Dismount 23, each used only when the game says it can be, its name logged the first
> time. **A ward's subdivision**: each division has its own Housing Merchant, but the vendor list kept
> only the first one in a ward (the main division's), and from a subdivision the errands set off for it
> and couldn't get there. Every spot is kept now, and the errands walk to the nearest one of that name
> in the zone (the one in sight first), and use it by its name if its id differs.
> 0.7.5: from private chambers the errands teleported out and then waited outside the house for ever:
> "outside your home" compared the ward's territory with the game's "original house territory", read
> inside the chambers, which isn't the ward's there. It's read from the house's own id now (bits 32-47:
> 340 for The Lavender Beds, for the house and its chambers alike), and if arriving still doesn't read
> as home after 20 s, the log says what was compared. Going up to the chambers, from a second
> recording: the door is "Entrance to Additional Chambers" (found by "Additional Chambers" in its name),
> and "Move to your private chambers" takes you in with no question (0.7.4 waited for one and handed
> the rest to you); a question that does come is answered yes only if it's "Retire to your own chambers?".
> 0.7.4: the errands go up to **private chambers** themselves, from a recording of doing it by hand:
> into the company's hall, to the door to the chambers (found by "Chambers" in its name, a guess: its
> name isn't in the recording, and the names near you are logged if it isn't found), its menu's "Move
> to your private chambers", and yes to "Retire to your own chambers?". Anything not as expected hands
> the rest to you, and the errands go on once you're in.
> 0.7.3, from a rehearsal and recordings in a company chamber: **the storeroom button was the wrong
> one.** The editor's bar is Move [i:0], Rotate [i:1], "Place selected object in your inventory"
> [i:2] and "Place selected item into your storeroom" [i:3]; the plugin pressed [i:2], so Clear out
> and Put them back would have sent pieces to your bags. It presses [i:3] now (`UiTable.LayoutStore`;
> the bags' one is `LayoutToBags`), and the editor's modes are named to match (3 to bags, 6 to the
> storeroom). **A rehearsal's dyes for pieces not placed yet** are tried on a stand-in: the first time
> it comes to each dye, a piece placed here that takes a dye gets every dye step (Furnishing Color, the
> piece picked, the dye window, the dye found and chosen) but Dye, and is checked to keep its own.
> **Private chambers** in your company's house count as your home for the errands ("go home" is the
> company's house; at its hall you walked up to your chambers, until 0.7.4), and
> Settings → Getting has **Home is**, for a home the game doesn't say is yours. A rehearsal that
> leaves the getting out says why, in its report. **The ask** ("Clear out first?") draws its own dark
> backdrop and a solid card: ImGui's dim took the game style's pale colour, over the whole screen and
> through the card. The let-go: pressing Move while holding a piece, by hand, drops what was changed,
> as Esc does (so 0.7.2's "it sets it down" was wrong); 0.7.2's rehearsal never shifts what it holds in
> any case.
> 0.7.2: a rehearsal's move no longer shifts the piece it holds (0.7.1 showed it at its spot and wrote
> its old spot back before letting go, and the check after found an Icebox away from where it was).
> It's picked up and let go as it is, and its spot and turn are checked after. **Clear out with a full storeroom**: the storeroom's room is read before
> starting (its pages' slots less those in use); the pieces it has room for are put away first and the
> rest once the design is placed (each piece placed from the storeroom makes room for one), found again
> by item and spot. The ask before starting says so. A piece the game refuses puts the rest off too,
> rather than each waiting 4 s; any still without room at the end are under **Needs you**.
> 0.7.1: the rehearsal, from a report and a recording of one. After closing a storeroom row's menu it
> waits only for the menu (the game keeps the row's preview, as the piece it holds, until another row
> is picked, so 0.7.0 waited 3 s a piece and reported each one); a preview left at the end isn't called
> a piece held; and the let-go of a moved piece is confirmed, so it no longer waits for Continue.
> 0.7.0: the plugin and the site, closer, with the token from Settings → Getting (none of it yet
> tried in the game). **From the site** on Start: builds and designs sent with the site's Send to
> game wait there (checked about once a minute; a new one is said in chat and a notification),
> Open loads one ([Things sent from the site](#things-sent-from-the-site)). **This house** says
> whether the home is one of your designs on the site, **Verified in game**, or offers **Verify as
> …** or **Post it** ([Verified homes](#verified-homes)). In anyone's home that's a published,
> verified design, a small **visitor panel** shows it, with Like, Save and Stamp
> ([Visitors](#visitors)). In your verified home, **Take a shot** (or `/hstudio shot`) sends a
> picture with where the camera was to the design's page ([Shots from the game](#shots-from-the-game)).
> Settings → Getting has two new switches for the first two.
> Also in 0.7.0: **Rehearse the run** (Ready's Preview → Rehearse the run) does every step a
> real start does, errands and fixtures too, and commits nothing: every purchase's question is
> checked and cancelled, pieces are taken to their spots and let go, dyes picked and not applied.
> Then a report of what it would have done and what didn't work ([Rehearsal](#rehearsal)).
> 0.6.0: a tidier window, from the flow mock-up (docs/mockups/flow.html). Ready is three steps,
> **Get**, **Place** and **Finish**, with one brass button whose words say what it does ("Get 12,
> then place", "Clear out 14, then place", "Swap and place"); the warnings and the item limit are
> chips that open a line of detail; Carry on is a one-line banner; Clear out and Swap are one choice
> (Keep what's here / Clear out / Swap). Start has a **This house** card (how full, Save, Send to
> my builds, Check for changes). Done has one undo card and a fixtures card. Settings has tabs
> (Placing, Getting, Beta, Help), one line a setting. The errands' screens show the route trip by
> trip and a journey line while they go. New: get things first from Ready, and set the fixtures
> after placing (both Beta).
> 0.5.2: the errands click through NPCs' and retainers' words themselves, and search the market
> board for each item (its name typed in, its row found in the results and opened, checked to be
> that item's listings before anything is bought). Whatever doesn't work is asked of you, as before.
> 0.5.1: what a recording of each by hand showed. Set the fixtures for me chooses **Remodel
> Interior** (the menu's real words; it looked for "Interior Fixtures", which isn't there), presses
> the window's apply and says yes to "Proceed with fixture changes?". The errands' shop purchase,
> retainer pick, board purchase and their questions are the game's own now, and no longer hold for
> Continue; a shop's question is checked against the price (an item's name in it can be a plural).
> The door is said yes to only for "Enter …".
> 0.5.0: new features. **Clear out** what's placed and not in the design first, and **swap** to a
> design keeping what was there to **Swap back** to ([Clearing out and swapping](#clearing-out-and-swapping)).
> A **shopping list** that says where each missing piece comes from: your retainers, a vendor (with
> the NPC and the gil), the market board (Universalis' prices for your data centre), or crafting (with
> **Open in Teamcraft**) ([Shopping](#shopping)). **Looking after a home**: Check for changes since a
> run, warnings for pieces the game may refuse and for the item limit, **Carry on** after a
> crash or a disconnect, and the game's own menu words in German, French and Japanese
> ([Looking after a home](#looking-after-a-home)). **Send to my builds** sends the house you're in to
> your builds on the site ([Sending a house to the site](#sending-a-house-to-the-site)). And, behind
> Settings → Beta: **Set the fixtures for me**, **Put back what changed since a run**, and
> **Get everything for me**, which fetches what's missing from your retainers, vendors and the
> market board (with vnavmesh and Lifestream; you search the board for each item, it buys), goes
> home, places it, and goes again until it's done
> ([Getting everything for you](#getting-everything-for-you-experimental)).
> 0.4.3: a scan's small fixes. Stopping a Put them
> back run by a zone change or a logout no longer reads the editor or clicks its Rotate (the
> editor may be half torn down then, as with a held piece in 0.4.1); a diagnostics recording
> left running out of the house reads the editor's state only in a home; one piece reads as
> one piece (the shopping list's Missing line, "1 needs you", Near me's count, the other
> side's pieces).
> 0.4.2: a check of 0.4.1. The outside kept for saving
> is told apart by the world you're on (0.4.1 read it from the game's house ids, which may not
> carry it in the ward, and a save from inside could then have lost its outside); a held piece
> isn't set down while a zone change or logout is under way (it showed as that before the
> territory changed); Try again for a garden also needs its plot.
> 0.4.1: a scan's fixes. A piece the editor holds when
> a run stops is only set down while you're still in that home (not after a zone change or a
> logout); the dye window's fallback only opens on the piece being dyed; Put them back for a
> garden is tied to its plot; Try again only runs in the home the run was in; the outside kept
> for saving is told apart by world too; a save writes beside the file and then replaces it, so
> a failed write never leaves it cut short; the release workflow takes the typed version through
> the environment, and the build workflow's token only reads.
> 0.4.0: **Save this house as a build** saves the whole
> home: its size and style (Minimalist II too), every floor's walls, flooring and light, the house's
> window and door, its outside with dyes (an all-in-one house as one), partition wallpapers and framed
> paintings, and the plot; the file always ends in `.build`. A deep scan fixed: Put them back only
> runs in the home (and on the side) its backup is of, so it can't put a whole house away; a pause no
> longer counts against the step it paused (a piece was skipped on resume); a bag piece the game
> stored late is placed, not given up on; a piece put down by hand just before a run isn't taken for
> the one placed; a piece the editor still holds when a run stops is set down; matching gives undyed
> spots the undyed copies (dyes can't be taken off) and no longer slows the game on designs with
> hundreds of one item; the game's "set down" gets a fixed flag; place anywhere and unloading are
> safer; your own garden saves aren't pruned with the backups; files with impossible numbers are
> read safely. Not yet seen in the game: the fixtures, outside and wallpaper reads.
> 0.3.5: a storeroom row that holds the copy (the dye)
> another piece of the same item needs is placed for that piece instead of passed over, so mixed-dye
> groups (a dozen partitions in three dyes) take far fewer picks; **Put them back** also puts away
> the pieces placed since the backup; and the header's "you can decorate" pill isn't cut short. 0.3.4 turns **Place anywhere** on by default, makes the
> furnishing list again when a piece isn't in it (a bag piece just stored wasn't), never re-picks a
> storeroom row already seen to hold the piece in another dye, and gives up on a piece the list won't
> show after 60 rows instead of 600. **Save this house as a build** is on the Start view, and works
> in any home you may decorate without the editor open. 0.3.3 made Quick (0.3.2's pace) the default, with Steady
> for a slow connection and Daring for the impatient, and fixes the storeroom picking a piece in the
> wrong dye, a garden or house whose placed furniture couldn't be read being planned as empty (every
> piece placed again), and a few slow spots in the window (see [Pace](#pace)). 0.3.2 also fixes a freeze on closing a
> design and the title bar's buttons; 0.3.0 is a new window (see [The window](#the-window)). Moving placed pieces and placing from
> the storeroom (the game places the piece, then it's moved to its spot) work in game, at about 2.4 s
> a piece in 0.3.1. 0.3.2 stops scanning the storeroom list row by row: storeroom pieces whose names
> the list shows go first (each placement brings more rows into view), and a row it can't read is
> predicted from the list's sort order and found in a pick or a few. Bag pieces are stored first and
> placed from the storeroom (placing from the bag was refused in 0.3.0). Opening the editor from
> nothing now accepts it opening in Move. Still unverified: the sort order for categories no log has
> shown, "Store" from a bag item's menu, and opening the Housing menu from nothing (see
> [docs/AUTOMATION.md](docs/AUTOMATION.md)). Keep the **backup** on, and if anything stops or feels
> slow, send the log: it ends with a **Timing** summary.

## What it does

- **Load** a `.build` or MakePlace `.json`: file picker, drag a file from Explorer onto the window,
  or pick from Recent (your files and the backups runs saved). The last one reopens when the game starts.
- **Match** every piece to what you have, nearest first and the right dye first: **in place**,
  **to move**, **in the storeroom** (the design's dye preferred), **in your bags** (bag, row, slot),
  or **missing**, with a **shopping list** of where to get what's missing (your retainers, vendors,
  the market board, crafting; see [Shopping](#shopping)) and a checklist.
- **Start placing** runs three phases, one piece at a time at the [pace](#pace) you set (Quick, the
  default: 0.15 s between pieces ± 25 % and 0.3 s between any two clicks in the game's windows):
  1. **Move**: placed pieces are picked up and set at their spot with the editor's own functions.
  2. **Place**: storeroom pieces are picked in the furnishing list and placed where the game puts
     them, then moved to their exact spot; pieces from your bags are placed from the item's menu, the
     held piece is aimed and set down. Each is checked to sit exactly at its spot.
  3. **Dye**: every piece whose dye differs is dyed in the editor's dye mode with the game's dye
     window, using dyes from your bags. Pieces whose dye you don't have are skipped and listed.

  Floor pieces go first, then what sits on them or hangs on walls. Pause, resume, skip, stop
  (also `/hstudio pause|resume|stop`), and per-phase progress with the step being done.
- **The Housing menu, the editor's modes and the Storeroom tab are opened for you** (Rotate to
  place, Furnishing Color to dye, through the Housing menu as you would). Only if it can't find its
  way does the run ask you, in the window and in chat.
- **The storeroom list** isn't in storeroom order, so the run finds a piece's row by its name in the
  list when it can read the list, else picks rows (one per different piece, skipping the rest of
  each group); every row it learns is kept for the house until the storeroom changes.
- **When it's done**: what didn't work and why (with "Show where" lighting its spot), the dyes
  you're short of, "Try the N again", and "Put them back" to restore the backup the run saved: the
  pieces you had go back to their spots, then the pieces placed since (not in the backup) are put
  away in the storeroom with the editor's Store mode (new in 0.3.5, not yet seen in the game: a piece
  Store mode won't take is listed, to put away by hand).
- **Safety**: only in a home you may decorate, with the furnishing editor open; it stops on leaving,
  a zone change, combat or a cutscene, losing permission, a window that doesn't appear in time, the
  furniture list changing under it, or a few pieces in a row failing; whatever a piece opened is
  closed when it stops. A backup `.build` of what's placed is saved before every run (the newest 50 are kept; files you save in the backups folder yourself stay).
- **Inside and the garden separately** (garden coordinates are turned to match your plot).
- **Markers in the world** where each piece goes, which way it faces, and the one being worked on.
- **Near me** (only pieces within a few yalms of you: a room) and the floors to place; placing
  follows them. The Pieces list also filters by state and name.
- **Preview first**: walks every piece, marking each spot and logging what it would do, touching nothing.
- **Rehearse the run**: every step a real start does, committing nothing (see [Rehearsal](#rehearsal)).
- **Activity log** of every step, with the windows and values used, copyable.

### Looking after a home

- **How full it is**: Start shows how many pieces are placed where you are against what the home
  holds ("312 of 450 pieces placed inside"). The numbers are the site's (`Core/ItemLimits.cs`, from
  `ITEM_LIMITS` and `OUTDOOR_LIMIT`): inside an apartment 150, a cottage 300, a house 450, a mansion
  600; a garden 40, 60 or 80. Ready's first check chip is the count once Start is done against the
  limit ("447 of 450", with a small meter that warms near full and goes danger past it; a chosen
  Clear out's pieces don't count). Its detail says when a design won't fit: the design alone ("This design has
  480 pieces inside; a house (medium) holds 450."), or with the pieces already there that aren't in
  it (they stay, and count), and what Start does then: it places pieces until the home is full, the
  game refuses the rest, and after a few refused in a row the run stops. Near me and the floors
  leave pieces out, and the count follows them.
- **Pieces the game may refuse** (`Core/Clashes.cs`): what can be told without the game's own check
  and without the pieces' sizes (the plugin has none): copies of one item at the same spot (within
  0.02 yalms), spots below the home's lowest floor or above its top floor's ceiling (the basement's
  floor at -7, an apartment's at 0; the ceilings at 6.5 for a cottage or an apartment, 13.5 with an
  upper floor, Shirogane's 0.3 lower; from the site's `interiors.json`, read from the game's models;
  0.05 of slack; between two floors isn't told, as the stairs run there), spots outside the
  house's walls (the box its model fills, by size), and an indoor furnishing in the garden or an
  outdoor one inside (the game's HousingFurniture and HousingYardObject sheets). Ready says so in a
  check chip, "2 may be refused" (in danger with Place anywhere off; neutral with it on, as the game
  takes them then) and "1 can't go here" for the other side's furnishings, whose detail names them
  with **Show where**; Pieces lists each under **Pieces the game may refuse**, with Show where.
- **Carry on after a crash or a disconnect** (`Core/RunProgress.cs`, `Session/Care.cs`): while a run
  goes, its progress is kept in `resume.json` in the plugin's folder (the design file, the home, the
  design's pieces it was given, how many are through and the phase), written beside it and moved
  over it at most twice a second. When it finishes, or you stop it, the file goes. Otherwise (the
  game closed, you were disconnected or changed zones, or the run stopped for safety), the next time
  that design is loaded in that home Ready's banner says "A run was cut off at piece N of M." with
  **Carry on** or **Forget**.
  Carrying on starts a run on those pieces as they're matched now, so the ones done are in place and
  nothing is done twice; its numbers go on from the run cut off. It only places: it never clears out
  or swaps, whatever the Place stage has picked (that's the brass button's, which asks first, and a
  swap keeps what's here); the banner's tooltip says so while Clear out or Swap is picked. A file that's cut short or edited
  reads as none (and is dropped). A restore's pieces to put away aren't carried on.
- **Check for changes** (`Core/Drift.cs`): after every run (not a preview), what's placed is kept as
  a snapshot of the home (`snapshots/<home>.build`, the same file a backup is; the home is told by
  side, world, territory, house and plot). It's dropped when the next run starts and made again when
  it ends in the home, so a run that ended away (a zone change) leaves none rather than a stale one.
  On Start, in a home you may decorate with a snapshot, the **This house** card has a chip for
  what changed since your last run there ("Check for changes" until checked, "Checking…" while it
  checks, then "3 changed" or "Nothing changed"); pressing it checks, and opens the changes under the
  card (they close by themselves in another home, after a run, or when there's no check to show): what's placed now
  against the snapshot, paired as the matching pairs pieces (the
  same item, the same dye, the nearest; within 0.05 yalms and 0.03 radians counts as unmoved): moved,
  added, taken away, in another dye, with **Show where they were**. Nothing is touched by checking.
- **Put them back where they were** (Settings → Beta → Put back what changed;
  off by default): after asking, the snapshot (a copy of it) is loaded as the design and a run
  started on it once it's matched: the moved pieces are moved back, the ones taken away placed again
  from your storeroom or bags, and the ones dyed since dyed back (when Dye after placing is on). The
  pieces added since stay where they are (it doesn't put anything away). Not yet tried in the game.
- **Other client languages** (`Core/MenuWords.cs`, `Game/GameWords.cs`): the menu entries the run
  chooses by their words ("Place" in a bag item's menu and a storeroom row's, "Store", Furnishing
  Color's "Paint") are read from the game's own text, the Addon sheet in the client's language
  (rows 1751 and 6229 Place, 6840 Store, 6272 Paint: Platzieren / Placer / 配置する, Ins Lagerhaus
  legen / Ranger dans l'entrepôt / 倉庫に入れる, Färben / Teindre / 染色する). Each row's English
  words are checked first; if a patch moved the rows, the built-in words are used (logged). The
  "Place" words typed in Settings still override them. Everything else the run matches by words is
  the game's own text already (item and dye names from the sheets in the client's language, the dye
  window's "No Color" read from the window). Not yet tried on a German, French or Japanese client.

### What it doesn't do

- **Walls, floors, lights, the house's outside**: listed ("set these yourself"); with Settings →
  Beta → Set the fixtures for me, the plugin can put them in from your bags (see
  [Setting the fixtures](#setting-the-fixtures-experimental)).
- **Removing a dye** (a piece dyed in your house but undyed in the design): listed.
- **Pieces the game refuses** (floating, sunk or overlapping) if **Place anywhere** is switched off
  in Settings → Placing → More options. It's on by default: the game's placement check is off while a run
  lasts (as MakePlace does), and back on when it ends.
- **The garden's storeroom**: you put those pieces down, the plugin sets them at their spot.
- **Paintings' and fish prints' pictures, aquarium fish, a partition's wall material**.

## Clearing out and swapping

**Clear out** (Ready's Place stage, one choice of **Keep what's here / Clear out N / Swap**; Keep
until you choose another, and only for the design loaded; Clear out is greyed when there's nothing
to clear, and a choice that can't apply any more, nothing being left to clear, goes back to Keep for
good rather than coming back by itself). The pieces placed in your home that the design doesn't use
(`Plan.Extras`) are put away in the storeroom before the design is placed, with the editor's Store
mode, the same steps Put them back uses (`PlacementRunner.StoreSteps`: the Store button, each piece
picked with the editor's own pick, a yes/no answered, the piece watched leaving the furniture list).
The run's filters count: the floors switched off and what Near me leaves out stay where they are
(`Core/ClearOut.Pick`, tested). Starting asks first ("Clear out first?"), and the backup is saved
before as usual. Once they're put away, the design is matched again against what's placed now and
the run carries on with it (`PlacementRunner.AfterClearOut`): whether storing a piece renumbers the
furniture slots the moves use isn't known, so nothing planned before the clear out is trusted after
it; the storeroom's list is made again before it's read (it doesn't show pieces that came in while
it was open). The garden works the same way (Store mode on the outdoor editor, as for Put them
back). Done says how many were put away; a piece Store mode wouldn't take is under **Needs you**,
with Show where, to put away by hand ("Try again" puts those away again, last). A preview lists them
first ("Would put … away").

**A full storeroom** (0.7.2): before starting, the storeroom's room is read
(`InventoryReader.StoreroomRoom`: the slots of the pages the game sent, less those holding a piece; a
storeroom of 9 pages holding 449 has room for 1). If it can't take every piece, the ones it can are
put away first and the rest wait (`Core/ClearOutSplit`, tested); "Clear out first?" says so. Once the
design is placed (each piece placed from the storeroom makes room for one), they're put away last
(`PlacementRunner.PlanClearOutLater`), each found again by its item and spot, as storing and placing
may have given it another furniture slot. If the game refuses a piece (it says something, or 3.5 s
go by with the room at 0), the rest of the first ones wait too (`PutOffClearOut`) rather than each
being refused, and later pieces aren't tried until the room changes. Any still without room at the end
are under **Needs you**: make room and Try again, or put them in your bags by hand. A preview or
rehearsal lists the waiting ones last, where a run would put them away. A run stopped before the end
leaves them placed (the log says so). Not yet seen in the game: Store mode from the plugin (also new in Put
them back, 0.3.5).

**Swap** (Ready's Place stage; not for a backup loaded as the design; the brass button then reads
"Swap and place" and asks first): clears out,
places the design, and keeps the backup it saves under a name you'll know, "<home> before
<design>" ("Medium house in Mist, plot 12 before Winter Lodge"; `Core/SwapNames`), in the backups
folder. That backup is saved even with the backup switched off (a swap doesn't start without it),
isn't one of the backups' own names so it's never pruned, and goes second on the Recent list (the
first is the design that reopens when the game starts), marked **KEPT** with "Kept from a swap"
(`SwapNames.IsKept`: a `.build` in the backups folder, not a backup's own name, named "… before
…"). Done's undo card then offers **Swap back** in place of Put them back: it loads that backup as the design and runs it with what isn't in it put away
first (Put them back puts it away last), only in the home the backup is of. Another day, open it
from Recent, choose Clear out, and press the brass button.

## Setting the fixtures (experimental)

Settings → Beta → **Set the fixtures for me** (with Experimental features on) gives Done a card for
the fixtures left with **Set them** (Beta; it asks first, as Ready's does), and Ready's Finish
stage **Set them for me after placing** (Beta; when the placing run started from Ready finishes, or
the errands finish with every placing run of theirs finished and their summary put away, and fixtures
are left, setting them starts by itself, once, for that design, opening the window. Never after a run
stopped, failed or previewed, the errands stopped, a different run, or another design loaded. It's
followed every framework tick (`UI/Model/AfterPlacing`, tested), whether the window is open or not)
and, under
**Tick off**, a **Set them for me** button: the design's fixtures for the side you're on (inside, each floor's
wall, flooring and light; on your plot, the outside's parts) that are in your bags are set through
the game's own fixtures window (`Session/FixtureRunner`, with `Core/FixtureJobs` for the plan,
tested). Nothing of it has been tried in the game, so it goes **step by step** whatever Settings
says: before each action it holds, shows it, and waits for **Continue** (in Ready's Finish stage or at the top of Done, or
`/hstudio continue`). The steps:

1. The furnishing editor is closed (its own close) or the Housing menu opened (the housing agent),
   and **Remodel Interior** (or Remodel Exterior) chosen by its words, at its place in the menu
   (inside it's the third, [i:2], as recorded); only when the menu's first two entries read as
   Indoor Furnishings and Furnishing Color (the recorded [i:0] and [i:1]); otherwise you're asked
   to choose it. If the window
   (`HousingEditInterior` / `HousingEditExterior`) doesn't open, by itself or by you, it stops.
2. The window's container (InventoryType 2012 inside, 2011 outside) is read and logged, and its slots
   checked against what's set: floor by floor, five parts each (wall, window, door, flooring,
   light; the outside's eight parts in their order). Only if what's set is where that order says is
   a fixture put in its slot by the plugin, with the game's item move from your bag (what dragging
   it there does); if it doesn't go in 3 s, or the slots can't be told apart, you're asked to put it
   in, and the window is watched for it.
3. The window's apply (`HousingEditInterior` [i:0, empty, empty, empty, empty], as recorded; the
   outside's taken to be the same), then yes to "Proceed with fixture changes?" (only to that
   question); the window closes. The run waits up to 5 minutes for the game's tables
   (`FixtureReader`) to show each fixture, logs which are set, and ticks them.

What it leaves to you: a floor's window and door (the house's, set from outside), a themed outside,
and the outside's dyes; fixtures not in your bags are counted. It stops on leaving the home, a zone
change, combat, a logout, losing permission, and the window closing before the apply; it never
presses anything to clean up (an open fixtures window is left for you to apply or close). Every
window name, container and word it uses is in `Automation/FixtureTable.cs`, with where each came
from. A recording of setting one fixture by hand showed the menu's entry, the apply and its
question. The game itself puts a fixture in by a slot clicked (`HousingEditInterior` [i:2, u:0,
empty, empty, u:1]) and the fixture chosen from its icons (`ContextIconMenu` [i:0, i:0, u:51218,
u:0, empty]); which slot and which number those are isn't known yet, so the plugin's item move is
still the guess. **Still to confirm**: the same on your plot (Remodel Exterior with a fence), and
which slot and fixture a recording like that one used.

## The window

One window, no tabs: what it shows follows what you're doing.

- **Start** (nothing loaded): where you are ("In your house (medium) · you can decorate") above
  the title, and the drop zone as one row (**Choose a file**, or drop a file anywhere on the window).
  In a home you may decorate, the **This house** card: the home and how full it is ("Medium house ·
  312 of 450 pieces placed inside", with a meter), a chip for what changed since your last run there
  (see [Looking after a home](#looking-after-a-home)), and **Save as a build**: the whole
  home as it is (no design, and no need for the furnishing editor or Rotate), the same file the site
  and MakePlace open. It holds the house's size and style (a district's, Minimalist or Minimalist II),
  every floor's walls, flooring and light, the house's window and door, its outside (roof, walls or
  an all-in-one house, window, door, decorations, placard and fence, with their dyes; seen in the
  ward, so walk past your house once before saving from inside), every piece with its dye, the
  wallpaper on partitions and the paintings in frames, the plot, and inside, the lighting level
  (`properties.light`, 0–5, 0 the lights out, as the game's Adjust Lighting has it; a garden's has none). A garden needs Outdoor
  Furnishings opened once, as the game loads its list only then. The name you type gets `.build`.
  Beside it, **Send to my builds** sends the same home straight to your builds on the site (see
  [Sending a house to the site](#sending-a-house-to-the-site)). Linked, the card has a line about
  the home on the site ("On Housing Studio: Cozy Café · Verified in game · Open", **Verify as …**, or
  "Not on Housing Studio yet · Post it"; [Verified homes](#verified-homes)), and, verified, **Take a
  shot** ([Shots from the game](#shots-from-the-game)). Then **From the site**, when anything sent
  from the site is waiting (each with **Open** and ×; [Things sent from the site](#things-sent-from-the-site)),
  then **Recent** (your files, the
  backups runs saved, and what a swap kept, marked KEPT; right-click a file to take it off the list)
  and "Looking for a design? **Browse designs**" (opens Housing Studio's Browse page).
- **Ready** (a design loaded): whether it fits your home (the header's pill); a banner when a run
  of it was cut off (**Carry on** / **Forget**); **check chips** for what's worth knowing (the item
  limit as a meter, pieces the game may refuse, dyes short, the storeroom not read, Near me, floors
  left out, pieces placed from the other side), each opening a short detail with its actions (Show
  where, Pieces, See which, Copy list); then three numbered stages:
  **1 · Get** (how many things are missing and about what they cost, where they come from:
  retainers, vendors, market, craft; **Get them for me first** (Beta, with the errands switched on);
  **See the list**), **2 · Place** (how many to do and already in place, to move / to place / to dye,
  and **Keep what's here / Clear out N / Swap** with a line saying what it does) and **3 · Finish**
  (the fixtures left and ticked, what they are, **Set them for me after placing** (Beta), and **Tick
  off**: tick them as you set them, kept per design). The stage in hand has a brass number, done ones
  a tick. One brass button says what it does ("Get 12, then place", "Clear out 14, then place",
  "Swap and place", "Start placing", with " · fixtures" when they follow), beside **Preview** (a menu:
  **Mark the spots**, the preview, or **Rehearse the run**, see [Rehearsal](#rehearsal)), with
  about how long it takes at your pace (and the errands' trips first). With Get them for me first on,
  it opens the errands only when they can get something and set out now; otherwise it places what you
  have, and the line under it starts "Not getting first:" and why. Its tooltip says what it does
  to the game (stay in the house, the editor, the backup). The header has **Pieces**, **Settings**
  and **×** (close this design).
- **Running** (a run or a preview): the pieces done, the three phases, the piece in hand with its dye
  and the step being done, what it's doing (the last lines of the log, with Copy), **Pause** /
  **Resume** and **Stop**. In step-by-step mode a brass callout says what the next click does, with
  **Continue**; when the run needs you to do something, the same callout says what.
- **Done** (until you press Done): "<design> is in" and one line of what happened ("445 of 447
  pieces"; after the errands their own summary says what they got), what was moved, placed and
  dyed, what **needs you** (each with why, and **Show**, **Copy** or, for the pieces you don't have,
  **Shopping list**), one undo card, "Not what you wanted?" (**Swap back** after a swap, else **Put
  them back**, which restores the backup the run saved; both ask first), a card for the fixtures
  left (**Set them**, Beta, with the fixtures run on; else where to set them), and **Try the N
  again** (or **Carry on** when you stopped it; it clears out again only if the run stopped was
  clearing out). After a preview, the brass button is Ready's ("Get 12, then place", "Start placing",
  "Clear out 14, then place", "Swap and place", " · fixtures"), and does what it does there, asking
  first as Ready does.
- **Pieces** (from Ready): every piece by floor, the same item in the same dye stacked
  ("Hingan Bookshelf ×9"), filtered by **To do / In place / Missing / All**, **Find a piece** and
  **Near me**; each with its state and a **Show where** button that lights its spot in your home (and
  points at it when it's off screen); a missing piece that's on a retainer says "On Momo (seen today
  05.24)" with a RETAINER pill instead of MISSING. **Shopping list** and **Copy checklist** at the foot.
- **Shopping list** (from Pieces' foot and Done): what's still to get by where it comes from, with
  the gil; see [Shopping](#shopping).
- **Settings** (from the header, or Dalamud's settings button), in four tabs, each setting one row
  with one line under its name (the longer words are its tooltip): **Placing** (place for me, dye
  after placing, keep a backup, [pace](#pace): Steady, Quick or Daring, show where pieces go, and
  **More options**: place anywhere, pace variation, when to stop, chat, how far Near me reaches,
  storeroom and bags apart, floors to place, marker options, save what's placed as a build),
  **Getting** (market prices from Universalis; the errands' market ceiling, other worlds and spare
  bag slots; the **Housing Studio account**: the token that lets Send to my builds work, masked once
  linked, with **Unlink** ("Not accepted by the site" when the site has refused it); **Check for
  things sent from the site** and **Show homes on Housing Studio when I visit**, both on), **Beta** (one switch for the features not yet tried in the game, then
  each feature's own with its Beta tag: [Get everything for me](#getting-everything-for-you-experimental),
  Get things first from Ready, [Set the fixtures for me](#setting-the-fixtures-experimental), Set
  fixtures after placing, Put back what changed) and **Help** (step by step, the old preview method,
  the recorder, the "Place" menu words, trust the errands' new clicks, the windows and clicks used).
  The tab is kept while the window is open.

While a run goes, Pieces and Settings are hidden, so nothing that steers the run changes under it.

Two small panels of their own, apart from the window, in the same look (glass, no title bar, sized
to what they hold): the **visitor panel** ([Visitors](#visitors)) and a **shot's preview**
([Shots from the game](#shots-from-the-game)). Neither takes focus when it appears, closes on Esc,
or makes a sound; they aren't drawn in combat, cutscenes or between zones, and Dalamud hides them
with the HUD and in Group Pose, as it does the window. They first appear on the right of the
screen; drag them anywhere, and they come back there.
[docs/UI.md](docs/UI.md) maps each part of the design to the code that draws it.

## Shopping

What a design needs that you don't have (its missing pieces, and the dyes it needs beyond those in your
bags) and where each comes from. Ready's Get stage says how many things are missing and about what
they cost ("12 missing · about 61,400 gil"), with a chip for each source ("4 retainers", "5 vendors",
"2 market", "1 craft"); the **Shopping list** view (Ready's **See the list**, Pieces' foot, Done)
lists it by source, and **Copy list** copies the same,
with each source and price, to paste anywhere.

Each thing is taken from the first source that has it, in this order (`Core/Sourcing.cs`,
`ShoppingPlanner`, tested in `ShoppingTests.cs`):

1. **On your retainers** ("On Momo · seen today 05.24"): the retainer holding most of it first. Two
   records, put together (`RetainerStocks.Merge`, the plugin's own first as it knows when):
   - **The plugin's own**: whenever a retainer's inventory window (`InventoryRetainer` or
     `InventoryRetainerLarge`) is open, its seven bag pages are counted (HQ and collectable copies as
     the item) with the retainer's name and the time, per character, in `retainers.json` in the
     plugin's config folder (written in the background; again when the counts change, or at most every
     5 minutes). A retainer's market listings and equipped gear aren't counted. Open each retainer once
     and the list knows it (`Game/Retainers.cs`, `RetainerRecord`).
   - **Allagan Tools**, when it's installed, through its IPC (asked once a minute while there's
     something to get, and when the design's match changes): `AllaganTools.IsInitialized` → `bool`,
     `AllaganTools.GetCharactersOwnedByActive(false)` → `HashSet<ulong>` (the retainers' ids), and
     `AllaganTools.GetCharacterItems(id)` → `HashSet<ulong[]>`, each array item id then quantity. Every
     call is wrapped: not installed, not ready, other signatures or a throw all mean "nothing from it"
     (logged once). Its retainers are named from the game's retainer list when that's been sent this
     session, else from **AutoRetainer** (below); once either names them, only those ids are kept, so
     a company chest isn't taken for a retainer.
   - **AutoRetainer**, when it's installed: `AutoRetainer.GetOfflineCharacterData(contentId)` (its own
     `OfflineCharacterData`, taken as an object and read by name: `RetainerData`, each `Name` and
     `RetainerID`) gives your retainers' names and ids before the game's list is sent. It keeps no
     record of what retainers hold, so it names, it doesn't count. Wrapped like Allagan Tools
     (`Game/AutoRetainer.cs`).
2. **Buy from vendors** ("Material Supplier, Mist (10.9, 11.4) · 1,500 gil each"), with a map-pin button
   that flags the NPC on the map (`IGameGui.OpenMapWithMapLink`). `Game/VendorData.cs` reads the game's
   data once, in the background, when the plugin starts: every gil shop's items (`GilShopItem`; only
   furnishings and dyes kept), every NPC's shops (`ENpcBase.ENpcData`: a `GilShop` row, or one reached
   through a `TopicSelect` menu, a `PreHandler` or a `CustomTalk`'s script arguments), each NPC's name
   (`ENpcResident`) and spot (its `Level` rows; for the residential districts, whose NPCs have none, the
   wards' own layout files `planevent.lgb`, `planlive.lgb` and `bg.lgb`, so the housing vendors are
   always placed). The price is the item's `PriceMid`; a shop item behind a quest or achievement is
   marked. Of the NPCs selling an item, `VendorChoice` picks the residential districts' (the district of
   the house you're in first), then any whose spot is known, then the rest; the cheapest of those.
3. **Market board** ("About 12,000 gil each on Light"): for what no retainer or vendor covers and can be
   traded, Universalis (`https://universalis.app/api/v2/{dataCentre}/{ids}?listings=20&entries=0`,
   up to 100 ids a request) for the data centre you're on. The estimate buys whole stacks, cheapest
   first, or the one stack that covers the rest for less, tax included (`MarketQuote.CostFor`); if
   fewer are listed than needed, the rest falls through. Answers are kept 15 minutes (and shown up to
   an hour while a new one is asked); a request times out after 10 seconds and fails quietly (asked
   again after 2 minutes, the list says so). Off with Settings → Getting → **Market
   prices** (asks Universalis), on by default. The plugin asks the internet for two things only:
   these prices, and **Send to my builds** (see [Sending a house to the site](#sending-a-house-to-the-site)).
4. **Craft** ("Carpenter, level 50"), from `RecipeLookup`, with **Open in Teamcraft**: opens
   `https://ffxivteamcraft.com/import/<base64>`, the base64 of `itemId,null,count` joined by `;` (the
   format Allagan Tools' and Artisan's exports use), which makes a Teamcraft list of them.
5. **Not known**: no vendor sells it and none is listed (or it can't be traded).

### For an errand runner

`ShoppingPlan.Lines` is the errand list: each `ShoppingLine` is how many (`Count`) of a `Need`
(`ItemId`, `Kind` piece or dye, `Stain`) come from one `Source`, with the source itself:
`Retainer` (a `RetainerStock`: `RetainerId`, `Name`, `SeenAt`, item counts), `Vendor` (a `VendorOffer`:
`ItemId`, `Price`, `NpcId`, `NpcName`, `ShopId`, `ShopName`, `TerritoryId`, `PlaceName`, `MapId`,
`MapPosition`, world `Position`, residential `District`, `NeedsUnlock`; `Located` says whether it can
be walked to), `Market` and `MarketCost` (the listings to buy: world, price, quantity), `Craft`
(`RecipeId`, job, level), and `Gil`. `ShoppingPlan.VendorGil` / `MarketGil` / `TotalGil` are the totals.
The live plan is `Plugin.Shop.Plan(...)` (`Session/Shopping.cs`), and every vendor for an item is
`Plugin.Shop.Vendors.Offers(itemId)`, best first.

## Getting everything for you (experimental)

**Nothing of this has been tried in the game.** It drives your character and spends gil, so it's
behind two switches (Settings → Beta: Experimental features, and **Get everything for me**), and nothing
moves or is bought until you've seen the estimate and said yes to the gil it names.

With both on, the Shopping list's foot gets **Get everything for me**, and Ready's Get stage **Get
them for me first** (when something's missing): with it on, Ready's brass button ("Get 12, then
place") opens the same screen, **Get, before setting out**:

- **The plugins it needs**, as chips: [vnavmesh](https://github.com/awgil/ffxiv_navmesh) (walking)
  and [Lifestream](https://github.com/NightmareXIV/Lifestream) (teleports, going home, other worlds)
  with a green tick, or a red "not installed" chip (press it for what it does and its page);
  AutoRetainer only when retainers are involved, and only optional: if you have it, it's asked to keep
  out of the way while a bell is used.
- **What you already have**, in one line: what's placed, in your storeroom and bags (what the design
  is matched against), and what's on your retainers (fetched first).
- **The route, trip by trip**: "Trip 1" with the bag slots it fills, then one line per stop: the bell
  ("Bell in your house", else Limsa's) with each retainer's things, each vendor ("Mist · Material
  Supplier") with what's bought there and its gil, the market board ("Limsa · Market board", with the
  world when it isn't yours) with the most it may cost ("≤ 53,000"), and **Home, and place** ("11
  pieces go in"). A trip takes no more than your free bag slots (less the **slots to keep free**, 2 by
  default), counting stacks from the game's stack sizes; a market listing is sold whole, so it's never
  split. The trips after the first are worked out again after the one before, from what's still missing.
- **Ready's choice for what's here**: with Clear out or Swap picked on Ready, one line says so ("Then
  clears out 14 before placing", "Then swaps, keeping what's here as “…”"), and the errands' **first
  placing** does it: the pieces chosen are put away first (as far as they're still where they were when
  it comes home: matched again by item and spot, never more than were chosen), and for a swap what's
  here is kept under that name, as Ready's own start does. Later trips' placing doesn't clear out.
- **Not got this way**, one row per reason: to craft (with **Teamcraft**), not known, vendors it doesn't
  go to (a city or zone whose aetheryte you haven't, or a shop that wants a quest), past the market
  ceiling, not listed on the worlds it buys on, no room, or sold for a currency not allowed or not
  enough of ("2 for Wolf Marks": allow it in Settings → Getting → Currencies).
- **Limits**, one line ("Market up to 100,000 · home world only · 2 slots spare") and **Change**,
  which opens them in place: the **market ceiling** (100,000 gil by default; the market's estimate, tax
  included, never goes past it, and neither does what's really spent), **other worlds** (off: your home
  world only), **home is** (your house, your company's, your apartment: where Lifestream's "go home"
  goes), **bag slots to keep free**, and **trust the new clicks** (below).

**Set out · up to 61,400 gil** is the confirmation: there's no question after it, so the button names
the most it spends (vendors, and the market board up to its estimate under your ceiling), and is
greyed out, saying why, while it can't start (`ErrandRunner.CanStart`). It starts the very plan shown,
whose total is the gil agreed; just before, the estimate is worked out once more, and if it no longer
says the same (your bags, prices or limits changed: `ErrandPlanner.Same`, tested), nothing starts and
the screen shows the new one.

While it goes, the screen says where it is ("At the *market board*", "On the way *home*") with the
trip's **journey** (Bell → Vendors → Market → Home → Place, the stops the trip has), what it's doing
("Buying Riviera Table ×2", the price for one, "the game asked, and the price matched"), **Got** and
**Spent** against what you agreed, the log, Pause and Stop; while it places, the usual figure and phases
under the journey. After, a summary as Done's: what it got on how many trips for how much gil, what
wasn't got, and why it stopped.

The plan (`Core/Errands.cs`, `ErrandPlanner`, tested in `ErrandTests.cs`) is pure:
from the shopping plan (`ShoppingPlanner`), units of each line, ordered vendors (by district, zone,
NPC: one stop per NPC, each item with its own offer, `ErrandItem.Vendor`) → retainers (first instead
when your home has a bell, `ErrandOptions.BellAtHome`) → the market (your own world first, then by
world), packed into trips by bag slots. The run
(`Session/ErrandRunner.cs`) is steps on the framework thread like the placing run's, every one logged:

1. **Retainers**: a summoning bell in your house if there is one (looked for within 60 yalms when it
   starts), else Limsa Lominsa Lower Decks (its aetheryte, then Hawkers' Alley's bells). The bell,
   the retainer chosen in the list, their greeting (you click it through), **Entrust or withdraw
   items**, then each stack's menu (`AgentInventoryContext`) and **Retrieve** (the whole stack).
2. **Vendors**: your own district's housing vendors are walked to from your home's teleport (it lands
   you outside your door); another district's through its city's Residential District Aethernet
   (Lifestream's housing address, ward 1); others by teleporting to their zone's aetheryte. Walk up to 2 yalms, talk to
   them, choose the shop in their menu by its name (each of their shops in turn, the last one closed
   and their menu used again, or they're talked to again), buy in lots of up to 99 (the row found by the item's name in the
   window), answering the yes/no if one comes.
3. **The market board**: Limsa's aetheryte (a world visit first, if the listing is on another world),
   the nearest board. **You** search for each item and open its listings (that click isn't known yet);
   the plugin then reads the listings from the game (`InfoProxyItemSearch`) and buys, cheapest first,
   only those at or under the planned price for one, within what's left of the item's planned gil,
   of the ceiling and of your gil (`MarketRule`, tested).
4. **Home**: back to your home world, Lifestream's home teleport, your door (the nearest object
   called "Entrance") and the yes/no; if it can't find the door (or for an apartment), you go in.
5. **Placing**: the usual run (`PlacementRunner`, unchanged) on what's now in your bags, then the home
   is matched again, and the next trip worked out afresh. It ends when nothing's left that it can get.

**Safety, in code.** Every frame (outside its placing run, which has its own): combat, a duty or a
duty pop, a cutscene, a logout, a zone change no travel asked for: it stops. Before each purchase:
your gil, your free slots, the gil you agreed to (all trips together) and the market ceiling; after
it, what was really spent (a vendor dearer than planned, or a listing dearer than shown, stops it).
A purchase's yes/no is answered only when it's about that purchase: the board's must name the chosen
listing's price (with tax, without, or for one; `MarketRule.QuestionMatches`, tested), a shop's the
item's name. Otherwise it's closed, nothing is bought, and the errands stop.
A trip that got nothing, more trips than planned (+3), or a next trip that would go past the gil
agreed: it stops. A window that doesn't open, a menu without the expected entry, a path vnavmesh
can't find, a plugin that stops answering: it stops, saying which. **Stop** (or `/hstudio stop`)
stops walking and travel, its placing run if it's going, and closes the windows it opened; AutoRetainer
is let be again. Placing, fixtures and errands never run at once (the errands start their own placing).

**The clicks** are all in `Automation/ErrandTable.cs`, with where each came from. A recording
of doing each by hand confirmed: a gil shop's purchase (`Shop` [i:0, row, count, empty], the row as
the list shows it), the NPC's menu ([i:index]), the bell's retainer pick (`RetainerList` [i:2,
u:row, empty, empty]), the retainer item menu's "Retrieve from Retainer", the board's purchase
(`ItemSearchResult` [i:2, row]), and the questions each asks ("Purchase 1 riviera table for 2,000
gil?", "Purchase 1 hedge-trimmed flower bed for 22,107 gil (1,052 fee included)?", "Enter the
estate hall?"); those go without holding. Each purchase's question is checked against its price
before yes. Still guesses, held for **Continue** until **trust the new clicks** is on: opening a
retainer's item menu from the plugin, and the objects found by name (the door's "Entrance", the
board's). NPCs' and retainers' words are clicked through as the game does it (`Talk` [], no
values, a click every half second; after 15 that don't move them on, you're asked). The board is
searched by the item's name (`ItemSearch` [i:9, empty, empty, s:name, s:name, empty, empty,
empty], as recorded with "hedge"), its row found among the search window's text values that read
like the name (when the name is there twice, the first is taken to be the search box), and opened
(`ItemSearch` [i:5, row], as recorded; held for Continue until trusted); the listings that come must
be that item's (`InfoProxyItemSearch`'s item), or they're closed, nothing is bought, and you're
asked to open the right one.

Words are matched in English only so far. IPC names used (`Game/Vnavmesh.cs`, `Game/Lifestream.cs`,
`Game/AutoRetainer.cs`): `vnavmesh.Nav.IsReady`, `vnavmesh.SimpleMove.PathfindAndMoveTo`,
`vnavmesh.SimpleMove.PathfindInProgress`, `vnavmesh.Path.IsRunning`, `vnavmesh.Path.Stop`;
`Lifestream.IsBusy`, `Abort`, `Teleport`, `TeleportToHome`, `TeleportToFC`, `TeleportToApartment`,
`ChangeWorld`, `CanVisitSameDC`, `ExecuteCommand` (the "/li" text when a typed call fails);
`AutoRetainer.GetSuppressed` / `SetSuppressed`. Each is wrapped: missing, another version or a
throw means "can't" (logged once), and the run stops where it needed it.

## Rehearsal

**Preview → Rehearse the run** (Ready's footer) does every step a real start would do, in the same
order, with the same windows, clicks and waits, but never commits anything: nothing is bought,
placed, moved, stored, dyed or set, and nothing needs cleaning up afterwards. It's for debugging and
testing a whole run without spending gil. (**Mark the spots**, beside it in the menu, is the preview
as it always was: it lights each spot and touches nothing in the game.)

It follows Ready's choices as the brass button would: with **Get them for me first** it opens the
errands' estimate, whose button reads **Rehearse these errands**; otherwise it rehearses placing at
once (Keep, Clear out or Swap as chosen), and the fixtures after it when **Set them for me after
placing** is on. Step by step, Pause, Stop and the errands' "Trust the new clicks" work as in a real
run.

**At each commit point** it does everything up to it, logs "Would …" and backs out:

| | A real run | A rehearsal |
| --- | --- | --- |
| Move a placed piece | picks it up, sets its spot, sets it down | picks it up and lets go of it as it is by pressing the editor's **Move** button (`UiTable.LetGo`), never the set-down, and never shifting it while held. By hand, Move while holding drops what was changed, as Esc does; 0.7.1 shifted the held piece and wrote its old spot back first, and its check found a piece away from where it was, so nothing is shifted now. If the editor still holds it, you're asked to press Esc. Then it checks the piece's spot and turn are as they were. |
| Place from the storeroom | finds and picks the row, the preview shows, Place | the same up to the preview, which is moved to the piece's spot for a moment; "Place" is found in the row's menu (its words checked), then the menu is closed (as a run closes a row that isn't the piece); the game keeps the row's preview shown (as the editor's active item) until another row is picked, so only the menu closing is waited for, and a preview left at the end isn't a piece held; it checks nothing was placed. |
| Place from your bags | the item's menu → Store, then from the storeroom (or Place, then set down) | the item's menu opened, "Store" (or "Place") found, the menu closed. |
| Placed by hand | waits for you | doesn't wait: noted. |
| Clear out | the storeroom button, the piece picked | the storeroom button ([i:3]) pressed; the piece isn't picked (in that mode the pick itself puts it away). |
| Dye | the dye window, the dye chosen, Dye, yes | the dye window opened on the piece and the dye chosen, then closed without Dye (a "dye it?" question is answered no); it checks the dye is the piece's own again once the window has closed (the window shows its preview on the piece while open, which the log names). Pieces not placed yet are counted ("once placed"); the first time it comes to each dye, it's tried on a **stand-in** (a placed piece that takes a dye, one dyed already first), every step but Dye. With no stand-in, only counted. |
| A vendor's purchase | the purchase, its question checked, yes | the purchase sent, the game's question read and checked against the price (`MarketRule.QuestionMatches`: whether it matched is in the report), then Cancel; it checks nothing came to your bags and no gil went. |
| The market board | search, listings, the listing, its question, yes | the same up to the question, checked, then Cancel. |
| A retainer | the bell, the retainer, their bags, the item's menu, Retrieve | the same up to the menu, "Retrieve from Retainer" found, the menu closed. |
| Fixtures | each put in its slot, apply, yes | each put in its slot, then the window closed without applying (its close button; closing is expected to keep the fixtures as they were and give the items back, which it checks). Every fixtures step waits for Continue, as setting them does. |

**Teleports and walking happen for real**: their fees are the only gil a rehearsal spends (the
screens say so). As nothing is bought, each trip is made once as planned (no working out again, no
"the last trip got nothing" stop), going home and in between them as usual, and placing is rehearsed
once, after the last trip, on what you already own. Gil and bag-slot checks still run and are noted
("A real run would stop here: not enough gil") without stopping it; a step that fails is noted and
the rehearsal goes on with the next stop (the rest of that stop left out), stopping only for going
home, going in or placing, or after 6 failures. Its Got and Spent cards read **Would get** and
**Would spend**.

**Nothing in a rehearsal changes the plugin's own records either**: no backup is saved (none is
needed), no swap is kept, place anywhere isn't switched on, Care keeps no snapshot or `resume.json`,
fixture ticks and the Recent list are left alone. (The storeroom rows it learns by picking are kept,
as they're true of the storeroom.)

**A guard** (`Core/Rehearsal.cs`, `Rehearsals`): each runner says whether it's rehearsing, and while
one is, the clicks that commit (`RehearsalRules`: Yes on the game's question, the dye window's Dye and
its yes, the fixtures window's apply) and the editor's set-down are refused and noted as "refused: it
would commit", whatever step tried. The door's "Enter the estate hall?" is let through (going in is
part of the trip). A real run never sees the guard.

**After it**: **Rehearsal *done*** (or *stopped*), what it would have done ("Would place 12, move 3,
store 2, dye 4 (1 once placed), buy 6 for 12,400 gil, take 2 from retainers and set 3 fixtures"),
how the purchase questions read, and **what didn't work**: each step that failed, timed out or didn't
match, with its message. **Copy report** copies all that with the whole log. There's no Put them back,
Swap back or Try again after a rehearsal (nothing changed).

## Sending a house to the site

**Send to my builds** (Start's This house card, beside Save as a build) puts the home you're in straight
into your builds on Housing Studio, with no file in between: the site's Build opens it as it opens
one saved there.

- **Linking.** On the site, Settings → Housing Studio → _Send houses from the game_ makes a token
  (`hsp_` and 43 letters; shown there once, as the site keeps only its hash). Paste it in the
  plugin's Settings → Getting → **Housing Studio account** and press **Link** (it's checked for its shape
  first). It's kept in the plugin's config (`SiteToken`), shown masked (`hsp_••••••••3xQz`), and
  **Unlink** forgets it. Revoking it on the site stops it at once. It's never written to the log, the
  activity log or the diagnostics.
- **Sending.** The button needs a link and a home you may decorate. It reads what's placed on the
  framework thread, as saving does (`Backups.Layout`: the same JSON a `.build` holds), names it
  ("Mist house, 31 Oct 2026"; rename it on the site; inside, with its lighting level, as a saved
  file has it), works out the site's code for the home
  (`SiteLink.HouseCode`: `s1i2` is a medium house in Mist), then posts it from a worker thread to
  `{SupabaseUrl}/rest/v1/rpc/hs_plugin_save_build` with the site's publishable key as `apikey` and
  the token in the body, waiting 20 seconds at most (`Core/SiteLink.cs`). Under the buttons it says
  _Sending…_, then _Sent: Mist house, 31 Oct 2026 is in your builds. Open it_ (opens the build on the
  site), or why it couldn't: the site's own words (20 sends an hour a token, 50 builds a person,
  Build switched off, too big), a token the site doesn't know (with a way to Settings), or no
  connection / no answer in time (look in your builds before sending again: it may have arrived).
- The site keeps every rule its own saving keeps; see the site's `hs_plugin_tokens` notes. A build
  sent this way has no preview on its card until it's saved once on the site (the site makes the
  preview from the furniture's sizes).

## Things sent from the site

On Housing Studio, **Send to game** on a build or a design puts it in your plugin's inbox (the site's
`hs_send_to_game`: only what you could download yourself; sending again brings it back to the top;
30 at most). With the account linked and **Check for things sent from the site** on (Settings →
Getting, on by default), the plugin asks what's waiting (`hs_plugin_inbox_list`, a light read)
about once a minute while you're logged in, five seconds after logging in and ten after the plugin
loads; never during a run, in combat, a cutscene or between zones. After a failure it asks less
often (2, 4, 8 minutes, at most every 15), back to once a minute on the next answer; a token the
site refuses stops it until you link again. Something newer than the newest already told of
(`InboxSeenAt` in the config) is said in chat and as a Dalamud notification: "“Cozy Café” arrived
from Housing Studio. /hstudio to open it." (several: "… and 2 more arrived …").

Start lists the newest five under **From the site**, above Recent: a badge (a file for a build, a
house for a design), the name, "Design · sent today 21:04" (· opened, once it has been). **Open**
fetches it (`hs_plugin_inbox_get`, counted as an action, 120 an hour), writes its layout as JSON to
`FromSite/<its file name>` in the plugin's config folder (the site's file name, made safe: only its
last part, no letters Windows won't have, 80 at most, `.build` or `.json` kept; an older copy of
the same name is replaced), and loads it as choosing that file does (it goes in Recent). It's
greyed out during a run; one that arrives while a run starts waits in Recent. **×** takes it off
the inbox (`hs_plugin_inbox_remove`). What went wrong is said under the list in the site's words:
one that's gone (deleted, or no longer shared) is taken off the list.

## Verified homes

A design's address on the site (world, district, ward, plot or apartment) can be **verified in
game**: the plugin, in the home, tells the site it's that design (`hs_plugin_verify`), and its page
says so for as long as its address stays the same.

- **Where you are, as the site's address** (`Game/HomeReader.cs` reads, `Core/SiteHome.cs` makes
  it): the world you're on now (its English name, from the World sheet; the house id may not carry
  it, see 0.4.2), the district from the house id's territory ("s1…" Mist, "f1…" the Lavender Beds,
  "w1…" the Goblet, "e1…" Shirogane, "r1…" Empyreum; then the house's original territory, for a
  Minimalist interior, then the interior's own name), the ward (the house id's, counted from 1), and:
  a **house**, its plot 1–60 (the house id's plot index counts both divisions, 0–29 the main ward and
  30–59 the subdivision, so the subdivision is 31–60); an **apartment**, its number as the game shows
  it ("Apartment No. 41") and its building (the house id's division: 0 the main ward's, 1 the
  subdivision's); a **private chamber**, its house's plot and its number ("Private Chambers 3"). The
  house id's room is 0 for the house itself, so a chamber's or an apartment's number is taken as
  the game shows it, from 1. A company workshop isn't a home on the site. A garden is the house on
  its plot.
- **This house** (Start, in a home you may decorate, linked): once a visit (and again after
  verifying), the site is asked which of your designs have this address (`hs_plugin_home`, a light
  read; in a garden, once you've stayed on the plot two seconds). The card then says "On Housing
  Studio: Cozy Café · Verified in game · Open" (Open goes to its page), or, with designs at this
  address not yet verified, **Verify as Cozy Café** (one button each, three at most; inside only:
  a garden says to verify from inside), or "Not on Housing Studio yet · Post it" (opens the site's
  new design page). An address the site says isn't the design's (`address`), and anything else that
  went wrong, is said in the site's words with **Try again**. Not linked: "Link your account to
  verify this home · Settings".

## Visitors

In any home's interior, yours or anyone's, with **Show homes on Housing Studio when I visit** on
(Settings → Getting, on by default), the plugin asks the site once a visit, two seconds after you're
in, whether a published design is verified as this home (`hs_plugin_here`, with your token when
linked, else as anyone: these addresses are on their postcards). If one is, a line in chat says
"This home is on Housing Studio: Cozy Café by Koko." (not for your own), and the **visitor panel**
shows it: "This home is on Housing Studio", its cover (the small copy, loaded once; none when the
picture can't be read), its name, "by" its maker, ✓ Verified in game; **Like**, **Save** (a
favourite) and **Stamp** (its postcard, stamped from inside the home; only while the site has stamps
on), each with its count, pressed again to take it back (`hs_plugin_act`, as your account, keeping
every rule the site's own keep; their refusals are shown in its words); your own design says "Your
design" instead; not linked, "Link your account to like, save or stamp" opens Settings. **Open its
page**, ‹ › between several (three at most), and × (gone for this visit). It goes when you leave the
home. Off: nothing is asked. When the site can't be asked, nothing shows (the reason is in /xllog).

## Shots from the game

In your own home verified as one of your designs (inside, or its garden, while you may decorate),
This house has **Take a shot**, and `/hstudio shot` takes one too (handy with the HUD hidden: Scroll
Lock hides the game's HUD and with it the plugin's windows; Group Pose hides them as well). The
picture goes to the design's page, beside its 3D view from the same spot.

- **The camera**: the scene's current camera (Graphics.Scene.CameraManager's, the one drawing the
  game, Group Pose's own while it's on). Its place and the way it looks come from its view matrix
  (the camera is the view's inverse; which way along its axis is the projection's to say), the
  target 10 yalms ahead; the vertical field of view from the projection (2·atan(1 / M22), in
  degrees; the render camera's own FoV, radians, only if the projection can't say). In the game's
  axes; a garden's both points in the plot's frame (`Transforms.WardToPlot`, as its pieces are
  saved). `Core/ShotPose.cs` (`CameraMath`), tested.
- **The picture**: the game's frame, grabbed before Dalamud draws its windows over it
  (`ITextureProvider.CreateFromImGuiViewportAsync`, `TakeBeforeImGuiRender`). The camera is read when
  it's asked for and again once the picture is in: if it moved, there's no shot ("Hold it still and
  take it again"). On a worker, it's made 2560 at most on its long side (1920 if need be), written as
  a JPEG at 0.9 quality, lower in steps to 0.5 until it's 3.8 MB at most, and a preview made.
- **Before sending**: a small panel shows the preview, "For Cozy Café · 2560×1440 · 1.8 MB",
  **Send to Cozy Café** and **Discard**. Sending posts a form to the edge function `hs-plugin-shot`
  (the token, the design, the pose as JSON, the JPEG; 90 seconds at most); then "Sent: it's on Cozy
  Café's page", **Open its page** and **Close**. The site's words otherwise: verify it first (This
  house asks the site again), 12 shots a design (delete one on the site), too many this hour. With the
  HUD hidden, chat says when it's taken or why not.
- Every texture is let go of as soon as it's done with (the frame and the sized picture once the
  JPEG is written, the preview with the shot); the JPEG once it's sent. Unloading lets go of them all.

## Pace

Settings → Pace sets how fast a run goes. Each piece is many small steps; a pace sets the pause
between pieces, the least time between two clicks, and how long the plugin waits for the game to
settle (its "the same for a few frames" checks). Timeouts aren't touched: they only decide when to
give up.

| Pace | Between pieces | Between clicks | Waits for the game |
|---|---|---|---|
| Steady | 0.6 s | 0.45 s | half as long again |
| **Quick** (default) | 0.15 s | 0.3 s | as 0.3.2 |
| Daring | 0.05 s | 0.2 s | two thirds |

Quick is 0.3.2's pace, as tested in the game. Steady is for a slow connection or a busy house, or if
pieces get skipped. Daring is for the daring: every step still waits for the game to say it's done,
but with less to spare; if pieces get skipped, go back to Quick. Before 0.3.3 the pace was only the
pause between pieces: Careful (1.5 s) becomes Steady, the rest Quick.

## Using it

1. `/hstudio`, then load a design (drop it on the window, **Choose a file**, or pick it from Recent).
2. Go inside your house or apartment (or to your plot for the garden), with permission to decorate.
   The pill under the title turns green: "Fits your house (medium) · you can decorate".
3. Housing → **Indoor Furnishings** (or Outdoor), which opens the editor and the furnishing list
   (the run can also open the Housing menu for you).
4. **Preview first** to see it (**Rehearse the run** to try every step without committing), then **Start placing**. The first time, switch on **Step by step**
   (Settings → Help): it holds before every click and shows what it's about to
   do; press **Continue** (or `/hstudio continue`).

If you'd rather put pieces down yourself, switch off **Place pieces for me** in Settings → Placing: the run
then waits for you to put each piece down anywhere and sets it at its spot.

## Sending diagnostics

If a run stops at a step, or does something unexpected:

1. Settings → Help → Record what I do → **Start**.
2. By hand: place one piece from your bags, one from the storeroom (pick it in the furnishing list's
   Storeroom tab, choose Place), switch the editor to its Dye mode and dye one piece. Clicking the
   editor's mode buttons and the Storeroom tab while recording helps too.
3. **Stop**, then Copy the recording and log → **Copy**, and paste it in an issue or a message.

The recording lists every window that opened and closed, every click's values (yours and the
plugin's), the menus' entries and the housing state; that's what's needed to fix the table of
windows and clicks (`HousingStudio/Automation/UiTable.cs`). If your item menu's "place" entry has
other words, type them in Settings → Help → The "Place" words. More in [docs/AUTOMATION.md](docs/AUTOMATION.md).

**Before you use it:** plugins that act in the game for you are against Square Enix's terms of
service; this one only works on your own furniture in a home you may decorate, through the game's
own windows, at a human pace, but the choice is yours. Placing binds a piece to you and dyeing uses
up the dye, as doing it by hand does. This plugin is installed from a custom repository, which
goatcorp doesn't review.

Install: see [docs/PUBLISHING.md](docs/PUBLISHING.md#3-install-it-in-the-game) (custom repository URL
`https://www.kokos-tools.com/dalamud/repo.json`, Koko's repository, which lists every plugin Koko
makes).

## Developing

Requirements: the .NET 10 SDK, and Dalamud's assemblies: either XIVLauncher with the game run once
(Windows: `%AppData%\XIVLauncher\addon\Hooks\dev`), or the release zip unpacked anywhere with
`DALAMUD_HOME` pointing at it:

```sh
curl -L https://goatcorp.github.io/dalamud-distrib/latest.zip -o dalamud.zip
unzip dalamud.zip -d ~/dalamud && export DALAMUD_HOME=~/dalamud/
dotnet test HousingStudio.Tests      # the game-free core and the window's logic, runs anywhere
dotnet build HousingStudio.sln -c Release
# → HousingStudio/bin/x64/Release/HousingStudio/latest.zip (and the DLL for a dev plugin)
```

It builds on Linux and macOS too (for checking); it only runs in the game on Windows (or Wine).

```
HousingStudio/
  Core/        game-free, unit-tested: reading .build/.json, axes and turns, plots, floors,
               dyes, the lighting level (Lighting), matching, the shopping list, pacing, backups, sending to the site
               (SiteLink) and its other calls and their answers (SiteLink.Calls: the inbox,
               homes, visitors, shots), the site's address of a home (SiteHome, HomeAddress),
               a shot's camera pose and size (ShotPose: CameraMath, ShotSize); and for the automation the
               step queue (StepQueue), the run's order and dye planning (RunSequence),
               callbacks as data (UiCall) and menu/list helpers (Menus); where to get what's
               missing (Sourcing: the shopping planner; Vendors, Market, Teamcraft), clearing out
               and swaps' names (ClearOut), the fixtures' plan (FixtureJobs).
  Automation/  UiTable (every window and click, with sources), FixtureTable (the fixtures
               windows, guesses with sources), GameUi (reading and clicking windows),
               UiRecorder (diagnostics recording).
  Game/        GameData (Lumina sheets), HousingContext (where you are), HousingEditor (the
               editor's state, pick up / set down), HousingActions (item menus, the dye window,
               aiming the held piece), FurnitureReader, FixtureReader (the home's fixtures, inside and out, and its lighting level, for saving it), InventoryReader, PlaceAnywhere;
               VendorData (who sells what, where), MarketPrices (Universalis), Retainers
               (the retainer record, Allagan Tools' IPC), AutoRetainer (retainers' names over IPC);
               HomeReader (the home you're in as the site's address, and the camera for a shot).
  Session/     LayoutSession (the design matched to the game), PlacementRunner (the run: each
               piece's steps; PlacementRunner.ClearOut, clearing out and swapping), FixtureRunner
               (Set them for me), Care (snapshots, the run's progress, putting back), Backups
               (saving what's placed, and its JSON for sending), ActivityLog, Shopping (the
               shopping plan, kept up to date), GameLink (the site link: the inbox, verified homes
               and visitors, shots; every call from a worker, its answer back on the framework thread).
  UI/          the window: Kit/ (the design system's tokens, fonts, icons and controls drawn with
               ImGui's draw lists), Model/ (game-free, unit-tested: words, the Pieces list, pace,
               fixture ticks, SVG icon paths), Views/ (Start, Ready, Running, Done, Pieces,
               Shopping, Settings, GameLink: From the site, This house's line, Take a shot), ViewModel,
               MainWindow, Overlay (markers in the world), VisitorPanel and ShotPanel (the two small
               panels, on SmallPanel). See docs/UI.md.
HousingStudio.Tests/   xUnit tests for Core and UI/Model
tools/         make-plots.mjs, icon.svg + make-icon.mjs
docs/          AUTOMATION.md, RESEARCH.md, PUBLISHING.md
```

The coordinate conventions mirror the site's `src/features/housing-studio/makeplace.ts` and
`build/model.ts`: MakePlace stores a location as `[x, z, y]` of the game's, and the turn as a
quaternion about its own up axis the other way round (`yaw = -2·atan2(qz, qw)`); garden pieces are in
the plot's own coordinates.

After a game patch, see [docs/PUBLISHING.md](docs/PUBLISHING.md#4-things-to-know).

## Credits and licence

The housing editor's patterns and offsets were found by the MakePlace plugin (jawslouis) and are
kept current by its forks (RemakePlace, DisPlace); the furnishing dye flow and its values come from
RemakePlace; placing from the storeroom through the furnishing list and aiming its preview from
Refurnish; context-menu and callback conventions from ECommons. Fonts: Geist and Geist Mono (Vercel)
and Cormorant Garamond (the Cormorant Project), under the SIL Open Font License 1.1
(`HousingStudio/Resources/Fonts`). Icons: drawn from Lucide's paths (ISC licence).

Licensed under the GNU AGPL-3.0 (`LICENSE`).
