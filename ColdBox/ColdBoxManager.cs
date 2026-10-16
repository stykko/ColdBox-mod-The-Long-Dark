using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Owns every cooler in the scene: registry, interaction (hold to open, action menu), placement mode and spawning.
    /// Future crafting only needs to call <see cref="RequestNewPlacement"/> when the recipe is completed.
    /// </summary>
    internal static class ColdBoxManager
    {
        internal static readonly List<ColdBoxBehaviour> Boxes = new List<ColdBoxBehaviour>();

        /// <summary>Hook for a future crafting recipe / refund. Raised after a cooler was removed from the world.</summary>
        internal static event Action<ColdBoxBehaviour> OnBoxRemoved;

        static readonly Queue<Action> nextFrame = new Queue<Action>();

        // hold-to-open
        static bool holding;
        static ColdBoxBehaviour holdTarget;
        static float holdStartTime;

        // placement mode
        static ColdBoxBehaviour placingBox;
        static bool placingIsNew;
        static Action<bool> placingOnDone;   // told whether a new cooler was really placed (crafted item consumed or kept)
        static Vector3 placingOrigPos;
        static Quaternion placingOrigRot;
        static float placingYaw;

        // fallback menu (keys 1-5)
        static ColdBoxBehaviour fallbackBox;
        static List<KeyValuePair<string, Action>> fallbackEntries;

        // ---------------- creation ----------------
        /// <summary>Builds a cooler, attaches the game Container and registers it. Returns null if the resources failed to load.</summary>
        internal static ColdBoxBehaviour CreateBox(Vector3 pos, Quaternion rot, string id, string sceneKey)
        {
            var go = ColdBoxBuilder.Build(pos, rot);   // inactive root
            if (go == null) return null;

            var beh = go.AddComponent<ColdBoxBehaviour>();
            ColdBoxContainer.Attach(go);
            beh.SetId(string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id);
            beh.SetSceneKey(sceneKey);
            go.SetActive(true);   // Awake of every component runs now
            Boxes.Add(beh);
            return beh;
        }

        internal static ColdBoxBehaviour FindById(string id)
        {
            foreach (var b in Boxes) if (b != null && b.GetId() == id) return b;
            return null;
        }

        /// <summary>Starts placement mode (used when placing the crafted item from the inventory).</summary>
        internal static void RequestNewPlacement(Action<bool> onDone = null)
        {
            if (placingBox != null) { onDone?.Invoke(false); return; }
            placingOnDone = onDone;
            var cam = GameBridge.PlayerCamera;
            if (cam == null) return;
            var beh = CreateBox(cam.transform.position + cam.transform.forward * 2f, Quaternion.identity, null, GameManager.m_ActiveScene);
            if (beh == null) { placingOnDone = null; onDone?.Invoke(false); return; }
            BeginPlacement(beh, true);
        }

        // ---------------- per-frame update ----------------
        internal static void Update()
        {
            var cfg = ColdBoxSettings.Instance;
            if (cfg == null) return;

            Boxes.RemoveAll(b => b == null);
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < timed[i].Key) continue;
                var act = timed[i].Value; timed.RemoveAt(i);
                try { act(); } catch (Exception e) { Melon<Core>.Logger.Error(e); }
            }
            while (nextFrame.Count > 0) { try { nextFrame.Dequeue()(); } catch (Exception e) { Melon<Core>.Logger.Error(e); } }

            ColdBoxSave.TryRestore();
            ColdBoxDecay.Refresh();
            ColdBoxCrafting.TryRegister();
            ColdBoxSteam.Sweep();
            ColdBoxIcons.ValidateOverlays();
            ColdBoxLoc.Update();
            ColdBoxIcons.UpdateFrozenBadges(storageOpen);
            if (storageOpen && Time.unscaledTime - storageOpenedAt > 2f)
            {
                // safety net: forget the flag when the container panel is gone but the close callback never arrived
                var cp = InterfaceManager.GetPanel<Panel_Container>();
                if (cp == null || !cp.gameObject.activeInHierarchy) storageOpen = false;
            }

            if (!GameBridge.InGame) return;
            if (placingBox != null) { UpdatePlacement(cfg); return; }
            if (fallbackBox != null) { UpdateFallbackMenu(); return; }

            // cancel the hold when the interact button is released
            if (holding && Time.time - holdStartTime > ColdBoxSettings.Instance.holdSeconds + 3f)
            {
                holding = false;   // safety net: the progress bar never reported back
                holdTarget = null;
            }
            if (holding)
            {
                if (holdTarget != null && InputManager.GetInteractReleased(holdTarget))
                {
                    var bar = InterfaceManager.GetPanel<Panel_GenericProgressBar>();
                    if (bar != null) bar.Cancel();
                }
                return;
            }

            if (GameBridge.IsOverlayActive) return;   // inventory, container, action picker, pause menu...

            AutoCloseLids(cfg);

            var box = FindLookedAt(cfg.interactDistance);
            if (box == null) return;

            GameBridge.SetHoverText(HoverText(box), box.gameObject);
            if (InputManager.GetInteractPressed(box))
            {
                box.Touch();
                if (box.IsOpen()) ShowMenu(box); else BeginHold(box, cfg);
            }
        }

        static string TranslateProblem(string problem)
        {
            const string blocked = "blocked by ";
            if (problem != null && problem.StartsWith(blocked)) return ColdBoxLoc.F("blocked by {0}", problem.Substring(blocked.Length));
            return ColdBoxLoc.T(problem);
        }

        static string HoverText(ColdBoxBehaviour box)
        {
            string snow = box.IsCold() ? ColdBoxLoc.T("Snow") + ": " + Mathf.RoundToInt(box.GetSnowFraction() * 100f) + "%" : ColdBoxLoc.T("No snow");
            string text = ColdBoxLoc.T("Snow Cooler") + "\n" + snow;
            if (!box.IsOpen()) text += "\n" + ColdBoxLoc.T("Hold to open");
            return text;
        }

        internal static void Defer(Action a) { nextFrame.Enqueue(a); }

        static readonly List<KeyValuePair<float, Action>> timed = new List<KeyValuePair<float, Action>>();
        /// <summary>Runs an action after the given real-time seconds.</summary>
        internal static void DeferSeconds(float seconds, Action a) { timed.Add(new KeyValuePair<float, Action>(Time.unscaledTime + seconds, a)); }

        static ColdBoxBehaviour FindLookedAt(float maxDist)
        {
            var cam = GameBridge.PlayerCamera;
            if (cam == null) return null;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            ColdBoxBehaviour best = null;
            float bestDist = maxDist;
            foreach (var b in Boxes)
            {
                if (b == null || b.IsPlacing()) continue;
                var col = b.GetComponent<BoxCollider>();
                if (col == null) continue;
                RaycastHit hit;
                if (col.Raycast(ray, out hit, maxDist) && hit.distance < bestDist) { bestDist = hit.distance; best = b; }
            }
            return best;
        }

        // Lids left open (picker cancelled, player walked away) close by themselves.
        static void AutoCloseLids(ColdBoxSettings cfg)
        {
            var player = GameManager.GetPlayerTransform();
            foreach (var b in Boxes)
            {
                if (b == null || !b.IsOpen()) continue;
                bool far = player != null && Vector3.Distance(player.position, b.transform.position) > cfg.interactDistance + 1.5f;
                bool idle = Time.time - b.GetLastUseTime() > 15f;
                if (far || idle) { b.CloseLid(); GameBridge.PlayLidClose(b.gameObject); }
            }
        }

        // ---------------- hold to open ----------------
        static void BeginHold(ColdBoxBehaviour box, ColdBoxSettings cfg)
        {
            var bar = InterfaceManager.GetPanel<Panel_GenericProgressBar>();
            if (bar == null) { OpenLidAndMenu(box); return; }

            holding = true;
            holdTarget = box;
            holdStartTime = Time.time;
            Action<bool, bool, float> onExit = (success, playerCancel, progress) =>
            {
                var target = holdTarget;
                holding = false;
                holdTarget = null;
                if (success && !playerCancel && target != null) Defer(() => OpenLidAndMenu(target));
            };
            bar.Launch(ColdBoxLoc.T("Opening the cooler"), cfg.holdSeconds, 0f, 0f, false, onExit);
        }

        static void OpenLidAndMenu(ColdBoxBehaviour box)
        {
            if (box == null) return;
            var player = GameManager.GetPlayerTransform();
            if (player != null) box.OpenLidAwayFrom(player.position); else box.OpenLid();
            GameBridge.PlayLidOpen(box.gameObject);
            ShowMenu(box);
        }

        // ---------------- action menu ----------------
        static List<KeyValuePair<string, Action>> BuildMainEntries(ColdBoxBehaviour box)
        {
            var list = new List<KeyValuePair<string, Action>>();
            list.Add(new KeyValuePair<string, Action>("Cover with Snow", () => LoadSnow(box)));
            list.Add(new KeyValuePair<string, Action>("Food Storage", () => OpenStorage(box)));
            float water = box.GetCollectibleWater();
            if (ColdBoxSettings.Instance.enableWater && water > 0.05f)
                list.Add(new KeyValuePair<string, Action>("Collect Water " + water.ToString("0.0") + " L", () => CollectWater(box)));
            list.Add(new KeyValuePair<string, Action>("Move / Remove", () => ShowMoveMenu(box)));
            list.Add(new KeyValuePair<string, Action>("Close Lid", () => CloseLid(box)));
            return list;
        }

        static List<KeyValuePair<string, Action>> BuildMoveEntries(ColdBoxBehaviour box)
        {
            var list = new List<KeyValuePair<string, Action>>();
            list.Add(new KeyValuePair<string, Action>("Move", () => BeginPlacement(box, false)));
            list.Add(new KeyValuePair<string, Action>("Remove", () => RemoveBox(box)));
            return list;
        }

        // Icon keywords per menu entry (see ColdBoxIcons: names are matched against the game's UI atlas at runtime).
        static string IconFor(string label)
        {
            if (label.StartsWith("Cover")) return ColdBoxIcons.Pick("snow", "ice", "freez", "cold", "water");
            if (label.StartsWith("Food")) return ColdBoxIcons.Pick("container", "chest", "crate", "storage", "box", "backpack", "inventory", "food");
            if (label.StartsWith("Collect")) return ColdBoxIcons.Pick("water", "drink", "bottle", "canteen", "flask");
            if (label.StartsWith("Move /")) return ColdBoxIcons.Pick("radial_place", "ico_carry");
            if (label.StartsWith("Move")) return ColdBoxIcons.Pick("radial_place", "ico_carry");
            if (label.StartsWith("Remove")) return ColdBoxIcons.Pick("trash", "drop", "dismantle", "break", "harvest", "remove");
            if (label.StartsWith("Back")) return ColdBoxIcons.Pick("back", "return", "exit", "cancel");
            return ColdBoxIcons.Pick("close", "cancel", "exit", "back");
        }

        static void ShowMenu(ColdBoxBehaviour box) { ShowEntries(box, BuildMainEntries(box)); }
        static void ShowMoveMenu(ColdBoxBehaviour box) { ShowEntries(box, BuildMoveEntries(box)); }

        static void ShowEntries(ColdBoxBehaviour box, List<KeyValuePair<string, Action>> entries)
        {
            if (!ColdBoxSettings.Instance.useFallbackMenu)
            {
                try
                {
                    var picker = InterfaceManager.GetPanel<Panel_ActionPicker>();
                    if (picker != null)
                    {
                        var items = new Il2CppSystem.Collections.Generic.List<ActionPickerItemData>();
                        var labels = new List<string>();
                        var spriteNames = new List<string>();
                        foreach (var e in entries)
                        {
                            var action = e.Value;
                            Action wrapped = () => Defer(action);   // run the callback outside the picker's own update
                            string sprite = IconFor(e.Key);
                            labels.Add(e.Key); spriteNames.Add(sprite);
                            items.Add(new ActionPickerItemData(sprite, ColdBoxLoc.Entry(e.Key), wrapped));
                        }
                        picker.ShowActionPicker(box.gameObject, items.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ActionPickerItemData>>());
                        DeferSeconds(0.05f, () => ColdBoxIcons.ApplyCustomPictures(labels, spriteNames));   // after the picker has filled its entries
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Melon<Core>.Logger.Warning("Game action picker failed, using the fallback menu: " + ex.Message);
                }
            }
            fallbackBox = box;
            fallbackEntries = entries;
        }

        // Simple keyboard menu used only when the game picker is unavailable (keys 1-5, Escape closes).
        static void UpdateFallbackMenu()
        {
            if (fallbackBox == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { fallbackBox = null; return; }
            for (int i = 0; i < fallbackEntries.Count && i < 5; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    var action = fallbackEntries[i].Value;
                    fallbackBox = null;
                    action();
                    return;
                }
            }
        }

        internal static void DrawFallbackMenu()
        {
            if (fallbackBox == null || fallbackEntries == null) return;
            var sb = new System.Text.StringBuilder(ColdBoxLoc.T("Snow Cooler") + "\n");
            for (int i = 0; i < fallbackEntries.Count && i < 5; i++) sb.Append(i + 1).Append(") ").Append(ColdBoxLoc.Entry(fallbackEntries[i].Key)).Append('\n');
            sb.Append("Esc) Cancel");
            GUI.Box(new Rect(20f, 20f, 360f, 30f + 22f * (fallbackEntries.Count + 2)), sb.ToString());
        }

        // ---------------- menu actions ----------------
        static void LoadSnow(ColdBoxBehaviour box)
        {
            if (box == null) return;
            if (ColdBoxSettings.Instance.enableWater && box.GetWaterPending() > 0.01f && !box.IsCold()) { GameBridge.ShowMessage("Collect the water from the previous load first."); ShowMenu(box); return; }
            if (!box.CanLoadSnow()) { GameBridge.ShowMessage("The cooler still has snow."); ShowMenu(box); return; }
            box.LoadSnow();
            GameBridge.PlayLidClose(box.gameObject);
            GameBridge.ShowMessage("You loaded snow into the cooler.");
            ShowMenu(box);
        }

        internal static bool storageOpen;       // the Snow Cooler's own storage panel is showing
        static float storageOpenedAt;

        static void OpenStorage(ColdBoxBehaviour box)
        {
            var container = box == null ? null : box.GetContainer();
            if (container == null) return;
            ColdBoxContainer.ApplyCapacity(container);   // picks up changes made in Mod Settings
            ColdBoxContainer.EnsureSafeDefaults(container);   // audio names may only be available now
            Action onClosed = () =>
            {
                // Called by the game while it is closing the panel: do the bare minimum here and defer the rest.
                Melon<Core>.Logger.Msg("Storage panel closed.");
                storageOpen = false;
                Defer(() =>
                {
                    if (box == null) return;
                    box.CloseLid();
                    GameBridge.PlayLidClose(box.gameObject);
                });
            };
            Melon<Core>.Logger.Msg("Opening storage panel (" + box.GetId() + ").");
            storageOpen = true;
            storageOpenedAt = Time.unscaledTime;
            container.ShowContainerContents(onClosed);
        }

        static void CollectWater(ColdBoxBehaviour box)
        {
            if (box == null) return;
            float liters = box.TakeWater();
            if (liters < 0.05f)
            {
                GameBridge.ShowMessage(box.IsCold() ? "The snow is still melting." : "There is no water to collect.");
                return;
            }
            float given = GameBridge.AddNonPotableWater(liters);
            box.ReturnWater(liters - given);   // whatever did not fit stays in the cooler (never lost, never duplicated)
            GameBridge.PlayClick();
            if (given < 0.05f) GameBridge.ShowMessage("You cannot carry more water right now.");
            else GameBridge.ShowMessage(ColdBoxLoc.F("You collected {0} L of non-potable water.", given.ToString("0.0")));
            ShowMenu(box);
        }

        static void CloseLid(ColdBoxBehaviour box)
        {
            if (box == null) return;
            box.CloseLid();
            GameBridge.PlayLidClose(box.gameObject);
        }

        static void RemoveBox(ColdBoxBehaviour box)
        {
            if (box == null) return;
            var container = box.GetContainer();
            if (container != null && !ColdBoxContainer.IsEmpty(container))
            {
                GameBridge.ShowMessage("Empty the cooler before removing it.");
                ShowMoveMenu(box);
                return;
            }
            if (box.GetWaterPending() > 0.05f || box.IsCold())
            {
                GameBridge.ShowMessage("The snow and water left inside are lost.");
            }
            Boxes.Remove(box);
            UnityEngine.Object.Destroy(box.gameObject);
            if (ColdBoxItem.GiveToPlayer()) GameBridge.ShowMessage("The cooler is back in your inventory.");
            else GameBridge.ShowMessage("You removed the cooler.");
            OnBoxRemoved?.Invoke(box);
        }

        // ---------------- placement mode ----------------
        static void BeginPlacement(ColdBoxBehaviour box, bool isNew)
        {
            if (box == null || placingBox != null) return;
            placingBox = box;
            placingIsNew = isNew;
            placingOrigPos = box.transform.position;
            placingOrigRot = box.transform.rotation;
            var cam = GameBridge.PlayerCamera;
            placingYaw = cam != null ? cam.transform.eulerAngles.y : box.transform.eulerAngles.y;
            box.SetPlacing(true);
            GameBridge.ShowMessage("Click: place   Right click: cancel   Wheel / Q E: rotate");
        }

        static void UpdatePlacement(ColdBoxSettings cfg)
        {
            var box = placingBox;
            if (box == null) { placingBox = null; return; }
            var cam = GameBridge.PlayerCamera;
            if (cam == null) return;

            // rotation
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f || Input.GetKeyDown(KeyCode.E)) placingYaw += cfg.rotateStepDegrees;
            if (scroll < 0f || Input.GetKeyDown(KeyCode.Q)) placingYaw -= cfg.rotateStepDegrees;

            // position: where the crosshair hits a surface (game placement mask first, any collider as fallback)
            string problem = "no surface in range";
            RaycastHit hit;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            bool hasHit = Physics.Raycast(ray, out hit, 4f, PlayerManager.GetLayerMaskForPlaceMeshRaycast(), QueryTriggerInteraction.Ignore)
                          || Physics.Raycast(ray, out hit, 4f, ~0, QueryTriggerInteraction.Ignore);
            if (hasHit)
            {
                box.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, placingYaw, 0f));
                problem = CheckPlacement(box, hit);
            }
            else
            {
                box.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 2f, Quaternion.Euler(0f, placingYaw, 0f));
            }
            bool valid = problem == null;

            if (problem != lastPlacementProblem)
            {
                lastPlacementProblem = problem;
                Melon<Core>.Logger.Msg("Placement check: " + (problem ?? "ok"));
            }
            GameBridge.SetHoverText(valid ? ColdBoxLoc.T("Place here") : ColdBoxLoc.F("Cannot place here: {0}", TranslateProblem(problem)), box.gameObject);

            if (Input.GetMouseButtonDown(0) && valid)
            {
                box.SetPlacing(false);
                box.Touch();
                placingBox = null;
                GameBridge.PlayClick();
                GameBridge.ShowMessage("Cooler placed.");
                var done = placingOnDone; placingOnDone = null;
                done?.Invoke(true);
            }
            else if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
            }
        }

        static string lastPlacementProblem = "";

        /// <summary>Returns null when the cooler can stand at the hit point, otherwise a short reason.</summary>
        static string CheckPlacement(ColdBoxBehaviour box, RaycastHit hit)
        {
            if (hit.normal.y < 0.7f) return "surface too steep";

            var col = box.GetComponent<BoxCollider>();
            if (col == null) return null;
            var t = box.transform;
            Vector3 half = col.size * 0.5f;
            half.x *= 0.95f; half.z *= 0.95f;
            half.y -= 0.08f;   // keep clear of the surface the cooler stands on
            Vector3 center = t.TransformPoint(col.center + new Vector3(0f, 0.05f, 0f));

            var player = GameManager.GetPlayerTransform();
            foreach (var c in Physics.OverlapBox(center, half, t.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null || c.transform.IsChildOf(t)) continue;
                if (player != null && c.transform.IsChildOf(player.root)) continue;
                if (c.bounds.max.y <= hit.point.y + 0.15f) continue;   // floors, rugs and other flat things are fine to stand on
                return "blocked by " + c.gameObject.name;
            }
            return null;
        }

        static void CancelPlacement()
        {
            var box = placingBox;
            placingBox = null;
            if (box == null) return;
            if (placingIsNew)
            {
                Boxes.Remove(box);
                UnityEngine.Object.Destroy(box.gameObject);
            }
            else
            {
                box.transform.SetPositionAndRotation(placingOrigPos, placingOrigRot);
                box.SetPlacing(false);
            }
            GameBridge.ShowMessage("Placement cancelled.");
            var done = placingOnDone; placingOnDone = null;
            done?.Invoke(false);
        }
    }
}
