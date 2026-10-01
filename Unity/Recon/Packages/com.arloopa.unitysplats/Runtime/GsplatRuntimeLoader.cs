// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using Gsplat.Formats;
using UnityEngine;

namespace Gsplat
{
    /// <summary>The Gaussian-splat containers understood by <see cref="GsplatRuntimeLoader"/>.</summary>
    public enum GsplatFileFormat
    {
        Auto,
        Ply,
        Sog,
        Spz,
        Glb
    }

    /// <summary>
    /// Decodes PlayCanvas-compatible Gaussian-splat files at runtime and creates an in-memory
    /// <see cref="GsplatAsset"/>. Call this API on Unity's main thread, assign the returned asset
    /// to a <see cref="GsplatRenderer"/>, and destroy the asset when it is no longer needed.
    /// </summary>
    public static class GsplatRuntimeLoader
    {
        public static GsplatAsset LoadFile(
            string path,
            CompressionMode compression = CompressionMode.Spark,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified,
            ProgressCallback progress = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A splat file path is required.", nameof(path));

            using var stream = File.OpenRead(path);
            GsplatAsset asset = Load(stream, FormatFromPath(path), compression, sourceCoordinates, progress);
            asset.name = Path.GetFileNameWithoutExtension(path);
            return asset;
        }

