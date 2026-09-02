using UnityEngine;
using UnityEngine.Rendering;

namespace Recon
{
    /// <summary>
    /// Unlit materials created in code, so the ballistics and spatter overlays need no prefab and no
    /// asset in the repo. Two reasons that matters here: the physics overlay has to work in a scene
    /// built by script (Recon/Bootstrap ReconAR scene), and one person owns a scene or prefab file at
    /// a time, so anything created at runtime is one less merge conflict.
    ///
    /// Unlit is not a style choice: an AR overlay drawn over the camera feed has no lighting rig, and
    /// a lit shader would make the arc and the stains change colour with the URP light setup.
    /// </summary>
    public static class RuntimeMaterials
    {
        /// <summary>URP first, then the built-in fallbacks, so this survives a pipeline change.</summary>
        static Shader FindUnlitShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null) Debug.LogError("[Recon] No unlit shader found; the physics overlay will be invisible.");
            return shader;
        }

        /// <summary>An opaque unlit material, e.g. the trajectory line and the impact marker.</summary>
        public static Material CreateUnlit(Color colour, string name)
        {
            var shader = FindUnlitShader();
            if (shader == null) return null;
            var material = new Material(shader) { name = name };
            SetColour(material, colour);
            return material;
        }

        /// <summary>
        /// A transparent unlit material for the spatter decals. Configured the same way
        /// Editor/SceneBootstrap configures the AR plane material, so the two look consistent.
        /// </summary>
        public static Material CreateUnlitTransparent(Color colour, string name)
        {
            var material = CreateUnlit(colour, name);
            if (material == null) return null;

            material.SetFloat("_Surface", 1f);          // transparent
            material.SetFloat("_Blend", 0f);            // alpha
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            // Double sided: a stain seen from the far side of a thin AR plane should still be there.
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            SetColour(material, colour);
            return material;
        }

        static void SetColour(Material material, Color colour)
        {
            // URP uses _BaseColor, the built-in unlit shaders use _Color. Set whichever exists.
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        }

        /// <summary>
        /// A flat quad in the XY plane facing +Z, 1 x 1, centred on the origin: the spatter decal.
        /// Built here rather than with GameObject.CreatePrimitive, which would also attach a
        /// MeshCollider that the ballistics raycast would then hit.
        /// </summary>
        public static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "ReconUnitQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            mesh.SetNormals(new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
