using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Recon.Contract;
using Recon.Scenes;

namespace Recon.Tests.Core
{
    /// <summary>
    /// The two decisions SceneLoader must not get wrong: which hash the downloaded bytes are
    /// checked against, and whether a file already on the device can be reused instead of pulling
    /// 45 MB over the tailnet again. Both are pure, so both are tested here rather than by
    /// watching a phone.
    /// </summary>
    public class SceneAssetCacheTests
    {
        const string PlySha = "62fe0cee7b788759386be0a19781959e7809c6c74d7a6db042b499cf80c1fc06";
        const string OtherSha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        string m_dir;

        [SetUp]
        public void SetUp()
        {
            m_dir = Path.Combine(Path.GetTempPath(), "recon-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_dir)) Directory.Delete(m_dir, true);
        }

        /// <summary>Writes "abc", whose SHA-256 is the published test vector in <see cref="OtherSha"/>.</summary>
        string WriteAbc(string name = "splat_unity.ply")
        {
            var path = Path.Combine(m_dir, name);
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
            return path;
        }

        static SceneDetail Detail(string plySha)
        {
            return new SceneDetail
            {
                Id = "cmf2cold0001",
                Name = "Scene 2 cold",
                UnitScale = 0.312834,
                Job = new JobInfo { Status = JobInfo.Ready, Progress = 100 },
                AssetUrl = "/api/scenes/cmf2cold0001/asset",
                Assets = new List<AssetInfo>
                {
                    new AssetInfo { Id = "a1", Kind = AssetInfo.SplatPly, Sha256 = plySha, ByteSize = 45088768 },
                },
            };
        }

        static SceneMetadata Metadata(string sha) => new SceneMetadata
        {
            SceneId = "scn_2cold",
            PlyFile = "splat_unity.ply",
            Handedness = "left",
            UpAxis = "y",
            UnitScale = 0.312834,
            ScaleMethod = "marker",
            Sha256 = sha,
        };

        // ---- which hash to trust ---------------------------------------------------------

        [Test]
        public void ExpectedSha_UsesTheAssetRow_WhichIsWhatCustodyRecorded()
        {
            Assert.That(SceneAssetCache.ExpectedSha256(Detail(PlySha), Metadata(PlySha)), Is.EqualTo(PlySha));
        }

        [Test]
        public void ExpectedSha_FallsBackToMetadataWhenTheAssetRowHasNoUsableHash()
        {
            // A file source synthesises an asset row; a placeholder metadata hash is what the
            // sample metadata.json ships with.
            Assert.That(SceneAssetCache.ExpectedSha256(Detail(null), Metadata(PlySha)), Is.EqualTo(PlySha));
            Assert.That(SceneAssetCache.ExpectedSha256(Detail(Sha256Hex.Placeholder), Metadata(PlySha)), Is.EqualTo(PlySha));
        }

        [Test]
        public void ExpectedSha_NullWhenNobodyStatedOne()
        {
            // Not fatal: an FTW-30 dev scene has no custody hash and still needs to render for a
            // frame-rate measurement. The loader reports it as unverified instead.
            Assert.That(SceneAssetCache.ExpectedSha256(Detail(null), Metadata(Sha256Hex.Placeholder)), Is.Null);
            Assert.That(SceneAssetCache.ExpectedSha256(Detail(null), null), Is.Null);
        }

        [Test]
        public void ExpectedSha_RefusesWhenTheDatabaseAndTheMetadataDisagree()
        {
            // Two records of the same file that do not match is a custody problem, not a rounding
            // problem: one of them describes bytes nobody has. Refuse rather than pick a winner.
            var e = Assert.Throws<CustodyMismatchException>(
                () => SceneAssetCache.ExpectedSha256(Detail(PlySha), Metadata(OtherSha)));

            Assert.That(e.Message, Does.Contain(PlySha));
            Assert.That(e.Message, Does.Contain(OtherSha));
        }

        [Test]
        public void ExpectedSha_IgnoresCasing()
        {
            Assert.That(SceneAssetCache.ExpectedSha256(Detail(PlySha.ToUpperInvariant()), Metadata(PlySha)),
                Is.EqualTo(PlySha.ToUpperInvariant()));
        }

        // ---- cache by sha ----------------------------------------------------------------

        [Test]
        public void ShouldDownload_TrueWhenThereIsNoFileYet()
        {
            Assert.That(SceneAssetCache.ShouldDownload(Path.Combine(m_dir, "missing.ply"), OtherSha), Is.True);
        }

        [Test]
        public void ShouldDownload_FalseWhenTheFileOnDiskAlreadyHashesToTheExpectedValue()
        {
            // The point of the cache: opening the same scene twice must not pull 45 MB twice.
            var path = WriteAbc();

            Assert.That(SceneAssetCache.ShouldDownload(path, OtherSha), Is.False);
        }

        [Test]
        public void ShouldDownload_TrueWhenTheCachedFileIsNotTheExpectedBytes()
        {
            // A half-finished download from a dropped tailnet connection, or a scene re-exported
            // server side under the same id. Both look like a file that is present and wrong.
            var path = WriteAbc();

            Assert.That(SceneAssetCache.ShouldDownload(path, PlySha), Is.True);
        }

        [Test]
        public void ShouldDownload_TrueWhenNoHashIsKnown_EvenIfAFileIsThere()
        {
            // Without a hash there is no way to tell a complete file from a truncated one, so
            // fetching fresh bytes is the only honest answer.
            var path = WriteAbc();

            Assert.That(SceneAssetCache.ShouldDownload(path, null), Is.True);
        }

        [Test]
        public void ShouldDownload_TrueWhenTheFileIsEmpty()
        {
            var path = Path.Combine(m_dir, "empty.ply");
            File.WriteAllBytes(path, new byte[0]);

            Assert.That(SceneAssetCache.ShouldDownload(path, OtherSha), Is.True);
        }

        // ---- verification after the download ---------------------------------------------

        [Test]
        public void Verify_PassesWhenTheBytesMatchBothTheAssetRowAndTheHeader()
        {
            var path = WriteAbc();

            var result = SceneAssetCache.Verify(path, OtherSha, OtherSha);

            Assert.That(result.Verified, Is.True);
            Assert.That(result.ComputedSha256, Is.EqualTo(OtherSha));
        }

        [Test]
        public void Verify_RefusesWhenTheDownloadedBytesDoNotMatchTheExpectedHash()
        {
            var path = WriteAbc();

            var e = Assert.Throws<CustodyMismatchException>(() => SceneAssetCache.Verify(path, PlySha, PlySha));

            Assert.That(e.Message, Does.Contain(OtherSha), "computed hash");
            Assert.That(e.Message, Does.Contain(PlySha), "expected hash");
            Assert.That(e.Message, Does.Contain(path));
        }

        [Test]
        public void Verify_RefusesWhenTheHeaderContradictsTheAssetRow()
        {
            // Both come from the same Asset row server side, so a disagreement means the response
            // did not come from the scene that was asked for.
            var path = WriteAbc();

            var e = Assert.Throws<CustodyMismatchException>(() => SceneAssetCache.Verify(path, OtherSha, PlySha));

            Assert.That(e.Message, Does.Contain("X-Asset-SHA256"));
        }

        [Test]
        public void Verify_HeaderOnly_StillVerifies()
        {
            var path = WriteAbc();

            var result = SceneAssetCache.Verify(path, null, OtherSha);

            Assert.That(result.Verified, Is.True);
        }

        [Test]
        public void Verify_NoHashAnywhere_IsUnverifiedButNotRefused()
        {
            // The FTW-30 dev-scene path: renders, and says out loud that custody is unchecked.
            var path = WriteAbc();

            var result = SceneAssetCache.Verify(path, null, null);

            Assert.That(result.Verified, Is.False);
            Assert.That(result.ComputedSha256, Is.EqualTo(OtherSha));
            Assert.That(result.Reason, Does.Contain("no sha256"));
        }

        [Test]
        public void Verify_MissingFile_Throws()
        {
            Assert.Throws<FileNotFoundException>(
                () => SceneAssetCache.Verify(Path.Combine(m_dir, "missing.ply"), OtherSha, OtherSha));
        }

        // ---- where a scene is cached ------------------------------------------------------

        [Test]
        public void SceneFolder_IsOneFolderPerSceneId()
        {
            var folder = SceneAssetCache.SceneFolder("/data/user/0/app/files", "cmf2cold0001");

            Assert.That(folder, Does.Contain("scenes"));
            Assert.That(folder, Does.EndWith("cmf2cold0001"));
        }

        [Test]
        public void SceneFolder_SanitisesAnIdThatWouldEscapeTheCache()
        {
            var folder = SceneAssetCache.SceneFolder("/data/user/0/app/files", "../../etc");

            Assert.That(folder, Does.Not.Contain(".."));
        }
    }
}
