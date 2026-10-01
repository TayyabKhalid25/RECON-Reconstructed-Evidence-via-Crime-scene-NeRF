// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

// SPZ format specification: https://github.com/nianticlabs/spz
// Decoding logic ported from nianticlabs/spz/src/cc/load-spz.cc

using System;
using System.IO;
using System.IO.Compression;
using Gsplat.Internal;
using UnityEngine;

namespace Gsplat
{
    internal struct SpzHeader
    {
        public uint Version;
        public uint NumPoints;
        public byte ShDegree;
        public byte FractionalBits;
        public byte Flags;
    }

    // Values used by SPZ_ADOBE_coordinate_system (0xADBE0003). Values 1-8 use
    // the ordinary XYZ axis order; values 9-16 use the rotated family where Y
    // and Z are exchanged by a quarter turn around X.
    internal enum SpzCoordinateSystem : uint
    {
        Unspecified = 0,
        LDB = 1,
        RDB = 2,
        LUB = 3,
        RUB = 4,
        LDF = 5,
        RDF = 6,
        LUF = 7,
        RUF = 8,
        LFD = 9,
        RFD = 10,
        LFU = 11,
        RFU = 12,
        LBD = 13,
        RBD = 14,
        LBU = 15,
        RBU = 16,
    }

    internal class SpzData
    {
        public SpzHeader Header;
        // Null means the coordinate-system extension was absent. A present
        // Unspecified value intentionally resolves to the SPZ default (RUB).
        public SpzCoordinateSystem? StorageCoordinates;
        public byte[] Positions;  // v1: NumPoints*6 (float16 xyz), v2+: NumPoints*9 (24-bit fixed xyz)
        public byte[] Alphas;     // NumPoints * 1
        public byte[] Colors;     // NumPoints * 3 (quantized SH DC per channel)
        public byte[] Scales;     // NumPoints * 3 (quantized log-scale per axis)
        public byte[] Rotations;  // v1-2: NumPoints*3 (xyz as unsigned bytes, offset-encoded), v3+: NumPoints*4 (smallest-three)
        public byte[] SH;         // NumPoints * ShDim * 3, layout per point: [R0,G0,B0, R1,G1,B1, ..., R_{Dim-1},G_{Dim-1},B_{Dim-1}]
    }

    internal static class SpzLoader
    {
        const uint SpzMagic = 0x5053474e; // "NGSP" little-endian
        const uint AdobeCoordinateSystemExtension = 0xADBE0003;
        const byte FlagHasExtensions = 0x2;
        const int V4HeaderSize = 32;
        // Stream index → human-readable name, matching the order written by the C++ writer
        // (load-spz.cc serializeNgsp). Used purely for error messages.
        static readonly string[] V4StreamNames = { "positions", "alphas", "colors", "scales", "rotations", "sh" };
        // Hard cap on splat count. 128M is well above a practical in-memory Unity asset;
        // per-stream checked sizing below applies the tighter 2 GiB managed-array limit.
        const uint MaxSupportedPointCount = 1u << 27;
        // Matches the value hardcoded by the upstream C++ writer; reject anything else
        // so a malformed/hostile fractionalBits doesn't silently rescale positions.
        const byte MaxFractionalBits = 12;

        const float ColorScale = 0.15f;
        const float Sqrt1_2 = 0.70710678118f;

        // Number of SH coefficients per color channel, excluding DC (degree 0).
        // Matches GsplatUtils.SHBandsToCoefficientCount.
        public static int ShDim(byte degree) => degree * (degree + 2);

        static int ValidatePointCount(uint numPoints)
        {
            if (numPoints > MaxSupportedPointCount)
                throw new NotSupportedException(
                    $"SPZ: point count {numPoints} exceeds supported maximum {MaxSupportedPointCount}");
            return (int)numPoints;
        }

        static void ValidateFractionalBits(byte fractionalBits)
        {
            if (fractionalBits > MaxFractionalBits)
                throw new NotSupportedException(
                    $"SPZ: fractionalBits {fractionalBits} exceeds supported maximum {MaxFractionalBits}");
        }

        public static SpzData Load(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            return Load(fs);
        }

        static void ValidateShDegree(byte shDegree, string container)
        {
            if (shDegree > 4)
                throw new NotSupportedException(
                    $"{container} SH degree {shDegree} is out of spec (max 4).");
        }

