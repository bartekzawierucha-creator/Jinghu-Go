using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using JianghuGo.Client.Application;

namespace JianghuGo.Android
{
    /// <summary>World hub: the full-screen map, a context card for the chosen place, and sheets for the rest.</summary>
    public sealed partial class MainActivity
    {
        private async Task ShowWorldTabAsync()
        {
            var c = _me;
            var exploration = _exploration;
            if (exploration == null)
            {
                exploration = new ExplorationFlow(_api, _location, c.Id);
                await exploration.StartAsync(Ct);
            }

            var ui = NewScreen(null, tab: TabWorld);
            _exploration = exploration;
            var page = ui.Content;
            var gen = _screen;
            _worldOpen = true;
            if (!_paused)
            {
                _location.Start();
                _compass.Start();
            }

            // map with a status banner over it
            var mapFrame = new FrameLayout(this);
            ui.AddWeighted(page, mapFrame, 1f, 0);
            var map = new WorldMapView(this);
            mapFrame.AddView(map, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            var banner = new LinearLayout(this) { Orientation = Orientation.Vertical };
            banner.SetPadding(Px(10), Px(6), Px(10), Px(6));
            var bannerBg = new global::Android.Graphics.Drawables.GradientDrawable();
            bannerBg.SetColor(Color.Argb(200, 244, 240, 231));
            bannerBg.SetCornerRadius(Px(10));
            banner.Background = bannerBg;
            mapFrame.AddView(banner, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top) { LeftMargin = Px(8), RightMargin = Px(56), TopMargin = Px(8) });
            var status = ui.Text("…", banner, 12, Ui.Ink, maxLines: 1);
            var regionLine = ui.Text(string.Empty, banner, 11, Ui.Jade, maxLines: 1);
            regionLine.Visibility = ViewStates.Gone;
            var eventLine = ui.Text(string.Empty, banner, 12, Ui.Gold, bold: true, maxLines: 1);

            // context card: the chosen (or nearest) place
            var cardRow = ui.HRow(page, 58);
            var card = ui.Card(cardRow, 1f, 6, null, 0);
            var cardInner = ui.HRow(card, 44, 0);
            var nodeIcon = ui.Icon(cardInner, "map_qi", 40);
            var nodeText = ui.Box(cardInner, 1f, marginDp: 8);
            var nodeName = ui.Text("Szukam miejsc w okolicy…", nodeText, 14, Ui.Ink, bold: true);
            var nodeSub = ui.Text(string.Empty, nodeText, 11, Ui.Muted);
            var act = ui.Btn("Zbierz", () => { }, cardInner, sp: 13, heightDp: 40);
            act.LayoutParameters = new LinearLayout.LayoutParams(Px(104), Px(40)) { Gravity = GravityFlags.CenterVertical };

            var tools = ui.HRow(page, 44);
            var placesBtn = ui.Btn("Okolica", () => { }, tools, secondary: true, sp: 11, icon: "map_other");
            var peopleBtn = ui.Btn("Ludzie", () => { }, tools, secondary: true, sp: 11, icon: "map_cultivator");
            var flameBtn = ui.Btn("Płomień", () => { }, tools, secondary: true, sp: 11, icon: "map_flame");

            NodeDto selected = null;
            var people = new List<NearbyCultivatorDto>();
            var lastPeople = DateTime.MinValue;

            void Interact(NodeDto node) => Run(async () =>
            {
                if (!node.InRange || node.Harvested)
                {
                    SetStatus(node.Harvested ? "Już to zebrałeś." : node.NamePl + ": " + node.DistanceM + " m — podejdź bliżej.");
                    return;
                }

                var r = await exploration.InteractAsync(node.Key, Ct);
                if (r.BattleId.HasValue)
                {
                    var combat = new CombatFlow(_api);
                    await combat.OpenAsync(r.BattleId.Value, Ct);
                    ShowBattle(combat);
                    return;
                }

                Render();
                Sfx.Play(Sfx.Coin);
                ShowLoot("Zebrano: " + node.NamePl, r.RewardQi, r.Items.Select(i => (i.ItemId, (long)i.Quantity)).ToList(), null);
                RefreshHud();
            });

            void Select(NodeDto node)
            {
                selected = node;
                RenderCard();
            }

            void RenderCard()
            {
                var n = exploration.Nearby;
                var node = selected != null && n != null ? n.Nodes.FirstOrDefault(x => x.Key == selected.Key) : null;
                node ??= n?.Nodes.Where(x => !x.Harvested).OrderBy(x => x.DistanceM).FirstOrDefault();
                selected = node;
                if (node == null)
                {
                    nodeName.Text = n == null ? "Szukam miejsc w okolicy…" : "Nic w zasięgu — przejdź się";
                    nodeSub.Text = string.Empty;
                    act.Enabled = false;
                    act.Alpha = 0.4f;
                    return;
                }

                var beast = WorldMapView.IsBeastLair(node.Kind);
                var boss = WorldMapView.IsBossLair(node.Kind);
                var iconName = boss ? "map_boss" : beast ? "map_beast" : node.Kind switch
                {
                    "qi_spring" => "map_qi", "spirit_herb" => "map_herb", "lotus_pond" => "map_lotus", "bamboo_grove" => "map_bamboo",
                    "shrine" or "sacred_shrine" => "map_shrine", "sacred_waterfall" => "map_lotus", "sacred_peak" => "map_ore", "ore_vein" or "jade_outcrop" => "map_ore", "ancient_ruins" => "map_ruins", _ => "map_event",
                };
                nodeIcon.SetImageBitmap(Ui.Art(this, ui.ArtId(iconName) != 0 ? ui.ArtId(iconName) : Resource.Drawable.map_event));
                nodeName.Text = NodeGlyph(node.Kind) + " " + node.NamePl + (node.Harvested ? " ✓" : string.Empty);
                nodeSub.Text = node.DistanceM + " m" + (node.RewardQi > 0 ? "  ·  +" + node.RewardQi + " Qi" : string.Empty) + (boss ? "  ·  BOSS" : string.Empty);
                var ready = node.InRange && !node.Harvested;
                act.Text = node.Harvested ? "Zebrane" : !node.InRange ? "Podejdź" : beast ? "Walcz" : "Zbierz";
                act.Enabled = ready;
                act.Alpha = ready ? 1f : 0.45f;
            }

            void Render()
            {
                if (gen != _screen) return;
                var n = exploration.Nearby;
                status.Text = (exploration.Problem == null ? "📍 Pozycja potwierdzona" : "📍 " + DescribeProblem(exploration.Problem)) +
                              (exploration.Session != null ? "  ·  " + Math.Round(exploration.Session.DistanceM) + " m" : string.Empty);
                if (n?.Region is { } region)
                {
                    regionLine.Visibility = ViewStates.Visible;
                    regionLine.Text = (region.Night ? "🌙 " : "☀ ") + region.NamePl + " · " + region.TerrainNamePl + (region.WeatherNamePl != null ? " · " + region.WeatherNamePl + (region.TemperatureC.HasValue ? " " + Math.Round(region.TemperatureC.Value) + "°C" : string.Empty) : string.Empty)
                                      + (region.BossNamePl != null ? " · władca: " + region.BossNamePl : string.Empty);
                }

                eventLine.SetCompoundDrawables(null, null, null, null);
                if (n?.Event != null)
                {
                    eventLine.Visibility = ViewStates.Visible;
                    eventLine.Text = n.Event.NamePl + "  ×" + (n.Event.RewardMultiplierPermille / 1000.0).ToString("0.#");
                    ui.WithIcon(eventLine, "event_" + n.Event.Kind, 22);
                }
                else if (n?.NearestFlameSite != null)
                {
                    eventLine.Visibility = ViewStates.Visible;
                    eventLine.Text = n.NearestFlameSite.NamePl + " — " + (n.NearestFlameSite.DistanceM < 1000 ? n.NearestFlameSite.DistanceM + " m" : n.NearestFlameSite.DistanceM / 1000 + " km");
                    ui.WithIcon(eventLine, "map_flame", 22);
                }
                else
                {
                    eventLine.Visibility = ViewStates.Gone;
                }

                map.Player = _location.Last;
                map.Heading = _compass.Heading;
                map.Nearby = n;
                map.CultivatorsNearby = people.Count;
                map.Invalidate();
                placesBtn.Text = "Okolica" + (n != null ? " " + n.Nodes.Count(x => !x.Harvested) : string.Empty);
                peopleBtn.Text = "Ludzie" + (people.Count > 0 ? " " + people.Count : string.Empty);
                RenderCard();
            }

            act.Click += (_, __) =>
            {
                if (selected != null) Interact(selected);
            };
            map.NodeTapped += node =>
            {
                Select(node);
                if (node.InRange && !node.Harvested) Interact(node);
            };
            _onLocation = () =>
            {
                map.Player = _location.Last;
                map.Heading = _compass.Heading;
                map.Invalidate();
            };

            placesBtn.Click += (_, __) =>
            {
                var nodes = exploration.Nearby?.Nodes.OrderBy(x => x.Harvested).ThenBy(x => x.DistanceM).ToList() ?? new List<NodeDto>();
                var body = Sheet("Miejsca w okolicy", 0.6f, out var close, "map_other");
                ui.Pages(body, nodes.Count, 1, 6, (i, cell) =>
                {
                    var node = nodes[i];
                    var row = ui.HRow(cell, 44, 0);
                    ui.Text(NodeGlyph(node.Kind) + "  " + node.NamePl + " — " + node.DistanceM + " m" + (node.Harvested ? "  ✓" : string.Empty), row, 13, node.InRange && !node.Harvested ? Ui.Gold : Ui.Ink);
                    var ready = node.InRange && !node.Harvested;
                    ui.Btn(ready ? (WorldMapView.IsBeastLair(node.Kind) ? "Walcz" : "Zbierz") : "Pokaż", () =>
                    {
                        close();
                        Select(node);
                        if (ready) Interact(node);
                    }, row, secondary: !ready, sp: 12, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(92), Px(36));
                }, empty: "Nic w pobliżu — przejdź kawałek.");
                ui.Text("Prawdziwe miejsca: © współtwórcy OpenStreetMap · pogoda: Open-Meteo", body, 10, Ui.Muted, center: true);
            };

            peopleBtn.Click += (_, __) =>
            {
                var body = Sheet("Kultywatorzy w okolicy", 0.55f, out var close, "map_cultivator");
                ui.Text("Widać tylko imię i przybliżoną odległość — nigdy dokładne położenie.", body, 11, Ui.Muted, maxLines: 2);
                ui.Pages(body, people.Count, 1, 5, (i, cell) =>
                {
                    var o = people[i];
                    var row = ui.HRow(cell, 44, 0);
                    ui.Icon(row, "portrait_" + o.Path, 36);
                    ui.Text(o.Name + "  ·  " + PathName(o.Path) + "  ·  " + o.DistanceBand, row, 13, Ui.Ink, bold: true).LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = Px(8), Gravity = GravityFlags.CenterVertical };
                    ui.Btn("Wyzwij", () => Run(async () =>
                    {
                        close();
                        var battle = await _api.ChallengeAsync(c.Id, o.CharacterId, Ct);
                        var combat = new CombatFlow(_api);
                        await combat.OpenAsync(battle.Id, Ct);
                        ShowBattle(combat);
                    }), row, sp: 12, heightDp: 36).LayoutParameters = new LinearLayout.LayoutParams(Px(92), Px(36));
                }, empty: "Nikogo w promieniu 2 km.");
            };

