using System;
using NUnit.Framework;
using Recon.Api;

namespace Recon.Tests.Core
{
    /// <summary>
    /// URL and header shapes, kept out of the UnityWebRequest glue so they can be tested without
    /// a device or a server. Every string here is a line in docs/API.md.
    /// </summary>
    public class ApiRoutesTests
    {
        const string Base = "http://legion:3000";

        [Test]
        public void Login_IsThePathFromTheContract()
        {
            Assert.That(ApiRoutes.Login(Base), Is.EqualTo("http://legion:3000/api/auth/login"));
        }

        [Test]
        public void BaseUrl_TrailingSlashesDoNotDoubleUp()
        {
            // The default in ReconSettings has no trailing slash, but a hand-typed tailnet URL
            // usually does, and "//api/auth/login" 404s on Next in a way that looks like the
            // route is missing.
            Assert.That(ApiRoutes.Login("http://legion:3000/"), Is.EqualTo("http://legion:3000/api/auth/login"));
            Assert.That(ApiRoutes.Login("  http://legion:3000//  "), Is.EqualTo("http://legion:3000/api/auth/login"));
        }

        [Test]
        public void ReadyScenes_FiltersServerSide()
        {
            // ?status=READY is the contract's filter; filtering client side would download the
            // whole list for every case the investigator owns.
            Assert.That(ApiRoutes.ReadyScenes(Base), Is.EqualTo("http://legion:3000/api/scenes?status=READY"));
        }

        [Test]
        public void Scenes_WithNoStatus_OmitsTheQuery()
        {
            Assert.That(ApiRoutes.Scenes(Base, null), Is.EqualTo("http://legion:3000/api/scenes"));
        }

        [Test]
        public void Scene_AndAsset_UseTheSceneId()
        {
            Assert.That(ApiRoutes.Scene(Base, "cmf2cold0001"), Is.EqualTo("http://legion:3000/api/scenes/cmf2cold0001"));
            Assert.That(ApiRoutes.Asset(Base, "cmf2cold0001"), Is.EqualTo("http://legion:3000/api/scenes/cmf2cold0001/asset"));
            Assert.That(ApiRoutes.Anchor(Base, "cmf2cold0001"), Is.EqualTo("http://legion:3000/api/scenes/cmf2cold0001/anchor"));
        }

        [Test]
        public void SceneId_IsEscaped_SoAFolderNameCannotWalkThePath()
        {
            // FileSceneSource ids are folder names, and a scene picked from disk can be handed
            // straight to the API source when the user flips the toggle.
            Assert.That(ApiRoutes.Scene(Base, "dev scene/../admin"),
                Is.EqualTo("http://legion:3000/api/scenes/dev%20scene%2F..%2Fadmin"));
        }

        [Test]
        public void AssetOfKind_IsTheShapeBeingRequestedOfTrackB()
        {
            // As of 2026-09-02 the asset route hard-codes kind SPLAT_PLY and ignores this query,
            // so the request 404s or returns .ply bytes. It is the shape being asked for, and
            // ApiSceneSource turns the failure into MetadataUnavailableException rather than
            // guessing the frame. See docs/FRAMES.md.
            Assert.That(ApiRoutes.AssetOfKind(Base, "cmf2cold0001", "METADATA_JSON"),
                Is.EqualTo("http://legion:3000/api/scenes/cmf2cold0001/asset?kind=METADATA_JSON"));
        }

        [Test]
        public void Resolve_TurnsTheRelativeAssetUrlIntoAnAbsoluteOne()
        {
            // SceneDetail.assetUrl comes back relative ("/api/scenes/<id>/asset").
            Assert.That(ApiRoutes.Resolve(Base, "/api/scenes/x/asset"), Is.EqualTo("http://legion:3000/api/scenes/x/asset"));
            Assert.That(ApiRoutes.Resolve(Base, "api/scenes/x/asset"), Is.EqualTo("http://legion:3000/api/scenes/x/asset"));
        }

        [Test]
        public void Resolve_LeavesAnAbsoluteUrlAlone_SoAnObjectStoreRedirectStillWorks()
        {
            // urlFor() in web/src/lib/storage.ts is the seam that would return a presigned URL.
            const string presigned = "https://bucket.example.com/scn/x.ply?sig=abc";
            Assert.That(ApiRoutes.Resolve(Base, presigned), Is.EqualTo(presigned));
        }

        [Test]
        public void EmptyBaseUrl_ThrowsAndNamesTheSetting()
        {
            var e = Assert.Throws<ArgumentException>(() => ApiRoutes.Login(""));
            Assert.That(e.Message, Does.Contain("apiBaseUrl"));
        }

        [Test]
        public void EmptySceneId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ApiRoutes.Scene(Base, ""));
        }

        [Test]
        public void HeaderNamesMatchTheServer()
        {
            Assert.That(ApiRoutes.AuthorizationHeader, Is.EqualTo("Authorization"));
            Assert.That(ApiRoutes.BearerValue("abc.def.ghi"), Is.EqualTo("Bearer abc.def.ghi"));
            Assert.That(ApiRoutes.AssetSha256Header, Is.EqualTo("X-Asset-SHA256"));
        }
    }
}
