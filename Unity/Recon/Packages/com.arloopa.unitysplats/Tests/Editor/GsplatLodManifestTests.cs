// SPDX-License-Identifier: MIT

#if false // Temporarily disabled with lod-meta.json support.

using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public class GsplatLodManifestTests
    {
        [Test]
        public void ParsesPlayCanvasPlaybotFixture()
        {
            string path = Path.GetFullPath(
                "Packages/com.arloopa.unitysplats/Tests/Editor/Fixtures/playbot-lod-meta.json");
            GsplatLodManifest manifest = GsplatLodManifest.Parse(File.ReadAllText(path));

            Assert.That(manifest.LodLevels, Is.EqualTo(7));
            Assert.That(manifest.Filenames, Has.Length.EqualTo(7));
            Assert.That(manifest.Leaves, Has.Length.EqualTo(2));
            Assert.That(manifest.Leaves[0].Lods[0].Offset, Is.EqualTo(0));
            Assert.That(manifest.Leaves[0].Lods[0].Count, Is.EqualTo(248155));
            Assert.That(manifest.Leaves[1].Lods[0].Offset, Is.EqualTo(248155));
            Assert.That(manifest.Leaves[1].Lods[0].EndExclusive, Is.EqualTo(500000));
            Assert.That(manifest.RequiredSplatCounts[0], Is.EqualTo(500000));
            Assert.That(manifest.RequiredSplatCounts[2], Is.EqualTo(250000));

            // Default RUB -> Unity RUF flips Z and reorders the bound endpoints.
            Assert.That(manifest.Bounds.min.z, Is.EqualTo(-1.042389f).Within(1e-6f));
            Assert.That(manifest.Bounds.max.z, Is.EqualTo(1.039343f).Within(1e-6f));
            Assert.DoesNotThrow(() => manifest.ValidateFileSplatCount(0, 500000));
            Assert.Throws<FormatException>(() => manifest.ValidateFileSplatCount(0, 499999));
        }

        [Test]
        public void AllowsDuplicateFilenamesAndNormalizesOverlappingActiveRanges()
        {
            const string json = "{\"lodLevels\":1,\"filenames\":[\"same.sog\",\"same.sog\"]," +
                                "\"tree\":{\"bound\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"children\":[" +
                                "{\"bound\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"lods\":{\"0\":{\"file\":0,\"offset\":2,\"count\":5}}}," +
                                "{\"bound\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"lods\":{\"0\":{\"file\":0,\"offset\":5,\"count\":4}}}]}}";
            Assert.DoesNotThrow(() => GsplatLodManifest.Parse(json));

            GsplatActiveRange[] normalized = GsplatLodRangeNormalizer.Normalize(new[]
            {
                new GsplatActiveRange(5, 4),
                new GsplatActiveRange(2, 5),
                new GsplatActiveRange(9, 1),
            }, 10);
            Assert.That(normalized, Has.Length.EqualTo(1));
            Assert.That(normalized[0], Is.EqualTo(new GsplatActiveRange(2, 8)));
        }

        [TestCase("{\"lodLevels\":0,\"filenames\":[\"a.sog\"],\"tree\":{}}")]
        [TestCase("{\"lodLevels\":1,\"filenames\":[],\"tree\":{}}")]
        [TestCase("{\"lodLevels\":1,\"filenames\":[\"a.sog\"],\"tree\":{\"bound\":{\"min\":[1,0,0],\"max\":[0,1,1]},\"lods\":{\"0\":{\"file\":0}}}}")]
        [TestCase("{\"lodLevels\":1,\"filenames\":[\"a.sog\"],\"tree\":{\"bound\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"lods\":{\"0\":{\"file\":2}}}}")]
        [TestCase("{\"lodLevels\":1,\"filenames\":[\"a.sog\"],\"tree\":{\"bound\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"lods\":{\"0\":{\"file\":0,\"offset\":2147483647,\"count\":1}}}}")]
        [TestCase("{\"lodLevels\":1,\"filenames\":[\"a.sog\"],\"tree\":{\"bound\":{\"min\":[0,0,0],\"max\":[1,1,1]},\"children\":[]}}")]
        public void RejectsInvalidManifest(string json)
        {
            Assert.Throws<FormatException>(() => GsplatLodManifest.Parse(json));
        }

        [Test]
        public void MatchesPlayCanvasDistanceBandsAndBehindPenalty()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            int near = GsplatLodSelector.SelectLod(bounds, new Vector3(0, 0, -4), Vector3.forward,
                45f, 1f, 5f, 3f, 6, 0, 6);
            int firstCoarse = GsplatLodSelector.SelectLod(bounds, new Vector3(0, 0, -10), Vector3.forward,
                45f, 1f, 5f, 3f, 6, 0, 6);
            int secondCoarse = GsplatLodSelector.SelectLod(bounds, new Vector3(0, 0, -16), Vector3.forward,
                45f, 1f, 5f, 3f, 6, 0, 6);
            int behind = GsplatLodSelector.SelectLod(bounds, new Vector3(0, 0, -10), Vector3.back,
                45f, 1f, 5f, 3f, 6, 0, 6, 3f);

            Assert.That(near, Is.EqualTo(0));
            Assert.That(firstCoarse, Is.EqualTo(1));
            Assert.That(secondCoarse, Is.EqualTo(2));
            Assert.That(behind, Is.EqualTo(2));
        }

        [Test]
        public void ResolvesHttpResourcesAndQueryExtensions()
        {
            Assert.That(GsplatLodUri.Resolve("https://example.test/splats/scene/", "../0/meta.json"),
                Is.EqualTo("https://example.test/splats/0/meta.json"));
            Assert.That(GsplatLodUri.Resolve("https://example.test/splats/scene/", "/assets/x.sog"),
                Is.EqualTo("https://example.test/assets/x.sog"));
            Assert.That(GsplatLodUri.Resolve("https://example.test/splats/scene/", "//cdn.test/x.sog"),
                Is.EqualTo("https://cdn.test/x.sog"));
            Assert.That(GsplatLodUri.ExtensionOf("https://example.test/file.spz?v=2"), Is.EqualTo(".spz"));
            Assert.That(GsplatLodUri.IsUnpackedSogMetadata("https://example.test/0/meta.json?v=2"), Is.True);
        }

        [Test]
        public void ExpandsExplicitStreamingAssetsManifestScheme()
        {
            string expected = GsplatLodUri.Resolve(Application.streamingAssetsPath, "playbot/lod-meta.json");
            Assert.That(GsplatLodUri.ResolveManifestUri("streaming-assets://playbot/lod-meta.json"),
                Is.EqualTo(expected));
        }
    }
}
#endif
