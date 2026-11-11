using Godot;
using Galaxus.Core;
using Galaxus.Core.Galaxy;

namespace Galaxus.Ui;

/// <summary>
/// The trading screen: every market you know, side by side. Pick goods on the left to see what
/// each market you've seen buys and sells them for (with how old that is and how far away it is);
/// the best runs from where you are sit along the bottom. Opens from the galaxy map and the
/// station's market tab, and shows only what you know.
/// </summary>
public partial class TradeScreen : PanelContainer
{
    private Session _session = null!;
    private Tree _goods = null!, _markets = null!;
    private RichTextLabel _runs = null!;
    private Label _title = null!;
    private List<string> _goodsIds = [];

    public void Init(Session session) => _session = session;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 24);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.05f, 0.04f, 0.98f), BorderColor = Ui.Green with { A = 0.6f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        });

        var box = new VBoxContainer();
        AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        _title = Ui.Label("TRADING", Ui.Amber);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_title);
        header.AddChild(Ui.Button("Close", () => Visible = false));

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(columns);
        _goods = Ui.Table(("Goods", 0), ("Known", 36));
        _goods.CustomMinimumSize = new Vector2(130, 0);
        _goods.SizeFlagsStretchRatio = 1;
        _goods.ItemSelected += ShowMarkets;
        columns.AddChild(_goods);
        _markets = Ui.Table(("Market", 0), ("Buy", 40), ("Sell", 40), ("Stock", 38), ("Week", 44), ("Seen", 48), ("Away", 46));
        _markets.SetColumnCustomMinimumWidth(0, 90);
        _markets.SizeFlagsStretchRatio = 2.8f;
        columns.AddChild(_markets);

        box.AddChild(Ui.Label("Best runs from here, by profit a day of travel:", Ui.Amber));
        _runs = Ui.Text();
        _runs.FitContent = true;
        _runs.ScrollActive = false;
        box.AddChild(_runs);
    }

    /// <summary>Opens on the goods you're carrying, or the first good you know.</summary>
    public void Open(string? goods = null)
    {
        Visible = true;
        var db = _session.Db;
        _title.Text = $"TRADING FROM {_session.Here.Name.ToUpperInvariant()}: {_session.Career.SeenMarkets.Count} markets known. Prices are as you last saw them.";
        _goodsIds = [.. db.Commodities.Values.Where(c => c.Legality != "contraband" || _session.Career.Cargo.ContainsKey(c.Id))
            .OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => c.Id)];
        goods ??= _session.Career.Cargo.Keys.FirstOrDefault() ?? _goodsIds.FirstOrDefault();
        _goods.Clear();
        var root = _goods.CreateItem();
        for (int i = 0; i < _goodsIds.Count; i++)
        {
            var id = _goodsIds[i];
            int known = TradeIntel.Known(_session, id).Count;
            var item = _goods.CreateItem(root);
            item.SetText(0, db.Commodities[id].Name);
            item.SetText(1, known.ToString());
            if (known == 0) item.SetCustomColor(0, Ui.Dim);
            if (_session.Career.Cargo.ContainsKey(id)) item.SetCustomColor(0, Ui.Amber);
            item.SetMetadata(0, i);
            if (id == goods) item.Select(0);
        }
        ShowMarkets();
        var galaxy = _session.Galaxy;
        var runs = TradeIntel.Routes(_session, null, 8);
        _runs.Text = runs.Count == 0
            ? "None yet. Visit more markets to learn their prices."
            : string.Join("\n", runs.Select(r => $"{db.Commodities[r.CommodityId].Name}: buy {r.Tonnes} t here at {r.Buy:N0}, sell at {galaxy.Systems[r.To].Name} for {r.Sell:N0}. About {r.Profit:N0} cr in {r.Days} days" + (r.AgeDays > 7 ? $" ({r.AgeDays} days old)." : ".")));
    }

    private void ShowMarkets()
    {
        _markets.Clear();
        var root = _markets.CreateItem();
        if (_goods.GetSelected() is not { } selected) return;
        var id = _goodsIds[selected.GetMetadata(0).AsInt32()];
        var galaxy = _session.Galaxy;
        var (low, high) = Market.Range(_session.Db.Commodities[id]);
        foreach (var (system, price, age) in TradeIntel.Known(_session, id).OrderByDescending(k => k.Price.Sell))
        {
            var item = _markets.CreateItem(root);
            item.SetText(0, system.Name + (system.Id == _session.Career.SystemId ? " (here)" : ""));
            item.SetText(1, price.Stock > 0 ? price.Buy.ToString("N0") : "none");
            item.SetText(2, price.Sell.ToString("N0"));
            item.SetText(3, price.Stock.ToString("N0"));
            item.SetText(4, Math.Abs(price.Trend) < 0.03f ? "steady" : $"{(price.Trend > 0 ? "+" : "")}{price.Trend * 100:0}%");
            item.SetText(5, age == 0 ? "today" : $"{age} days");
            var route = galaxy.Route(_session.Career.SystemId, system.Id);
            item.SetText(6, system.Id == _session.Career.SystemId ? "" : route.Count > 1 ? $"{galaxy.RouteDays(route)} days" : "direct");
            // White: cheap to buy. Amber: pays well.
            if (price.Stock > 0 && price.Buy < low + (high - low) * 0.3f) item.SetCustomColor(1, Colors.White);
            if (price.Sell > low + (high - low) * 0.65f) item.SetCustomColor(2, Ui.Amber);
            if (Math.Abs(price.Trend) >= 0.03f) item.SetCustomColor(4, price.Trend > 0 ? Ui.Amber : Colors.White);
            if (price.Deal) item.SetCustomColor(0, Ui.Amber);
        }
    }
}