        static int CheckedStreamSize(int pointCount, int bytesPerPoint, string streamName)
        {
            try
            {
                return checked(pointCount * bytesPerPoint);
            }
            catch (OverflowException exception)
            {
                throw new NotSupportedException(
                    $"SPZ {streamName} stream exceeds the supported 2 GiB managed-array limit.", exception);
            }
        }

        public static SpzData Load(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var stream = new MemoryStream(bytes, writable: false);
            return Load(stream);
        }

        public static SpzData Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("SPZ input stream must be readable and seekable.", nameof(stream));
            if (stream.Length - stream.Position < 4)
                throw new InvalidDataException("Input is too short to be an SPZ file.");

            long start = stream.Position;
            var sniff = ReadExact(stream, 4);
            stream.Position = start;

            // v1–3: single gzip stream wrapping the 16-byte SPZ header + sequential attribute streams.
            if (sniff[0] == 0x1F && sniff[1] == 0x8B) return LoadGzip(stream);

            // v4: plaintext 32-byte header with "NGSP" magic, then optional extensions, TOC, zstd-per-attribute streams.
            uint magic = BitConverter.ToUInt32(sniff, 0);
            if (magic == SpzMagic) return LoadZstd(stream);

            throw new NotSupportedException("Input is not a recognized SPZ file (no gzip or NGSP magic).");
        }

        // v1–3: gzip-wrapped payload.
        static SpzData LoadGzip(Stream fs)
        {
            using var gz = new GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);

            var headerBytes = ReadExact(gz, 16);

