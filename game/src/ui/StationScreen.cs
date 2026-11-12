using Godot;
using Galaxus.Core;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Missions;
using Galaxus.Core.People;

namespace Galaxus.Ui;

/// <summary>
/// The docked view: jobs, jobs in hand, market, shipyard, outfitting, people, news, standing and the logbook. There's no walking
/// about, so everything at a station happens on these screens.
/// </summary>
public partial class StationScreen : PanelContainer
{
    private Session _session = null!;
    private Action _undock = null!;
    private Action _openMap = null!;
    private Action _shipChanged = null!;

    private RichTextLabel _header = null!;
    private Label _status = null!;
    private Tree _jobs = null!, _inHand = null!, _market = null!, _yard = null!, _standing = null!;
    private RichTextLabel _jobText = null!, _yardText = null!, _news = null!;
    private Button _accept = null!, _buyShip = null!;
    private Label _jobReason = null!, _yardReason = null!, _marketNote = null!;

    private TabContainer _tabs = null!;
    private List<MissionOffer> _offers = [];
    private PortraitView _clientFace = null!;
    private Label _clientName = null!;
    private Tree _people = null!;
    private PortraitView _personFace = null!;
    private RichTextLabel _personText = null!;
    private List<Person> _here = [];
    private string? _shownPerson;
    private Label _personDetails = null!;
    private HBoxContainer _topics = null!;
    private Button _haggle = null!;
    private readonly Dictionary<string, List<string>> _transcripts = [];
    private List<ShipOffer> _hulls = [];
    private List<CommodityDef> _commodities = [];

    public string StationName { get; private set; } = "";
    /// <summary>The surface port you're docked at, or null for the system's orbital station.</summary>
    public string? Port { get; set; }
    public event Action? OpenSystemMapRequested;

