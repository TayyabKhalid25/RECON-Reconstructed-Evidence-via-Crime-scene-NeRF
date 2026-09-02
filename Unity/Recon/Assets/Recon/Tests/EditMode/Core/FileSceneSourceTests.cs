using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Recon.Api;
using Recon.Contract;

namespace Recon.Tests.Core
{
    /// <summary>
    /// The source that keeps Track C unblocked. No web DB migration had run as of 2026-09-02, so
    /// every step after "fetch a scene" would otherwise be untestable; a folder of scenes pushed
    /// with Unity/push-dev-scene.ps1 exercises the same <see cref="ISceneSource"/> the API
    /// implements, which means SceneLoader has one code path rather than two.
    /// </summary>
    public class FileSceneSourceTests
    {
        string m_root;

        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(Path.GetTempPath(), "recon-file-source-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
        }

        /// <summary>Writes a scene folder. Returns the .ply's sha256 so tests can assert against it.</summary>
        string WriteScene(string sceneId, double unitScale, string handedness = "left", string upAxis = "y",
            bool withMetadata = true, bool withPly = true)
        {
            var folder = Path.Combine(m_root, sceneId);
            Directory.CreateDirectory(folder);

            string sha = null;
            if (withPly)
            {
                var bytes = Encoding.ASCII.GetBytes("ply\nformat binary_little_endian 1.0\nelement vertex 0\nend_header\n" + sceneId);
                File.WriteAllBytes(Path.Combine(folder, "splat_unity.ply"), bytes);
                sha = Recon.Scenes.Sha256Hex.ComputeHex(bytes);
            }

            if (withMetadata)
            {
                var json = "{" +
                    $"\"sceneId\":\"{sceneId}\"," +
                    "\"plyFile\":\"splat_unity.ply\"," +
                    "\"splatCount\":258157,\"sourceFrames\":316," +
                    $"\"handedness\":\"{handedness}\",\"upAxis\":\"{upAxis}\"," +
                    $"\"unitScale\":{unitScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    "\"scaleMethod\":\"marker\"," +
                    "\"boundingBox\":{\"min\":[-15.1,-15.3,-18.9],\"max\":[15.8,4.6,13.1]}," +
                    "\"originHint\":[0,0,0]," +
                    "\"metrics\":{\"psnr\":30.26,\"ssim\":0.946,\"lpips\":0.117}," +
                    "\"training\":{\"iterations\":7000,\"minutes\":2.39,\"peakVramMb\":1349}," +
                    $"\"sha256\":\"{sha ?? Recon.Scenes.Sha256Hex.Placeholder}\"," +
                    "\"createdAt\":\"2026-09-02T16:12:41Z\"}";
                File.WriteAllText(Path.Combine(folder, "metadata.json"), json);
            }

            return sha;
        }

        [Test]
        public void ListReady_ReturnsFoldersThatHaveBothFiles()
        {
            WriteScene("scn_2cold", 0.312834);
            WriteScene("scn_nometa", 0.3, withMetadata: false);
            WriteScene("scn_noply", 0.3, withPly: false);
            Directory.CreateDirectory(Path.Combine(m_root, "empty-folder"));

            var scenes = new FileSceneSource(m_root).ListReadyAsync().GetAwaiter().GetResult();

            Assert.That(scenes, Has.Count.EqualTo(1));
            Assert.That(scenes[0].Id, Is.EqualTo("scn_2cold"));
        }

