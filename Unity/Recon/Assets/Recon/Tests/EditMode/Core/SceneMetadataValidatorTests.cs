using NUnit.Framework;
using Recon.Contract;
using Recon.Scenes;

namespace Recon.Tests.Core
{
    /// <summary>
    /// Runs under both the Unity Test Runner and `dotnet test Unity/Recon.Core.Tests`.
    /// Uses only NUnit constraint asserts, which exist in NUnit 3.5 (Unity) and 3.14 (dotnet).
    /// </summary>
    public class SceneMetadataValidatorTests
    {
        // docs/results/2026-09-02-scene2cold-metadata.json, as the pipeline actually wrote it.
        const string RealMetadata = @"{
  ""sceneId"": ""scn_2cold"",
  ""plyFile"": ""splat_unity.ply"",
  ""splatCount"": 258157,
  ""sourceFrames"": 316,
  ""handedness"": ""left"",
  ""upAxis"": ""y"",
  ""unitScale"": 0.312834,
  ""scaleMethod"": ""marker"",
  ""boundingBox"": { ""min"": [-15.099974632263184, -15.281909942626953, -18.924325942993164],
                     ""max"": [15.831721305847168, 4.558213233947754, 13.144667625427246] },
  ""originHint"": [0, 0, 0],
  ""metrics"": { ""psnr"": 30.256061553955078, ""ssim"": 0.9457913637161255, ""lpips"": 0.11730971932411194 },
  ""training"": { ""iterations"": 7000, ""minutes"": 2.39, ""peakVramMb"": 1349 },
  ""sha256"": ""62fe0cee7b788759386be0a19781959e7809c6c74d7a6db042b499cf80c1fc06"",
  ""createdAt"": ""2026-09-02T16:12:41Z""
}";

        static SceneMetadata Good()
        {
            var m = SceneMetadata.FromJson(RealMetadata);
            return m;
        }

        [Test]
        public void RealPipelineMetadata_ParsesEveryField()
        {
            var m = Good();
            Assert.That(m.SceneId, Is.EqualTo("scn_2cold"));
            Assert.That(m.PlyFile, Is.EqualTo("splat_unity.ply"));
            Assert.That(m.SplatCount, Is.EqualTo(258157));
            Assert.That(m.SourceFrames, Is.EqualTo(316));
            Assert.That(m.Handedness, Is.EqualTo("left"));
            Assert.That(m.UpAxis, Is.EqualTo("y"));
            Assert.That(m.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
            Assert.That(m.ScaleMethod, Is.EqualTo("marker"));
            Assert.That(m.BoundingBox.Min[2], Is.EqualTo(-18.924325942993164).Within(1e-9));
            Assert.That(m.BoundingBox.Max[1], Is.EqualTo(4.558213233947754).Within(1e-9));
            Assert.That(m.OriginHint, Is.EqualTo(new double[] { 0, 0, 0 }));
            Assert.That(m.Quality.Psnr, Is.EqualTo(30.256061553955078).Within(1e-9));
            Assert.That(m.TrainingInfo.PeakVramMb, Is.EqualTo(1349));
            Assert.That(m.Sha256, Has.Length.EqualTo(64));
            Assert.That(m.CreatedAt, Is.EqualTo("2026-09-02T16:12:41Z"));
        }

        [Test]
        public void RealPipelineMetadata_IsValid_NoWarnings()
        {
            var r = SceneMetadataValidator.Validate(Good());
            Assert.That(r.IsValid, Is.True, r.ToString());
            Assert.That(r.Warnings, Is.Empty);
        }

        [Test]
        public void SampleFromDocs_UnitScaleZero_IsRejectedAsNonMetric()
        {
            // docs/samples/metadata.example.json ships unitScale 0.0 and a zero sha on purpose.
            var m = Good();
            m.UnitScale = 0.0;
            var r = SceneMetadataValidator.Validate(m);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors, Has.Exactly(1).Contains("unitScale is 0"));
        }

        [TestCase("right")]
        [TestCase("Left")]
        [TestCase("")]
        [TestCase(null)]
        public void WrongHandedness_IsRejected(string handedness)
        {
            var m = Good();
            m.Handedness = handedness;
            var r = SceneMetadataValidator.Validate(m);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors, Has.Exactly(1).Contains("handedness"));
        }

        [TestCase("z")]
        [TestCase("Y")]
        [TestCase(null)]
        public void WrongUpAxis_IsRejected(string upAxis)
        {
            var m = Good();
            m.UpAxis = upAxis;
            var r = SceneMetadataValidator.Validate(m);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors, Has.Exactly(1).Contains("upAxis"));
        }

        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void NonPositiveOrNonFiniteUnitScale_IsRejected(double unitScale)
        {
            var m = Good();
            m.UnitScale = unitScale;
            Assert.That(SceneMetadataValidator.Validate(m).IsValid, Is.False);
        }

        [Test]
        public void ScaleMethodNone_IsRejected_EvenWithPositiveUnitScale()
        {
            var m = Good();
            m.ScaleMethod = "none";
            var r = SceneMetadataValidator.Validate(m);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.Errors, Has.Exactly(1).Contains("scaleMethod"));
        }

        [Test]
        public void ImplausibleUnitScale_IsWarning_NotError()
        {
            var m = Good();
            m.UnitScale = 250;
            var r = SceneMetadataValidator.Validate(m);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Warnings, Has.Exactly(1).Contains("plausible range"));
        }

        [Test]
        public void PlaceholderSha_IsWarning()
        {
            var m = Good();
            m.Sha256 = new string('0', 64);
            var r = SceneMetadataValidator.Validate(m);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Warnings, Has.Exactly(1).Contains("sha256"));
        }

        [Test]
        public void EnsureValid_ThrowsWithEveryErrorListed()
        {
            var m = Good();
            m.Handedness = "right";
            m.UpAxis = "z";
            m.UnitScale = 0;
            var ex = Assert.Throws<SceneRejectedException>(() => SceneMetadataValidator.EnsureValid(m));
            Assert.That(ex.Result.Errors, Has.Count.EqualTo(3));
            Assert.That(ex.Message, Does.Contain("handedness").And.Contain("upAxis").And.Contain("unitScale"));
        }

        [Test]
        public void NullMetadata_IsRejected()
        {
            Assert.That(SceneMetadataValidator.Validate(null).IsValid, Is.False);
        }

        [Test]
        public void RoundTrip_PreservesFieldNames()
        {
            var json = Good().ToJson();
            Assert.That(json, Does.Contain("\"unitScale\"").And.Contain("\"upAxis\"").And.Contain("\"handedness\"").And.Contain("\"peakVramMb\""));
            var again = SceneMetadata.FromJson(json);
            Assert.That(again.UnitScale, Is.EqualTo(0.312834).Within(1e-9));
        }
    }
}