            flameBtn.Click += (_, __) => Run(async () =>
            {
                var sites = await _api.GetFlameSitesAsync(Ct);
                var nearest = exploration.Nearby?.NearestFlameSite;
                var body = Sheet("Miejsca Płomienia", 0.7f, out var close, "map_flame");
                ui.Text("Dostrój się, stojąc w promieniu " + Math.Round(sites.RadiusM) + " m od miejsca. Płomień odnawia się co " + sites.CooldownHours + " h.", body, 12, Ui.Muted, maxLines: 2);
                var attune = ui.Btn(nearest != null && nearest.InRange ? "Dostrój się: " + nearest.NamePl : "Najbliższe: " + (nearest?.NamePl ?? "—"), () => Run(async () =>
                {
                    if (nearest == null || !nearest.InRange)
                    {
                        SetStatus("Musisz stać przy jednym z 12 miejsc Płomienia.");
                        return;
                    }

                    var r = await exploration.AttuneAsync(nearest.Id, Ct);
                    close();
                    Sfx.Play(Sfx.Breakthrough, 0.6f);
                    ShowBanner("PŁOMIEŃ", nearest.NamePl, "+" + r.RewardQi + " Qi", "site_" + nearest.Id);
                    RefreshHud();
                }), body, secondary: nearest == null || !nearest.InRange, sp: 14, heightDp: 46, icon: "map_flame");
                var here = _location.Last;
                var list = sites.Items.OrderBy(x => here == null ? 0 : DistanceKm(here.Lat, here.Lon, x.Lat, x.Lon)).ToList();
                ui.Pages(body, list.Count, 2, 3, (i, cell) =>
                {
                    var s = list[i];
                    var cardS = ui.Card(cell, 1f, 6, null, 0);
                    var row = ui.HRow(cardS, 46, 0);
                    ui.Icon(row, "site_" + s.Id, 42);
                    var col = ui.Box(row, 1f, marginDp: 6);
                    ui.Text(s.NamePl, col, 12, Ui.Gold, bold: true, maxLines: 2);
                    if (here != null) ui.Text(Math.Round(DistanceKm(here.Lat, here.Lon, s.Lat, s.Lon)).ToString("N0") + " km", col, 11, Ui.Muted);
                });
            });

