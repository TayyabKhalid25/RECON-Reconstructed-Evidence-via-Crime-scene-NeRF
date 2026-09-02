using NUnit.Framework;
using Recon.Contract;

namespace Recon.Tests.Core
{
    /// <summary>
    /// The anchor wire format from PR #18 (web/src/lib/anchor.ts serialiseAnchor), which is a
    /// <b>decomposed</b> transform on purpose: a 16-float 4x4 matrix in JSON carries a silent
    /// row-major versus column-major trap and a transposed matrix misplaces the twin subtly
    /// instead of failing visibly. Nothing here reassembles a matrix.
    /// </summary>
    public class AnchorContractTests
    {
        // Byte-for-byte the shape serialiseAnchor() produces, wrapped as both endpoints wrap it.
        const string AnchorJson = @"{
          ""anchor"": {
            ""id"": ""cmfanchor0001"",
            ""anchorId"": ""ua-9f2c1b7d4e8a4c0fb1e6d3a5c7b9e1f3"",
            ""provider"": ""arcore-cloud-anchors"",
            ""position"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0 },
            ""rotation"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0, ""w"": 1.0 },
            ""scale"": 1,
            ""expiresAt"": ""2027-09-02T10:00:00.000Z"",
            ""expired"": false,
            ""deviceLabel"": ""Victus test phone"",
            ""createdAt"": ""2026-09-02T10:00:00.000Z"",
            ""frame"": { ""handedness"": ""left"", ""upAxis"": ""y"", ""space"": ""anchor->scene"" }
          }
        }";

        [Test]
        public void AnchorRecord_ParsesEveryFieldTheServerSerialises()
        {
            var a = AnchorResponse.FromJson(AnchorJson).Anchor;

            Assert.That(a.Id, Is.EqualTo("cmfanchor0001"));
            Assert.That(a.AnchorId, Is.EqualTo("ua-9f2c1b7d4e8a4c0fb1e6d3a5c7b9e1f3"));
            Assert.That(a.Provider, Is.EqualTo("arcore-cloud-anchors"));
            Assert.That(a.Position.X, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.Rotation.W, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(a.Scale, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(a.ExpiresAt, Is.EqualTo("2027-09-02T10:00:00.000Z"));
            Assert.That(a.Expired, Is.False);
            Assert.That(a.DeviceLabel, Is.EqualTo("Victus test phone"));
            Assert.That(a.CreatedAt, Is.EqualTo("2026-09-02T10:00:00.000Z"));
        }

        [Test]
        public void AnchorRecord_CarriesTheFrameBlockTheServerStates()
        {
            // Stated in the payload so a client that starts flipping axes is contradicting the
            // response it was handed (docs/FRAMES.md).
            var a = AnchorResponse.FromJson(AnchorJson).Anchor;

            Assert.That(a.Frame, Is.Not.Null);
            Assert.That(a.Frame.Handedness, Is.EqualTo("left"));
            Assert.That(a.Frame.UpAxis, Is.EqualTo("y"));
            Assert.That(a.Frame.Space, Is.EqualTo("anchor->scene"));
            Assert.That(a.Frame.MatchesUnityConvention, Is.True);
        }

        [Test]
        public void AnchorRecord_FrameThatIsNotUnityConvention_IsDetected()
        {
            var frame = new AnchorFrame { Handedness = "right", UpAxis = "z", Space = "anchor->scene" };
            Assert.That(frame.MatchesUnityConvention, Is.False);
        }

        [Test]
        public void AnchorRecord_NullExpiresAt_MeansNoExpiryRecorded_NotNeverExpires()
        {
            const string json = @"{""anchor"":{""id"":""a"",""anchorId"":""ua-1"",""provider"":""arcore-cloud-anchors"",
              ""position"":{""x"":1,""y"":2,""z"":3},""rotation"":{""x"":0,""y"":0,""z"":0,""w"":1},""scale"":1,
              ""expiresAt"":null,""expired"":false,""deviceLabel"":null,""createdAt"":""2026-09-02T10:00:00.000Z"",
              ""frame"":{""handedness"":""left"",""upAxis"":""y"",""space"":""anchor->scene""}}}";

            var a = AnchorResponse.FromJson(json).Anchor;

            Assert.That(a.ExpiresAt, Is.Null);
            Assert.That(a.HasExpiry, Is.False);
            Assert.That(a.Expired, Is.False);
            Assert.That(a.DeviceLabel, Is.Null);
        }

