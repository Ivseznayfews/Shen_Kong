using System;
using System.Collections.Generic;
using UnityEngine;

namespace BedCheck
{
    public sealed class ModEntry : IMod
    {
        private ModLog log;
        private GameObject marker;
        private GameItem markedItem;
        private bool rebuildingWindow;
        private bool enabled;
        public void Init(ModManifest manifest) { log = new ModLog(manifest); }
        public void OnEnable()
        {
            if (enabled) return;
            enabled = true;
            ModHook.OnShutterClosedLate += Open;
            ModHook.OnShutterOpenedEarly += Close;
            ModHook.OnLeavingStoreEarly += Close;
        }
        public void OnDisable()
        {
            if (!enabled) return;
            enabled = false;
            ModHook.OnShutterClosedLate -= Open;
            ModHook.OnShutterOpenedEarly -= Close;
            ModHook.OnLeavingStoreEarly -= Close;
            Close();
        }
        private void Close()
        {
            currentSnapshot = null;
            ClearMark();
            if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow("bedcheck.window");
        }
        internal static string L(string key, params object[] args) => ModHelper.GetLocalized("bedcheck", key, args);
        private void ClearMark()
        {
            if (marker != null) UnityEngine.Object.Destroy(marker);
            marker = null;
            markedItem = null;
        }

