using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Component of a cooler placed in the world. Handles the lid, the snow load (melts with game time) and the
    /// non-potable water that the melted snow leaves behind. Only simple types are exposed publicly because the class
    /// is injected into Il2Cpp; everything the manager needs goes through the small public methods below.
    /// </summary>
    public class ColdBoxBehaviour : MonoBehaviour
    {
        public ColdBoxBehaviour(IntPtr ptr) : base(ptr) { }

        // how far the lid opens (setting "Lid opening angle"); it does not open all the way by default
        const float LidSpeedDegPerSec = 220f;

        Transform hinge;
        Transform snow;
        MeshRenderer tarpRenderer;
        Vector3 snowFullScale = Vector3.one;
        Container container;
        Collider box;
        Transform lidMesh;
        Transform contents;       // visual copies of the stored items
        Vector3 hingeAbs;         // hinge position with a positive X; the real side is hingeSide
        int hingeSide = 1;        // +1 hinge at +X, -1 hinge at -X
        float snowMeshTopY = 0.66f;
        float contentsTimer;
        int contentsSignature = int.MinValue;

        float lidAngle;
        bool steamOn;
        bool open;
        bool placing;

        float snowHoursLeft;     // game hours of cold left
        float snowTotalHours;    // length of the load that is melting (kept so settings changes do not break it)
        float loadLiters;        // liters of water the current load will leave
        float waterPendingL;     // liters already melted and not collected yet
        float lastUseTime;

        string id = "";
        string sceneKey = "";

        // ---------------- identity ----------------
        public string GetId() { return id; }
        public void SetId(string value) { id = value ?? ""; }
        public string GetSceneKey() { return sceneKey; }
        public void SetSceneKey(string value) { sceneKey = value ?? ""; }

        // ---------------- state ----------------
        public bool IsOpen() { return open; }
        public bool IsPlacing() { return placing; }
        public bool IsCold() { return snowHoursLeft > 0f; }
        public float GetSnowHoursLeft() { return snowHoursLeft; }
        public float GetSnowTotalHours() { return snowTotalHours; }
        public float GetLoadLiters() { return loadLiters; }
        public float GetWaterPending() { return waterPendingL; }
        public float GetLastUseTime() { return lastUseTime; }
        public void Touch() { lastUseTime = Time.time; }

        /// <summary>Water that can be collected right now: only when the whole load has melted.</summary>
        public float GetCollectibleWater() { return snowHoursLeft <= 0.001f ? waterPendingL : 0f; }

        public float GetSnowFraction()
        {
            return snowTotalHours <= 0f ? 0f : Mathf.Clamp01(snowHoursLeft / snowTotalHours);
        }

        public Container GetContainer() { return container; }

        // ---------------- lifecycle ----------------
        public void Awake()
        {
            hinge = transform.Find("LidHinge");
            snow = transform.Find("Body/Snow");
            var tarpT = transform.Find("Body/Tarp");
            if (tarpT != null) tarpRenderer = tarpT.GetComponent<MeshRenderer>();
            if (snow != null) snowFullScale = snow.localScale;
            container = GetComponent<Container>();
            box = GetComponent<BoxCollider>();
            contents = transform.Find("Contents");
            if (hinge != null)
            {
                hingeAbs = new Vector3(Mathf.Abs(hinge.localPosition.x), hinge.localPosition.y, hinge.localPosition.z);
                hingeSide = hinge.localPosition.x >= 0f ? 1 : -1;
                if (hinge.childCount > 0) lidMesh = hinge.GetChild(0);
            }
            if (snow != null)
            {
                var mf = snow.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) snowMeshTopY = mf.sharedMesh.bounds.max.y;
            }
            if (container == null) Melon<Core>.Logger.Warning("ColdBox has no Container: it cannot store food.");
            if (hinge == null) Melon<Core>.Logger.Warning("ColdBox has no 'LidHinge': the lid will not animate.");
            lastUseTime = Time.time;
            ApplyVisuals();
        }

        public void Update()
        {
            if (hinge != null)
            {
                float target = open ? ColdBoxBuilder.OpenSign * hingeSide * ColdBoxSettings.Instance.lidOpenAngle : 0f;
                lidAngle = Mathf.MoveTowards(lidAngle, target, LidSpeedDegPerSec * Time.deltaTime);
                hinge.localRotation = Quaternion.Euler(0f, 0f, lidAngle);
            }

            // cold mist while the lid is open and there is snow; one puff at the moment it starts
            bool wantSteam = ColdBoxSettings.Instance.enableSteam && open && snowHoursLeft > 0f && Mathf.Abs(lidAngle) > 8f;
            bool opened = wantSteam && !steamOn;
            steamOn = wantSteam;
            ColdBoxSteam.Tick(gameObject, hinge, lidAngle, wantSteam, opened);

            if (snowHoursLeft > 0f) AdvanceMelt(GameBridge.GetGameHoursDelta());

            contentsTimer -= Time.unscaledDeltaTime;
            if (contentsTimer <= 0f)
            {
                contentsTimer = 0.5f;
                RefreshContents();
            }
        }

        /// <summary>Melts snow for the given amount of game hours and moves the water to the pending tank.</summary>
        public void AdvanceMelt(float hours)
        {
            if (hours <= 0f || snowHoursLeft <= 0f) return;
            float melted = Mathf.Min(snowHoursLeft, hours);
            snowHoursLeft -= melted;
            if (ColdBoxSettings.Instance.enableWater && snowTotalHours > 0f) waterPendingL += melted / snowTotalHours * loadLiters;
            if (snowHoursLeft <= 0.001f)
            {
                snowHoursLeft = 0f;
                if (ColdBoxSettings.Instance.enableWater) waterPendingL = Mathf.Max(waterPendingL, loadLiters);   // avoid rounding leftovers
            }
            ApplyVisuals();
        }

        // ---------------- lid ----------------
        public void OpenLid() { open = true; lastUseTime = Time.time; }

        /// <summary>
        /// Opens the lid with the hinge on the side farthest from the player, so it always swings away from them.
        /// The hinge can only move while the lid is fully closed.
        /// </summary>
        public void OpenLidAwayFrom(Vector3 playerWorldPos)
        {
            if (!open && Mathf.Abs(lidAngle) < 1f && hinge != null && lidMesh != null)
            {
                float playerX = transform.InverseTransformPoint(playerWorldPos).x;
                int side = playerX >= 0f ? -1 : 1;
                if (ColdBoxSettings.Instance.invertLidSide) side = -side;
                SetHingeSide(side);
            }
            OpenLid();
        }

        void SetHingeSide(int side)
        {
            if (side == hingeSide) return;
            hingeSide = side;
            hinge.localPosition = new Vector3(side * hingeAbs.x, hingeAbs.y, hingeAbs.z);
            lidMesh.localPosition = -hinge.localPosition;   // the lid mesh is baked in root space
        }
        public void CloseLid() { open = false; }

        public void SetPlacing(bool value)
        {
            placing = value;
            if (value) open = false;
            if (box != null) box.enabled = !value;   // the placement raycast must not hit the box itself
        }

        // ---------------- snow / water ----------------
        public bool CanLoadSnow() { return snowHoursLeft <= 0f && (waterPendingL <= 0.01f || !ColdBoxSettings.Instance.enableWater); }

        /// <summary>Fills the cooler with a full load of snow using the current settings.</summary>
        public void LoadSnow()
        {
            var cfg = ColdBoxSettings.Instance;
            snowTotalHours = cfg.HoursPerLoad;
            snowHoursLeft = snowTotalHours;
            loadLiters = cfg.litersPerLoad;
            waterPendingL = 0f;
            lastUseTime = Time.time;
            ApplyVisuals();
        }

        /// <summary>Removes the collectible water from the tank and returns it (liters).</summary>
        public float TakeWater()
        {
            float l = GetCollectibleWater();
            waterPendingL -= l;
            if (waterPendingL < 0.001f) waterPendingL = 0f;
            return l;
        }

        /// <summary>Puts back the water that did not fit into the player's supply.</summary>
        public void ReturnWater(float liters) { waterPendingL += Mathf.Max(0f, liters); }

        /// <summary>Restores the full state from a save file.</summary>
        public void RestoreState(float snowHours, float totalHours, float liters, float waterL)
        {
            snowHoursLeft = Mathf.Max(0f, snowHours);
            snowTotalHours = Mathf.Max(0f, totalHours);
            loadLiters = Mathf.Max(0f, liters);
            waterPendingL = Mathf.Max(0f, waterL);
            ApplyVisuals();
        }

        // The snow mesh shrinks with the remaining snow and disappears when it is gone.
        void ApplyVisuals()
        {
            float f = GetSnowFraction();
            // the tarp has two textures: plain, and with snow inside
            if (tarpRenderer != null)
            {
                var wanted = f > 0.01f ? ColdBoxBuilder.TarpSnowMaterial : ColdBoxBuilder.TarpMaterial;
                if (wanted != null && tarpRenderer.sharedMaterial != wanted) tarpRenderer.sharedMaterial = wanted;
            }
            if (snow == null) return;
            snow.gameObject.SetActive(f > 0.01f);
            snow.localScale = new Vector3(snowFullScale.x, snowFullScale.y * Mathf.Lerp(0.3f, 1f, f), snowFullScale.z);
            PlaceContentsOnSnow(f);
        }

        // ---------------- stored food made visible ----------------
        // Stored items are inactive game objects, so nothing is drawn for them. Their meshes are copied (renderers only,
        // no game components) into a small pile inside the cooler. This is purely visual; the real items stay in the Container.
        const float ContentsFloorY = 0.14f;
        const int MaxVisibleItems = 30;

        void PlaceContentsOnSnow(float snowFraction)
        {
            if (contents == null) return;
            float snowTop = snowFraction > 0.01f ? snowMeshTopY * Mathf.Lerp(0.3f, 1f, snowFraction) * snowFullScale.y : 0f;
            // food rests on the floor, or sinks into the snow so it never reaches the lid
            float y = snowTop > 0f ? Mathf.Clamp(snowTop - 0.14f, ContentsFloorY, 0.42f) : ContentsFloorY;
            contents.localPosition = new Vector3(0f, y, 0f);
        }

        void RefreshContents()
        {
            if (contents == null || container == null) return;
            var items = new Il2CppSystem.Collections.Generic.List<GearItem>();
            container.GetItems(items);
            if (RejectNonFood(items)) { items.Clear(); container.GetItems(items); }

            int signature = items.Count;
            for (int i = 0; i < items.Count; i++) if (items[i] != null) signature = signature * 31 + items[i].m_InstanceID;
            if (signature == contentsSignature) return;
            contentsSignature = signature;

            for (int i = contents.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(contents.GetChild(i).gameObject);

            const int cols = 3, rows = 5;
            int shown = 0;
            for (int i = 0; i < items.Count && shown < MaxVisibleItems; i++)
            {
                var gi = items[i];
                if (gi == null) continue;
                if (BuildItemVisual(gi, shown, cols, rows)) shown++;
            }
        }

        // Safety net behind the inventory filter: anything that is not food goes back to the player.
        bool RejectNonFood(Il2CppSystem.Collections.Generic.List<GearItem> items)
        {
            var rejected = new System.Collections.Generic.List<GearItem>();
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].m_FoodItem == null) rejected.Add(items[i]);
            if (rejected.Count == 0) return false;

            foreach (var gi in rejected)
            {
                try { container.RemoveGear(gi, true); }
                catch (Exception e) { Melon<Core>.Logger.Warning("Could not return " + gi.name + ": " + e.Message); }
            }
            GameBridge.ShowMessage("The cooler only stores food.");
            return true;
        }

        bool BuildItemVisual(GearItem gi, int index, int cols, int rows)
        {
            var renderers = gi.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            var holder = new GameObject("Item" + index);
            holder.transform.SetParent(contents, false);

            Quaternion inv = Quaternion.Inverse(gi.transform.rotation);
            float maxDim = 0f, lowest = float.MaxValue;
            int parts = 0;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var child = new GameObject("Mesh");
                child.transform.SetParent(holder.transform, false);
                Vector3 pos = inv * (r.transform.position - gi.transform.position);
                Vector3 scale = r.transform.lossyScale;
                child.transform.localPosition = pos;
                child.transform.localRotation = inv * r.transform.rotation;
                child.transform.localScale = scale;
                child.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                child.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;

                Vector3 size = Vector3.Scale(mf.sharedMesh.bounds.size, scale);
                maxDim = Mathf.Max(maxDim, size.x, size.y, size.z);
                lowest = Mathf.Min(lowest, pos.y + Vector3.Scale(mf.sharedMesh.bounds.center, scale).y - size.y * 0.5f);
                parts++;
            }
            if (parts == 0) { UnityEngine.Object.Destroy(holder); return false; }

            float fit = maxDim > 0.20f ? 0.20f / maxDim : 1f;   // very large items are shrunk to fit the box
            holder.transform.localScale = Vector3.one * fit;

            int perLayer = cols * rows;
            int layer = index / perLayer, slot = index % perLayer;
            float x = -0.26f + (slot % cols) * 0.27f;
            float z = -0.45f + (slot / cols) * 0.19f;
            holder.transform.localPosition = new Vector3(x, layer * 0.10f - lowest * fit, z);
            return true;
        }
    }
}