        public static GsplatAsset Load(
            byte[] bytes,
            GsplatFileFormat format = GsplatFileFormat.Auto,
            CompressionMode compression = CompressionMode.Spark,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified,
            ProgressCallback progress = null)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, false);
            return Load(stream, format, compression, sourceCoordinates, progress);
        }

        public static GsplatAsset Load(
            byte[] bytes,
            string extensionOrFormat,
            CompressionMode compression = CompressionMode.Spark,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified,
            ProgressCallback progress = null)
        {
            return Load(bytes, ParseFormat(extensionOrFormat), compression, sourceCoordinates, progress);
        }

        public static GsplatAsset Load(
            Stream stream,
            GsplatFileFormat format = GsplatFileFormat.Auto,
            CompressionMode compression = CompressionMode.Spark,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified,
            ProgressCallback progress = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("The splat stream must be readable.", nameof(stream));

            // ZIP offsets and GLB buffer offsets are relative to byte zero. Preserve the zero-copy
            // path for normal files/MemoryStreams, but normalize arbitrary or non-seekable streams.
            MemoryStream ownedStream = null;
            Stream input = stream;
            if (!stream.CanSeek || stream.Position != 0)
            {
                ownedStream = new MemoryStream();
                stream.CopyTo(ownedStream);
                ownedStream.Position = 0;
                input = ownedStream;
            }

            try
            {
                if (format == GsplatFileFormat.Auto)
                    format = DetectFormat(input);

                SourceCoordinates coordinates = ResolveCoordinates(format, sourceCoordinates);
                GsplatDecodedData decoded = Decode(input, format, coordinates, progress);
                progress?.Invoke("Packing Unity splat asset", 0f);

                GsplatAsset asset = CreateAsset(compression);
                try
                {
                    asset.name = $"Runtime {format} Gsplat";
                    asset.LoadFromDecoded(decoded, progress);
                    progress?.Invoke("Gaussian splat ready", 1f);
                    return asset;
                }
                catch
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(asset);
                    else
                        UnityEngine.Object.DestroyImmediate(asset);
                    throw;
                }
            }
            finally
            {
                ownedStream?.Dispose();
            }
        }

        /// <summary>Loads an unpacked SOG <c>meta.json</c> with a caller-provided file resolver.</summary>
        public static GsplatAsset LoadUnpackedSog(
            string metadata,
            Func<string, byte[]> resolveFile,
            CompressionMode compression = CompressionMode.Spark,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified,
            ProgressCallback progress = null)
        {
            SourceCoordinates coordinates = ResolveCoordinates(GsplatFileFormat.Sog, sourceCoordinates);
            GsplatDecodedData decoded = PlayCanvasSogReader.ReadUnpacked(
                metadata, resolveFile, coordinates, progress);
            GsplatAsset asset = CreateAsset(compression);
            try
            {
                asset.name = "Runtime SOG Gsplat";
                asset.LoadFromDecoded(decoded, progress);
                return asset;
            }
            catch
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(asset);
                else
                    UnityEngine.Object.DestroyImmediate(asset);
                throw;
            }
        }

        public static GsplatFileFormat FormatFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A path is required.", nameof(path));
            return ParseFormat(Path.GetExtension(path));
        }

        public static GsplatFileFormat ParseFormat(string extensionOrFormat)
        {
            if (string.IsNullOrWhiteSpace(extensionOrFormat))
                return GsplatFileFormat.Auto;

            string value = extensionOrFormat.Trim().TrimStart('.').ToLowerInvariant();
            return value switch
            {
                "auto" => GsplatFileFormat.Auto,
                "ply" => GsplatFileFormat.Ply,
                "sog" => GsplatFileFormat.Sog,
                "spz" => GsplatFileFormat.Spz,
                "glb" => GsplatFileFormat.Glb,
                _ => throw new NotSupportedException(
                    $"Unsupported Gaussian-splat format '{extensionOrFormat}'. Expected PLY, SOG, SPZ, or GLB.")
            };
        }

        public static SourceCoordinates ResolveCoordinates(
            GsplatFileFormat format,
            SourceCoordinates sourceCoordinates)
        {
            if (sourceCoordinates != SourceCoordinates.Unspecified)
                return sourceCoordinates;
            return format == GsplatFileFormat.Glb ? SourceCoordinates.LUF : SourceCoordinates.RUB;
        }

        static GsplatDecodedData Decode(
            Stream stream,
            GsplatFileFormat format,
            SourceCoordinates sourceCoordinates,
            ProgressCallback progress)
        {
            return format switch
            {
                GsplatFileFormat.Ply => PlayCanvasPlyReader.Read(stream, sourceCoordinates, progress),
                GsplatFileFormat.Sog => PlayCanvasSogReader.ReadBundle(stream, sourceCoordinates, progress),
                GsplatFileFormat.Spz => PlayCanvasSpzReader.Read(stream, sourceCoordinates, progress),
                GsplatFileFormat.Glb => PlayCanvasGlbReader.Read(stream, sourceCoordinates, progress),
                _ => throw new NotSupportedException($"Cannot decode Gaussian-splat format {format}.")
            };
        }

        static GsplatAsset CreateAsset(CompressionMode compression)
        {
            return compression switch
            {
                CompressionMode.Uncompressed => ScriptableObject.CreateInstance<GsplatAssetUncompressed>(),
                CompressionMode.Spark => ScriptableObject.CreateInstance<GsplatAssetSpark>(),
                _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, null)
            };
        }

        static GsplatFileFormat DetectFormat(Stream stream)
        {
            long position = stream.Position;
            var magic = new byte[4];
            int count = stream.Read(magic, 0, magic.Length);
            stream.Position = position;
            if (count < 2)
                throw new InvalidDataException("The Gaussian-splat stream is too short to identify.");

            if (count == 4 && magic[0] == (byte)'p' && magic[1] == (byte)'l' && magic[2] == (byte)'y')
                return GsplatFileFormat.Ply;
            if (count == 4 && magic[0] == (byte)'g' && magic[1] == (byte)'l' &&
                magic[2] == (byte)'T' && magic[3] == (byte)'F')
                return GsplatFileFormat.Glb;
            if (count == 4 && magic[0] == (byte)'N' && magic[1] == (byte)'G' &&
                magic[2] == (byte)'S' && magic[3] == (byte)'P')
                return GsplatFileFormat.Spz;
            if (magic[0] == 0x1f && magic[1] == 0x8b)
                return GsplatFileFormat.Spz;
            if (magic[0] == (byte)'P' && magic[1] == (byte)'K')
                return GsplatFileFormat.Sog;

            throw new InvalidDataException(
                "The stream is not a recognized PLY, SOG, SPZ, or Gaussian-splat GLB file.");
        }
    }
}
