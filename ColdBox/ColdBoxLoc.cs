using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace ColdBox
{
    /// <summary>
    /// Interface language (English by default, Russian optional). The English text is the key everywhere in the code;
    /// <see cref="T"/> returns the Russian version when that language is selected and the key itself otherwise, so a
    /// missing translation simply shows English.
    ///
    /// Mod Settings builds its labels from fixed attribute strings, so those labels are translated afterwards: while Russian
    /// is selected (and for a few seconds after switching back to English) the visible UILabels whose text is one of our
    /// strings are rewritten in the right language.
    /// </summary>
    internal static class ColdBoxLoc
    {
        static readonly Dictionary<string, string> ru = new Dictionary<string, string>
        {
            // ---- Mod Settings: sections ----
            { "Snow Cooler", "Снежный холодильник" },
            { "Interaction", "Взаимодействие" },
            { "Placement", "Размещение" },
            { "Crafting", "Крафт" },

            // ---- Mod Settings: names and descriptions ----
            { "Snow load duration (days)", "Длительность снежной загрузки (дни)" },
            { "How many game days a full snow load keeps the cooler cold (5 to 10). This is also the snow consumption speed.",
              "Сколько игровых дней полная загрузка снега держит холод в ящике (от 5 до 10). Это также скорость таяния снега." },
            { "Food decay while cold", "Порча еды на холоде" },
            { "Fraction of the normal decay that stored food suffers while there is snow (cooling efficiency). 1 = same as outside, 0.5 = half as fast.",
              "Доля обычной порчи, которую получает еда при наличии снега (эффективность охлаждения). 1 = как снаружи, 0.5 = вдвое медленнее." },
            { "Water from melted snow (liters)", "Вода из растаявшего снега (литры)" },
            { "Liters of NON-potable water that a full snow load leaves once it has melted.",
              "Литры НЕпитьевой воды, которые остаются после таяния полной загрузки снега." },
            { "Food capacity (kg)", "Вместимость еды (кг)" },
            { "Maximum weight of food that fits in the cooler.", "Максимальный вес еды, который помещается в ящик." },
            { "Enable water from melted snow", "Вода из растаявшего снега" },
            { "When enabled, melted snow can be collected as non-potable water (added to the player's water supply).",
              "Если включено, растаявший снег можно собрать как непитьевую воду (добавляется в запас воды игрока)." },
            { "Time to open the lid (seconds)", "Время открытия крышки (сек.)" },
            { "How long the interact button must be held while looking at the cooler.",
              "Как долго нужно удерживать кнопку взаимодействия, глядя на ящик." },
            { "Interaction distance (m)", "Дистанция взаимодействия (м)" },
            { "Fallback menu (keys 1-5)", "Резервное меню (клавиши 1–5)" },
            { "Uses a simple built-in menu instead of the game action picker. Enable it only if the game picker does not display correctly.",
              "Использует простое встроенное меню вместо меню действий игры. Включайте, только если меню игры отображается неправильно." },
            { "Lid opening angle (degrees)", "Угол открытия крышки (градусы)" },
            { "How far the lid opens.", "Насколько широко открывается крышка." },
            { "Cold mist when opened", "Холодный пар при открытии" },
            { "A little mist comes out when you open the cooler while it still has snow.",
              "При открытии ящика со снегом выходит немного холодного пара." },
            { "Flip lid side", "Сменить сторону крышки" },
            { "The lid normally opens away from you. Enable this if it opens towards you instead.",
              "Обычно крышка открывается от вас. Включите, если она открывается к вам." },
            { "Degrees per rotation step", "Градусов за шаг поворота" },
            { "Craftable at the workbench", "Крафт на верстаке" },
            { "Adds the Snow Cooler recipe (10 cedar wood, 5 scrap metal, 5 tarps, quality tools). Takes effect on the next game start.",
              "Добавляет рецепт Снежного холодильника (10 кедровых поленьев, 5 кусков металла, 5 брезентов, качественные инструменты). Вступает в силу при следующем запуске игры." },
            { "Crafting time (minutes)", "Время крафта (минуты)" },

            // ---- action menu ----
            { "Cover with Snow", "Засыпать снег" },
            { "Food Storage", "Хранилище" },
            { "Collect Water", "Вода" },
            { "Move / Remove", "Переместить" },
            { "Close Lid", "Закрыть" },
            { "Move", "Переместить" },
            { "Remove", "Убрать" },

            // ---- hover text, progress bar ----
            { "Snow", "Снег" },
            { "No snow", "Нет снега" },
            { "Hold to open", "Удерживайте, чтобы открыть" },
            { "Opening the cooler", "Открываем холодильник" },
            { "Place here", "Поставить здесь" },
            { "Cannot place here: {0}", "Здесь нельзя поставить: {0}" },
            { "no surface in range", "нет поверхности рядом" },
            { "surface too steep", "поверхность слишком наклонная" },
            { "blocked by {0}", "мешает: {0}" },

            // ---- messages ----
            { "The cooler only stores food.", "В холодильнике можно хранить только еду." },
            { "Snow Cooler added to your inventory.", "Снежный холодильник добавлен в инвентарь." },
            { "Collect the water from the previous load first.", "Сначала соберите воду от прошлой загрузки." },
            { "The cooler still has snow.", "В холодильнике ещё есть снег." },
            { "You loaded snow into the cooler.", "Вы засыпали снег в холодильник." },
            { "The snow is still melting.", "Снег ещё тает." },
            { "There is no water to collect.", "Нет воды, которую можно собрать." },
            { "You cannot carry more water right now.", "Вы не можете нести больше воды." },
            { "You collected {0} L of non-potable water.", "Вы набрали {0} л непитьевой воды." },
            { "Empty the cooler before removing it.", "Освободите холодильник, прежде чем убрать его." },
            { "The snow and water left inside are lost.", "Снег и вода внутри пропадут." },
            { "The cooler is back in your inventory.", "Холодильник вернулся в ваш инвентарь." },
            { "You removed the cooler.", "Вы убрали холодильник." },
            { "Click: place   Right click: cancel   Wheel / Q E: rotate", "Клик: поставить   ПКМ: отмена   Колесо / Q E: повернуть" },
            { "Cooler placed.", "Холодильник установлен." },
            { "Placement cancelled.", "Установка отменена." },

            // ---- item ----
            { "A cooler box that is filled with snow to keep food cold for days. Place it, load it with snow, then store food inside.",
              "Ящик-холодильник, который засыпают снегом, чтобы еда оставалась холодной несколько дней. Поставьте его, засыпьте снегом и храните еду внутри." },
        };

        internal static bool Russian
        {
            get { var s = ColdBoxSettings.Instance; return s != null && s.language == ColdBoxSettings.UiLanguage.Russian; }
        }

        /// <summary>Translates a fixed English text (returns it unchanged for English or when there is no translation).</summary>
        internal static string T(string en)
        {
            if (en == null || !Russian) return en;
            return ru.TryGetValue(en, out var r) ? r : en;
        }

        /// <summary>Translates a text with {0}-style placeholders and fills them in.</summary>
        internal static string F(string enTemplate, params object[] args)
        {
            try { return string.Format(T(enTemplate), args); } catch (FormatException) { return enTemplate; }
        }

        /// <summary>Display text of an action menu entry (keys are the English labels, "Collect Water 2.4 L" carries the amount).</summary>
        internal static string Entry(string key)
        {
            const string water = "Collect Water";
            if (key != null && key.StartsWith(water + " "))
                return T(water) + key.Substring(water.Length).Replace(" L", Russian ? " л" : " L");
            return T(key);
        }

        // ---------------- Mod Settings labels ----------------

        static readonly List<UILabel> tracked = new List<UILabel>();
        static readonly HashSet<int> trackedIds = new HashSet<int>();
        static Dictionary<string, string> ruUpper, enUpper;   // lookups by upper-cased text (labels may be shown in capitals)
        static float nextScan, restoreUntil;
        static bool wasRussian, logged;

        static void EnsureTables()
        {
            if (ruUpper != null) return;
            ruUpper = new Dictionary<string, string>();
            enUpper = new Dictionary<string, string>();
            foreach (var kv in ru)
            {
                ruUpper[kv.Key.ToUpperInvariant()] = kv.Value;       // English text -> Russian
                enUpper[kv.Value.ToUpperInvariant()] = kv.Key;       // Russian text -> English
            }
        }

        /// <summary>Finds the translation of a label text, ignoring case; keeps all-capitals labels in capitals.</summary>
        static bool Lookup(Dictionary<string, string> map, string text, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(text)) return false;
            string up = text.ToUpperInvariant();
            if (!map.TryGetValue(up, out var v)) return false;
            bool allCaps = text == up && text != text.ToLowerInvariant();
            result = allCaps ? v.ToUpperInvariant() : v;
            return true;
        }

        /// <summary>Re-applies every text that is stored once instead of being translated when shown.</summary>
        static void ApplyToGame()
        {
            ColdBoxItem.RefreshTexts();
            foreach (var box in ColdBoxManager.Boxes)
                if (box != null) ColdBoxContainer.RefreshTitle(box.GetContainer());
        }

        /// <summary>Call every frame. Does nothing at all while English was never left.</summary>
        internal static void Update()
        {
            bool isRu = Russian;
            if (isRu != wasRussian)
            {
                wasRussian = isRu;
                restoreUntil = Time.unscaledTime + 4f;   // keep fixing labels for a moment after the switch
                nextScan = 0f;
                logged = false;
                ApplyToGame();
            }
            bool restoring = !isRu && Time.unscaledTime <= restoreUntil;
            if (!isRu && !restoring)
            {
                if (tracked.Count > 0) { tracked.Clear(); trackedIds.Clear(); }
                return;
            }

            EnsureTables();
            var map = isRu ? ruUpper : enUpper;

            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var l = tracked[i];
                if (l == null) { tracked.RemoveAt(i); continue; }
                string t = l.text;
                if (Lookup(map, t, out var v) && v != t) l.text = v;
            }

            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            var seen = logged ? null : new List<string>();
            foreach (var l in UnityEngine.Object.FindObjectsOfType<UILabel>())
            {
                if (l == null) continue;
                string t = l.text;
                if (string.IsNullOrEmpty(t)) continue;
                if (seen != null && seen.Count < 12 && t.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0) seen.Add(t);
                if (!Lookup(ruUpper, t, out _) && !Lookup(enUpper, t, out _)) continue;
                if (!trackedIds.Add(l.GetInstanceID())) continue;
                tracked.Add(l);
            }
            if (seen != null && (seen.Count > 0 || tracked.Count > 0))
            {
                logged = true;
                MelonLoader.Melon<Core>.Logger.Msg("Language labels: tracking " + tracked.Count + "; labels with 'snow': " + string.Join(" | ", seen));
            }
        }
    }
}
