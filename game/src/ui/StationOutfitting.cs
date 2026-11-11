using Godot;
using Galaxus.Core.Content;
using Galaxus.Core.Galaxy;
using Galaxus.Core.Ships;

namespace Galaxus.Ui;

/// <summary>
/// The outfitting tab: your ship's slots on the left, the parts on sale that fit the chosen slot in
/// the middle, and what a part would change on the right. Buying a part sells the old one in that
/// slot to the outfitter.
/// </summary>
public partial class StationScreen
{
    private Tree _slots = null!, _parts = null!;
    private RichTextLabel _partText = null!;
    private Button _fitPart = null!, _sellPart = null!;
    private Label _partReason = null!;
    private List<PartOffer> _shelf = [];
    private List<PartOffer> _fitting = [];

    private Control BuildOutfittingTab()
    {
        var tab = new HBoxContainer { Name = "Outfitting" };
        _slots = Ui.Table(("Slot", 62), ("Fitted", 0));
        _slots.CustomMinimumSize = new Vector2(170, 0);
        _slots.SizeFlagsHorizontal = SizeFlags.Fill;
        _slots.ItemSelected += ShowPartsForSlot;
        tab.AddChild(_slots);
        _parts = Ui.Table(("On sale", 0), ("Price", 50));
        _parts.CustomMinimumSize = new Vector2(170, 0);
        _parts.SizeFlagsHorizontal = SizeFlags.Fill;
        _parts.ItemSelected += ShowSelectedPart;
        tab.AddChild(_parts);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tab.AddChild(side);
        _partText = Ui.Text();
        side.AddChild(_partText);
        var actions = new HBoxContainer();
        side.AddChild(actions);
        _fitPart = Ui.Button("Buy and fit", FitSelectedPart);
        actions.AddChild(_fitPart);
        _sellPart = Ui.Button("Sell fitted part", SellFittedPart);
        actions.AddChild(_sellPart);
        _partReason = Ui.Label("", Ui.Amber, wrap: true);
        _partReason.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        side.AddChild(_partReason);
        return tab;
    }

    private void RefreshOutfitting()
    {
        int keep = SelectedSlot;
        _shelf = _session.Outfitter();
        var hull = _session.Db.Hulls[_session.Career.HullId];
        var fitted = Outfitting.Fitted(_session);
        _slots.Clear();
        var root = _slots.CreateItem();
        for (int i = 0; i < hull.Slots.Count; i++)
        {
            var slot = hull.Slots[i];
            var item = _slots.CreateItem(root);
            item.SetText(0, $"{(slot.Type switch { "powerPlant" => "power", "cargo" => "cargo", _ => slot.Type })} {slot.Size}" + (slot.Turret ? " T" : ""));
            item.SetText(1, fitted[i]?.Name ?? "empty");
            if (fitted[i] == null) item.SetCustomColor(1, Ui.Dim);
            int fits = _shelf.Count(o => ShipFactory.SlotTakes(slot, o.Part));
            if (fits == 0) item.SetCustomColor(0, Ui.Dim);
            item.SetMetadata(0, i);
            if (i == keep) item.Select(0);
        }
        if (keep < 0) ShowPartsForSlot();
    }

    private int SelectedSlot => _slots.GetSelected() is { } item ? item.GetMetadata(0).AsInt32() : -1;
    private PartOffer? SelectedPart => _parts.GetSelected() is { } item ? _fitting[item.GetMetadata(0).AsInt32()] : null;