            async Task RefreshPeopleAsync()
            {
                if (DateTime.UtcNow - lastPeople < TimeSpan.FromSeconds(30)) return;
                lastPeople = DateTime.UtcNow;
                try
                {
                    people = (await _api.GetNearbyCultivatorsAsync(c.Id, Ct)).Items;
                }
                catch (ApiException)
                {
                    // no verified position yet
                }
            }

            async void Spin()
            {
                // the compass changes faster than GPS: keep the avatar facing the right way
                while (gen == _screen && !_lifetime.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(120, Ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }

                    if (gen == _screen && _compass.Heading != map.Heading) map.Heading = _compass.Heading;
                }
            }

            async void Loop()
            {
                while (gen == _screen && !_lifetime.IsCancellationRequested)
                {
                    if (!_paused)
                    {
                        try
                        {
                            await exploration.TickAsync(Ct);
                            await RefreshPeopleAsync();
                            Render();
                        }
                        catch (ApiException e)
                        {
                            if (gen == _screen) SetStatus(Describe(e));
                        }
                        catch (System.OperationCanceledException)
                        {
                            return;
                        }
                    }

                    try
                    {
                        await Task.Delay(_paused ? TimeSpan.FromSeconds(2) : exploration.RecommendedInterval, Ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            Render();
            Spin();
            Loop();
        }
    }
}