            var header = ParseHeaderGzip(headerBytes);
            var data = ReadStreams(gz, header);
            if ((header.Flags & FlagHasExtensions) != 0)
                data.StorageCoordinates = ReadExtensions(gz, null, "SPZ gzip");
            return data;
        }

        static SpzHeader ParseHeaderGzip(byte[] b)
        {
            uint magic = BitConverter.ToUInt32(b, 0);
            if (magic != SpzMagic)
                throw new InvalidDataException($"SPZ: bad magic 0x{magic:X8}, expected 0x{SpzMagic:X8}");

            uint version = BitConverter.ToUInt32(b, 4);
            if (version < 1 || version > 3)
                throw new NotSupportedException(
                    $"SPZ version {version} is not supported in the gzip container (expected 1–3).");

            byte fractionalBits = b[13];
            ValidateShDegree(b[12], "SPZ gzip");
            ValidateFractionalBits(fractionalBits);

            return new SpzHeader
            {
                Version = version,
                NumPoints = BitConverter.ToUInt32(b, 8),
                ShDegree = b[12],
                FractionalBits = fractionalBits,
                Flags = b[14],
            };
        }

        static SpzData ReadStreams(Stream stream, SpzHeader h)
        {
            int n = ValidatePointCount(h.NumPoints);
            bool float16Pos = h.Version == 1;
            bool smallestThree = h.Version >= 3;
            int shDim = ShDim(h.ShDegree);
            int positionBytes = CheckedStreamSize(n, float16Pos ? 6 : 9, "position");
            int colorBytes = CheckedStreamSize(n, 3, "color");
            int scaleBytes = CheckedStreamSize(n, 3, "scale");
            int rotationBytes = CheckedStreamSize(n, smallestThree ? 4 : 3, "rotation");
            int shBytes = CheckedStreamSize(n, checked(shDim * 3), "SH");

            return new SpzData
            {
                Header = h,
                Positions = ReadExact(stream, positionBytes),
                Alphas = ReadExact(stream, n),
                Colors = ReadExact(stream, colorBytes),
                Scales = ReadExact(stream, scaleBytes),
                Rotations = ReadExact(stream, rotationBytes),
                SH = ReadExact(stream, shBytes),
            };
        }

        // v4: 32-byte plaintext header, optional extensions, TOC, then N zstd-compressed streams.
        // Stream order (numStreams == 6): positions, alphas, colors, scales, rotations, sh.
        static SpzData LoadZstd(Stream fs)
        {
            var hb = ReadExact(fs, V4HeaderSize);
            uint magic = BitConverter.ToUInt32(hb, 0);
            if (magic != SpzMagic)
                throw new InvalidDataException($"SPZ v4: bad magic 0x{magic:X8}");

            uint version = BitConverter.ToUInt32(hb, 4);
            if (version != 4)
                throw new NotSupportedException($"SPZ NGSP container with version {version} is not supported (expected 4).");

            uint numPoints = BitConverter.ToUInt32(hb, 8);
            byte shDegree = hb[12];
            byte fractionalBits = hb[13];
            byte flags = hb[14];
            byte numStreams = hb[15];
            uint tocByteOffset = BitConverter.ToUInt32(hb, 16);
            // hb[20..32] are reserved (must be zero); not validated.

            ValidateShDegree(shDegree, "SPZ v4");
            ValidateFractionalBits(fractionalBits);
            int expectedStreams = shDegree > 0 ? 6 : 5;
            if (numStreams != expectedStreams)
                throw new NotSupportedException(
                    $"SPZ v4 with {numStreams} streams is not supported (expected {expectedStreams}).");
            long tocBytes = (long)expectedStreams * 16;
            if (tocByteOffset < V4HeaderSize)
                throw new InvalidDataException($"SPZ v4: tocByteOffset {tocByteOffset} overlaps header.");
            if (tocByteOffset + tocBytes > fs.Length)
                throw new InvalidDataException(
                    $"SPZ v4: tocByteOffset {tocByteOffset} + TOC size {tocBytes} is past EOF (file length {fs.Length}).");

            SpzCoordinateSystem? storageCoordinates = null;
            if ((flags & FlagHasExtensions) != 0)
            {
                fs.Position = V4HeaderSize;
                storageCoordinates = ReadExtensions(
                    fs, tocByteOffset - V4HeaderSize, "SPZ v4");
            }

            fs.Position = tocByteOffset;
            var toc = ReadExact(fs, (int)tocBytes);

            int n = ValidatePointCount(numPoints);
            int shDim = ShDim(shDegree);
            var expectedSizes = new[]
            {
                (long)n * 9,           // positions: 24-bit fixed (v4 always)
                (long)n,               // alphas
                (long)n * 3,           // colors
                (long)n * 3,           // scales
                (long)n * 4,           // rotations: smallest-three (v4 always)
                (long)n * shDim * 3,   // sh
            };

            var streams = new byte[expectedStreams][];
            using var zstd = new ZstdDecoderSession();
            for (int i = 0; i < expectedStreams; i++)
            {
                string name = V4StreamNames[i];
                ulong compressedSize = BitConverter.ToUInt64(toc, i * 16);
                ulong uncompressedSize = BitConverter.ToUInt64(toc, i * 16 + 8);
                if (uncompressedSize != (ulong)expectedSizes[i])
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): TOC uncompressedSize {uncompressedSize} != expected {expectedSizes[i]}");
                if (compressedSize > int.MaxValue || uncompressedSize > int.MaxValue)
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): size exceeds 2GB limit");
                // zstd's worst-case expansion is ZSTD_COMPRESSBOUND ≈ src + src/128 + ~512 bytes.
                // Add 4 KB of headroom for frame metadata (header, dictionary id, checksum,
                // multi-frame splits). Anything beyond this is a malformed or hostile file
                // claiming a small uncompressed size but a huge compressed payload.
                ulong maxCompressed = uncompressedSize + uncompressedSize / 128 + 4096;
                if (compressedSize > maxCompressed)
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): compressedSize {compressedSize} exceeds zstd worst-case bound " +
                        $"{maxCompressed} for uncompressedSize {uncompressedSize}");

                // Degenerate empty stream (numPoints == 0). Skip the zstd round-trip entirely
                // rather than asking the decoder to write into a zero-length destination, which
                // ZstdSharp has historically been brittle about.
                if (uncompressedSize == 0)
                {
                    fs.Seek((long)compressedSize, SeekOrigin.Current);
                    streams[i] = Array.Empty<byte>();
                    continue;
                }

                var compressed = ReadExact(fs, (int)compressedSize);
                var dst = new byte[(int)uncompressedSize];
                int written;
                try
                {
                    written = zstd.Decompress(compressed, dst);
                }
                catch (Exception e)
                {
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): zstd decompression failed: {e.Message}", e);
                }
                if (written != (int)uncompressedSize)
                    throw new InvalidDataException(
                        $"SPZ v4 stream {i} ({name}): zstd produced {written} bytes, expected {uncompressedSize}");
                streams[i] = dst;
            }

            return new SpzData
            {
                Header = new SpzHeader
                {
                    Version = version,
                    NumPoints = numPoints,
                    ShDegree = shDegree,
                    FractionalBits = fractionalBits,
                    Flags = flags,
                },
                StorageCoordinates = storageCoordinates,
                Positions = streams[0],
                Alphas = streams[1],
                Colors = streams[2],
                Scales = streams[3],
                Rotations = streams[4],
                SH = expectedStreams > 5 ? streams[5] : Array.Empty<byte>(),
            };
        }

        // Parse the SPZ extension stream: repeated little-endian
        // [type:u32][byteLength:u32][payload] records. For v4, remainingBytes
        // bounds the plaintext header zone; legacy gzip records run to EOF.
        static SpzCoordinateSystem? ReadExtensions(
            Stream stream, long? remainingBytes, string container)
        {
            SpzCoordinateSystem? coordinateSystem = null;
            long remaining = remainingBytes ?? -1;
            var recordHeader = new byte[8];

            while (remaining != 0)
            {
                if (remaining > 0 && remaining < recordHeader.Length)
                    throw new InvalidDataException(
                        $"{container}: truncated extension record header ({remaining} bytes remain).");

                int first = stream.ReadByte();
                if (first < 0)
                {
                    if (remaining < 0) break;
                    throw new EndOfStreamException($"{container}: extension block ended unexpectedly.");
                }

                recordHeader[0] = (byte)first;
                ReadExactInto(stream, recordHeader, 1, recordHeader.Length - 1);
                if (remaining > 0) remaining -= recordHeader.Length;

                uint type = BitConverter.ToUInt32(recordHeader, 0);
                uint payloadLength = BitConverter.ToUInt32(recordHeader, 4);
                if (remaining >= 0 && payloadLength > (ulong)remaining)
                    throw new InvalidDataException(
                        $"{container}: extension 0x{type:X8} payload length {payloadLength} exceeds the {remaining} bytes left in the extension block.");

                if (type == AdobeCoordinateSystemExtension)
                {
                    if (payloadLength != sizeof(uint))
                        throw new InvalidDataException(
                            $"{container}: SPZ_ADOBE_coordinate_system payload is {payloadLength} bytes; expected 4.");

                    var payload = ReadExact(stream, sizeof(uint));
                    uint value = BitConverter.ToUInt32(payload, 0);
                    if (value > (uint)SpzCoordinateSystem.RBU)
                        throw new InvalidDataException(
                            $"{container}: SPZ_ADOBE_coordinate_system value {value} is not defined.");

                    // Match the upstream library: the first extension of a type wins.
                    if (!coordinateSystem.HasValue)
                        coordinateSystem = (SpzCoordinateSystem)value;
                }
                else
                {
                    SkipExact(stream, payloadLength, container, type);
                }

                if (remaining > 0) remaining -= payloadLength;
            }

            return coordinateSystem;
        }

        static void SkipExact(Stream stream, uint count, string container, uint extensionType)
        {
            var scratch = new byte[4096];
            long remaining = count;
            while (remaining > 0)
            {
                int read = stream.Read(scratch, 0, (int)Math.Min(scratch.Length, remaining));
                if (read == 0)
                    throw new EndOfStreamException(
                        $"{container}: extension 0x{extensionType:X8} payload ended unexpectedly.");
                remaining -= read;
            }
        }

        static void ReadExactInto(Stream stream, byte[] destination, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(destination, offset, count);
                if (read == 0) throw new EndOfStreamException("Unexpected end of SPZ payload");
                offset += read;
                count -= read;
            }
        }

        static byte[] ReadExact(Stream stream, int count)
        {
            if (count == 0) return Array.Empty<byte>();
            var buf = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buf, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("Unexpected end of SPZ payload");
                offset += read;
            }
            return buf;
        }

        // Decode 24-bit signed fixed-point XYZ position (v2+).
        public static Vector3 DecodePosition(byte[] positions, int i, byte fractionalBits)
        {
            float scale = 1.0f / (1 << fractionalBits);
            int b = i * 9;
            return new Vector3(
                Fixed24ToFloat(positions, b + 0) * scale,
                Fixed24ToFloat(positions, b + 3) * scale,
                Fixed24ToFloat(positions, b + 6) * scale);
        }

        // Decode float16 XYZ position (v1 only).
        public static Vector3 DecodePositionFloat16(byte[] positions, int i)
        {
            int b = i * 6;
            return new Vector3(
                Mathf.HalfToFloat(BitConverter.ToUInt16(positions, b + 0)),
                Mathf.HalfToFloat(BitConverter.ToUInt16(positions, b + 2)),
                Mathf.HalfToFloat(BitConverter.ToUInt16(positions, b + 4)));
        }

        static float Fixed24ToFloat(byte[] buf, int offset)
        {
            int v = buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
            return v;
        }

        // Decode opacity as logit (pre-sigmoid), matching what PLY stores and PackSplat expects.
        public static float DecodeAlphaLogit(byte[] alphas, int i)
        {
            float a = Mathf.Clamp(alphas[i] / 255.0f, 1e-6f, 1f - 1e-6f);
            return Mathf.Log(a / (1f - a));
        }

        // Decode opacity as [0,1] post-sigmoid value, for GsplatAssetUncompressed.
        public static float DecodeAlphaLinear(byte[] alphas, int i) => alphas[i] / 255.0f;

        // Decode quantized color to raw SH DC coefficient (same form as PLY's f_dc_0/1/2).
        public static Vector3 DecodeColor(byte[] colors, int i)
        {
            int b = i * 3;
            return new Vector3(
                (colors[b + 0] / 255.0f - 0.5f) / ColorScale,
                (colors[b + 1] / 255.0f - 0.5f) / ColorScale,
                (colors[b + 2] / 255.0f - 0.5f) / ColorScale);
        }

        // Decode quantized log-scale to ln(scale), matching PLY's scale_0/1/2.
        public static Vector3 DecodeScaleLog(byte[] scales, int i)
        {
            int b = i * 3;
            return new Vector3(
                scales[b + 0] / 16.0f - 10.0f,
                scales[b + 1] / 16.0f - 10.0f,
                scales[b + 2] / 16.0f - 10.0f);
        }

        public static Quaternion DecodeRotation(byte[] rotations, int i, bool usesSmallestThree)
        {
            return usesSmallestThree
                ? DecodeSmallestThree(rotations, i * 4)
                : DecodeXyz3Bytes(rotations, i * 3);
        }

        // v1-2: xyz stored as unsigned bytes with offset encoding: x = byte/127.5 - 1.0.
        // byte 0 = -1.0, byte 127/128 ≈ 0.0, byte 255 = +1.0. w is derived (always non-negative).
        static Quaternion DecodeXyz3Bytes(byte[] r, int offset)
        {
            float x = r[offset + 0] / 127.5f - 1.0f;
            float y = r[offset + 1] / 127.5f - 1.0f;
            float z = r[offset + 2] / 127.5f - 1.0f;
            float w = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y - z * z));
            return new Quaternion(x, y, z, w);
        }

        // v3+: smallest-three quaternion. 32 bits: 2-bit index of largest component,
        // then three components each as (9-bit magnitude, 1-bit sign). Components are
        // packed high-index-first at LSB: index 3 gets bits[0:9], index 2 gets bits[10:19], etc.
        static Quaternion DecodeSmallestThree(byte[] r, int offset)
        {
            uint comp = r[offset]
                | ((uint)r[offset + 1] << 8)
                | ((uint)r[offset + 2] << 16)
                | ((uint)r[offset + 3] << 24);
            const uint cMask = (1u << 9) - 1u;
            int iLargest = (int)(comp >> 30);
            float[] q = new float[4];
            float sumSq = 0f;

            for (int i = 3; i >= 0; i--)
            {
                if (i == iLargest) continue;
                uint mag = comp & cMask;
                uint neg = (comp >> 9) & 1u;
                comp >>= 10;
                float val = Sqrt1_2 * mag / (float)cMask;
                if (neg == 1) val = -val;
                q[i] = val;
                sumSq += val * val;
            }
            q[iLargest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sumSq));
            return new Quaternion(q[0], q[1], q[2], q[3]);
        }

        // Dequantize a single SH byte: (x - 128) / 128.
        public static float UnquantizeSH(byte[] sh, int byteIndex)
            => (sh[byteIndex] - 128f) / 128f;
    }
}