    private void ShowPartsForSlot()
    {
        _parts.Clear();
        var root = _parts.CreateItem();
        _fitPart.Disabled = true;
        int index = SelectedSlot;
        var hull = _session.Db.Hulls[_session.Career.HullId];
        if (index < 0)
        {
            _fitting = [];
            _partText.Text = (_shelf.Count == 0 ? "The outfitter has nothing in this week. " : $"The outfitter has {_shelf.Count} parts in this week. ")
                + "Stock changes weekly, and bigger stations carry higher marks.\n\nPick a slot to see what fits it. Buying a part sells the old one to the outfitter for half its list price.";
            _sellPart.Disabled = true;
            _partReason.Text = "";
            return;
        }
        var slot = hull.Slots[index];
        _fitting = [.. _shelf.Where(o => ShipFactory.SlotTakes(slot, o.Part))
            .OrderByDescending(o => o.Part.Size).ThenBy(o => o.Price)];
        for (int i = 0; i < _fitting.Count; i++)
        {
            var offer = _fitting[i];
            var item = _parts.CreateItem(root);
            item.SetText(0, offer.Part.Name);
            item.SetText(1, $"{offer.Price:N0}");
            var (low, high) = Outfitting.Range(offer.Part);
            if (offer.Price < low + (high - low) * 0.3f) item.SetCustomColor(1, Colors.White);
            else if (offer.Price > low + (high - low) * 0.7f) item.SetCustomColor(1, Ui.Amber);
            if (offer.Price > _session.Career.Credits + (Outfitting.Fitted(_session)[index] is { } old ? Outfitting.SellValue(old) : 0))
                item.SetCustomColor(0, Ui.Dim);
            item.SetMetadata(0, i);
        }
        ShowFitted(index);
    }

    /// <summary>What's in the slot now, and whether it can be sold.</summary>
    private void ShowFitted(int index)
    {
        var slot = _session.Db.Hulls[_session.Career.HullId].Slots[index];
        var part = Outfitting.Fitted(_session)[index];
        string sizes = $"{Outfitting.Label(slot.Type)} slot, size {slot.Size}{(slot.Turret ? ", turret" : "")}";
        _partText.Text = part == null
            ? $"[color=#{Ui.Hex(Ui.Amber)}]Empty[/color]: {sizes}.\n\n" + (_fitting.Count == 0 ? "Nothing on sale here fits it." : $"{_fitting.Count} parts on sale fit it.")
            : $"[color=#{Ui.Hex(Ui.Amber)}]{part.Name}[/color] by {Builder(part)}: {sizes}.\n{part.Description}\n\n{string.Join("\n", Numbers(part))}\n\n" +
              $"Sells for {Outfitting.SellValue(part):N0} cr. " + (_fitting.Count == 0 ? "Nothing on sale here fits this slot." : $"{_fitting.Count} parts on sale fit it.");
        var reason = Outfitting.CannotRemove(_session, index);
        _sellPart.Disabled = reason != null;
        _partReason.Text = part != null ? reason ?? "" : "";
    }

    private void ShowSelectedPart()
    {
        int index = SelectedSlot;
        if (SelectedPart is not { } offer || index < 0) return;
        var part = offer.Part;
        var before = Outfitting.WithPart(_session, index, Outfitting.Fitted(_session)[index]);
        var after = Outfitting.WithPart(_session, index, part);
        int cost = Outfitting.Cost(_session, index, offer);
        var (low, high) = Outfitting.Range(part);
        var changes = Changes(before, after);
        _partText.Text =
            $"[color=#{Ui.Hex(Ui.Amber)}]{part.Name}[/color] by {Builder(part)}, size {part.Size}" + (part.Mark > 0 ? $", mark {part.Mark}" : ", one of a kind") + $"\n{part.Description}\n\n" +
            string.Join("\n", Numbers(part)) + "\n\n" +
            (changes.Count > 0 ? $"[color=#{Ui.Hex(Ui.Amber)}]Fitted, your ship would have[/color]\n{string.Join("\n", changes)}\n\n" : "") +
            $"Price {offer.Price:N0} cr here (usually {low:N0} to {high:N0})" + (cost != offer.Price ? $", {cost:N0} cr after selling the old part." : ".");
        var reason = Outfitting.CannotFit(_session, index, offer);
        _fitPart.Disabled = reason != null;
        _partReason.Text = reason ?? "";
    }

    private static string Kind(string kind) => kind switch
    {
        "laser" => "Laser", "pulse" => "Pulse laser", "beam" => "Beam", "plasma" => "Plasma", "rail" => "Railgun",
        "scatter" => "Scattergun", "flak" => "Flak", "missile" => "Missile launcher", "torpedo" => "Torpedo tube", _ => kind,
    };

    private string Builder(PartDef part) => _session.Db.Builders.TryGetValue(part.Builder, out var b) ? b.Name : part.Builder;

