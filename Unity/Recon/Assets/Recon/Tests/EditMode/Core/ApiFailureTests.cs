using NUnit.Framework;
using Recon.Api;

namespace Recon.Tests.Core
{
    /// <summary>
    /// How a failed call reaches the user. Every non-2xx goes through the error envelope so the
    /// message on the phone is the message the server wrote, and a transport failure names the URL
    /// it could not reach — on a tailnet, "which host did it try" is the whole diagnosis.
    /// </summary>
    public class ApiFailureTests
    {
        [Test]
        public void ApiException_KeepsCodeMessageAndStatus()
        {
            var e = new ApiException("NOT_FOUND", "Scene not found", 404);

            Assert.That(e.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(e.Message, Does.Contain("Scene not found"));
            Assert.That(e.HttpStatus, Is.EqualTo(404));
        }

        [Test]
        public void FromResponse_UsesTheServersEnvelope()
        {
            var e = ApiException.FromResponse(404, @"{""error"":{""code"":""NOT_FOUND"",""message"":""Scene has no reconstructed asset yet""}}",
                "http://legion:3000/api/scenes/x/asset");

            Assert.That(e.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(e.Message, Does.Contain("Scene has no reconstructed asset yet"));
            Assert.That(e.HttpStatus, Is.EqualTo(404));
        }

        [Test]
        public void FromResponse_NamesTheUrl()
        {
            var e = ApiException.FromResponse(401, @"{""error"":{""code"":""UNAUTHORIZED"",""message"":""Missing or invalid session token""}}",
                "http://legion:3000/api/scenes?status=READY");

            Assert.That(e.Message, Does.Contain("http://legion:3000/api/scenes?status=READY"));
            Assert.That(e.IsUnauthorized, Is.True);
        }

        [Test]
        public void FromResponse_BodyWithNoEnvelope_StillProducesSomethingReadable()
        {
            // A reverse proxy, or a crash before the route handler ran.
            var e = ApiException.FromResponse(502, "<html><head><title>502</title></head></html>", "http://legion:3000/api/scenes");

            Assert.That(e.HttpStatus, Is.EqualTo(502));
            Assert.That(e.Code, Is.EqualTo(ApiException.HttpCode));
            Assert.That(e.Message, Does.Contain("502"));
        }

        [Test]
        public void Network_NamesTheUrlAndCarriesNoHttpStatus()
        {
            // The commonest cause is the dev API not being bound to 0.0.0.0, or the phone being
            // off the tailnet (docs/NETWORK.md), and neither produces an HTTP status at all.
            var e = ApiException.Network("http://legion:3000/api/scenes", "Cannot resolve destination host");

            Assert.That(e.Code, Is.EqualTo(ApiException.NetworkCode));
            Assert.That(e.HttpStatus, Is.EqualTo(0));
            Assert.That(e.Message, Does.Contain("http://legion:3000/api/scenes"));
            Assert.That(e.Message, Does.Contain("Cannot resolve destination host"));
        }

        [Test]
        public void MetadataUnavailable_IsAnApiException_AndSaysExactlyWhatIsMissing()
        {
            // The contract gap, verified 2026-09-02: the API exposes scene.unitScale but no route
            // serves metadata.json, so handedness/upAxis/scaleMethod cannot be asserted. Refusing
            // is correct (docs/FRAMES.md); guessing "left"/"y" because the pipeline usually writes
            // them is what this exception exists to prevent.
            var e = new MetadataUnavailableException("cmf2cold0001",
                "http://legion:3000/api/scenes/cmf2cold0001/asset?kind=METADATA_JSON",
                "404 NOT_FOUND: Scene has no reconstructed asset yet");

            Assert.That(e, Is.InstanceOf<ApiException>());
            Assert.That(e.Message, Does.Contain("metadata.json"));
            Assert.That(e.Message, Does.Contain("http://legion:3000/api/scenes/cmf2cold0001/asset?kind=METADATA_JSON"));
            Assert.That(e.Message, Does.Contain("cmf2cold0001"));
            Assert.That(e.SceneId, Is.EqualTo("cmf2cold0001"));
        }

        // ---- session store --------------------------------------------------------------

        [Test]
        public void InMemorySessionStore_StartsEmpty()
        {
            var store = new InMemorySessionStore();

            Assert.That(store.Token, Is.Null);
            Assert.That(store.HasToken, Is.False);
        }

        [Test]
        public void InMemorySessionStore_SetsAndClears()
        {
            var store = new InMemorySessionStore();

            store.Set("abc.def.ghi");
            Assert.That(store.Token, Is.EqualTo("abc.def.ghi"));
            Assert.That(store.HasToken, Is.True);

            store.Clear();
            Assert.That(store.Token, Is.Null);
            Assert.That(store.HasToken, Is.False);
        }

        [Test]
        public void InMemorySessionStore_BlankTokenIsTreatedAsCleared()
        {
            // An empty token would otherwise be sent as "Bearer " and come back 401, which reads
            // like an expired session rather than "we never logged in".
            var store = new InMemorySessionStore();
            store.Set("abc");

            store.Set("   ");

            Assert.That(store.HasToken, Is.False);
            Assert.That(store.Token, Is.Null);
        }
    }
}
