using NUnit.Framework;
using Recon.Contract;

namespace Recon.Tests.Core
{
    /// <summary>
    /// The wire contract, exercised against JSON shaped exactly the way the routes on `main`
    /// serialise it. Every literal below is what Next's `Response.json` produces for the select
    /// in the matching route handler, dates included (ISO-8601 with milliseconds and a Z), so a
    /// field renamed on the web side breaks a test here rather than a phone in a lab.
    ///
    /// Runs under both the Unity Test Runner and `dotnet test Unity/Recon.Core.Tests`; NUnit
    /// constraint asserts only, no UnityEngine.
    /// </summary>
    public class ApiContractTests
    {
        // ---- error envelope: web/src/lib/api.ts apiError() --------------------------------

        [Test]
        public void ErrorEnvelope_Parses_CodeAndMessage()
        {
            const string json = @"{""error"":{""code"":""NOT_FOUND"",""message"":""Scene not found""}}";

            Assert.That(ApiErrorEnvelope.TryParse(json, out var err), Is.True);
            Assert.That(err.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(err.Message, Is.EqualTo("Scene not found"));
            Assert.That(err.Details, Is.Null);
        }

        [Test]
        public void ErrorEnvelope_KeepsZodIssueDetails()
        {
            // web/src/lib/api.ts zodError() puts err.issues in `details`.
            const string json = @"{""error"":{""code"":""BAD_REQUEST"",""message"":""Request validation failed"",
                ""details"":[{""code"":""invalid_type"",""path"":[""rotation"",""w""],""message"":""Required""}]}}";

            Assert.That(ApiErrorEnvelope.TryParse(json, out var err), Is.True);
            Assert.That(err.Details, Is.Not.Null);
            Assert.That(err.ToString(), Does.Contain("BAD_REQUEST"));
            Assert.That(err.ToString(), Does.Contain("Required"));
        }

        [Test]
        public void ErrorEnvelope_RejectsSuccessBody()
        {
            // A success body is the raw object, never wrapped in `error`.
            Assert.That(ApiErrorEnvelope.TryParse(@"{""scenes"":[]}", out var err), Is.False);
            Assert.That(err, Is.Null);
        }

        [Test]
        public void ErrorEnvelope_RejectsNonJson_WithoutThrowing()
        {
            // A proxy's HTML error page, or .ply bytes served where JSON was expected.
            Assert.That(ApiErrorEnvelope.TryParse("<html><body>502 Bad Gateway</body></html>", out _), Is.False);
            Assert.That(ApiErrorEnvelope.TryParse("ply\nformat binary_little_endian 1.0\n", out _), Is.False);
            Assert.That(ApiErrorEnvelope.TryParse("", out _), Is.False);
            Assert.That(ApiErrorEnvelope.TryParse(null, out _), Is.False);
        }

        [Test]
        public void ErrorEnvelope_RejectsErrorFieldThatIsNotAnObject()
        {
            Assert.That(ApiErrorEnvelope.TryParse(@"{""error"":""boom""}", out _), Is.False);
        }

        // ---- POST /api/auth/login -------------------------------------------------------

        [Test]
        public void LoginResponse_ParsesTokenAndUser()
        {
            const string json = @"{
              ""token"": ""eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJjbWYwMDAxIn0.sig"",
              ""user"": { ""id"": ""cmf0001abcd"", ""email"": ""investigator@recon.local"", ""role"": ""INVESTIGATOR"" }
            }";

            var r = LoginResponse.FromJson(json);

