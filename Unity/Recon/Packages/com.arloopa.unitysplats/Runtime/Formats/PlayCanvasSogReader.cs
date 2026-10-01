// Port of the PlayCanvas SOG v1/v2 decode rules.
// PlayCanvas engine copyright (c) 2011-2026 PlayCanvas Ltd; MIT license.
// Unity port copyright (c) 2026 ARLOOPA.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;
using WebP;

namespace Gsplat.Formats
{
    /// <summary>Reads bundled .sog files and unpacked SOG metadata plus lossless WebP images.</summary>
    public static class PlayCanvasSogReader
    {
        const float Sqrt2 = 1.4142135623730951f;

        public static GsplatDecodedData ReadBundle(byte[] bytes,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            return ReadBundle(stream, sourceCoordinates, progress);
        }

        public static GsplatDecodedData ReadBundle(Stream stream,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .ToDictionary(e => Normalize(e.FullName), StringComparer.OrdinalIgnoreCase);

            byte[] ReadEntry(string filename)
            {
                string normalized = Normalize(filename);
                if (!entries.TryGetValue(normalized, out ZipArchiveEntry entry))
                    entry = entries.Values.FirstOrDefault(e =>
                        string.Equals(Path.GetFileName(e.FullName), Path.GetFileName(filename),
                            StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new FileNotFoundException($"SOG entry '{filename}' is missing.");
                using Stream input = entry.Open();
                using var output = new MemoryStream(entry.Length > 0 && entry.Length <= int.MaxValue
                    ? (int)entry.Length : 0);
                input.CopyTo(output);
                return output.ToArray();
            }

            string metadata = Encoding.UTF8.GetString(ReadEntry("meta.json"));
            return ReadUnpacked(metadata, ReadEntry, sourceCoordinates, progress);
        }

        /// <param name="resolveFile">Resolves a filename from meta.json to its encoded bytes.</param>
        public static GsplatDecodedData ReadUnpacked(string metadata,
            Func<string, byte[]> resolveFile,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            if (string.IsNullOrWhiteSpace(metadata)) throw new ArgumentException("SOG metadata is empty.", nameof(metadata));
            if (resolveFile == null) throw new ArgumentNullException(nameof(resolveFile));

            var root = JsonValues.Object(MiniJson.Deserialize(metadata), "root");
            int version = root.OptionalInt("version", 1);
            if (version != 1 && version != 2)
                throw new NotSupportedException($"SOG version {version} is not supported (expected 1 or 2).");

            var meansMeta = RequiredObject(root, "means");
            var scalesMeta = RequiredObject(root, "scales");
            var quatsMeta = RequiredObject(root, "quats");
            var sh0Meta = RequiredObject(root, "sh0");
            var shNMeta = root.OptionalObject("shN");

            int count = version == 2
                ? root.RequiredInt("count")
                : JsonValues.Int(meansMeta.RequiredArray("shape")[0], "means.shape[0]");
            if (count <= 0) throw new FormatException("SOG splat count must be positive.");

            float[] meansMin = meansMeta.FloatArray("mins", 3);
            float[] meansMax = meansMeta.FloatArray("maxs", 3);

            progress?.Invoke("Decoding SOG images", 0f);
            DecodedImage meansLow = DecodeImage(resolveFile, FileAt(meansMeta, 0, "means"));
            DecodedImage meansHigh = DecodeImage(resolveFile, FileAt(meansMeta, 1, "means"));
            DecodedImage scales = DecodeImage(resolveFile, FileAt(scalesMeta, 0, "scales"));
            DecodedImage quats = DecodeImage(resolveFile, FileAt(quatsMeta, 0, "quats"));
            DecodedImage sh0 = DecodeImage(resolveFile, FileAt(sh0Meta, 0, "sh0"));
            ValidateDimensions(meansLow, meansHigh, scales, quats, sh0);
            if (count > meansLow.Width * meansLow.Height)
                throw new FormatException($"SOG count {count} exceeds image capacity {meansLow.Width * meansLow.Height}.");

            float[] scaleCodebook = version == 2 ? Codebook(scalesMeta, "scales.codebook") : null;
            float[] sh0Codebook = version == 2 ? Codebook(sh0Meta, "sh0.codebook") : null;
            float[] scaleMin = version == 1 ? scalesMeta.FloatArray("mins", 3) : null;
            float[] scaleMax = version == 1 ? scalesMeta.FloatArray("maxs", 3) : null;
            float[] sh0Min = version == 1 ? sh0Meta.FloatArray("mins", 4) : null;
            float[] sh0Max = version == 1 ? sh0Meta.FloatArray("maxs", 4) : null;

            int shBands = 0;
            int shCoefficients = 0;
            float[] shCodebook = null;
            float shMin = 0f, shMax = 0f;
            DecodedImage shCentroids = default;
            DecodedImage shLabels = default;
            if (shNMeta != null && shNMeta.TryGetValue("files", out object filesValue) &&
                filesValue is List<object> shFiles && shFiles.Count >= 2)
            {
                shCentroids = DecodeImage(resolveFile, JsonValues.String(shFiles[0], "shN.files[0]"));
                shLabels = DecodeImage(resolveFile, JsonValues.String(shFiles[1], "shN.files[1]"));
                if (shLabels.Width != meansLow.Width || shLabels.Height != meansLow.Height)
                    throw new FormatException("SOG SH label image dimensions do not match the splat images.");
                shBands = shCentroids.Width switch { 192 => 1, 512 => 2, 960 => 3, _ => 0 };
                if (shBands == 0)
                    throw new FormatException($"Unsupported SOG SH centroid width {shCentroids.Width}.");
                shCoefficients = GsplatUtils.SHBandsToCoefficientCount((byte)shBands);
                if (version == 2) shCodebook = Codebook(shNMeta, "shN.codebook");
                else
                {
                    shMin = Scalar(shNMeta, "mins");
                    shMax = Scalar(shNMeta, "maxs");
                }
            }

            bool flipRows = DetermineVerticalFlip(quats, count);
            bool antialiased = root.OptionalBool("antialias", false);
            var result = new GsplatDecodedData(count, (byte)shBands) { Antialiased = antialiased };
            var (px, py, pz) = GsplatUtils.AxisSigns(sourceCoordinates);
            float qxSign = py * pz;
            float qySign = px * pz;
            float qzSign = px * py;
            Vector3 boundsMin = Vector3.positiveInfinity;
            Vector3 boundsMax = Vector3.negativeInfinity;

            for (int i = 0; i < count; i++)
            {
                int x = i % meansLow.Width;
                int y = i / meansLow.Width;
                Rgba32 low = meansLow.Pixel(x, y, flipRows);
                Rgba32 high = meansHigh.Pixel(x, y, flipRows);
                Rgba32 scalePixel = scales.Pixel(x, y, flipRows);
                Rgba32 quatPixel = quats.Pixel(x, y, flipRows);
                Rgba32 colorPixel = sh0.Pixel(x, y, flipRows);

                float nx = Mathf.Lerp(meansMin[0], meansMax[0], ((high.R << 8) | low.R) / 65535f);
                float ny = Mathf.Lerp(meansMin[1], meansMax[1], ((high.G << 8) | low.G) / 65535f);
                float nz = Mathf.Lerp(meansMin[2], meansMax[2], ((high.B << 8) | low.B) / 65535f);
                Vector3 position = new(px * Unlog(nx), py * Unlog(ny), pz * Unlog(nz));
                result.Positions[i] = position;
                boundsMin = Vector3.Min(boundsMin, position);
                boundsMax = Vector3.Max(boundsMax, position);

                float sx = version == 2 ? scaleCodebook[scalePixel.R] : Mathf.Lerp(scaleMin[0], scaleMax[0], scalePixel.R / 255f);
                float sy = version == 2 ? scaleCodebook[scalePixel.G] : Mathf.Lerp(scaleMin[1], scaleMax[1], scalePixel.G / 255f);
                float sz = version == 2 ? scaleCodebook[scalePixel.B] : Mathf.Lerp(scaleMin[2], scaleMax[2], scalePixel.B / 255f);
                result.Scales[i] = new Vector3(Mathf.Exp(sx), Mathf.Exp(sy), Mathf.Exp(sz));

                Quaternion q = DecodeQuaternion(quatPixel);
                var qWxyz = new Vector4(q.w, qxSign * q.x, qySign * q.y, qzSign * q.z).normalized;
                result.Rotations[i] = qWxyz;

                float dcR = version == 2 ? sh0Codebook[colorPixel.R] : Mathf.Lerp(sh0Min[0], sh0Max[0], colorPixel.R / 255f);
                float dcG = version == 2 ? sh0Codebook[colorPixel.G] : Mathf.Lerp(sh0Min[1], sh0Max[1], colorPixel.G / 255f);
                float dcB = version == 2 ? sh0Codebook[colorPixel.B] : Mathf.Lerp(sh0Min[2], sh0Max[2], colorPixel.B / 255f);
                float alpha = version == 2 ? colorPixel.A / 255f : Sigmoid(Mathf.Lerp(sh0Min[3], sh0Max[3], colorPixel.A / 255f));
                result.Colors[i] = new Vector4(dcR, dcG, dcB, alpha);

                if (shBands > 0)
                {
                    Rgba32 labelPixel = shLabels.Pixel(x, y, flipRows);
                    int label = labelPixel.R | (labelPixel.G << 8);
                    int paletteX = label % 64;
                    int paletteY = label / 64;
                    if (paletteY >= shCentroids.Height)
                        throw new FormatException($"SOG SH label {label} is outside the centroid palette.");
                    for (int k = 0; k < shCoefficients; k++)
                    {
                        Rgba32 coefficient = shCentroids.Pixel(paletteX * shCoefficients + k, paletteY, flipRows);
                        float r = version == 2 ? shCodebook[coefficient.R] : Mathf.Lerp(shMin, shMax, coefficient.R / 255f);
                        float g = version == 2 ? shCodebook[coefficient.G] : Mathf.Lerp(shMin, shMax, coefficient.G / 255f);
                        float b = version == 2 ? shCodebook[coefficient.B] : Mathf.Lerp(shMin, shMax, coefficient.B / 255f);
                        float sign = ShSign(sourceCoordinates, k);
                        result.SHs[i * shCoefficients + k] = sign * new Vector3(r, g, b);
                    }
                }

                if ((i & 0x3fff) == 0) progress?.Invoke("Decoding SOG splats", i / (float)count);
            }

            result.Bounds = new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin);
            progress?.Invoke("Decoding SOG splats", 1f);
            return result;
        }

        static Dictionary<string, object> RequiredObject(Dictionary<string, object> root, string name) =>
            root.TryGetValue(name, out object value)
                ? JsonValues.Object(value, name)
                : throw new FormatException($"Required SOG section '{name}' is missing.");

        static string FileAt(Dictionary<string, object> section, int index, string name)
        {
            string[] files = section.StringArray("files");
            if (index < 0 || index >= files.Length || string.IsNullOrWhiteSpace(files[index]))
                throw new FormatException($"SOG {name}.files[{index}] is missing.");
            return files[index];
        }

        static float[] Codebook(Dictionary<string, object> section, string name)
        {
            if (!section.TryGetValue("codebook", out object raw))
                throw new FormatException($"Required SOG {name} is missing.");
            List<object> array = JsonValues.Array(raw, name);
            if (array.Count != 256) throw new FormatException($"SOG {name} must contain 256 entries.");
            var result = new float[256];
            for (int i = 0; i < result.Length; i++)
                result[i] = array[i] == null ? float.NaN : JsonValues.Float(array[i], $"{name}[{i}]");
            if (float.IsNaN(result[0]))
                result[0] = result[1] + (result[1] - result[255]) / 255f;
            for (int i = 0; i < result.Length; i++)
                if (!float.IsFinite(result[i])) throw new FormatException($"SOG {name}[{i}] is not finite.");
            return result;
        }

        static float Scalar(Dictionary<string, object> section, string name)
        {
            if (!section.TryGetValue(name, out object value))
                throw new FormatException($"Required SOG shN.{name} is missing.");
            if (value is List<object> list)
            {
                if (list.Count == 0) throw new FormatException($"SOG shN.{name} is empty.");
                value = list[0];
            }
            return JsonValues.Float(value, $"shN.{name}");
        }

        static DecodedImage DecodeImage(Func<string, byte[]> resolver, string filename)
        {
            byte[] bytes = resolver(filename) ?? throw new FileNotFoundException($"SOG image '{filename}' was not resolved.");
            int width, height;
            Texture2DExt.GetWebPDimensions(bytes, out width, out height);
            byte[] rgba = Texture2DExt.LoadRGBAFromWebP(bytes, ref width, ref height, false, out Error error);
            if (error != Error.Success || rgba == null)
                throw new InvalidDataException($"Could not decode SOG WebP '{filename}': {error}.");
            return new DecodedImage(width, height, rgba);
        }

        static void ValidateDimensions(params DecodedImage[] images)
        {
            int width = images[0].Width;
            int height = images[0].Height;
            if (images.Any(image => image.Width != width || image.Height != height))
                throw new FormatException("All base SOG images must have matching dimensions.");
        }

        static bool DetermineVerticalFlip(DecodedImage quaternions, int count)
        {
            int samples = Mathf.Min(128, count);
            int step = Mathf.Max(1, count / samples);
            int flipped = 0, direct = 0;
            for (int index = 0; index < count; index += step)
            {
                int x = index % quaternions.Width;
                int y = index / quaternions.Width;
                if (quaternions.Pixel(x, y, true).A >= 252) flipped++;
                if (quaternions.Pixel(x, y, false).A >= 252) direct++;
            }
            return flipped >= direct;
        }

        static Quaternion DecodeQuaternion(Rgba32 pixel)
        {
            float a = (pixel.R / 255f - 0.5f) * Sqrt2;
            float b = (pixel.G / 255f - 0.5f) * Sqrt2;
            float c = (pixel.B / 255f - 0.5f) * Sqrt2;
            float missing = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));
            return (pixel.A - 252) switch
            {
                0 => new Quaternion(a, b, c, missing),
                1 => new Quaternion(missing, b, c, a),
                2 => new Quaternion(b, missing, c, a),
                3 => new Quaternion(b, c, missing, a),
                _ => throw new FormatException($"Invalid SOG quaternion mode byte {pixel.A}.")
            };
        }

