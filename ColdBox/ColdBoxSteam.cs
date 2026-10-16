using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColdBox
{
    /// <summary>
    /// Cold mist that spills over the rim when the lid is opened while the cooler still has snow.
    /// It is NOT a Unity particle system (the game's stripped assemblies lack much of that API). Instead a handful of
    /// soft, cloud-shaped billboards are animated from code: each one appears over the opening, drifts outwards and
    /// sinks (cold air is heavy), grows and fades away. The cloud texture is generated at runtime.
    /// </summary>
    internal static class ColdBoxSteam
    {
        const int MaxPuffs = 7;
        const float PuffLife = 3.2f;
        const float PeakAlpha = 0.65f;      // the hand-painted textures are already translucent
        const float LidLength = 0.9f;       // how far the open lid reaches from its hinge

        sealed class Puff
        {
            public GameObject go;
            public Transform t;
            public Material mat;
            public float age = -1f;          // < 0 = idle
            public Vector3 vel;
            public float size, roll;
        }

        sealed class Mist
        {
            public Transform host;
            public readonly List<Puff> puffs = new List<Puff>();
            public float spawnTimer;
            public float hingeSide = 1f;
            public Vector3 hinge;   // hinge position, cooler-local
        }

        static readonly Dictionary<int, Mist> mists = new Dictionary<int, Mist>();
        static Material baseMat;
        static Texture2D[] textures;
        static bool matFailed;

        // ---------------- texture and material ----------------

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Wispy cloud: fractal noise inside a soft round falloff.</summary>
        static Texture2D MakeCloud()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.Clamp01(1f - r);
                    falloff = falloff * falloff * (3f - 2f * falloff);

                    float f = 0f, amp = 0.55f, freq = 3f;
                    for (int o = 0; o < 4; o++) { f += amp * ValueNoise(u * freq + 7.3f * o, v * freq + 2.1f * o); amp *= 0.5f; freq *= 2f; }
                    float cloud = Mathf.Clamp01((f - 0.25f) * 1.9f);
                    float a = falloff * cloud;
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels((Il2CppStructArray<Color>)px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        static Material GetMaterial()
        {
            if (baseMat != null) return baseMat;
            if (matFailed) return null;
            try
            {
                Shader sh = null;
                foreach (var n in new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Sprites/Default", "Particles/Standard Unlit" })
                {
                    sh = Shader.Find(n);
                    if (sh != null) { Melon<Core>.Logger.Msg("Mist shader: " + n); break; }
                }
                if (sh == null)
                {
                    // borrow the shader of any particle already in the game
                    foreach (var r in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>())
                        if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null) { sh = r.sharedMaterial.shader; break; }
                }
                if (sh == null) throw new InvalidOperationException("no usable transparent shader");
                baseMat = new Material(sh) { name = "ColdBox_Mist" };
                var list = new List<Texture2D>();
                for (int i = 1; i <= 3; i++)
                {
                    var t = ColdBoxIcon.LoadEmbedded("ColdBox.mist" + i + ".png");
                    if (t != null) list.Add(t);
                }
                if (list.Count == 0) list.Add(MakeCloud());   // fallback: generated cloud
                textures = list.ToArray();
                baseMat.mainTexture = textures[0];
                baseMat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                return baseMat;
            }
            catch (Exception e)
            {
                matFailed = true;
                Melon<Core>.Logger.Warning("Mist disabled: " + e.Message);
                return null;
            }
        }

        static void SetAlpha(Material m, float a)
        {
            // "Particles/Alpha Blended" doubles _TintColor, the others use _Color
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.45f, 0.5f, 0.55f, a * 0.5f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.9f, 0.96f, 1f, a));
        }

        // ---------------- puffs ----------------

        static Puff NewPuff()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ColdBox_MistPuff";
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            var rend = go.GetComponent<MeshRenderer>();
            var mat = new Material(GetMaterial());
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            go.SetActive(false);
            return new Puff { go = go, t = go.transform, mat = mat };
        }

        static void Launch(Mist mist, Puff p)
        {
            // random spot over the opening (local to the cooler), starting just above the rim
            // The lid stands over the hinge side, so the mist starts on the free half and spills over the front edge.
            float hs = mist.hingeSide;
            var local = new Vector3(-hs * UnityEngine.Random.Range(-0.05f, 0.28f), 0.62f, UnityEngine.Random.Range(-0.5f, 0.45f));
            p.t.position = mist.host.TransformPoint(local);

            Vector3 side = mist.host.right * -hs;
            p.vel = side * UnityEngine.Random.Range(0.08f, 0.15f) + Vector3.up * UnityEngine.Random.Range(0.03f, 0.07f)
                    + mist.host.forward * UnityEngine.Random.Range(-0.04f, 0.04f);
            p.size = UnityEngine.Random.Range(0.4f, 0.6f);
            p.mat.mainTexture = textures[UnityEngine.Random.Range(0, textures.Length)];
            p.roll = UnityEngine.Random.Range(0f, 360f);
            p.age = 0f;
            p.t.localScale = new Vector3(p.size, p.size, 1f);
            SetAlpha(p.mat, 0f);
            p.go.SetActive(true);
        }

        static bool Spawn(Mist mist)
        {
            foreach (var p in mist.puffs)
                if (p.age < 0f) { Launch(mist, p); return true; }
            if (mist.puffs.Count >= MaxPuffs) return false;
            var np = NewPuff();
            mist.puffs.Add(np);
            Launch(mist, np);
            return true;
        }

        /// <summary>Call every frame from the cooler. <paramref name="on"/> = lid open with snow; <paramref name="burst"/> = it just opened.</summary>
        internal static void Tick(GameObject host, Transform hinge, float lidAngle, bool on, bool burst)
        {
            try
            {
                if (host == null) return;
                int id = host.GetInstanceID();
                mists.TryGetValue(id, out var mist);
                if (mist == null)
                {
                    if (!on) return;
                    if (GetMaterial() == null) return;
                    mist = new Mist { host = host.transform };
                    mists[id] = mist;
                }

                if (hinge != null)
                {
                    mist.hinge = hinge.localPosition;
                    mist.hingeSide = mist.hinge.x >= 0f ? 1f : -1f;
                }
                float ang = Mathf.Abs(lidAngle) * Mathf.Deg2Rad;
                float sinA = Mathf.Sin(ang), cosA = Mathf.Cos(ang);

                float dt = Time.deltaTime;
                if (on)
                {
                    if (burst) { for (int i = 0; i < 3; i++) Spawn(mist); mist.spawnTimer = 0.5f; }
                    mist.spawnTimer -= dt;
                    if (mist.spawnTimer <= 0f)
                    {
                        Spawn(mist);
                        mist.spawnTimer = UnityEngine.Random.Range(0.55f, 1.0f);
                    }
                }

                var cam = GameBridge.PlayerCamera;
                Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
                foreach (var p in mist.puffs)
                {
                    if (p.age < 0f) continue;
                    p.age += dt;
                    float k = p.age / PuffLife;
                    if (k >= 1f) { p.age = -1f; p.go.SetActive(false); continue; }

                    p.vel += Vector3.down * (0.06f * dt);            // sinks more and more
                    p.vel *= 1f - 0.35f * dt;                        // slows down in the air
                    p.t.position += p.vel * dt;
                    KeepBelowLid(mist, p, sinA, cosA);

                    float grow = p.size * (1f + 1.1f * k);
                    p.t.localScale = new Vector3(grow, grow, 1f);
                    p.t.rotation = face * Quaternion.Euler(0f, 0f, p.roll + 12f * p.age);

                    float fadeIn = Mathf.Clamp01(k / 0.2f);
                    float fadeOut = 1f - Mathf.Clamp01((k - 0.35f) / 0.65f);
                    SetAlpha(p.mat, PeakAlpha * fadeIn * fadeIn * fadeOut);
                }
            }
            catch (Exception e)
            {
                Melon<Core>.Logger.Warning("Mist update failed: " + e.Message);
            }
        }

        /// <summary>
        /// The open lid is a flat panel hinged on one rim. Mist is never allowed to cross it: a puff that ends up on the
        /// wrong side of the lid's plane (while within the lid's extent) is pushed back to the underside and loses the
        /// velocity that pointed into the lid. Works in cooler-local space mirrored so the hinge is always at +u.
        /// </summary>
        static void KeepBelowLid(Mist mist, Puff p, float sinA, float cosA)
        {
            if (sinA < 0.05f) return;   // lid (almost) closed: nothing above the opening to collide with
            var host = mist.host;
            Vector3 lp = host.InverseTransformPoint(p.t.position);
            float hs = mist.hingeSide;
            float u = hs * (lp.x - mist.hinge.x), y = lp.y - mist.hinge.y;   // relative to the hinge, mirrored

            // lid direction in (u,y) is (-cos, sin); the underside normal is (-sin, -cos)
            float along = -cosA * u + sinA * y;
            if (along < -0.05f || along > LidLength) return;                 // beside the lid: free
            float margin = 0.06f + p.size * 0.35f;                           // keep the whole billboard clear of the panel
            float dist = -sinA * u - cosA * y;                               // > 0 = under the lid
            if (dist >= margin) return;

            float push = margin - dist;
            u += -sinA * push; y += -cosA * push;
            lp.x = mist.hinge.x + hs * u; lp.y = mist.hinge.y + y;
            p.t.position = host.TransformPoint(lp);

            // drop the velocity component pointing into the lid
            Vector3 v = host.InverseTransformDirection(p.vel);
            float vu = hs * v.x, vy = v.y;
            float into = -(-sinA * vu - cosA * vy);                          // > 0 = moving towards / through the lid
            if (into > 0f) { vu -= -sinA * -into; vy -= -cosA * -into; }
            v.x = hs * vu; v.y = vy;
            p.vel = host.TransformDirection(v);
        }

        /// <summary>Removes the puffs of coolers that no longer exist. Called from the manager's update.</summary>
        internal static void Sweep()
        {
            if (mists.Count == 0) return;
            List<int> dead = null;
            foreach (var kv in mists)
                if (kv.Value.host == null) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead == null) return;
            foreach (var id in dead)
            {
                foreach (var p in mists[id].puffs) if (p.go != null) UnityEngine.Object.Destroy(p.go);
                mists.Remove(id);
            }
        }
    }
}