        [Test]
        public void AnchorRecord_ExpiredIsServerComputed_AndSurvivesTheRoundTrip()
        {
            // A resolve that silently returns a dead Cloud Anchor looks exactly like broken
            // tracking on the phone, so the flag is read, never recomputed from the clock.
            const string json = @"{""anchor"":{""id"":""a"",""anchorId"":""ua-1"",""provider"":""arcore-cloud-anchors"",
              ""position"":{""x"":0,""y"":0,""z"":0},""rotation"":{""x"":0,""y"":0,""z"":0,""w"":1},""scale"":1,
              ""expiresAt"":""2026-08-01T00:00:00.000Z"",""expired"":true,""deviceLabel"":null,
              ""createdAt"":""2026-07-01T00:00:00.000Z"",
              ""frame"":{""handedness"":""left"",""upAxis"":""y"",""space"":""anchor->scene""}}}";

            var a = AnchorResponse.FromJson(json).Anchor;

            Assert.That(a.Expired, Is.True);
            Assert.That(a.HasExpiry, Is.True);
        }

        [Test]
        public void AnchorResponse_MissingAnchorWrapper_Throws()
        {
            Assert.Throws<ReconContractException>(() => AnchorResponse.FromJson(@"{""anchorId"":""ua-1""}"));
        }

        // ---- POST body -------------------------------------------------------------------

        [Test]
        public void AnchorInput_SendsExpiresAt_NotTtlDays()
        {
            // web/src/lib/anchor.ts AnchorInput takes `expiresAt` as an ISO datetime. FTW-54 is
            // the ticket that would change this to ttlDays; until it lands, sending ttlDays
            // would be silently dropped by Zod and the anchor would get no expiry at all.
            var input = new AnchorInput
            {
                AnchorId = "ua-1",
                Position = new Vec3 { X = 1, Y = 2, Z = 3 },
                Rotation = new Quat { X = 0, Y = 0, Z = 0, W = 1 },
                ExpiresAt = "2027-09-02T10:00:00.000Z",
            };

            var json = input.ToJson();

            Assert.That(json, Does.Contain("\"expiresAt\""));
            Assert.That(json, Does.Not.Contain("ttlDays"));
        }

        [Test]
        public void AnchorInput_OmitsOptionalFieldsItWasNotGiven()
        {
            // Zod's `.optional()` accepts absent, not null: sending `"scale": null` fails
            // validation where sending nothing at all lets the server default apply.
            var input = new AnchorInput
            {
                AnchorId = "ua-1",
                Position = new Vec3 { X = 0, Y = 0, Z = 0 },
                Rotation = new Quat { X = 0, Y = 0, Z = 0, W = 1 },
            };

            var json = input.ToJson();

            Assert.That(json, Does.Contain("\"anchorId\""));
            Assert.That(json, Does.Not.Contain("scale"));
            Assert.That(json, Does.Not.Contain("expiresAt"));
            Assert.That(json, Does.Not.Contain("deviceLabel"));
            Assert.That(json, Does.Not.Contain("provider"));
        }

        [Test]
        public void AnchorInput_RoundTripsThroughTheServerShape()
        {
            var input = new AnchorInput
            {
                AnchorId = "ua-1",
                Provider = "arcore-cloud-anchors",
                Position = new Vec3 { X = 0.25, Y = -1.5, Z = 3.75 },
                Rotation = new Quat { X = 0, Y = 0.7071068, Z = 0, W = 0.7071068 },
                Scale = 1.0,
                DeviceLabel = "Victus test phone",
            };

            var back = AnchorInput.FromJson(input.ToJson());

            Assert.That(back.AnchorId, Is.EqualTo("ua-1"));
            Assert.That(back.Position.Z, Is.EqualTo(3.75).Within(1e-9));
            Assert.That(back.Rotation.Y, Is.EqualTo(0.7071068).Within(1e-9));
            Assert.That(back.Scale, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(back.DeviceLabel, Is.EqualTo("Victus test phone"));
        }
    }
}