        private void Locate(GameItem item)
        {
            ClearMark();
            var window = CustomUIManager.Instance == null ? null : CustomUIManager.Instance.GetWindow("bedcheck.window");
            var status = window == null ? null : window.Get("locate_status");
            if (item == null || item.parentInventory == null)
            {
                if (status != null) status.SetText(L("locate_unavailable"));
                return;
            }
            try
            {
                var parentWindow = GraphUtils.FindParentType<PixelWindow>(item.parentInventory, null, null);
                if (parentWindow != null && !parentWindow.IsVisible()) parentWindow.Show();
                var element = item as GameItemElement;
                var anchor = element == null ? null : element.rectTransform;
                if (anchor == null || !anchor.gameObject.activeInHierarchy)
                {
                    if (status != null) status.SetText(L("locate_hidden"));
                    return;
                }
                marker = new GameObject("BedCheckMarker", typeof(RectTransform));
                var frame = (RectTransform)marker.transform;
                frame.SetParent(anchor, false);
                frame.anchorMin = Vector2.zero;
                frame.anchorMax = Vector2.one;
                frame.offsetMin = new Vector2(-3f, -3f);
                frame.offsetMax = new Vector2(3f, 3f);
                AddEdge(frame, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 4));
                AddEdge(frame, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -4), new Vector2(0, 0));
                AddEdge(frame, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(4, 0));
                AddEdge(frame, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-4, 0), new Vector2(0, 0));
                frame.SetAsLastSibling();
                markedItem = item;
                if (status != null) status.SetText(L("locate_shown", item.name));
            }
            catch (Exception ex)
            {
                log.Warning("Locate failed: " + ex.GetType().Name + ": " + ex.Message);
                if (status != null) status.SetText(L("locate_unavailable"));
            }
        }

        private static void AddEdge(RectTransform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var edge = new GameObject("edge", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rect = (RectTransform)edge.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            var image = edge.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(1f, 0.1f, 0.1f, 1f);
            image.raycastTarget = false;
        }

        private static void CheckPurifier(List<string> issues, GameItem item)
        {
            var slot = MachineHelper.GetSlot(item, 2) as GameSlotInventory;
            if (slot == null) throw new InvalidOperationException("Purifier slot unavailable");
            if (slot.childItem == null) issues.Add(L("purifier_empty_slot"));
            else if (!slot.childItem.IsTag("LIQUID_CONTAINER_TAG")) throw new InvalidOperationException("Unknown purifier vessel");
            else if (WaterHelper.GetTotalVolume(slot.childItem) <= 0) issues.Add(L("purifier_empty_liquid"));
        }
        private static void CheckFarm(List<string> issues, GameItem item)
        {
            var slot = MachineHelper.GetSlot(item, 2) as GameSlotInventory;
            if (slot == null) throw new InvalidOperationException("Farm slot unavailable");
            if (slot.childItem == null) issues.Add(L("no_vessel"));
            else if (WaterHelper.GetFreeCapacity(slot.childItem) <= 0) issues.Add(L("full_vessel"));
        }
        private static void CheckFurnace(List<string> issues, GameItem item)
        {
            var input = MachineHelper.GetSlot(item, 2);
            var modules = MachineHelper.GetSlot(item, 1);
            if (input == null || modules == null) throw new InvalidOperationException("Furnace slots unavailable");
            var contents = GraphUtils.FindAllChildrenType<GameItem>(input, null, null);
            var fitted = GraphUtils.FindAllChildrenType<GameItem>(modules, null, null);
            int metal = 0, junk = 0;
            bool blast = false, recycling = false;
            foreach (var component in fitted)
            {
                if (component.identifier == "furnace_module_blast") blast = true;
                if (component.identifier == "furnace_module_junk") recycling = true;
            }
            foreach (var material in contents)
            {
                if (material.identifier == "scrap_metal" || material.identifier == "common_ore") metal++;
                else if (material.identifier == "junk") junk++;
            }
            int required = blast ? 3 : 2;
            if (DecisionRules.FurnaceNeedsInput(metal, blast, junk, recycling))
                issues.Add(recycling ? L("materials_recycle_low", metal, required, junk) : L("materials_metal_low", metal, required));
        }
        private static GameSlotInventory Slot(GameItem item, int index)
        {
            var result = MachineHelper.GetSlot(item, index) as GameSlotInventory;
            if (result == null) throw new InvalidOperationException("Expected machine slot " + index);
            return result;
        }
        private static void CheckPrinter(List<string> issues, GameItem item)
        {
            var input = MachineHelper.GetSlot(item, 2);
            var chip = Slot(item, 3).childItem;
            if (input == null) throw new InvalidOperationException("Printer input unavailable");
            if (chip == null)
            {
                if (input.childItems.Count > 0) issues.Add(L("printer_chip_missing"));
                return;
            }
            if (MachineProgressHelper.GetMachineState(item) == "STATE_READY" &&
                !MachineProgressHelper.HasRequiredAdditionalItems(chip, input.childItems))
                issues.Add(L("printer_material_missing"));
        }
        private static void CheckHydroponic(List<string> issues, GameItem item)
        {
            var vessel = Slot(item, 2).childItem;
            var seed = Slot(item, 3).childItem;
            if (vessel == null && seed == null) return;
            if (vessel == null) issues.Add(L("hydro_water_missing"));
            else if (seed != null && WaterHelper.GetCurrentCapacityML(vessel) < HydroponicHelper.GetWaterUsePerCycle(seed)) issues.Add(L("hydro_water_insufficient"));
            if (seed == null) issues.Add(L("hydro_seed_missing"));
        }
        private static void CheckDesequencer(List<string> issues, GameItem item)
        {
            var chip = Slot(item, 2).childItem;
            var input = Slot(item, 3).childItem;
            if (chip == null && input != null) issues.Add(L("desequencer_chip_missing"));
            if (chip != null && input == null) issues.Add(L("desequencer_input_missing"));
        }
        private static void CheckEvaporator(List<string> issues, GameItem item)
        {
            var vessel = Slot(item, 2).childItem;
            if (vessel != null && vessel.IsTag("LIQUID_CONTAINER_TAG") && WaterHelper.GetTotalVolume(vessel) <= 0)
                issues.Add(L("evaporator_empty"));
        }
        private static void CheckAquaculture(List<string> issues, GameItem item)
        {
            var water = MachineRAS.GetWaterSlot(item);
            var tank = MachineRAS.GetTankGrid(item);
            if (water == null || tank == null) throw new InvalidOperationException("Aquaculture layout unavailable");
            if (MachineRAS.HasLiveStock(item))
            {
                if (!MachineRAS.HasWater(item)) issues.Add(L("ras_water_missing"));
                var feeder = MachineRAS.GetDispenserSlot(item);
                if (feeder != null && feeder.childItem != null && MachineFeedDispenser.GetAvailableKcal(feeder.childItem, true, true, true) <= 0)
                    issues.Add(L("ras_feeder_empty"));
            }
        }
        private static void CheckDnaManipulator(List<string> issues, GameItem item)
        {
            var inputDisk = Slot(item, 2).childItem;
            var seed = Slot(item, 3).childItem;
            if (inputDisk != null && seed == null)
                issues.Add(L("dna_seed_missing"));
        }
        private static readonly HashSet<string> VanillaIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "furnace", "hydroponic", "plant_dna_manipulator", "printer", "deal_maker", "recharger",
            "powerblock", "makeshift_reactor", "security_alarm", "storage_coordinator", "mirage_projector",
            "moisture_farm", "water_purifier", "desequencer", "bottle_printer", "box_dispenser",
            "heating_plate", "blender", "evaporator", "chem_finisher", "cassette_player",
            "vending_machine", "vending_fountain", "wine_rack", "fridge", "ras", "livestock_feed_dispenser", "auto_butcher"
        };
        private static bool IsVanillaMachine(GameItem item)
        {
            if (item == null || !VanillaIds.Contains(item.identifier ?? "")) return false;
            if (item.IsTag(ModHelper.MOD_ID_TAG) && !string.IsNullOrEmpty(item.GetTagReadonly(ModHelper.MOD_ID_TAG).GetString())) return false;
            return true;
        }
        private static void CheckBlender(List<string> issues, GameItem item)
        {
            var input = MachineHelper.GetSlot(item, 1);
            var container = Slot(item, 2).childItem;
            if (input == null) throw new InvalidOperationException("Blender input unavailable");
            if (input.childItems.Count == 0) return;
            if (container == null) issues.Add(L("blender_container_missing"));
            else if (container.IsTag("LIQUID_CONTAINER_TAG") && WaterHelper.GetFreeCapacity(container) <= 0)
                issues.Add(L("blender_container_full"));
        }
        private static void CheckFountain(List<string> issues, GameItem item)
        {
            int installed = 0, nonempty = 0;
            for (int i = 1; i <= 3; i++)
            {
                var source = Slot(item, i).childItem;
                if (source == null) continue;
                installed++;
                GameItem vessel = source;
                if (source.identifier == "water_purifier" || source.identifier == "moisture_farm")
                {
                    if (source.contentWindow == null) throw new InvalidOperationException("Water source machine unavailable");
                    vessel = Slot(source, 2).childItem;
                }
                if (vessel == null) continue;
                if (!vessel.IsTag("LIQUID_CONTAINER_TAG")) throw new InvalidOperationException("Unknown fountain vessel");
                if (WaterHelper.GetTotalVolume(vessel) > 0) nonempty++;
            }
            if (installed > 0 && nonempty == 0) issues.Add(L("fountain_no_liquid"));
        }
        private static void CheckChemFinisher(List<string> issues, GameItem item)
        {
            var vessel = Slot(item, 1).childItem;
            if (vessel == null) return;
            if (!vessel.IsTag("LIQUID_CONTAINER_TAG")) throw new InvalidOperationException("Unknown chemical vessel");
            if (WaterHelper.GetTotalVolume(vessel) <= 0) issues.Add(L("chem_empty_vessel"));
            else if (item.IsTag("SELECTED_CHEM_ID_TAG"))
            {
                var recipe = ChemFinisherRecipe.GetRecipeById(item.GetTagReadonly("SELECTED_CHEM_ID_TAG").GetString());
                if (recipe == null) throw new InvalidOperationException("Unknown chemical recipe");
                if (!recipe.CanOutput(vessel)) issues.Add(L("chem_recipe_not_ready"));
            }
        }
        private static void CheckReactor(List<string> issues, GameItem item)
        {
            int integrity = ReactorHelper.GetIntegrity(item);
            int decay = ReactorHelper.GetDecay(item);
            switch (DecisionRules.Reactor(integrity, decay))
            {
                case ReactorRisk.Critical: issues.Add(L("reactor_critical", integrity, decay)); break;
                case ReactorRisk.NoPower: issues.Add(L("reactor_no_power", integrity)); break;
                case ReactorRisk.PowerRisk: issues.Add(L("reactor_power_risk", integrity, decay)); break;
            }
        }
        private static void CheckAutoButcher(List<string> issues, GameItem item)
        {
            var parent = item.parentInventory == null ? null : item.parentInventory.GetParentItem();
            if (parent == null) return;
            GameSlotInventory hostSlot;
            if (parent.IsTag("CAGE_TAG")) hostSlot = AnimalCage.GetButcherSlot(parent);
            else if (parent.IsTag("RAS_TAG")) hostSlot = MachineRAS.GetButcherSlot(parent);
            else return;
            if (hostSlot == null || hostSlot.childItem != item) return;
            var output = MachineHelper.GetBodyStorage(item, 0) as GameGridInventory;
            if (output == null) throw new InvalidOperationException("Butcher output unavailable");
            if (!output.HasFreeCell()) issues.Add(L("butcher_output_full"));
        }
        private static void CheckHeatingPlate(List<string> issues, GameItem item)
        {
            if (item.GetTagReadonly("HEATING_PLATE_STATE").GetInt() <= 0) return;
            var vessel = Slot(item, 1).childItem;
            if (vessel == null) issues.Add(L("heater_no_vessel"));
            else if (vessel.IsTag("LIQUID_CONTAINER_TAG") && WaterHelper.GetTotalVolume(vessel) <= 0)
                issues.Add(L("heater_empty_vessel"));
        }
        private static void CheckBottlePrinter(List<string> issues, GameItem item)
        {
            var output = MachineHelper.GetSlot(item, 1) as GameGridInventory;
            if (output == null) throw new InvalidOperationException("Bottle printer output unavailable");
            if (!output.HasFreeCell()) issues.Add(L("bottle_output_full"));
        }
        private static void CheckBoxDispenser(List<string> issues, GameItem item)
        {
            var output = MachineHelper.GetSlot(item, 1) as GameGridInventory;
            if (output == null) throw new InvalidOperationException("Box dispenser output unavailable");
            if (!output.HasFreeCell()) issues.Add(L("box_output_full"));
        }
        private static void CheckFeedDispenser(List<string> issues, GameItem item)
        {
            var parent = item.parentInventory == null ? null : item.parentInventory.GetParentItem();
            if (parent == null || !parent.IsTag("CAGE_TAG")) return;
            var slot = AnimalCage.GetDispenserSlot(parent);
            var cage = AnimalCage.GetCageInventory(parent);
            if (slot == null || cage == null) throw new InvalidOperationException("Cage layout unavailable");
            if (slot.childItem != item) return;
            bool live = false;
            foreach (var animal in cage.childItems)
                if (animal.IsTag("ANIMAL_ITEM_TAG") && !animal.IsTag("ANIMAL_IS_DEAD_TAG")) { live = true; break; }
            if (live && MachineFeedDispenser.GetAvailableKcal(item, true, true, true) <= 0)
                issues.Add(L("cage_feeder_empty"));
        }
        private static void CheckPowerBlock(List<string> issues, GameItem item)
        {
            if (PlayerStore.Instance.isPowerOn) return;
            foreach (var adjacent in item.FindAdjacentType<GameItem>())
            {
                if (adjacent.IsTag("power_source_item") || adjacent.IsTag("MACHINE_RECHARGABLE_TAG"))
                {
                    issues.Add(L("powerblock_off"));
                    return;
                }
            }
        }
        private static bool HasAgeableWine(GameItem item)
        {
            var input = MachineHelper.GetSlot(item, 2);
            if (input == null) throw new InvalidOperationException("Wine rack input unavailable");
            var contents = GraphUtils.FindAllChildrenType<GameItem>(input, null, null);
            foreach (var wine in contents)
            {
                if (wine.IsTag("WINE_CONTAINER_TAG") && DecisionRules.AgeableWine(true,
                    wine.IsTag("WINE_COUNTERFEIT_TAG"), wine.GetTagReadonly("WINE_CONTAINER_STATE").GetInt())) return true;
            }
            return false;
        }
        private static bool HasStoredGoods(GameItem item)
        {
            if (item.identifier == "wine_rack") return HasAgeableWine(item);
            if (item.identifier == "fridge")
            {
                var chill = MachineFridge.GetChillZone(item);
                var freeze = MachineFridge.GetFreezeZone(item);
                if (chill == null || freeze == null) throw new InvalidOperationException("Fridge zones unavailable");
                return chill.childItems.Count > 0 || freeze.childItems.Count > 0;
            }
            if (item.identifier == "vending_fountain")
            {
                for (int i = 1; i <= 3; i++) if (Slot(item, i).childItem != null) return true;
                return false;
            }
            if (item.identifier == "vending_machine")
            {
                var stock = VendingMachineHelper.GetVendingInventory(item);
                if (stock == null) throw new InvalidOperationException("Vending stock unavailable");
                return stock.childItems.Count > 0;
            }
            return true;
        }
        private static void CheckRecharger(List<string> issues, GameItem item)
        {
            var inventory = MachineHelper.GetSlot(item, 0);
            if (inventory == null) throw new InvalidOperationException("Recharger slot unavailable");
            if (PlayerStore.Instance.isPowerOn) return;
            foreach (var battery in inventory.childItems)
                if (battery.IsTag("power_source_item")) { issues.Add(L("recharger_power_off")); return; }
        }
        private sealed class Row
        {
            internal GameItem Item;
            internal string Name;
            internal List<string> Issues;
        }
        private sealed class Snapshot
        {
            internal int Checked, Unsupported;
            internal bool ScanIncomplete;
            internal readonly List<Row> Rows = new List<Row>();
            internal bool SameAs(Snapshot other)
            {
                if (other == null || ScanIncomplete != other.ScanIncomplete || Checked != other.Checked || Unsupported != other.Unsupported || Rows.Count != other.Rows.Count) return false;
                for (int i = 0; i < Rows.Count; i++)
                {
                    var a = Rows[i]; var b = other.Rows[i];
                    if (a.Item != b.Item || a.Name != b.Name || a.Issues.Count != b.Issues.Count) return false;
                    for (int j = 0; j < a.Issues.Count; j++) if (a.Issues[j] != b.Issues[j]) return false;
                }
                return true;
            }
        }
        private Snapshot currentSnapshot;
        private void Open()
        {
            if (!PlayerStore.HasInstance || CustomUIManager.Instance == null || EmporiumEntry.Instance == null) return;
            try { Render(Scan(true), null, 1f); }
            catch (Exception ex) { log.Warning("Opening checklist failed: " + ex.GetType().Name + ": " + ex.Message); }
        }
        private Snapshot Scan(bool logErrors)
        {
            int checkedCount = 0, unsupported = 0, uncertain = 0;
            var result = new Snapshot();
            var seen = new HashSet<GameItem>();
            foreach (var item in PlayerStore.Instance.FindAllItem(false))
            {
                try
                {
                    if (item == null || !seen.Add(item) || !GeneralHelper.IsItemOwned(item)) continue;
                    if (!IsVanillaMachine(item)) continue;
                    bool supported = item.identifier == "powerblock" || item.identifier == "box_dispenser" || item.identifier == "heating_plate" || item.identifier == "bottle_printer" || item.identifier == "livestock_feed_dispenser" || item.identifier == "makeshift_reactor" || item.identifier == "auto_butcher" || item.identifier == "chem_finisher" || item.identifier == "vending_fountain" || item.identifier == "blender" || item.identifier == "fridge" || item.identifier == "vending_machine" || item.identifier == "recharger" || item.identifier == "plant_dna_manipulator" || item.identifier == "wine_rack" || item.IsTag("RAS_TAG") || item.identifier == "security_alarm" || item.identifier == "mirage_projector" || item.identifier == "furnace" || item.identifier == "moisture_farm" || item.identifier == "water_purifier" || item.identifier == "printer" || item.identifier == "desequencer" || item.identifier == "hydroponic" || item.identifier == "evaporator";
                    var issues = new List<string>();
                    int uncertainBefore = uncertain;
                    if (item.IsTag("BROKEN_MACHINE_TAG")) issues.Add(L("broken"));
                    if (!supported)
                    {
                        unsupported++;
                        issues.Add(L("unsupported_machine"));
                    }
                    else
                    {
                        checkedCount++;
                        try
                        {
                            if (item.identifier != "makeshift_reactor" && item.identifier != "powerblock" && item.contentWindow == null)
                                throw new InvalidOperationException("Machine window unavailable");
                            if (item.identifier == "powerblock") CheckPowerBlock(issues, item);
                            else if (item.identifier == "box_dispenser") CheckBoxDispenser(issues, item);
                            else if (item.identifier == "heating_plate") CheckHeatingPlate(issues, item);
                            else if (item.identifier == "bottle_printer") CheckBottlePrinter(issues, item);
                            else if (item.identifier == "livestock_feed_dispenser") CheckFeedDispenser(issues, item);
                            else if (item.identifier == "makeshift_reactor") CheckReactor(issues, item);
                            else if (item.identifier == "auto_butcher") CheckAutoButcher(issues, item);
                            else if (item.identifier == "recharger") CheckRecharger(issues, item);
                            else
                            {
                                try
                                {
                                if (item.identifier != "blender" && item.identifier != "chem_finisher" && HasStoredGoods(item))
                                {
                                    var batterySlot = MachineHelper.GetBatterySlot(item);
                                    if (batterySlot == null) uncertain++;
                                    else if (batterySlot.childItem == null) issues.Add(L("no_battery"));
                                    else
                                    {
                                        int needed = MachineryHelper.GetMachinePowerUsage(item);
                                        var source = batterySlot.childItem;
                                        if (CurrentConverterHelper.IsCurrentConverter(source))
                                        {
                                            if (!CurrentConverterHelper.IsOn(source)) issues.Add(L("converter_off"));
                                        }
                                        else if (HypertechBatteryHelper.IsHypertechBattery(source))
                                        {
                                            int output = HypertechBatteryHelper.GetOutput(source);
                                            if (DecisionRules.InsufficientEnergy(output, needed)) issues.Add(L("hypertech_low", output, needed));
                                        }
                                        else if (source.IsTag("power_source_item"))
                                        {
                                            int energy = source.GetTagReadonly("power_source_item_energy").GetInt();
                                            if (DecisionRules.InsufficientEnergy(energy, needed)) issues.Add(L("battery_low_snapshot", energy, needed));
                                        }
                                        else issues.Add(L("power_uncertain"));
                                    }
                                }
                                }
                                catch (Exception ex)
                                {
                                    uncertain++;
                                    if (logErrors) log.Warning("Power check failed for " + item.identifier + ": " + ex.GetType().Name + ": " + ex.Message);
                                }
                            }
                            if (item.identifier == "chem_finisher") CheckChemFinisher(issues, item);
                            else if (item.identifier == "blender") CheckBlender(issues, item);
                            else if (item.identifier == "vending_fountain") CheckFountain(issues, item);
                            else if (item.identifier == "plant_dna_manipulator") CheckDnaManipulator(issues, item);
                            else if (item.IsTag("RAS_TAG")) CheckAquaculture(issues, item);
                            else if (item.identifier == "moisture_farm") CheckFarm(issues, item);
                            else if (item.identifier == "water_purifier") CheckPurifier(issues, item);
                            else if (item.identifier == "furnace") CheckFurnace(issues, item);
                            else if (item.identifier == "printer") CheckPrinter(issues, item);
                            else if (item.identifier == "desequencer") CheckDesequencer(issues, item);
                            else if (item.identifier == "hydroponic") CheckHydroponic(issues, item);
                            else if (item.identifier == "evaporator") CheckEvaporator(issues, item);
                        }
                        catch (Exception ex)
                        {
                            uncertain++;
                            if (logErrors) log.Warning("Machine check failed for " + item.identifier + ": " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                    if (uncertain > uncertainBefore) issues.Add(L("check_uncertain"));
                    if (issues.Count == 0) continue;
                    result.Rows.Add(new Row { Item = item, Name = item.name, Issues = issues });
                }
                catch (Exception ex)
                {
                    result.ScanIncomplete = true;
                    if (logErrors) log.Warning("Item scan failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            result.Checked = checkedCount;
            result.Unsupported = unsupported;
            return result;
        }
        private void Poll()
        {
            var manager = CustomUIManager.Instance;
            var window = manager == null ? null : manager.GetWindow("bedcheck.window");
            if (window == null || !window.IsAlive || !window.IsVisible || !PlayerStore.HasInstance || EmporiumEntry.Instance == null) return;
            try
            {
                var next = Scan(false);
                if (next.SameAs(currentSnapshot)) return;
                var scroll = window.Rect.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
                float position = scroll == null ? 1f : scroll.verticalNormalizedPosition;
                Render(next, window.Rect.anchoredPosition, position);
            }
            catch (Exception ex) { log.Warning("Automatic check failed: " + ex.GetType().Name + ": " + ex.Message); }
        }
        private void Render(Snapshot snapshot, Vector2? previousPosition, float scrollPosition)
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            if (markedItem != null && !snapshot.Rows.Exists(row => row.Item == markedItem)) ClearMark();
            rebuildingWindow = true;
            try { manager.CloseWindow("bedcheck.window"); }
            finally { rebuildingWindow = false; }
            var ui = CustomUIManager.Window("bedcheck.window", L("title"), CustomUIManager.LAYER_PAPER);
            ui.SetSize(600, 520).Center().SetDraggable(true).OnClose(() =>
            {
                if (!rebuildingWindow) { currentSnapshot = null; ClearMark(); }
            });
            if (previousPosition.HasValue) ui.SetPosition(previousPosition.Value);
            ui.AddLabel(L("issues_intro"));
            ui.AddLabel(markedItem == null ? L("locate_help") : L("locate_shown", markedItem.name), "locate_status");
            ui.AddLabel(L("auto_refresh_help"));
            ui.BeginScroll(310);
            int number = 0;
            foreach (var row in snapshot.Rows)
            {
                ui.AddHeader(L("machine_header", ++number, row.Name));
                var selected = row.Item;
                ui.AddButton(L("locate"), () => Locate(selected));
                foreach (var issue in row.Issues) ui.AddWarning(issue);
                ui.AddSeparator();
            }
            if (snapshot.ScanIncomplete) ui.AddWarning(L("scan_incomplete"));
            if (number == 0 && !snapshot.ScanIncomplete) ui.AddLabel(L(snapshot.Checked == 0 ? "no_checked_machines" : "no_issues"));
            ui.AddLabel(L("coverage", snapshot.Checked, snapshot.Unsupported));
            var window = ui.End().Show();
            currentSnapshot = snapshot;
            var poller = window.Rect.gameObject.AddComponent<RefreshPoller>();
            poller.Callback = Poll;
            poller.ScrollPosition = scrollPosition;
        }
    }
    public sealed class RefreshPoller : MonoBehaviour
    {
        internal Action Callback;
        internal float ScrollPosition;
        private float nextCheck;
        private bool restoreScroll = true;
        private void Start() { nextCheck = Time.unscaledTime + 2f; }
        private void Update()
        {
            if (restoreScroll)
            {
                restoreScroll = false;
                var scroll = GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
                if (scroll != null) scroll.verticalNormalizedPosition = Mathf.Clamp01(ScrollPosition);
            }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 2f;
            if (Callback != null) Callback();
        }
    }
}