    public void Init(Session session, Action undock, Action openMap, Action shipChanged)
    {
        _session = session;
        _undock = undock;
        _openMap = openMap;
        _shipChanged = shipChanged;
    }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);

        var root = new VBoxContainer();
        AddChild(root);

        _header = Ui.Text();
        _header.ScrollActive = false;
        _header.FitContent = true;
        _header.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        root.AddChild(_header);

        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _tabs = tabs;
        root.AddChild(tabs);
        tabs.AddChild(BuildJobsTab());
        tabs.AddChild(BuildInHandTab());
        tabs.AddChild(BuildMarketTab());
        tabs.AddChild(BuildShipyardTab());
        tabs.AddChild(BuildOutfittingTab());
        tabs.AddChild(BuildPeopleTab());
        tabs.AddChild(BuildNewsTab());
        tabs.AddChild(BuildStandingTab());
        tabs.AddChild(BuildLogbookTab());
        tabs.AddChild(BuildCrewTab());
        tabs.AddChild(BuildOrdersTab());

        var footer = new HBoxContainer();
        root.AddChild(footer);
        footer.AddChild(Ui.Button("Undock (Enter)", () => _undock()));
        footer.AddChild(Ui.Button("Galaxy map (M)", () => _openMap()));
        footer.AddChild(Ui.Button("System map (N)", () => OpenSystemMapRequested?.Invoke()));
        _status = Ui.Label("", Ui.Dim);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        footer.AddChild(_status);
    }

    /// <summary>Rebuilds every tab from the current session. Called on docking and after any purchase.</summary>
    public void Refresh(string? message = null)
    {
        var here = _session.Here;
        var galaxy = _session.Galaxy;
        var career = _session.Career;
        var db = _session.Db;
        StationName = Port ?? here.Stations.FirstOrDefault() ?? here.Name;
        var owner = galaxy.OwnerOf(here);
        string ownerText = owner == null ? "unclaimed" : $"[color=#{owner.Colour.TrimStart('#')}]{owner.Name}[/color]";
        var events = galaxy.EventsIn(here.Id).Select(e => db.Events.TryGetValue(e.EventId, out var d) ? d.Name : e.EventId).ToList();

        var profile = Galaxus.Core.Galaxy.SystemProfiles.For(galaxy, here, db);
        _header.Text =
            $"[color=#{Ui.Hex(Ui.Amber)}]{StationName.ToUpperInvariant()}[/color]  {here.Name}, {here.Region} {here.Arm} arm. Held by {ownerText}. {here.Economy} economy." +
            $" Population {profile.PopulationText}, {profile.Security.ToLowerInvariant()} security." +
            (profile.CapitalOf != null ? $" [color=#{Ui.Hex(Ui.Amber)}]Capital of {profile.CapitalOf}.[/color]" : "") +
            (events.Count > 0 ? $"  [color=#{Ui.Hex(Ui.Red)}]Now: {string.Join(", ", events)}[/color]" : "") +
            $"\nDay {galaxy.Day}.  {career.Credits:N0} cr.  {db.Hulls[career.HullId].Name}, hold {career.CargoUsed}/{career.CargoCapacity(db)} t.";
        _status.Text = message ?? "";

        RefreshJobs();
        RefreshInHand();
        RefreshMarket();
        RefreshShipyard();
        RefreshOutfitting();
        RefreshPeople();
        RefreshNews();
        RefreshStanding();
        RefreshLogbook();
        RefreshCrew();
        RefreshOrders();
    }

    private RichTextLabel _orders = null!;
    private Button _takeOrders = null!;
    /// <summary>Called when orders are taken, so the game can set a course.</summary>
    public Action? OrdersTaken { get; set; }

    /// <summary>Campaign orders: the debrief from the last mission, and the next orders, taken at your base.</summary>
    private Control BuildOrdersTab()
    {
        var tab = new VBoxContainer { Name = "Orders" };
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _orders = Ui.Text();
        _orders.FitContent = true;
        _orders.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_orders);
        tab.AddChild(scroll);
        _takeOrders = Ui.Button("Take your orders", () =>
        {
            if (Galaxus.Core.Campaigns.TakeOrders(_session) != null)
            {
                Refresh("Orders taken. Undock when you're ready; the course is set.");
                OrdersTaken?.Invoke();
            }
        });
        tab.AddChild(_takeOrders);
        return tab;
    }

    private void RefreshOrders()
    {
        var def = Galaxus.Core.Campaigns.Current(_session);
        int index = _tabs.GetChildCount() - 1;
        _tabs.SetTabHidden(index, def == null);
        if (def == null) return;
        var career = _session.Career;
        bool atBase = StationName == def.BaseName;
        var step = Galaxus.Core.Campaigns.Step(_session);
        var inHand = Galaxus.Core.Campaigns.OrdersInHand(_session);
        string amber = Ui.Hex(Ui.Amber);
        var text = $"[color=#{amber}]{def.Name.ToUpperInvariant()}[/color]   ";
        text += step != null ? $"Mission {career.CampaignStep + 1} of {def.Steps.Count}: {step.Title}\n\n" : "Campaign complete.\n\n";
        if (career.CampaignDebrief is { } debrief) text += debrief + "\n\n";
        if (inHand != null)
            text += $"[color=#{amber}]ORDERS[/color]\n{inHand.Offer.Mission.Briefing}\n\n{_session.NextStep(inHand)}";
        else if (step != null)
            text += atBase ? "Your next orders are ready." : $"Report to the {def.BaseName} for your next orders. It's with the fleet at {_session.Galaxy.Systems[career.CampaignBase].Name}.";
        else
            text += "The galaxy is yours. Keep flying for them, or go your own way.";
        _orders.Text = text;
        _takeOrders.Visible = atBase && step != null && inHand == null;
    }

    /// <summary>For the showcase and tutorials: 0 jobs, 1 in hand, 2 market, 3 shipyard, 4 people, 5 news, 6 standing.</summary>
    public void ShowTab(int index, int slot = -1)
    {
        _tabs.CurrentTab = index;
        if (slot >= 0 && _slots.GetRoot()?.GetChild(slot) is { } item) { item.Select(0); ShowPartsForSlot(); }
        if (slot >= 0 && _parts.GetRoot()?.GetChildCount() > 0) { _parts.GetRoot().GetChild(0).Select(0); ShowSelectedPart(); }
    }

    /// <summary>Selects the first job posted by a faction of the given race. Returns false if there isn't one.</summary>
    public bool SelectJobFrom(string race)
    {
        var root = _jobs.GetRoot();
        if (root == null) return false;
        foreach (var item in root.GetChildren())
        {
            var offer = _offers[item.GetMetadata(0).AsInt32()];
            if (_session.Galaxy.Factions[offer.GiverFactionId].RaceId != race || !offer.Playable) continue;
            item.Select(0);
            ShowSelectedJob();
            return true;
        }
        return false;
    }

    /// <summary>Selects a ship in the shipyard by hull id. Returns false if it isn't sold here.</summary>
    public bool SelectShip(string hullId)
    {
        var root = _yard.GetRoot();
        if (root == null) return false;
        foreach (var item in root.GetChildren())
        {
            if (_hulls[item.GetMetadata(0).AsInt32()].Hull.Id != hullId) continue;
            item.Select(0);
            _yard.ScrollToItem(item);
            ShowSelectedShip();
            return true;
        }
        return false;
    }

    // Jobs ---------------------------------------------------------------------------------

    private Control BuildJobsTab()
    {
        var tab = new HBoxContainer { Name = "Jobs" };
        _jobs = Ui.Table(("Job", 0), ("Pay", 60), ("Days", 30));
        _jobs.SizeFlagsStretchRatio = 1.1f;
        _jobs.ItemSelected += ShowSelectedJob;
        tab.AddChild(_jobs);

        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tab.AddChild(side);
        var client = new HBoxContainer();
        side.AddChild(client);
        _clientFace = new PortraitView { CustomMinimumSize = new Vector2(60, 70) };
        client.AddChild(_clientFace);
        _clientName = Ui.Label("", Ui.Amber, wrap: true);
        _clientName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _clientName.VerticalAlignment = VerticalAlignment.Center;
        client.AddChild(_clientName);
        _jobText = Ui.Text();
        side.AddChild(_jobText);
        var actions = new HBoxContainer();
        side.AddChild(actions);
        _accept = Ui.Button("Accept job", AcceptSelected);
        actions.AddChild(_accept);
        _haggle = Ui.Button("Haggle", HaggleSelected);
        actions.AddChild(_haggle);
        _jobReason = Ui.Label("", Ui.Amber, wrap: true);
        _jobReason.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actions.AddChild(_jobReason);
        return tab;
    }

    private void RefreshJobs()
    {
        // Illegal work is only shown to those who know who to ask.
        var all = _session.Board();
        _offers = [.. all.Where(o => o.Mission.Template.Legal || Galaxus.Core.Law.LawRules.KnowsUnderworld(_session.Career, o.GiverFactionId))];
        int hidden = all.Count - _offers.Count;
        _jobs.Clear();
        var root = _jobs.CreateItem();
        for (int i = 0; i < _offers.Count; i++)
        {
            var o = _offers[i];
            var item = _jobs.CreateItem(root);
            item.SetText(0, (o.Playable ? "" : "(later) ") + o.Mission.Title);
            item.SetText(1, $"{o.Mission.Reward:N0}");
            item.SetText(2, o.Mission.DeadlineDays.ToString());
            item.SetMetadata(0, i);
            if (!o.Playable) for (int c = 0; c < 3; c++) item.SetCustomColor(c, Ui.Dim);
            if (!o.Mission.Template.Legal) item.SetCustomColor(0, Ui.Amber);
        }
        _jobText.Text = (_offers.Count == 0 ? "No work posted here." : "Pick a job to read the briefing.") +
            (hidden > 0 ? "\n\nNot everything here is on the open board. Gossip in the bar, or a fence out in the system, could tell you who to ask." : "");
        _clientFace.Clear();
        _clientName.Text = "";
        _accept.Disabled = true;
        _haggle.Disabled = true;
        _jobReason.Text = "";
    }

    private MissionOffer? SelectedOffer => _jobs.GetSelected() is { } item ? _offers[item.GetMetadata(0).AsInt32()] : null;

    private void ShowSelectedJob()
    {
        if (SelectedOffer is not { } o) return;
        var galaxy = _session.Galaxy;
        var giver = galaxy.Factions[o.GiverFactionId];
        var destination = galaxy.Systems[o.DestinationSystemId];
        int travel = MissionBoard.TravelDays(galaxy, _session.Career.SystemId, o.DestinationSystemId);
        string cargo = o.Mission.Cargo != null ? $"{o.Mission.Tonnes} t of {o.Mission.Cargo.Name.ToLowerInvariant()}" : "none";
        if (o.Mission.Cargo is { Legality: "contraband" }) cargo += $" [color=#{Ui.Hex(Ui.Red)}](contraband: customs scans could find it)[/color]";
        string passenger = o.Mission.Passenger is { } p
            ? $"Passenger: {p.Name}{(p.Count > 1 ? $" and {p.Count - 1} more" : "")}, {_session.Db.Races.GetValueOrDefault(p.RaceId)?.Name.ToLowerInvariant() ?? p.RaceId}. " +
              $"Looks [color=#{Ui.Hex(p.Look == "respectable" ? Ui.Dim : p.Look == "shady" ? Ui.Amber : Ui.Red)}]{Passengers.LookName(p.Look)}[/color]. " +
              $"Needs {p.Count} berth{(p.Count == 1 ? "" : "s")} (you have {Math.Max(0, Passengers.FreeBerths(_session.Db, _session.Career))} free)\n"
            : "";
        _jobText.Text =
            $"[color=#{Ui.Hex(Ui.Amber)}]{o.Mission.Title}[/color]\n" +
            $"From [color=#{giver.Colour.TrimStart('#')}]{giver.Name}[/color]{(o.Mission.Template.Legal ? "" : $" [color=#{Ui.Hex(Ui.Red)}](illegal)[/color]")}\n\n" +
            $"{o.Mission.Briefing}\n\n" +
            $"Destination: {o.DestinationStation}, {destination.Name} ({travel} days away)\n" +
            passenger + (o.Mission.Cargo != null || o.Mission.Passenger == null ? $"Cargo: {cargo}\n" : "") +
            $"Deadline: {o.Mission.DeadlineDays} days\nPays: {o.Mission.Reward:N0} cr\n" +
            $"Your standing with them: {Career.RankNames[_session.Career.RankWith(giver.Id)]}";
        var person = o.ClientId == null ? null : PeopleDirectory.Contacts(_session.Db, galaxy, giver).FirstOrDefault(c => c.Id == o.ClientId);
        if (o.Mission.Passenger is { } traveller)
        {
            // Passenger jobs show who you'd be carrying.
            var face = traveller.AsPerson(_session.Here.Id);
            _clientFace.ShowPerson(_session.Db, galaxy, face);
            _clientName.Text = $"{traveller.Name}\nYour passenger, {RaceName(face)}" + (person != null ? $"\nBooked by {person.Name}" : "");
        }
        else if (person != null)
        {
            _clientFace.ShowPerson(_session.Db, galaxy, person);
            _clientName.Text = $"{person.Name}\n{RoleName(person)}, {RaceName(person)}" + (_session.Career.MetPeople.Contains(person.Id) ? "\nYou've worked together before." : "");
        }
        var reason = _session.Career.CannotAccept(_session.Db, o);
        _accept.Disabled = reason != null;
        _haggle.Disabled = _session.Career.Haggled.ContainsKey(o.Id);
        _jobReason.Text = reason ?? "";
    }

    private void HaggleSelected()
    {
        if (SelectedOffer is not { } o) return;
        var result = Conversation.Haggle(_session, o);
        AudioManager.Instance?.Ui(result.Accepted ? "paid" : "error");
        string who = o.ClientId != null ? PeopleDirectory.Contacts(_session.Db, _session.Galaxy, _session.Galaxy.Factions[o.GiverFactionId]).FirstOrDefault(c => c.Id == o.ClientId)?.Name ?? "The client" : "The client";
        Refresh($"{who}: {result.Text}");
        if (!result.Withdrawn) SelectOffer(o.Id);
    }

    private void SelectOffer(string offerId)
    {
        var root = _jobs.GetRoot();
        if (root == null) return;
        foreach (var item in root.GetChildren())
            if (_offers[item.GetMetadata(0).AsInt32()].Id == offerId) { item.Select(0); ShowSelectedJob(); return; }
    }

    private void AcceptSelected()
    {
        if (SelectedOffer is not { } o || _session.Career.CannotAccept(_session.Db, o) != null) return;
        _session.Career.Accept(_session.Galaxy, o);
        AudioManager.Instance?.Ui("confirm");
        Refresh($"Accepted: {o.Mission.Title}");
    }

    // In hand ------------------------------------------------------------------------------

    private Control BuildInHandTab()
    {
        var tab = new VBoxContainer { Name = "In hand" };
        _inHand = Ui.Table(("Job", 0), ("Aboard", 150), ("Destination", 130), ("Days left", 50), ("Pay", 60));
        tab.AddChild(_inHand);
        return tab;
    }

    private void RefreshInHand()
    {
        var galaxy = _session.Galaxy;
        _inHand.Clear();
        var root = _inHand.CreateItem();
        foreach (var job in _session.Career.Missions)
        {
            var item = _inHand.CreateItem(root);
            item.SetText(0, job.Offer.Mission.Title);
            var mission = job.Offer.Mission;
            item.SetText(1, mission.Passenger is { } p ? $"{p.Name}{(p.Count > 1 ? $" +{p.Count - 1}" : "")} ({Passengers.LookName(p.Look)})"
                : mission.Cargo != null && mission.Tonnes > 0 ? $"{mission.Tonnes} t {mission.Cargo.Name.ToLowerInvariant()}" : "");
            if (mission.Passenger is { Look: not "respectable" } shady) item.SetCustomColor(1, shady.Look == "shady" ? Ui.Amber : Ui.Red);
            item.SetText(2, galaxy.Systems[job.Offer.DestinationSystemId].Name);
            int left = job.DeadlineDay - galaxy.Day;
            int travel = MissionBoard.TravelDays(galaxy, _session.Career.SystemId, job.Offer.DestinationSystemId);
            item.SetText(3, $"{left} ({travel} to go)");
            if (travel > left) item.SetCustomColor(3, Ui.Red);
            item.SetText(4, $"{job.Offer.Mission.Reward:N0}");
        }
    }

    // Market -------------------------------------------------------------------------------

    private Control BuildMarketTab()
    {
        var tab = new VBoxContainer { Name = "Market" };
        _market = Ui.Table(("Goods", 0), ("Category", 80), ("Buy", 50), ("Sell", 50), ("Stock", 45), ("Week", 45), ("Held", 35));
        _market.ItemSelected += ShowMarketTip;
        tab.AddChild(_market);
        var actions = new HBoxContainer();
        tab.AddChild(actions);
        actions.AddChild(Ui.Button("Buy 1 t", () => Trade(1)));
        actions.AddChild(Ui.Button("Buy 10 t", () => Trade(10)));
        actions.AddChild(Ui.Button("Sell 1 t", () => Trade(-1)));
        actions.AddChild(Ui.Button("Sell all", () => Trade(-10000)));
        actions.AddChild(Ui.Button("Fill up fuel", FillUp));
        actions.AddChild(Ui.Button("Trading screen", () => _tradeScreen.Open(_market.GetSelected() is { } i ? _commodities[i.GetMetadata(0).AsInt32()].Id : null)));
        _marketNote = Ui.Label("", Ui.Dim);
        _marketNote.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actions.AddChild(_marketNote);
        return tab;
    }

    private void RefreshMarket()
    {
        var db = _session.Db;
        var here = _session.Here;
        string? selected = _market.GetSelected() is { } sel ? _commodities[sel.GetMetadata(0).AsInt32()].Id : null;
        var quotes = Market.Quotes(_session, here);
        // Anything in the hold that isn't traded here still shows, so you know you can't sell it.
        var traded = quotes.Select(q => q.Commodity.Id).ToHashSet();
        var held = _session.Career.Cargo.Keys.Where(id => !traded.Contains(id)).Select(id => db.Commodities[id]).ToList();
        _commodities = [.. quotes.Select(q => q.Commodity), .. held];
        _market.Clear();
        var root = _market.CreateItem();
        for (int i = 0; i < _commodities.Count; i++)
        {
            var c = _commodities[i];
            var q = i < quotes.Count ? quotes[i] : null;
            var item = _market.CreateItem(root);
            string deal = q is { Deal: true } ? (q.Stock > q.Wanted ? "  SURPLUS" : "  WANTED") : "";
            item.SetText(0, c.Name + deal);
            if (deal != "") item.SetCustomColor(0, Ui.Amber);
            item.SetText(1, c.Category + (c.Legality == "legal" ? "" : $" ({c.Legality})"));
            if (c.Legality == "contraband") item.SetCustomColor(1, Ui.Red);
            if (q != null)
            {
                var (low, high) = Market.Range(c);
                item.SetText(2, q.Stock > 0 ? q.Buy.ToString("N0") : "none");
                item.SetText(3, q.Sell.ToString("N0"));
                // White: cheap for this good. Amber: dear, worth bringing some here.
                if (q.Stock > 0 && q.Buy < low + (high - low) * 0.3f) item.SetCustomColor(2, Colors.White);
                if (q.Sell > low + (high - low) * 0.65f) item.SetCustomColor(3, Ui.Amber);
                item.SetText(4, q.Stock.ToString("N0"));
                item.SetText(5, Math.Abs(q.Trend) < 0.03f ? "steady" : $"{(q.Trend > 0 ? "+" : "")}{q.Trend * 100:0}%");
                if (Math.Abs(q.Trend) >= 0.03f) item.SetCustomColor(5, q.Trend > 0 ? Ui.Amber : Colors.White);
            }
            else
            {
                item.SetText(2, "");
                item.SetText(3, "not traded here");
            }
            int tonnes = _session.Career.Cargo.GetValueOrDefault(c.Id);
            item.SetText(6, tonnes > 0 ? tonnes.ToString() : "");
            item.SetMetadata(0, i);
            if (c.Id == selected) item.Select(0);
        }
        _marketNote.Text = MarketNote;
        ShowMarketTip();
    }

    private TradeScreen? _tradeScreenNode;

    /// <summary>The trading screen, made the first time it's opened.</summary>
    private TradeScreen _tradeScreen
    {
        get
        {
            if (_tradeScreenNode == null)
            {
                _tradeScreenNode = new TradeScreen();
                _tradeScreenNode.Init(_session);
                AddChild(_tradeScreenNode);
            }
            return _tradeScreenNode;
        }
    }

    private string MarketNote
    {
        get
        {
            float fuel = _session.FuelLy, tank = _session.FuelTankLy;
            return $"Fuel {fuel:0}/{tank:0} ly ({_session.FuelPricePerLy()} cr a light year). Stock is limited and prices move as you trade.";
        }
    }

    /// <summary>Fills the tank for direct jumps, as far as your credits go.</summary>
    private void FillUp()
    {
        int before = _session.Career.Credits;
        float added = _session.Refuel();
        AudioManager.Instance?.Ui(added > 0 ? "paid" : "error");
        Refresh(added > 0 ? $"Filled up: {added:0.0} ly of fuel for {before - _session.Career.Credits:N0} cr." : _session.FuelLy >= _session.FuelTankLy - 0.01f ? "Your tank is already full." : "You can't afford any fuel.");
    }

    /// <summary>For something in your hold: the best place you know to sell it, if anywhere beats here.</summary>
    private void ShowMarketTip()
    {
        if (_market.GetSelected() is not { } item) return;
        var c = _commodities[item.GetMetadata(0).AsInt32()];
        _marketNote.Text = _session.Career.Cargo.ContainsKey(c.Id) && TradeIntel.BestSale(_session, c.Id) is { } best
            ? $"Best sale you know for {c.Name.ToLowerInvariant()}: {best.System.Name}, {best.Sell:N0} cr" + (best.AgeDays > 0 ? $" ({best.AgeDays} days ago)." : ".")
            : MarketNote;
    }

    private void Trade(int tonnes)
    {
        if (_market.GetSelected() is not { } item) { _status.Text = "Pick some goods first."; return; }
        var c = _commodities[item.GetMetadata(0).AsInt32()];
        bool tradedHere = Market.Legal(_session.Db, _session.Here, _session.Galaxy).Any(x => x.Id == c.Id);
        if (!tradedHere) { AudioManager.Instance?.Ui("error"); Refresh($"No one here deals in {c.Name.ToLowerInvariant()}."); return; }
        int credits = _session.Career.Credits;
        int done = tonnes > 0 ? _session.Buy(c.Id, tonnes) : _session.Sell(c.Id, -tonnes);
        int change = Math.Abs(_session.Career.Credits - credits);
        AudioManager.Instance?.Ui(done == 0 ? "error" : tonnes > 0 ? "confirm" : "paid");
        Refresh(done == 0
            ? (tonnes > 0 ? "Can't buy: none left, no money or no room." : "You have none to sell.")
            : $"{(tonnes > 0 ? "Bought" : "Sold")} {done} t of {c.Name.ToLowerInvariant()} for {change:N0} cr.");
    }

    // Shipyard -----------------------------------------------------------------------------

    private Control BuildShipyardTab()
    {
        var tab = new HBoxContainer { Name = "Shipyard" };
        _yard = Ui.Table(("Ship", 0), ("Class", 55), ("Price", 60));
        _yard.ItemSelected += ShowSelectedShip;
        tab.AddChild(_yard);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tab.AddChild(side);
        _yardText = Ui.Text();
        side.AddChild(_yardText);
        var actions = new HBoxContainer();
        side.AddChild(actions);
        _buyShip = Ui.Button("Buy ship", BuySelectedShip);
        actions.AddChild(_buyShip);
        _yardReason = Ui.Label("", Ui.Amber, wrap: true);
        _yardReason.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actions.AddChild(_yardReason);
        return tab;
    }

    private void RefreshShipyard()
    {
        var db = _session.Db;
        _hulls = _session.Shipyard();
        _yard.Clear();
        var root = _yard.CreateItem();
        for (int i = 0; i < _hulls.Count; i++)
        {
            var (h, price) = _hulls[i];
            var item = _yard.CreateItem(root);
            item.SetText(0, $"{h.Name} ({db.Builders[h.Builder].Name})");
            item.SetText(1, h.Class);
            item.SetText(2, $"{price:N0}");
            // White: below its usual price. Amber: dear here.
            var (low, high) = Shipyards.Range(h);
            if (price < low + (high - low) * 0.3f) item.SetCustomColor(2, Colors.White);
            else if (price > low + (high - low) * 0.7f) item.SetCustomColor(2, Ui.Amber);
            item.SetMetadata(0, i);
            if (h.Id == _session.Career.HullId) item.SetCustomColor(0, Colors.White);
        }
        _yardText.Text = (_hulls.Count == 0 ? "Nothing on sale this week. Stock changes weekly, and bigger stations carry more.\n\n" : "Stock changes week by week.\n\n")
            + $"Your {db.Hulls[_session.Career.HullId].Name} is worth {_session.TradeInValue():N0} cr in trade here.";
        _buyShip.Disabled = true;
        _yardReason.Text = "";
    }

    private ShipOffer? SelectedHull => _yard.GetSelected() is { } item ? _hulls[item.GetMetadata(0).AsInt32()] : null;

    private void ShowSelectedShip()
    {
        if (SelectedHull is not { } offer) return;
        var h = offer.Hull;
        var db = _session.Db;
        int tradeIn = _session.TradeInValue();
        var spec = Galaxus.Core.Ships.ShipFactory.Build(db, h.Id);
        var builder = db.Builders[h.Builder];
        int capacity = h.CargoTonnes + (int)h.DefaultLoadout.Select(id => db.Parts[id]).Where(p => p.Type == "cargo").Sum(p => p.Stats.GetValueOrDefault("tonnes"));
        _yardText.Text =
            $"[color=#{Ui.Hex(Ui.Amber)}]{h.Name}[/color], {h.Class} by {builder.Name}\n{builder.Style}. {builder.Bias}.\n\n{h.Description}\n\n" +
            $"Top speed {spec.MaxSpeed:0} m/s, acceleration {spec.Acceleration:0} m/s²\n" +
            $"Hull {spec.MaxHull:0}, shields {spec.MaxShields:0}, {spec.Weapons.Count} guns\n" +
            $"Hold {capacity} t, {h.CrewBerths} crew berths\n" +
            $"Price {offer.Price:N0} cr here (usually {Shipyards.Range(h).Low:N0} to {Shipyards.Range(h).High:N0}), {offer.Price - tradeIn:N0} cr after trade-in";
        var reason = _session.Career.CannotBuyShip(db, offer, tradeIn);
        _buyShip.Disabled = reason != null;
        _yardReason.Text = reason ?? "";
    }

    private void BuySelectedShip()
    {
        int tradeIn = _session.TradeInValue();
        if (SelectedHull is not { } offer || _session.Career.CannotBuyShip(_session.Db, offer, tradeIn) != null) return;
        var h = offer.Hull;
        _session.Career.BuyShip(offer, tradeIn);
        AudioManager.Instance?.Ui("paid");
        _shipChanged();
        Refresh($"You now fly a {h.Name}.");
    }

    // People -------------------------------------------------------------------------------

    private Control BuildPeopleTab()
    {
        var tab = new HBoxContainer { Name = "People" };
        _people = Ui.Table(("Name", 0), ("Doing", 120));
        _people.ItemSelected += ShowSelectedPerson;
        tab.AddChild(_people);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tab.AddChild(side);
        var header = new HBoxContainer();
        side.AddChild(header);
        _personFace = new PortraitView { CustomMinimumSize = new Vector2(96, 112) };
        header.AddChild(_personFace);
        _personDetails = Ui.Label("", Ui.Green, wrap: true);
        _personDetails.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _personDetails.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_personDetails);
        _personText = Ui.Text();
        _personText.ScrollFollowing = true;
        side.AddChild(_personText);
        _topics = new HBoxContainer();
        side.AddChild(_topics);
        foreach (var topic in Enum.GetValues<Topic>())
        {
            var t = topic;
            _topics.AddChild(Ui.Button(topic switch
            {
                Topic.Rumour => "News",
                Topic.Work => "Work",
                Topic.Self => "Them",
                Topic.Drink => $"Drink ({Conversation.DrinkCost} cr)",
                _ => "Goodbye",
            }, () => Talk(t)));
        }
        _hire = Ui.Button("Hire", HireShownPerson);
        _topics.AddChild(_hire);
        return tab;
    }

    private void RefreshPeople()
    {
        _here = PeopleDirectory.AtStation(_session.Db, _session.Galaxy, _session.Here);
        _people.Clear();
        var root = _people.CreateItem();
        for (int i = 0; i < _here.Count; i++)
        {
            var p = _here[i];
            var item = _people.CreateItem(root);
            item.SetText(0, p.Name + (_session.Career.MetPeople.Contains(p.Id) ? "  (met)" : ""));
            item.SetText(1, RoleName(p));
            item.SetMetadata(0, i);
            if (p.Regular) item.SetCustomColor(0, Colors.White);
        }
        _shownPerson = null;
        _personFace.Clear();
        _personDetails.Text = "";
        _topics.Visible = false;
        _personText.Text = _here.Count == 0 ? "Nobody about." : "Regulars are in white. Pick someone to talk to them.";
    }

    private void ShowSelectedPerson()
    {
        if (_people.GetSelected() is not { } item) return;
        var p = _here[item.GetMetadata(0).AsInt32()];
        if (_shownPerson == p.Id) return;
        _shownPerson = p.Id;
        _personFace.ShowPerson(_session.Db, _session.Galaxy, p);
        _topics.Visible = true;
        if (!_transcripts.ContainsKey(p.Id))
            _transcripts[p.Id] = [$"[color=#{Ui.Hex(Ui.Amber)}]{p.Name}:[/color] {Conversation.Greeting(_session, p)}"];
        _session.Career.MetPeople.Add(p.Id);
        item.SetText(0, p.Name + "  (met)");
        ShowPerson(p);
    }

    /// <summary>For the showcase: say a few things to whoever is selected.</summary>
    public void TalkAbout(params Topic[] topics)
    {
        foreach (var topic in topics) Talk(topic);
    }

    private Person? ShownPerson => _here.FirstOrDefault(p => p.Id == _shownPerson);

    private void Talk(Topic topic)
    {
        if (ShownPerson is not { } p) return;
        var reply = Conversation.Ask(_session, p, topic);
        var lines = _transcripts[p.Id];
        lines.Add($"[color=#{Ui.Hex(Ui.Dim)}]You:[/color] {Lines.Captain(topic)}");
        lines.Add($"[color=#{Ui.Hex(Ui.Amber)}]{p.Name}:[/color] {reply.Text}");
        if (reply.Lead != null) lines.Add($"[color=#{Ui.Hex(Ui.Red)}](A lead. It's marked on your galaxy map.)[/color]");
        AudioManager.Instance?.Ui(reply.DispositionChange > 0 ? "confirm" : "click");
        ShowPerson(p);
        RefreshHeaderOnly();
    }

    private void ShowPerson(Person p)
    {
        var galaxy = _session.Galaxy;
        var faction = p.FactionId >= 0 ? galaxy.Factions[p.FactionId] : null;
        int feeling = Conversation.Disposition(_session.Career, p);
        string mood = feeling >= 25 ? "friendly" : feeling >= 8 ? "warm" : feeling > -8 ? "neutral" : feeling > -20 ? "cool" : "cold";
        _personDetails.Text =
            $"{p.Name}\n{RoleName(p)}. {Capitalise(RaceName(p))}{(p.Gender == "neither" ? "" : $", {p.Gender}")}." +
            (faction != null ? $"\nWorks under {faction.Name}." : "") +
            $"\n{(p.Regular ? "A regular here" : "Passing through")}. Seems {mood}.";
        _personText.Text = string.Join("\n", _transcripts[p.Id]);
        // Pilots, engineers and the like will sign on for the right money.
        var offer = CrewRules.Offer(_session, p);
        _hire.Visible = offer != null && !_session.Career.Crew.Any(c => c.PersonId == p.Id);
        if (offer != null)
        {
            string ship = offer.HullId != null && _session.Db.Hulls.TryGetValue(offer.HullId, out var h) ? $", flying their own {h.Name}" : "";
            _personDetails.Text += $"\nWould sign on as {CrewRules.JobName(offer.Job)} (skill {offer.Skill * 100:0}%{ship}).";
            _hire.Text = $"Hire: {offer.Fee:N0} cr, then {offer.Wage} cr a day";
            var reason = CrewRules.CannotHire(_session, p);
            _hire.Disabled = reason != null;
            _hire.TooltipText = reason ?? "";
        }
    }

    private Button _hire = null!;

    private void HireShownPerson()
    {
        if (ShownPerson is not { } p) return;
        if (CrewRules.CannotHire(_session, p) is { } reason) { _status.Text = reason; return; }
        var member = CrewRules.Hire(_session, p)!;
        _transcripts[p.Id].Add($"[color=#{Ui.Hex(Ui.Green)}]{p.Name} signs on as your {CrewRules.JobName(member.Job)}.[/color]");
        AudioManager.Instance?.Ui("paid");
        Refresh($"{p.Name} has joined your crew.");
    }

    /// <summary>Updates credits in the header after buying a round, without rebuilding every tab.</summary>
    private void RefreshHeaderOnly()
    {
        var career = _session.Career;
        var db = _session.Db;
        var text = _header.Text;
        int cut = text.LastIndexOf("\nDay ", StringComparison.Ordinal);
        if (cut >= 0)
            _header.Text = text[..cut] + $"\nDay {_session.Galaxy.Day}.  {career.Credits:N0} cr.  {db.Hulls[career.HullId].Name}, hold {career.CargoUsed}/{career.CargoCapacity(db)} t.";
    }

    /// <summary>For the showcase: selects the nth person present.</summary>
    public void SelectPerson(int index)
    {
        var items = _people.GetRoot()?.GetChildren();
        if (items == null || items.Count == 0) return;
        // Selecting fires ItemSelected, which shows the person.
        items[Math.Min(index, items.Count - 1)].Select(0);
    }

    private string RoleName(Person p) => _session.Db.Roles.TryGetValue(p.RoleId, out var r) ? r.Name : p.RoleId;
    private string RaceName(Person p) => _session.Db.Races.TryGetValue(p.RaceId, out var r) ? r.Name.ToLowerInvariant() : p.RaceId;
    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // News and standing --------------------------------------------------------------------

    private Control BuildNewsTab()
    {
        var tab = new VBoxContainer { Name = "News" };
        _news = Ui.Text();
        tab.AddChild(_news);
        return tab;
    }

    private void RefreshNews()
    {
        var galaxy = _session.Galaxy;
        // Word travels by ship, so the feed shows news from within a few jumps, newest first.
        var nearby = galaxy.WithinJumps(_session.Career.SystemId, 6).Select(s => s.Id).Append(_session.Career.SystemId).ToHashSet();
        var items = galaxy.News.Where(n => nearby.Contains(n.SystemId)).Reverse().Take(60).ToList();
        _news.Text = items.Count == 0 ? "Nothing worth reporting nearby." :
            string.Join("\n", items.Select(n =>
                $"[color=#{Ui.Hex(Ui.Dim)}]Day {n.Day}, {galaxy.Systems[n.SystemId].Name}[/color]  {n.Text}"));
    }

    private Control BuildStandingTab()
    {
        var tab = new VBoxContainer { Name = "Standing" };
        _fines = new VBoxContainer();
        tab.AddChild(_fines);
        _standing = Ui.Table(("Faction", 0), ("Rank", 90), ("Points", 50));
        tab.AddChild(_standing);
        return tab;
    }

    private VBoxContainer _fines = null!;

    /// <summary>Your record with each faction's law, and paying it off.</summary>
    private void RefreshFines()
    {
        foreach (var child in _fines.GetChildren()) child.QueueFree();
        var career = _session.Career;
        var galaxy = _session.Galaxy;
        foreach (var factionId in career.Offences.Select(o => o.FactionId).Distinct().OrderBy(f => f))
        {
            var faction = galaxy.Factions[factionId];
            var standing = Galaxus.Core.Law.LawRules.StandingWith(career, factionId);
            int owed = Galaxus.Core.Law.LawRules.Owed(career, factionId);
            var row = new HBoxContainer();
            _fines.AddChild(row);
            var text = Ui.Label($"{(standing == Galaxus.Core.Law.Standing.Wanted ? "WANTED" : "Fined")} by {faction.Name} for {Galaxus.Core.Law.Hails.Record(_session, factionId)}: {owed:N0} cr.",
                standing == Galaxus.Core.Law.Standing.Wanted ? Ui.Red : Ui.Amber, wrap: true);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(text);
            var pay = Ui.Button($"Pay {owed:N0} cr", () =>
            {
                if (!Galaxus.Core.Law.LawRules.Pay(career, factionId)) return;
                AudioManager.Instance?.Ui("paid");
                Refresh($"Paid {owed:N0} cr. Your record with {faction.Name} is clean.");
            });
            pay.Disabled = owed > career.Credits;
            row.AddChild(pay);
        }
        if (career.Offences.Count == 0) _fines.AddChild(Ui.Label("Your record is clean everywhere.", Ui.Dim));
    }

    private void RefreshStanding()
    {
        RefreshFines();
        var galaxy = _session.Galaxy;
        _standing.Clear();
        var root = _standing.CreateItem();
        var rows = _session.Career.Reputation.OrderByDescending(r => r.Value).ToList();
        foreach (var (factionId, points) in rows)
        {
            var item = _standing.CreateItem(root);
            item.SetText(0, galaxy.Factions[factionId].Name);
            item.SetCustomColor(0, Color.FromHtml(galaxy.Factions[factionId].Colour));
            item.SetText(1, Career.RankNames[_session.Career.RankWith(factionId)]);
            item.SetText(2, points.ToString());
        }
        if (rows.Count == 0)
        {
            var item = _standing.CreateItem(root);
            item.SetText(0, "Nobody knows you yet. Take some work.");
        }
    }
}