    /// <summary>A part's own numbers, in words.</summary>
    private static List<string> Numbers(PartDef part)
    {
        var s = part.Stats;
        float Get(string key) => s.GetValueOrDefault(key);
        return part.Type switch
        {
            "weapon" => [$"{Kind(part.Kind)}: {Get("damage"):0} damage, {Get("fireRate"):0.#} shots a second, range {Get("range"):N0} m, {Get("energyPerShot"):0.#} energy a shot"],
            "engine" => [$"Thrust {Get("thrust"):N0}, top speed {Get("speedFactor") * 100:0}% of the hull's"],
            "powerPlant" => [$"{Get("pips"):0} power pips, capacitor {Get("capacitor"):0}, recharging {Get("capacitorRecharge"):0.#} a second"],
            "shield" => [$"Shields {Get("capacity"):0}, recharging {Get("recharge"):0.#} a second after {Get("rechargeDelay"):0.#} s"],
            "sensor" => [$"Range {Get("range"):N0} m"],
            "cargo" => [$"Holds {Get("tonnes"):0} t"],
            _ => Outfitting.Effects(part) is { Count: > 0 } effects ? effects : ["It has no effect on this ship yet."],
        };
    }

    /// <summary>The ship's numbers that a change of part would move, as "now to then".</summary>
    private static List<string> Changes(ShipSpec a, ShipSpec b)
    {
        var lines = new List<string>();
        void Line(string name, float from, float to, string format = "0", string unit = "")
        {
            if (MathF.Abs(from - to) < 0.005f * MathF.Max(1, MathF.Abs(from))) return;
            string colour = Ui.Hex(to > from ? Colors.White : Ui.Red);
            lines.Add($"{name} {from.ToString(format)}{unit} to [color=#{colour}]{to.ToString(format)}{unit}[/color]");
        }
        static float Firepower(ShipSpec s) => s.Weapons.Sum(w => w.Damage * w.FireRate);
        Line("Top speed", a.MaxSpeed, b.MaxSpeed, unit: " m/s");
        Line("Acceleration", a.Acceleration, b.Acceleration, unit: " m/s²");
        Line("Hull", a.MaxHull, b.MaxHull);
        Line("Shields", a.MaxShields, b.MaxShields);
        Line("Shield recharge", a.ShieldRecharge, b.ShieldRecharge, "0.#", "/s");
        Line("Damage a second", Firepower(a), Firepower(b));
        Line("Longest gun range", a.Weapons.Select(w => w.Range).DefaultIfEmpty(0).Max(), b.Weapons.Select(w => w.Range).DefaultIfEmpty(0).Max(), "N0", " m");
        Line("Capacitor", a.CapacitorMax, b.CapacitorMax);
        Line("Weapon recharge", a.CapacitorRecharge, b.CapacitorRecharge, "0.#", "/s");
        Line("Power pips", a.Pips, b.Pips);
        Line("Sensor range", a.SensorRange, b.SensorRange, "N0", " m");
        Line("Hold", a.CargoTonnes, b.CargoTonnes, unit: " t");
        Line("Fuel tank", a.FuelTankLy, b.FuelTankLy, unit: " ly");
        Line("Hull repair", a.HullRepair, b.HullRepair, "0.#", "/s");
        return lines;
    }

    private void FitSelectedPart()
    {
        int index = SelectedSlot;
        if (SelectedPart is not { } offer || Outfitting.CannotFit(_session, index, offer) != null) return;
        int cost = Outfitting.Cost(_session, index, offer);
        Outfitting.Fit(_session, index, offer);
        AudioManager.Instance?.Ui("paid");
        _shipChanged();
        Refresh(cost >= 0 ? $"Fitted a {offer.Part.Name} for {cost:N0} cr." : $"Fitted a {offer.Part.Name} and got {-cost:N0} cr back.");
    }

    private void SellFittedPart()
    {
        int index = SelectedSlot;
        if (index < 0 || Outfitting.Fitted(_session)[index] is not { } part || Outfitting.CannotRemove(_session, index) != null) return;
        Outfitting.Remove(_session, index);
        AudioManager.Instance?.Ui("paid");
        _shipChanged();
        Refresh($"Sold the {part.Name} for {Outfitting.SellValue(part):N0} cr.");
    }
}
