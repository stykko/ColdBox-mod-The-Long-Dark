using Il2Cpp;
using Il2CppTLD.IntBackedUnit;
using MelonLoader;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Single place where the mod talks to game classes. Every member used here was checked against the
    /// Assembly-CSharp.dll of The Long Dark 2.55 (MelonLoader Il2CppAssemblies).
    /// </summary>
    internal static class GameBridge
    {
        // ---------------- player / screen ----------------
        public static Camera PlayerCamera
        {
            get
            {
                var c = GameManager.GetMainCamera();
                return c != null ? c : Camera.main;
            }
        }

        public static bool InGame => !GameManager.IsMainMenuActive() && !GameManager.IsEmptySceneActive();

        /// <summary>True when any game panel (inventory, container, action picker, progress bar, menus) is open.</summary>
        public static bool IsOverlayActive => InterfaceManager.IsOverlayActiveCached();

        public static void ShowMessage(string text) => HUDMessage.AddMessage(ColdBoxLoc.T(text), false, true);

        public static void SetHoverText(string text, GameObject target)
        {
            var hud = InterfaceManager.GetPanel<Panel_HUD>();
            if (hud != null) hud.SetHoverText(text, target, HoverTextState.CanInteract);
        }

        // ---------------- game time ----------------
        static float lastHours = -1f;
        static int lastSampleFrame = -100;
        static int cachedFrame = -1;
        static float cachedDelta;

        /// <summary>
        /// Game hours elapsed since the previous frame (TimeOfDay.GetHoursPlayedNotPaused). Computed once per frame so
        /// any number of boxes can ask for it. The baseline is retaken after scene loads or time going backwards.
        /// </summary>
        public static float GetGameHoursDelta()
        {
            if (Time.frameCount == cachedFrame) return cachedDelta;
            cachedFrame = Time.frameCount;
            cachedDelta = 0f;

            var tod = GameManager.GetTimeOfDayComponent();
            if (tod == null) return 0f;
            float hours = tod.GetHoursPlayedNotPaused();

            bool stale = Time.frameCount - lastSampleFrame > 3 || lastHours < 0f || hours < lastHours;
            if (!stale) cachedDelta = hours - lastHours;
            lastHours = hours;
            lastSampleFrame = Time.frameCount;
            return cachedDelta;
        }

        /// <summary>Absolute game hours played (used to melt snow for the time spent outside a scene).</summary>
        public static float GetGameHoursNow()
        {
            var tod = GameManager.GetTimeOfDayComponent();
            return tod == null ? 0f : tod.GetHoursPlayedNotPaused();
        }

        // ---------------- water ----------------
        static float NonPotableLiters()
        {
            var inv = GameManager.GetInventoryComponent();
            if (inv == null) return 0f;
            var gi = inv.GetNonPotableWaterSupply();
            if (gi == null || gi.m_WaterSupply == null) return 0f;
            return gi.m_WaterSupply.m_VolumeInLiters / ItemLiquidVolume.Liter;
        }

        /// <summary>
        /// Adds non-potable water to the player's water supply with the same call the game uses
        /// (Inventory.AddToWaterSupply). Returns the liters that were really added.
        /// </summary>
        public static float AddNonPotableWater(float liters)
        {
            var inv = GameManager.GetInventoryComponent();
            if (inv == null) return 0f;
            float before = NonPotableLiters();
            inv.AddToWaterSupply(ItemLiquidVolume.FromLiters(liters), LiquidQuality.NonPotable);
            float added = NonPotableLiters() - before;
            return Mathf.Clamp(added, 0f, liters);
        }

        // ---------------- audio ----------------
        // Wwise event names live in game assets, not in the DLL. They are read at runtime from any vanilla
        // container found in the scene (Container.m_OpenAudio / m_CloseAudio).
        static string openAudio, closeAudio, searchAudio;

        static void FindVanillaAudio()
        {
            if (!string.IsNullOrEmpty(openAudio)) return;
            foreach (var c in Object.FindObjectsOfType<Container>())
            {
                if (c == null || c.gameObject.name.StartsWith("ColdBox")) continue;
                if (string.IsNullOrEmpty(c.m_OpenAudio)) continue;
                openAudio = c.m_OpenAudio;
                closeAudio = c.m_CloseAudio;
                searchAudio = c.m_SearchAudio;
                return;
            }
        }

        /// <summary>Audio event names of a vanilla container; empty strings (never null) if none is loaded.</summary>
        public static void GetVanillaContainerAudio(out string open, out string close, out string search)
        {
            FindVanillaAudio();
            open = openAudio ?? "";
            close = closeAudio ?? "";
            search = searchAudio ?? "";
        }

        static void Play(bool open, GameObject at)
        {
            try
            {
                FindVanillaAudio();
                string id = open ? openAudio : closeAudio;
                if (!string.IsNullOrEmpty(id)) GameAudioManager.Play3DSound(id, at);
            }
            catch (System.Exception e) { Melon<Core>.Logger.Warning("Lid sound failed: " + e.Message); }
        }

        public static void PlayLidOpen(GameObject at)
        {
            Play(true, at);
        }

        public static void PlayLidClose(GameObject at)
        {
            Play(false, at);
        }

        public static void PlayClick() => GameAudioManager.PlayGUIButtonClick();
    }
}