            Assert.That(r.Token, Does.StartWith("eyJ"));
            Assert.That(r.User, Is.Not.Null);
            Assert.That(r.User.Id, Is.EqualTo("cmf0001abcd"));
            Assert.That(r.User.Email, Is.EqualTo("investigator@recon.local"));
            Assert.That(r.User.Role, Is.EqualTo("INVESTIGATOR"));
        }

        [Test]
        public void LoginResponse_RefusesABodyWithNoToken()
        {
            // Better a clear throw here than an empty Authorization header on every later call.
            Assert.Throws<ReconContractException>(() => LoginResponse.FromJson(@"{""user"":{""id"":""x""}}"));
        }

        // ---- GET /api/scenes?status=READY ------------------------------------------------

        const string SceneListJson = @"{
          ""scenes"": [
            {
              ""id"": ""cmf2cold0001"",
              ""name"": ""Scene 2 cold"",
              ""createdAt"": ""2026-09-02T16:12:41.000Z"",
              ""unitScale"": 0.312834,
              ""caseId"": ""cmfcase0001"",
              ""job"": { ""id"": ""cmfjob0001"", ""status"": ""READY"", ""progress"": 100 }
            },
            {
              ""id"": ""cmfnoscale002"",
              ""name"": ""Lab bench, no marker"",
              ""createdAt"": ""2026-09-01T09:04:00.000Z"",
              ""unitScale"": null,
              ""caseId"": ""cmfcase0001"",
              ""job"": { ""id"": ""cmfjob0002"", ""status"": ""PROCESSING"", ""progress"": 42 }
            }
          ]
        }";

        [Test]
        public void SceneList_ParsesEveryFieldTheRouteSelects()
        {
            var list = SceneListResponse.FromJson(SceneListJson);

            Assert.That(list.Scenes, Has.Count.EqualTo(2));

            var first = list.Scenes[0];
            Assert.That(first.Id, Is.EqualTo("cmf2cold0001"));
            Assert.That(first.Name, Is.EqualTo("Scene 2 cold"));
            Assert.That(first.CreatedAt, Is.EqualTo("2026-09-02T16:12:41.000Z"));
            Assert.That(first.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
            Assert.That(first.CaseId, Is.EqualTo("cmfcase0001"));
            Assert.That(first.Job.Id, Is.EqualTo("cmfjob0001"));
            Assert.That(first.Job.Status, Is.EqualTo("READY"));
            Assert.That(first.Job.Progress, Is.EqualTo(100));
        }

        [Test]
        public void SceneSummary_UnitScaleIsNullable_NotZero()
        {
            // Scene.unitScale is `Float?` in prisma/schema.prisma. Deserialising a missing marker
            // measurement into 0.0 would look exactly like "measured, and non-metric", which is a
            // different and much more alarming fact.
            var list = SceneListResponse.FromJson(SceneListJson);
            var second = list.Scenes[1];

            Assert.That(second.UnitScale, Is.Null);
            Assert.That(second.HasUnitScale, Is.False);
            Assert.That(list.Scenes[0].HasUnitScale, Is.True);
        }

        [Test]
        public void SceneSummary_IsReady_OnlyForAReadyJob()
        {
            var list = SceneListResponse.FromJson(SceneListJson);

            Assert.That(list.Scenes[0].IsReady, Is.True);
            Assert.That(list.Scenes[1].IsReady, Is.False);
        }

        [Test]
        public void SceneList_MissingScenesArray_Throws()
        {
            Assert.Throws<ReconContractException>(() => SceneListResponse.FromJson(@"{}"));
        }

        // ---- GET /api/scenes/:id --------------------------------------------------------

        const string SceneDetailJson = @"{
          ""scene"": {
            ""id"": ""cmf2cold0001"",
            ""name"": ""Scene 2 cold"",
            ""createdAt"": ""2026-09-02T16:12:41.000Z"",
            ""unitScale"": 0.312834,
            ""caseId"": ""cmfcase0001"",
            ""job"": { ""id"": ""cmfjob0001"", ""status"": ""READY"", ""progress"": 100, ""error"": null },
            ""assets"": [
              {
                ""id"": ""cmfasset0001"",
                ""kind"": ""SOURCE_VIDEO"",
                ""sha256"": ""aa7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"",
                ""byteSize"": 214748364,
                ""mimeType"": ""video/mp4"",
                ""createdAt"": ""2026-09-02T15:40:00.000Z""
              },
              {
                ""id"": ""cmfasset0002"",
                ""kind"": ""SPLAT_PLY"",
                ""sha256"": ""62fe0cee7b788759386be0a19781959e7809c6c74d7a6db042b499cf80c1fc06"",
                ""byteSize"": 45088768,
                ""mimeType"": ""application/octet-stream"",
                ""createdAt"": ""2026-09-02T16:12:41.000Z""
              }
            ],
            ""assetUrl"": ""/api/scenes/cmf2cold0001/asset""
          }
        }";

        [Test]
        public void SceneDetail_ParsesSceneJobAndAssets()
        {
            var scene = SceneDetailResponse.FromJson(SceneDetailJson).Scene;

            Assert.That(scene.Id, Is.EqualTo("cmf2cold0001"));
            Assert.That(scene.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
            Assert.That(scene.Job.Error, Is.Null);
            Assert.That(scene.AssetUrl, Is.EqualTo("/api/scenes/cmf2cold0001/asset"));
            Assert.That(scene.Assets, Has.Count.EqualTo(2));
        }

        [Test]
        public void SceneDetail_ByteSizeIsALong_BigIntSerialisedAsANumber()
        {
            // web/src/lib/api.ts serialiseAsset() sends Asset.byteSize (BigInt) as a JSON number;
            // a 214 MB walkthrough already overflows nothing, but an int would cap at ~2 GB.
            var scene = SceneDetailResponse.FromJson(SceneDetailJson).Scene;

            Assert.That(scene.Assets[0].ByteSize, Is.EqualTo(214748364L));
            Assert.That(scene.Assets[1].ByteSize, Is.EqualTo(45088768L));
        }

        [Test]
        public void SceneDetail_FindsTheSplatPlyAssetAmongOthers()
        {
            // The asset route serves kind SPLAT_PLY, so that row's sha256 is the custody value
            // the downloaded bytes get checked against.
            var scene = SceneDetailResponse.FromJson(SceneDetailJson).Scene;

            var ply = scene.SplatPly;
            Assert.That(ply, Is.Not.Null);
            Assert.That(ply.Kind, Is.EqualTo(AssetInfo.SplatPly));
            Assert.That(ply.Sha256, Is.EqualTo("62fe0cee7b788759386be0a19781959e7809c6c74d7a6db042b499cf80c1fc06"));
            Assert.That(scene.MetadataJson, Is.Null, "this scene has no METADATA_JSON asset row");
        }

        [Test]
        public void SceneDetail_FailedJobKeepsTheErrorText()
        {
            const string json = @"{""scene"":{""id"":""x"",""name"":""n"",""createdAt"":""2026-09-02T00:00:00.000Z"",
              ""unitScale"":null,""caseId"":""c"",
              ""job"":{""id"":""j"",""status"":""FAILED"",""progress"":30,""error"":""CUDA out of memory at 7000 iters""},
              ""assets"":[],""assetUrl"":""/api/scenes/x/asset""}}";

            var scene = SceneDetailResponse.FromJson(json).Scene;

            Assert.That(scene.Job.Status, Is.EqualTo("FAILED"));
            Assert.That(scene.Job.Error, Is.EqualTo("CUDA out of memory at 7000 iters"));
            Assert.That(scene.IsReady, Is.False);
            Assert.That(scene.SplatPly, Is.Null);
        }

        [Test]
        public void SceneDetail_MissingSceneWrapper_Throws()
        {
            Assert.Throws<ReconContractException>(() => SceneDetailResponse.FromJson(@"{""id"":""x""}"));
        }
    }
}
