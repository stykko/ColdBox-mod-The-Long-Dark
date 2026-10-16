using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace ColdBox
{
    /// <summary>
    /// Builds the cooler at runtime from the mesh and texture embedded in the DLL
    /// (Resources\parts.bin, meta.txt, texture.jpg). No Unity project or AssetBundles needed.
    /// </summary>
    internal static class ColdBoxBuilder
    {
        // Lid rotation direction (computed by the exporter from the side of the latch)
        internal static float OpenSign = -1f;

        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static Dictionary<string, float> meta;
        static Material bodyMat, snowMat, tarpSnowMat;

        /// <summary>Tarp texture without snow / with snow inside (the behaviour swaps them).</summary>
        internal static Material TarpMaterial => bodyMat;
        internal static Material TarpSnowMaterial => tarpSnowMat;
        static bool loaded;

        // ---------------- embedded resources ----------------
        static byte[] ReadResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("Embedded resource not found: " + name);
                using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
        }

        static bool Load()
        {
            if (loaded) return true;
            try
            {
                // meta.txt: key=value
                meta = new Dictionary<string, float>();
                foreach (var line in System.Text.Encoding.UTF8.GetString(ReadResource("ColdBox.meta.txt")).Split('\n'))
                {
                    var kv = line.Trim().Split('=');
                    if (kv.Length == 2 && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) meta[kv[0]] = v;
                }
                OpenSign = meta.ContainsKey("openSign") ? meta["openSign"] : -1f;

                // parts.bin: see tools/export_parts.py
                using (var br = new BinaryReader(new MemoryStream(ReadResource("ColdBox.parts.bin"))))
                {
                    if (br.ReadInt32() != 0x314D4243) throw new InvalidDataException("parts.bin: invalid signature");
                    int n = br.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));
                        int nv = br.ReadInt32(), ni = br.ReadInt32();
                        var pos = new Vector3[nv]; var nrm = new Vector3[nv]; var uv = new Vector2[nv]; var idx = new int[ni];
                        for (int k = 0; k < nv; k++) pos[k] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        for (int k = 0; k < nv; k++) nrm[k] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        for (int k = 0; k < nv; k++) uv[k] = new Vector2(br.ReadSingle(), br.ReadSingle());
                        for (int k = 0; k < ni; k++) idx[k] = br.ReadInt32();

                        var mesh = new Mesh { name = "ColdBox_" + name };
                        if (nv > 65000) mesh.indexFormat = IndexFormat.UInt32;
                        mesh.vertices = (Il2CppStructArray<Vector3>)pos;
                        mesh.normals = (Il2CppStructArray<Vector3>)nrm;
                        mesh.uv = (Il2CppStructArray<Vector2>)uv;
                        mesh.triangles = (Il2CppStructArray<int>)idx;
                        mesh.RecalculateBounds();
                        mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        meshes[name] = mesh;
                    }
                }

                // original texture
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)ReadResource("ColdBox.texture.jpg")))
                    throw new InvalidDataException("Could not decode texture.jpg");
                tex.filterMode = FilterMode.Trilinear;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                var texSnow = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(texSnow, (Il2CppStructArray<byte>)ReadResource("ColdBox.texture_snow.jpg")))
                    throw new InvalidDataException("Could not decode texture_snow.jpg");
                texSnow.filterMode = FilterMode.Trilinear;
                texSnow.wrapMode = TextureWrapMode.Repeat;
                texSnow.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                bodyMat = MakeMaterial(tex, Color.white, "ColdBox_Body");
                tarpSnowMat = MakeMaterial(texSnow, Color.white, "ColdBox_TarpSnow");
                snowMat = MakeMaterial(tex, Color.white, "ColdBox_Snow", 0.25f);   // snow slab: the light patch of the texture, slightly self-lit
                loaded = true;
                Melon<Core>.Logger.Msg($"Resources loaded: {meshes.Count} meshes, texture {tex.width}x{tex.height}.");
                return true;
            }
            catch (Exception e)
            {
                Melon<Core>.Logger.Error("Could not load the embedded resources: " + e);
                return false;
            }
        }

        // ---------------- material ----------------
        // Try the engine's Standard shader; if the game stripped it from the build, borrow the shader of an already loaded material.
        static Shader FindShader()
        {
            foreach (var n in new[] { "Standard", "Legacy Shaders/Diffuse", "Unlit/Texture" })
            {
                var s = Shader.Find(n);
                if (s != null) { Melon<Core>.Logger.Msg("Shader: " + n); return s; }
            }
            Shader fallback = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.shader == null) continue;
                string sn = m.shader.name ?? "";
                if (sn.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0) { Melon<Core>.Logger.Msg("Shader taken from a game material: " + sn); return m.shader; }
                if (fallback == null && sn.IndexOf("Diffuse", StringComparison.OrdinalIgnoreCase) >= 0) fallback = m.shader;
            }
            if (fallback != null) Melon<Core>.Logger.Msg("Shader (fallback): " + fallback.name);
            else Melon<Core>.Logger.Warning("No usable shader found: the cooler will look pink.");
            return fallback ?? Shader.Find("Hidden/InternalErrorShader");
        }

        static float Meta(string key) { return meta.ContainsKey(key) ? meta[key] : 0f; }

        static Material MakeMaterial(Texture2D tex, Color color, string name, float emission = 0f)
        {
            var m = new Material(FindShader()) { name = name };
            m.color = color;
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.05f);
            if (emission > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * emission);   // keeps the snow readable in dark interiors
            }
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return m;
        }

        // ---------------- construction ----------------
        static GameObject Part(string name, Transform parent, Material mat, Vector3 localPos, string meshKey = null)
        {
            if (!meshes.ContainsKey(meshKey ?? name))
            {
                Melon<Core>.Logger.Warning("Model part missing in parts.bin (skipped): " + (meshKey ?? name));
                return null;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = meshes[meshKey ?? name];
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Static, scaled-down copy of the cooler used as the visual of the inventory / world item.</summary>
        internal static bool AttachItemVisual(Transform parent, float scale)
        {
            if (!Load()) return false;
            var holder = new GameObject("ColdBoxItemVisual");
            holder.transform.SetParent(parent, false);
            holder.transform.localScale = Vector3.one * scale;
            Part("Shell", holder.transform, bodyMat, Vector3.zero);
            Part("Tarp", holder.transform, bodyMat, Vector3.zero);
            Part("Snow", holder.transform, snowMat, Vector3.zero);
            Part("Jugs", holder.transform, bodyMat, Vector3.zero);
            Part("Lid", holder.transform, bodyMat, Vector3.zero);
            Part("Latch", holder.transform, bodyMat, Vector3.zero);
            return true;
        }

        /// <summary>Collider size / center (root space, unscaled) for the item.</summary>
        internal static void GetColliderBounds(out Vector3 size, out Vector3 center)
        {
            Load();
            size = new Vector3(Meta("colSizeX"), Meta("colSizeY"), Meta("colSizeZ"));
            center = new Vector3(Meta("colCenterX"), Meta("colCenterY"), Meta("colCenterZ"));
        }

        /// <summary>Returns the INACTIVE root. Hierarchy: root(BoxCollider) / Body(Shell, Snow, Jugs) / LidHinge(Lid) / Latch / Contents.</summary>
        internal static GameObject Build(Vector3 pos, Quaternion rot)
        {
            if (!Load()) return null;

            // Stays INACTIVE: the manager adds the game Container and only then activates it, so Awake already sees the final setup.
            var root = new GameObject("ColdBox_Instance");
            root.SetActive(false);
            root.transform.SetPositionAndRotation(pos, rot);

            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
            Part("Shell", body.transform, bodyMat, Vector3.zero);
            Part("Tarp", body.transform, bodyMat, Vector3.zero);
            Part("Snow", body.transform, snowMat, Vector3.zero);
            Part("Jugs", body.transform, bodyMat, Vector3.zero);

            // Meshes are baked in root space; the lid pivot sits on the hinge and the mesh is offset to compensate.
            var hingePos = new Vector3(meta["hingeX"], meta["hingeY"], meta["hingeZ"]);
            var hinge = new GameObject("LidHinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = hingePos;
            Part("Lid", hinge.transform, bodyMat, -hingePos);

            Part("Latch", root.transform, bodyMat, Vector3.zero);

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(meta["colSizeX"], meta["colSizeY"], meta["colSizeZ"]);
            col.center = new Vector3(Meta("colCenterX"), meta["colCenterY"], Meta("colCenterZ"));

            // parent of the visual copies of the stored items (filled by ColdBoxBehaviour)
            var contents = new GameObject("Contents");
            contents.transform.SetParent(root.transform, false);
            return root;
        }
    }
}
