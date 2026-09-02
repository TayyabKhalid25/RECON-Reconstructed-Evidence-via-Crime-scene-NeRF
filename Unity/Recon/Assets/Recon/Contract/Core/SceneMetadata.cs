using Newtonsoft.Json;

namespace Recon.Contract
{
    /// <summary>
    /// metadata.json as the GPU worker writes it (docs/API.md, docs/samples/metadata.example.json,
    /// gpu/run_scene.py stage_metadata). Field names are the contract; changing one here means
    /// changing it there and telling all three tracks (changing-the-contract skill).
    /// Pure C#: no UnityEngine, so it compiles and is tested under dotnet as well.
    /// </summary>
    public sealed class SceneMetadata
    {
        [JsonProperty("sceneId")] public string SceneId { get; set; }
        [JsonProperty("plyFile")] public string PlyFile { get; set; }
        [JsonProperty("splatCount")] public long SplatCount { get; set; }
        [JsonProperty("sourceFrames")] public int SourceFrames { get; set; }

        /// <summary>"left" is the only value Unity accepts (docs/FRAMES.md).</summary>
        [JsonProperty("handedness")] public string Handedness { get; set; }

        /// <summary>"y" is the only value Unity accepts (docs/FRAMES.md).</summary>
        [JsonProperty("upAxis")] public string UpAxis { get; set; }

        /// <summary>Metres per scene unit. 0.0 means the scene is not metric and must be refused.</summary>
        [JsonProperty("unitScale")] public double UnitScale { get; set; }

        /// <summary>"marker" | "lidar" | "none".</summary>
        [JsonProperty("scaleMethod")] public string ScaleMethod { get; set; }

        [JsonProperty("boundingBox")] public Box BoundingBox { get; set; }
        [JsonProperty("originHint")] public double[] OriginHint { get; set; }
        [JsonProperty("metrics")] public Metrics Quality { get; set; }
        [JsonProperty("training")] public Training TrainingInfo { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; }

        public sealed class Box
        {
            [JsonProperty("min")] public double[] Min { get; set; }
            [JsonProperty("max")] public double[] Max { get; set; }
        }

        public sealed class Metrics
        {
            [JsonProperty("psnr")] public double Psnr { get; set; }
            [JsonProperty("ssim")] public double Ssim { get; set; }
            [JsonProperty("lpips")] public double Lpips { get; set; }
        }

        public sealed class Training
        {
            [JsonProperty("iterations")] public int Iterations { get; set; }
            [JsonProperty("minutes")] public double Minutes { get; set; }
            [JsonProperty("peakVramMb")] public int PeakVramMb { get; set; }
        }

        public static SceneMetadata FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new JsonException("metadata.json is empty.");
            var m = JsonConvert.DeserializeObject<SceneMetadata>(json);
            if (m == null) throw new JsonException("metadata.json did not parse to an object.");
            return m;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}
