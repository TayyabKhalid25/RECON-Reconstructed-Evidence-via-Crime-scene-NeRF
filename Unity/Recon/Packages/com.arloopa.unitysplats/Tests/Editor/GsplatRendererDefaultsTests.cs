// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public class GsplatRendererDefaultsTests
    {
        [Test]
        public void NewRendererEnablesGammaToLinear()
        {
            var gameObject = new GameObject("GsplatRenderer defaults test");
            try
            {
                var renderer = gameObject.AddComponent<GsplatRenderer>();
                Assert.IsTrue(renderer.GammaToLinear);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ProxyChildRelightingUsesPlayCanvasCompensationDefaults()
        {
            var splatObject = new GameObject("GsplatRenderer relighting defaults test");
            var proxyObject = new GameObject("Gsplat relighting proxy defaults test");
            proxyObject.SetActive(false);
            try
            {
                splatObject.AddComponent<GsplatRenderer>();
                proxyObject.transform.SetParent(splatObject.transform);
                var relighting = proxyObject.AddComponent<GsplatProxyRelighting>();

                Assert.AreEqual(0.5f, relighting.TextureScale);
                Assert.AreEqual(1f, relighting.Blend);
                Assert.AreEqual(2f, relighting.Brightness);
                Assert.AreEqual(1f, relighting.Background);
                Assert.IsNull(typeof(GsplatRenderer).GetField("RelightingEnabled"));
                Assert.IsNull(typeof(GsplatRenderer).GetField("RelightingProxy"));
                Assert.IsNull(typeof(GsplatRenderer).GetField("RelightingSource"));
            }
            finally
            {
                Object.DestroyImmediate(splatObject);
            }
        }

        [Test]
        public void RelightingUsesNormalCameraRenderingUnlessExplicitlyOnDemand()
        {
            var cameraObject = new GameObject("GsplatRelighting camera scheduling test");
            try
            {
                cameraObject.AddComponent<Camera>();
                var relighting = cameraObject.AddComponent<GsplatRelighting>();
                relighting.ProxyLayers = 1 << 30;
                relighting.Refresh();

                var field = typeof(GsplatRelighting).GetField(
                    "m_proxyCamera",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(field);
                var proxyCamera = field.GetValue(relighting) as Camera;
                Assert.IsNotNull(proxyCamera);
                Assert.IsTrue(proxyCamera.enabled,
                    "Game-camera proxy rendering must use Unity's normal camera loop.");

                relighting.RenderOnDemand = true;
                relighting.Refresh();
                Assert.IsFalse(proxyCamera.enabled,
                    "Only explicit on-demand users such as Scene view should disable scheduling.");
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RendererDiscoversRelightingOnProxyChild()
        {
            var cameraObject = new GameObject("Gsplat shared relighting camera test");
            var splatObject = new GameObject("Gsplat proxy discovery test");
            var proxyObject = new GameObject("Gsplat proxy child test");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                GsplatRelighting source = cameraObject.AddComponent<GsplatRelighting>();
                source.ProxyLayers = 1 << 30;
                source.Refresh();

                GsplatRenderer renderer = splatObject.AddComponent<GsplatRenderer>();
                proxyObject.transform.SetParent(splatObject.transform);
                proxyObject.layer = 30;
                GsplatProxyRelighting proxy = proxyObject.AddComponent<GsplatProxyRelighting>();
                proxy.SourceCamera = camera;
                proxy.Refresh();

                Assert.IsTrue(renderer.TryGetActiveRelighting(out GsplatRelighting resolved));
                Assert.AreSame(source, resolved);
            }
            finally
            {
                Object.DestroyImmediate(splatObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void BoxColliderFitsAssetLocalBounds()
        {
            var asset = ScriptableObject.CreateInstance<GsplatAssetSpark>();
            var gameObject = new GameObject("GsplatRenderer collider test");
            try
            {
                asset.Bounds = new Bounds(
                    new Vector3(1.5f, -2f, 3.25f),
                    new Vector3(8f, 4.5f, 2f));
                var renderer = gameObject.AddComponent<GsplatRenderer>();
                var boxCollider = gameObject.AddComponent<BoxCollider>();
                renderer.GsplatAsset = asset;

                Assert.IsTrue(renderer.FitBoxColliderToAssetBounds());
                Assert.AreEqual(asset.Bounds.center, boxCollider.center);
                Assert.AreEqual(asset.Bounds.size, boxCollider.size);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(asset);
            }
        }
    }
}