        static float ShSign(SourceCoordinates coordinates, int coefficient)
        {
            int band = coefficient < 3 ? 1 : coefficient < 8 ? 2 : 3;
            int bandStart = band * band - 1;
            return GsplatUtils.ShSign(coordinates, band, coefficient - bandStart);
        }

        static float Unlog(float value) => Mathf.Sign(value) * (Mathf.Exp(Mathf.Abs(value)) - 1f);
        static float Sigmoid(float value) => value >= 0f
            ? 1f / (1f + Mathf.Exp(-value))
            : Mathf.Exp(value) / (1f + Mathf.Exp(value));
        static string Normalize(string path) => path.Replace('\\', '/').TrimStart('.', '/');

        readonly struct DecodedImage
        {
            public readonly int Width;
            public readonly int Height;
            readonly byte[] m_rgba;

            public DecodedImage(int width, int height, byte[] rgba)
            {
                Width = width;
                Height = height;
                m_rgba = rgba;
                if (rgba == null || rgba.Length < width * height * 4)
                    throw new InvalidDataException("Decoded WebP buffer is smaller than its dimensions.");
            }

            public Rgba32 Pixel(int x, int y, bool flipRows)
            {
                if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                    throw new IndexOutOfRangeException($"SOG pixel ({x},{y}) is outside {Width}x{Height}.");
                int row = flipRows ? Height - 1 - y : y;
                int offset = (row * Width + x) * 4;
                return new Rgba32(m_rgba[offset], m_rgba[offset + 1], m_rgba[offset + 2], m_rgba[offset + 3]);
            }
        }

        readonly struct Rgba32
        {
            public readonly byte R, G, B, A;
            public Rgba32(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        }
    }
}