        [Test]
        public void ListReady_SummaryCarriesTheMetadataUnitScale_AndReadsAsReady()
        {
            WriteScene("scn_2cold", 0.312834);

            var scene = new FileSceneSource(m_root).ListReadyAsync().GetAwaiter().GetResult()[0];

            Assert.That(scene.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
            Assert.That(scene.HasUnitScale, Is.True);
            // A file on disk has no job; presenting it as READY is what lets the picker show it
            // through the same UI as an API scene.
            Assert.That(scene.IsReady, Is.True);
            Assert.That(scene.Job.Status, Is.EqualTo(JobInfo.Ready));
            Assert.That(scene.CreatedAt, Is.EqualTo("2026-09-02T16:12:41Z"));
        }

        [Test]
        public void ListReady_ANonMetricSceneIsStillListed_SoTheValidatorCanRefuseItVisibly()
        {
            // Hiding a unitScale-0 scene from the list would make "why is my scene missing" the
            // symptom. Listing it and refusing it at load time puts the reason on screen.
            WriteScene("scn_nonmetric", 0.0);

            var scenes = new FileSceneSource(m_root).ListReadyAsync().GetAwaiter().GetResult();

            Assert.That(scenes, Has.Count.EqualTo(1));
            Assert.That(scenes[0].UnitScale, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void ListReady_MissingRootFolder_IsEmptyNotAnError()
        {
            // "No scenes pushed yet" is a normal state on a fresh phone.
            var source = new FileSceneSource(Path.Combine(m_root, "does-not-exist"));

            Assert.That(source.ListReadyAsync().GetAwaiter().GetResult(), Is.Empty);
        }

        [Test]
        public void ListReady_UnreadableMetadataIsSkipped_NotFatal()
        {
            WriteScene("scn_good", 0.31);
            var bad = Path.Combine(m_root, "scn_broken");
            Directory.CreateDirectory(bad);
            File.WriteAllText(Path.Combine(bad, "metadata.json"), "{ this is not json");
            File.WriteAllBytes(Path.Combine(bad, "splat_unity.ply"), new byte[] { 1, 2, 3 });

            var scenes = new FileSceneSource(m_root).ListReadyAsync().GetAwaiter().GetResult();

            Assert.That(scenes, Has.Count.EqualTo(1));
            Assert.That(scenes[0].Id, Is.EqualTo("scn_good"));
        }

        [Test]
        public void GetScene_SynthesisesADetailWithARealSplatPlyAsset()
        {
            var sha = WriteScene("scn_2cold", 0.312834);

            var detail = new FileSceneSource(m_root).GetSceneAsync("scn_2cold").GetAwaiter().GetResult();

            Assert.That(detail.Id, Is.EqualTo("scn_2cold"));
            Assert.That(detail.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
            Assert.That(detail.SplatPly, Is.Not.Null);
            Assert.That(detail.SplatPly.Sha256, Is.EqualTo(sha));
            Assert.That(detail.SplatPly.ByteSize, Is.GreaterThan(0));
            Assert.That(detail.AssetUrl, Does.Contain("splat_unity.ply"));
        }

        [Test]
        public void GetScene_UnknownId_ThrowsNamingTheFolderItLookedIn()
        {
            var source = new FileSceneSource(m_root);

            var e = Assert.Throws<SceneSourceException>(() => source.GetSceneAsync("nope").GetAwaiter().GetResult());
            Assert.That(e.Message, Does.Contain("nope"));
            Assert.That(e.Message, Does.Contain(m_root));
        }

        [Test]
        public void GetScene_RejectsAnIdThatTriesToLeaveTheRoot()
        {
            WriteScene("scn_2cold", 0.31);
            var source = new FileSceneSource(m_root);

            // Ids reach here from a UI text field as well as from a listing.
            Assert.Throws<SceneSourceException>(() => source.GetSceneAsync("../..").GetAwaiter().GetResult());
            Assert.Throws<SceneSourceException>(() => source.GetSceneAsync("sub/scn_2cold").GetAwaiter().GetResult());
        }

        [Test]
        public void GetMetadata_ParsesTheFileOnDisk()
        {
            WriteScene("scn_2cold", 0.312834);

            var m = new FileSceneSource(m_root).GetMetadataAsync("scn_2cold").GetAwaiter().GetResult();

            Assert.That(m.Handedness, Is.EqualTo("left"));
            Assert.That(m.UpAxis, Is.EqualTo("y"));
            Assert.That(m.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
            Assert.That(m.SplatCount, Is.EqualTo(258157));
        }

        [Test]
        public void GetMetadata_ReturnsWhatTheFileSays_EvenWhenItIsWrongFramed()
        {
            // The source reports; SceneMetadataValidator refuses. Sanitising here would hide a
            // broken export behind a client that quietly "fixed" it (docs/FRAMES.md).
            WriteScene("scn_rightup", 0.31, handedness: "right", upAxis: "z");

            var m = new FileSceneSource(m_root).GetMetadataAsync("scn_rightup").GetAwaiter().GetResult();

            Assert.That(m.Handedness, Is.EqualTo("right"));
            Assert.That(m.UpAxis, Is.EqualTo("z"));
        }

        [Test]
        public void DownloadPly_ReturnsThePathOnDisk_WithoutCopyingTheFile()
        {
            // The .ply is already local and is tens of megabytes; copying it into
            // persistentDataPath would double the storage for no benefit.
            var sha = WriteScene("scn_2cold", 0.312834);
            var source = new FileSceneSource(m_root);
            var reported = new SynchronousProgress();

            var path = source.DownloadPlyAsync("scn_2cold", Path.Combine(m_root, "unused-dest"), reported)
                .GetAwaiter().GetResult();

            Assert.That(File.Exists(path), Is.True);
            Assert.That(path, Is.EqualTo(Path.Combine(m_root, "scn_2cold", "splat_unity.ply")));
            Assert.That(Directory.Exists(Path.Combine(m_root, "unused-dest")), Is.False);
            Assert.That(source.LastAssetSha256, Is.EqualTo(sha));
            Assert.That(reported.Last, Is.EqualTo(1f).Within(1e-6f));
        }

        /// <summary>
        /// System.Progress&lt;T&gt; posts to a SynchronizationContext or the thread pool, so a
        /// callback has not necessarily run by the time the awaited call returns. The loader's
        /// progress bar does not care; a test asserting on the value does.
        /// </summary>
        sealed class SynchronousProgress : IProgress<float>
        {
            public float Last { get; private set; } = -1f;
            public void Report(float value) => Last = value;
        }

        [Test]
        public void DisplayName_SaysWhereTheScenesCameFrom()
        {
            // Shown in the picker so nobody demos a stale local scene thinking it came from the API.
            var source = new FileSceneSource(m_root);

            Assert.That(source.DisplayName, Does.Contain("file"));
            Assert.That(source.DisplayName, Does.Contain(m_root));
        }

        [Test]
        public void Constructor_RejectsAnEmptyFolder()
        {
            Assert.Throws<ArgumentException>(() => new FileSceneSource(""));
        }
    }
}
