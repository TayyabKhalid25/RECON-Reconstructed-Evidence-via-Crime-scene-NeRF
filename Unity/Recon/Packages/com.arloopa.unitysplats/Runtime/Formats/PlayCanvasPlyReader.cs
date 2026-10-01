// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Reads the standard and compressed binary little-endian Gaussian-splat PLY layouts
    /// supported by PlayCanvas. The returned values are fully decoded and converted from
    /// <see cref="SourceCoordinates"/> to Unity's RUF coordinate frame.
    /// </summary>
    public static class PlayCanvasPlyReader
    {
        const int k_MaxHeaderBytes = 1024 * 1024;
        const int k_CompressedChunkSize = 256;
        const float k_ShC0 = 0.28209479177387814f;
        const float k_Sqrt2 = 1.4142135623730951f;

        static readonly string[] k_ChunkPropertyNames =
        {
            "min_x", "min_y", "min_z",
            "max_x", "max_y", "max_z",
            "min_scale_x", "min_scale_y", "min_scale_z",
            "max_scale_x", "max_scale_y", "max_scale_z",
            "min_r", "min_g", "min_b",
            "max_r", "max_g", "max_b"
        };

        static readonly string[] k_PackedVertexPropertyNames =
        {
            "packed_position", "packed_rotation", "packed_scale", "packed_color"
        };

        static readonly string[] k_RequiredVertexPropertyNames =
        {
            "x", "y", "z",
            "f_dc_0", "f_dc_1", "f_dc_2", "opacity",
            "scale_0", "scale_1", "scale_2",
            "rot_0", "rot_1", "rot_2", "rot_3"
        };

        /// <summary>
        /// Decodes a complete PLY byte array. The input array is not retained.
        /// </summary>
        public static GsplatDecodedData Read(
            byte[] bytes,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            using (var stream = new MemoryStream(bytes, false))
                return Read(stream, sourceCoordinates, progress);
        }

        /// <summary>
        /// Decodes a PLY from the stream's current position. The stream remains open.
        /// </summary>
        public static GsplatDecodedData Read(
            Stream stream,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progress = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("The PLY stream must be readable.", nameof(stream));
            if (!Enum.IsDefined(typeof(SourceCoordinates), sourceCoordinates))
                throw new ArgumentOutOfRangeException(nameof(sourceCoordinates));

            progress?.Invoke("Reading PLY header", 0f);
            PlyHeader header = ReadHeader(stream);

            if (stream.CanSeek && stream.Length - stream.Position < header.DataByteCount)
                throw new InvalidDataException(
                    $"PLY payload is truncated: expected {header.DataByteCount} bytes but only " +
                    $"{stream.Length - stream.Position} remain.");

            var reporter = new ProgressReporter(progress, header.DataByteCount);
            using (var input = new PlyBinaryReader(stream))
            {
                GsplatDecodedData result;
                if (IsCompressedCandidate(header))
                    result = ReadCompressed(input, header, sourceCoordinates, reporter);
                else
                    result = ReadStandard(input, header, sourceCoordinates, reporter);

                if (input.BytesRead != header.DataByteCount)
                    throw new InvalidDataException(
                        $"PLY payload layout mismatch: consumed {input.BytesRead} of " +
                        $"{header.DataByteCount} declared bytes.");

                result.Validate();
                reporter.Report("PLY decoded", input.BytesRead, true);
                return result;
            }
        }

        static GsplatDecodedData ReadStandard(
            PlyBinaryReader input,
            PlyHeader header,
            SourceCoordinates sourceCoordinates,
            ProgressReporter reporter)
        {
            PlyElement vertex = header.FindElement("vertex");
            if (vertex == null)
                throw new InvalidDataException("Standard Gaussian-splat PLY is missing the vertex element.");

            Dictionary<string, int> propertyIndexes = BuildPropertyIndex(vertex);
            for (int i = 0; i < k_RequiredVertexPropertyNames.Length; ++i)
            {
                if (!propertyIndexes.ContainsKey(k_RequiredVertexPropertyNames[i]))
                    throw new InvalidDataException(
                        $"Standard Gaussian-splat PLY is missing vertex property " +
                        $"'{k_RequiredVertexPropertyNames[i]}'.");
            }

            int[] shPropertyIndexes;
            byte shBands = GetStandardShLayout(vertex, out shPropertyIndexes);
            var result = new GsplatDecodedData(vertex.Count, shBands, ParseAntialiased(header.Comments));
            var transform = new CoordinateTransform(sourceCoordinates, shBands);

            int xIndex = propertyIndexes["x"];
            int yIndex = propertyIndexes["y"];
            int zIndex = propertyIndexes["z"];
            int dc0Index = propertyIndexes["f_dc_0"];
            int dc1Index = propertyIndexes["f_dc_1"];
            int dc2Index = propertyIndexes["f_dc_2"];
            int opacityIndex = propertyIndexes["opacity"];
            int scale0Index = propertyIndexes["scale_0"];
            int scale1Index = propertyIndexes["scale_1"];
            int scale2Index = propertyIndexes["scale_2"];
            int rot0Index = propertyIndexes["rot_0"];
            int rot1Index = propertyIndexes["rot_1"];
            int rot2Index = propertyIndexes["rot_2"];
            int rot3Index = propertyIndexes["rot_3"];
            int shCoefficientCount = GsplatUtils.SHBandsToCoefficientCount(shBands);
            double[] values = new double[vertex.Properties.Count];
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            for (int elementIndex = 0; elementIndex < header.Elements.Count; ++elementIndex)
            {
                PlyElement element = header.Elements[elementIndex];
                string phase = element == vertex ? "Reading PLY vertices" : $"Skipping PLY element '{element.Name}'";

                if (element != vertex)
                {
                    input.SkipExactly(checked((long)element.Count * element.RecordByteCount), reporter, phase);
                    reporter.Report(phase, input.BytesRead);
                    continue;
                }

                for (int splat = 0; splat < vertex.Count; ++splat)
                {
                    for (int property = 0; property < vertex.Properties.Count; ++property)
                        values[property] = input.ReadScalar(vertex.Properties[property].Type);

                    Vector3 position = new Vector3(
                        ToFiniteFloat(values[xIndex], "x", splat) * transform.PositionX,
                        ToFiniteFloat(values[yIndex], "y", splat) * transform.PositionY,
                        ToFiniteFloat(values[zIndex], "z", splat) * transform.PositionZ);
                    result.Positions[splat] = position;
                    Encapsulate(ref bounds, ref hasBounds, position);

                    float dc0 = ToFiniteFloat(values[dc0Index], "f_dc_0", splat);
                    float dc1 = ToFiniteFloat(values[dc1Index], "f_dc_1", splat);
                    float dc2 = ToFiniteFloat(values[dc2Index], "f_dc_2", splat);
                    double opacity = RequireFinite(values[opacityIndex], "opacity", splat);
                    result.Colors[splat] = new Vector4(dc0, dc1, dc2, ActivateOpacity(opacity));

                    result.Scales[splat] = new Vector3(
                        ExpScale(values[scale0Index], "scale_0", splat),
                        ExpScale(values[scale1Index], "scale_1", splat),
                        ExpScale(values[scale2Index], "scale_2", splat));

                    result.Rotations[splat] = NormalizeRotation(
                        values[rot0Index],
                        values[rot1Index] * transform.RotationX,
                        values[rot2Index] * transform.RotationY,
                        values[rot3Index] * transform.RotationZ,
                        splat);

                    for (int coefficient = 0; coefficient < shCoefficientCount; ++coefficient)
                    {
                        float sign = transform.ShSigns[coefficient];
                        float r = ToFiniteFloat(values[shPropertyIndexes[coefficient]],
                            $"f_rest_{coefficient}", splat);
                        float g = ToFiniteFloat(values[shPropertyIndexes[shCoefficientCount + coefficient]],
                            $"f_rest_{shCoefficientCount + coefficient}", splat);
                        float b = ToFiniteFloat(values[shPropertyIndexes[2 * shCoefficientCount + coefficient]],
                            $"f_rest_{2 * shCoefficientCount + coefficient}", splat);
                        result.SHs[splat * shCoefficientCount + coefficient] =
                            new Vector3(r * sign, g * sign, b * sign);
                    }

                    if ((splat & 1023) == 1023)
                        reporter.Report(phase, input.BytesRead);
                }

                reporter.Report(phase, input.BytesRead);
            }

            result.Bounds = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);
            return result;
        }

        static GsplatDecodedData ReadCompressed(
            PlyBinaryReader input,
            PlyHeader header,
            SourceCoordinates sourceCoordinates,
            ProgressReporter reporter)
        {
            ValidateCompressedHeader(header);

            PlyElement chunk = header.Elements[0];
            PlyElement vertex = header.Elements[1];
            PlyElement sh = header.Elements.Count == 3 ? header.Elements[2] : null;
            int chunkPropertyCount = chunk.Properties.Count;
            byte shBands = sh == null ? (byte)0 : ShBandsFromRawPropertyCount(sh.Properties.Count);
            int shCoefficientCount = GsplatUtils.SHBandsToCoefficientCount(shBands);
            var result = new GsplatDecodedData(vertex.Count, shBands, ParseAntialiased(header.Comments));
            var transform = new CoordinateTransform(sourceCoordinates, shBands);

            float[] chunks = new float[checked(chunk.Count * chunkPropertyCount)];
            for (int chunkIndex = 0; chunkIndex < chunk.Count; ++chunkIndex)
            {
                int offset = chunkIndex * chunkPropertyCount;
                for (int property = 0; property < chunkPropertyCount; ++property)
                {
                    float value = input.ReadSingle();
                    if (!IsFinite(value))
                        throw new InvalidDataException(
                            $"Compressed PLY chunk {chunkIndex} property " +
                            $"'{chunk.Properties[property].Name}' is not finite.");
                    chunks[offset + property] = value;
                }

                ValidateChunkRanges(chunks, offset, chunkPropertyCount, chunkIndex);
                if ((chunkIndex & 255) == 255)
                    reporter.Report("Reading compressed PLY chunks", input.BytesRead);
            }
            reporter.Report("Reading compressed PLY chunks", input.BytesRead);

            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            for (int splat = 0; splat < vertex.Count; ++splat)
            {
                uint packedPosition = input.ReadUInt32();
                uint packedRotation = input.ReadUInt32();
                uint packedScale = input.ReadUInt32();
                uint packedColor = input.ReadUInt32();
                int chunkOffset = (splat / k_CompressedChunkSize) * chunkPropertyCount;

                Vector3 position = UnpackPosition(packedPosition, chunks, chunkOffset, transform, splat);
                result.Positions[splat] = position;
                Encapsulate(ref bounds, ref hasBounds, position);

                result.Rotations[splat] = UnpackRotation(packedRotation, transform, splat);
                result.Scales[splat] = UnpackScale(packedScale, chunks, chunkOffset, splat);
                result.Colors[splat] = UnpackColor(
                    packedColor, chunks, chunkOffset, chunkPropertyCount, splat);

                if ((splat & 1023) == 1023)
                    reporter.Report("Reading compressed PLY vertices", input.BytesRead);
            }
            reporter.Report("Reading compressed PLY vertices", input.BytesRead);

            if (sh != null)
            {
                byte[] rawSh = new byte[sh.Properties.Count];
                for (int splat = 0; splat < sh.Count; ++splat)
                {
                    for (int property = 0; property < rawSh.Length; ++property)
                        rawSh[property] = input.ReadByte();

                    for (int coefficient = 0; coefficient < shCoefficientCount; ++coefficient)
                    {
                        float sign = transform.ShSigns[coefficient];
                        float r = DecodeQuantizedSh(rawSh[coefficient]) * sign;
                        float g = DecodeQuantizedSh(rawSh[shCoefficientCount + coefficient]) * sign;
                        float b = DecodeQuantizedSh(rawSh[2 * shCoefficientCount + coefficient]) * sign;
                        result.SHs[splat * shCoefficientCount + coefficient] = new Vector3(r, g, b);
                    }

                    if ((splat & 2047) == 2047)
                        reporter.Report("Reading compressed PLY spherical harmonics", input.BytesRead);
                }
                reporter.Report("Reading compressed PLY spherical harmonics", input.BytesRead);
            }

            result.Bounds = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);
            return result;
        }

        static Vector3 UnpackPosition(
            uint packed,
            float[] chunks,
            int offset,
            CoordinateTransform transform,
            int splat)
        {
            float tx = (packed >> 21) / 2047f;
            float ty = ((packed >> 11) & 0x3ffu) / 1023f;
            float tz = (packed & 0x7ffu) / 2047f;
            return new Vector3(
                LerpFinite(chunks[offset], chunks[offset + 3], tx, "position x", splat) * transform.PositionX,
                LerpFinite(chunks[offset + 1], chunks[offset + 4], ty, "position y", splat) * transform.PositionY,
                LerpFinite(chunks[offset + 2], chunks[offset + 5], tz, "position z", splat) * transform.PositionZ);
        }

        static Vector3 UnpackScale(uint packed, float[] chunks, int offset, int splat)
        {
            float tx = (packed >> 21) / 2047f;
            float ty = ((packed >> 11) & 0x3ffu) / 1023f;
            float tz = (packed & 0x7ffu) / 2047f;
            return new Vector3(
                ExpScale(LerpDouble(chunks[offset + 6], chunks[offset + 9], tx), "scale x", splat),
                ExpScale(LerpDouble(chunks[offset + 7], chunks[offset + 10], ty), "scale y", splat),
                ExpScale(LerpDouble(chunks[offset + 8], chunks[offset + 11], tz), "scale z", splat));
        }

        static Vector4 UnpackColor(
            uint packed,
            float[] chunks,
            int offset,
            int chunkPropertyCount,
            int splat)
        {
            float r = (packed >> 24) / 255f;
            float g = ((packed >> 16) & 0xffu) / 255f;
            float b = ((packed >> 8) & 0xffu) / 255f;
            float a = (packed & 0xffu) / 255f;

            if (chunkPropertyCount == 18)
            {
                r = LerpFinite(chunks[offset + 12], chunks[offset + 15], r, "color r", splat);
                g = LerpFinite(chunks[offset + 13], chunks[offset + 16], g, "color g", splat);
                b = LerpFinite(chunks[offset + 14], chunks[offset + 17], b, "color b", splat);
            }

            float dcR = ToFiniteFloat(((double)r - 0.5) / k_ShC0, "decoded DC r", splat);
            float dcG = ToFiniteFloat(((double)g - 0.5) / k_ShC0, "decoded DC g", splat);
            float dcB = ToFiniteFloat(((double)b - 0.5) / k_ShC0, "decoded DC b", splat);
            return new Vector4(dcR, dcG, dcB, a);
        }

        static Vector4 UnpackRotation(uint packed, CoordinateTransform transform, int splat)
        {
            float a = (((packed >> 20) & 0x3ffu) / 1023f - 0.5f) * k_Sqrt2;
            float b = (((packed >> 10) & 0x3ffu) / 1023f - 0.5f) * k_Sqrt2;
            float c = ((packed & 0x3ffu) / 1023f - 0.5f) * k_Sqrt2;
            float missing = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            float w;
            float x;
            float y;
            float z;
            switch (packed >> 30)
            {
                case 0:
                    w = missing; x = a; y = b; z = c;
                    break;
                case 1:
                    w = a; x = missing; y = b; z = c;
                    break;
                case 2:
                    w = a; x = b; y = missing; z = c;
                    break;
                default:
                    w = a; x = b; y = c; z = missing;
                    break;
            }

            return NormalizeRotation(
                w,
                x * transform.RotationX,
                y * transform.RotationY,
                z * transform.RotationZ,
                splat);
        }

        static float DecodeQuantizedSh(byte value)
        {
            return value * (8f / 255f) - 4f;
        }

        static Dictionary<string, int> BuildPropertyIndex(PlyElement element)
        {
            var result = new Dictionary<string, int>(element.Properties.Count, StringComparer.Ordinal);
            for (int i = 0; i < element.Properties.Count; ++i)
            {
                string name = element.Properties[i].Name;
                if (!result.TryAdd(name, i))
                    throw new InvalidDataException(
                        $"PLY element '{element.Name}' declares duplicate property '{name}'.");
            }
            return result;
        }

        static byte GetStandardShLayout(PlyElement vertex, out int[] propertyIndexes)
        {
            var indexedProperties = new Dictionary<int, int>();
            for (int property = 0; property < vertex.Properties.Count; ++property)
            {
                string name = vertex.Properties[property].Name;
                if (!name.StartsWith("f_rest_", StringComparison.Ordinal))
                    continue;

                string suffix = name.Substring("f_rest_".Length);
                int coefficient;
                if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out coefficient) ||
                    coefficient < 0)
                    throw new InvalidDataException($"Invalid spherical-harmonic property name '{name}'.");
                if (!indexedProperties.TryAdd(coefficient, property))
                    throw new InvalidDataException($"Duplicate spherical-harmonic property '{name}'.");
            }

            byte bands = ShBandsFromRawPropertyCount(indexedProperties.Count);
            propertyIndexes = new int[indexedProperties.Count];
            for (int i = 0; i < propertyIndexes.Length; ++i)
            {
                if (!indexedProperties.TryGetValue(i, out propertyIndexes[i]))
                    throw new InvalidDataException(
                        $"Spherical-harmonic properties must be contiguous from f_rest_0; f_rest_{i} is missing.");
            }
            return bands;
        }

        static byte ShBandsFromRawPropertyCount(int propertyCount)
        {
            switch (propertyCount)
            {
                case 0: return 0;
                case 9: return 1;
                case 24: return 2;
                case 45: return 3;
                default:
                    throw new InvalidDataException(
                        $"Unsupported spherical-harmonic property count {propertyCount}; expected 0, 9, 24, or 45.");
            }
        }

        static bool IsCompressedCandidate(PlyHeader header)
        {
            if (header.Elements.Count > 0 && header.Elements[0].Name == "chunk")
                return true;

            PlyElement vertex = header.FindElement("vertex");
            if (vertex == null)
                return false;
            for (int i = 0; i < vertex.Properties.Count; ++i)
            {
                if (vertex.Properties[i].Name.StartsWith("packed_", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static void ValidateCompressedHeader(PlyHeader header)
        {
            if (header.Elements.Count != 2 && header.Elements.Count != 3)
                throw new InvalidDataException(
                    "Compressed PlayCanvas PLY must contain chunk and vertex elements, plus optional SH data.");

            PlyElement chunk = header.Elements[0];
            PlyElement vertex = header.Elements[1];
            if (chunk.Name != "chunk" || vertex.Name != "vertex")
                throw new InvalidDataException(
                    "Compressed PlayCanvas PLY elements must be ordered as chunk, vertex, and optional sh.");
            if (chunk.Properties.Count != 12 && chunk.Properties.Count != 18)
                throw new InvalidDataException(
                    $"Compressed PLY chunks require 12 or 18 float properties, found {chunk.Properties.Count}.");

            for (int i = 0; i < chunk.Properties.Count; ++i)
            {
                if (chunk.Properties[i].Name != k_ChunkPropertyNames[i] ||
                    chunk.Properties[i].Type != PlyScalarType.Float32)
                    throw new InvalidDataException(
                        $"Compressed PLY chunk property {i} must be 'property float " +
                        $"{k_ChunkPropertyNames[i]}'.");
            }

            if (vertex.Properties.Count != k_PackedVertexPropertyNames.Length)
                throw new InvalidDataException("Compressed PLY vertex records require exactly four packed uint properties.");
            for (int i = 0; i < vertex.Properties.Count; ++i)
            {
                if (vertex.Properties[i].Name != k_PackedVertexPropertyNames[i] ||
                    vertex.Properties[i].Type != PlyScalarType.UInt32)
                    throw new InvalidDataException(
                        $"Compressed PLY vertex property {i} must be 'property uint " +
                        $"{k_PackedVertexPropertyNames[i]}'.");
            }

            int expectedChunkCount = vertex.Count == 0
                ? 0
                : (int)(((long)vertex.Count + k_CompressedChunkSize - 1) / k_CompressedChunkSize);
            if (chunk.Count != expectedChunkCount)
                throw new InvalidDataException(
                    $"Compressed PLY has {chunk.Count} chunks for {vertex.Count} vertices; " +
                    $"expected {expectedChunkCount} chunks of {k_CompressedChunkSize}.");

            if (header.Elements.Count == 3)
            {
                PlyElement sh = header.Elements[2];
                if (sh.Name != "sh")
                    throw new InvalidDataException("The third compressed PLY element must be named 'sh'.");
                if (sh.Count != vertex.Count)
                    throw new InvalidDataException(
                        $"Compressed PLY SH count {sh.Count} does not match vertex count {vertex.Count}.");
                ShBandsFromRawPropertyCount(sh.Properties.Count);
                for (int i = 0; i < sh.Properties.Count; ++i)
                {
                    if (sh.Properties[i].Name != $"f_rest_{i}" ||
                        sh.Properties[i].Type != PlyScalarType.UInt8)
                        throw new InvalidDataException(
                            $"Compressed PLY SH property {i} must be 'property uchar f_rest_{i}'.");
                }
            }
        }

        static void ValidateChunkRanges(float[] values, int offset, int propertyCount, int chunk)
        {
            for (int axis = 0; axis < 3; ++axis)
            {
                ValidateRange(values[offset + axis], values[offset + 3 + axis], "position", axis, chunk);
                ValidateRange(values[offset + 6 + axis], values[offset + 9 + axis], "scale", axis, chunk);
                if (propertyCount == 18)
                    ValidateRange(values[offset + 12 + axis], values[offset + 15 + axis], "color", axis, chunk);
            }
        }

        static void ValidateRange(float minimum, float maximum, string value, int axis, int chunk)
        {
            if (minimum > maximum)
                throw new InvalidDataException(
                    $"Compressed PLY chunk {chunk} has reversed {value} range on axis {axis}: " +
                    $"{minimum.ToString(CultureInfo.InvariantCulture)} > " +
                    $"{maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        static PlyHeader ReadHeader(Stream stream)
        {
            int headerBytes = 0;
            string firstLine = ReadHeaderLine(stream, ref headerBytes);
            if (firstLine != "ply")
                throw new InvalidDataException("PLY stream must begin with the ASCII line 'ply'.");

            var header = new PlyHeader();
            PlyElement currentElement = null;
            bool formatSeen = false;
            while (true)
            {
                string line = ReadHeaderLine(stream, ref headerBytes);
                if (line == null)
                    throw new InvalidDataException("PLY stream ended before end_header.");

                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;
                string[] words = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words[0] == "end_header")
                {
                    if (words.Length != 1)
                        throw new InvalidDataException("Unexpected tokens after end_header.");
                    break;
                }

                switch (words[0])
                {
                    case "format":
                        if (formatSeen || words.Length != 3 ||
                            words[1] != "binary_little_endian" || words[2] != "1.0")
                            throw new InvalidDataException(
                                "Only one 'format binary_little_endian 1.0' declaration is supported.");
                        formatSeen = true;
                        break;

                    case "comment":
                        header.Comments.Add(trimmed.Length > 7 ? trimmed.Substring(7).TrimStart() : string.Empty);
                        break;

                    case "obj_info":
                        // Legal PLY metadata which does not affect the binary layout.
                        break;

                    case "element":
                        if (words.Length != 3)
                            throw new InvalidDataException($"Malformed PLY element declaration '{line}'.");
                        int count;
                        if (!int.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out count))
                            throw new InvalidDataException($"Invalid PLY element count '{words[2]}'.");
                        if (header.FindElement(words[1]) != null)
                            throw new InvalidDataException($"Duplicate PLY element '{words[1]}'.");
                        currentElement = new PlyElement(words[1], count);
                        header.Elements.Add(currentElement);
                        break;

                    case "property":
                        if (currentElement == null)
                            throw new InvalidDataException("PLY property appears before any element declaration.");
                        if (words.Length > 1 && words[1] == "list")
                            throw new InvalidDataException("PLY list properties are not supported for Gaussian splats.");
                        if (words.Length != 3)
                            throw new InvalidDataException($"Malformed PLY property declaration '{line}'.");
                        PlyScalarType type;
                        if (!TryParseScalarType(words[1], out type))
                            throw new InvalidDataException($"Unsupported PLY scalar type '{words[1]}'.");
                        if (currentElement.FindProperty(words[2]) != null)
                            throw new InvalidDataException(
                                $"PLY element '{currentElement.Name}' declares duplicate property '{words[2]}'.");
                        currentElement.Properties.Add(new PlyProperty(words[2], type));
                        break;

                    default:
                        throw new InvalidDataException($"Unsupported PLY header directive '{words[0]}'.");
                }
            }

            if (!formatSeen)
                throw new InvalidDataException("PLY header is missing its binary_little_endian format declaration.");
            if (header.Elements.Count == 0)
                throw new InvalidDataException("PLY header declares no elements.");

            long byteCount = 0;
            try
            {
                for (int i = 0; i < header.Elements.Count; ++i)
                {
                    PlyElement element = header.Elements[i];
                    byteCount = checked(byteCount + checked((long)element.Count * element.RecordByteCount));
                }
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("PLY payload size exceeds the supported range.", exception);
            }
            header.DataByteCount = byteCount;
            return header;
        }

        static string ReadHeaderLine(Stream stream, ref int byteCount)
        {
            var bytes = new List<byte>(64);
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());

                ++byteCount;
                if (byteCount > k_MaxHeaderBytes)
                    throw new InvalidDataException($"PLY header exceeds {k_MaxHeaderBytes} bytes.");
                if (value == '\n')
                    break;
                bytes.Add((byte)value);
            }

            if (bytes.Count > 0 && bytes[bytes.Count - 1] == '\r')
                bytes.RemoveAt(bytes.Count - 1);
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        static bool TryParseScalarType(string value, out PlyScalarType type)
        {
            switch (value)
            {
                case "char":
                case "int8":
                    type = PlyScalarType.Int8; return true;
                case "uchar":
                case "uint8":
                    type = PlyScalarType.UInt8; return true;
                case "short":
                case "int16":
                    type = PlyScalarType.Int16; return true;
                case "ushort":
                case "uint16":
                    type = PlyScalarType.UInt16; return true;
                case "int":
                case "int32":
                    type = PlyScalarType.Int32; return true;
                case "uint":
                case "uint32":
                    type = PlyScalarType.UInt32; return true;
                case "float":
                case "float32":
                    type = PlyScalarType.Float32; return true;
                case "double":
                case "float64":
                    type = PlyScalarType.Float64; return true;
                default:
                    type = default;
                    return false;
            }
        }

        static float ActivateOpacity(double value)
        {
            if (value >= 0.0)
                return (float)(1.0 / (1.0 + Math.Exp(-value)));
            double exponential = Math.Exp(value);
            return (float)(exponential / (1.0 + exponential));
        }

        static float ExpScale(double logScale, string property, int splat)
        {
            RequireFinite(logScale, property, splat);
            return ToFiniteFloat(Math.Exp(logScale), $"exp({property})", splat);
        }

        static double RequireFinite(double value, string property, int splat)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidDataException(
                    $"PLY vertex {splat} property '{property}' is not finite.");
            return value;
        }

        static float ToFiniteFloat(double value, string property, int splat)
        {
            RequireFinite(value, property, splat);
            float result = (float)value;
            if (!IsFinite(result))
                throw new InvalidDataException(
                    $"PLY vertex {splat} property '{property}' is outside the supported float range.");
            return result;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static Vector4 NormalizeRotation(double w, double x, double y, double z, int splat)
        {
            RequireFinite(w, "rot_0", splat);
            RequireFinite(x, "rot_1", splat);
            RequireFinite(y, "rot_2", splat);
            RequireFinite(z, "rot_3", splat);
            double lengthSquared = w * w + x * x + y * y + z * z;
            if (double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared) || lengthSquared <= 1e-30)
                throw new InvalidDataException($"PLY vertex {splat} contains an invalid zero-length rotation.");

            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            return new Vector4(
                ToFiniteFloat(w * inverseLength, "normalized rot_0", splat),
                ToFiniteFloat(x * inverseLength, "normalized rot_1", splat),
                ToFiniteFloat(y * inverseLength, "normalized rot_2", splat),
                ToFiniteFloat(z * inverseLength, "normalized rot_3", splat));
        }

        static double LerpDouble(float minimum, float maximum, float value)
        {
            return (double)minimum * (1.0 - value) + (double)maximum * value;
        }

        static float LerpFinite(float minimum, float maximum, float value, string property, int splat)
        {
            return ToFiniteFloat(LerpDouble(minimum, maximum, value), property, splat);
        }

        static void Encapsulate(ref Bounds bounds, ref bool initialized, Vector3 point)
        {
            if (!initialized)
            {
                bounds = new Bounds(point, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }

        static bool ParseAntialiased(List<string> comments)
        {
            for (int i = 0; i < comments.Count; ++i)
            {
                string[] words = comments[i].Trim().Split(
                    new[] { ' ', '\t', '=', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                    continue;
                if (!words[0].Equals("antialiased", StringComparison.OrdinalIgnoreCase) &&
                    !words[0].Equals("antialias", StringComparison.OrdinalIgnoreCase) &&
                    !words[0].Equals("anti_alias", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (words.Length == 1)
                    return true;
                return words[1] == "1" || words[1].Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    words[1].Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    words[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        enum PlyScalarType
        {
            Int8,
            UInt8,
            Int16,
            UInt16,
            Int32,
            UInt32,
            Float32,
            Float64
        }

        sealed class PlyProperty
        {
            public readonly string Name;
            public readonly PlyScalarType Type;

            public PlyProperty(string name, PlyScalarType type)
            {
                Name = name;
                Type = type;
            }

            public int ByteCount
            {
                get
                {
                    switch (Type)
                    {
                        case PlyScalarType.Int8:
                        case PlyScalarType.UInt8: return 1;
                        case PlyScalarType.Int16:
                        case PlyScalarType.UInt16: return 2;
                        case PlyScalarType.Int32:
                        case PlyScalarType.UInt32:
                        case PlyScalarType.Float32: return 4;
                        case PlyScalarType.Float64: return 8;
                        default: throw new InvalidOperationException();
                    }
                }
            }
        }

        sealed class PlyElement
        {
            public readonly string Name;
            public readonly int Count;
            public readonly List<PlyProperty> Properties = new List<PlyProperty>();

            public PlyElement(string name, int count)
            {
                Name = name;
                Count = count;
            }

            public int RecordByteCount
            {
                get
                {
                    int result = 0;
                    for (int i = 0; i < Properties.Count; ++i)
                        result = checked(result + Properties[i].ByteCount);
                    return result;
                }
            }

            public PlyProperty FindProperty(string name)
            {
                for (int i = 0; i < Properties.Count; ++i)
                {
                    if (Properties[i].Name == name)
                        return Properties[i];
                }
                return null;
            }
        }

        sealed class PlyHeader
        {
            public readonly List<PlyElement> Elements = new List<PlyElement>();
            public readonly List<string> Comments = new List<string>();
            public long DataByteCount;

            public PlyElement FindElement(string name)
            {
                for (int i = 0; i < Elements.Count; ++i)
                {
                    if (Elements[i].Name == name)
                        return Elements[i];
                }
                return null;
            }
        }

        sealed class CoordinateTransform
        {
            public readonly float PositionX;
            public readonly float PositionY;
            public readonly float PositionZ;
            public readonly float RotationX;
            public readonly float RotationY;
            public readonly float RotationZ;
            public readonly float[] ShSigns;

            public CoordinateTransform(SourceCoordinates sourceCoordinates, byte shBands)
            {
                (PositionX, PositionY, PositionZ) = GsplatUtils.AxisSigns(sourceCoordinates);
                RotationX = PositionY * PositionZ;
                RotationY = PositionX * PositionZ;
                RotationZ = PositionX * PositionY;

                int coefficientCount = GsplatUtils.SHBandsToCoefficientCount(shBands);
                ShSigns = new float[coefficientCount];
                int coefficient = 0;
                for (int band = 1; band <= shBands; ++band)
                {
                    for (int local = 0; local < 2 * band + 1; ++local)
                        ShSigns[coefficient++] = GsplatUtils.ShSign(sourceCoordinates, band, local);
                }
            }
        }

        sealed class ProgressReporter
        {
            readonly ProgressCallback m_Callback;
            readonly long m_TotalBytes;
            string m_LastPhase;
            float m_LastProgress = -1f;

            public ProgressReporter(ProgressCallback callback, long totalBytes)
            {
                m_Callback = callback;
                m_TotalBytes = totalBytes;
            }

            public void Report(string phase, long bytesRead, bool force = false)
            {
                if (m_Callback == null)
                    return;
                float value = m_TotalBytes == 0
                    ? 1f
                    : Mathf.Clamp01((float)((double)bytesRead / m_TotalBytes));
                if (!force && phase == m_LastPhase && value < m_LastProgress + 0.005f)
                    return;
                m_LastPhase = phase;
                m_LastProgress = value;
                m_Callback(phase, value);
            }
        }

        sealed class PlyBinaryReader : IDisposable
        {
            readonly BinaryReader m_Reader;
            readonly byte[] m_SkipBuffer = new byte[64 * 1024];

            public long BytesRead { get; private set; }

            public PlyBinaryReader(Stream stream)
            {
                m_Reader = new BinaryReader(stream, Encoding.UTF8, true);
            }

            public double ReadScalar(PlyScalarType type)
            {
                try
                {
                    switch (type)
                    {
                        case PlyScalarType.Int8: BytesRead += 1; return m_Reader.ReadSByte();
                        case PlyScalarType.UInt8: BytesRead += 1; return m_Reader.ReadByte();
                        case PlyScalarType.Int16: BytesRead += 2; return m_Reader.ReadInt16();
                        case PlyScalarType.UInt16: BytesRead += 2; return m_Reader.ReadUInt16();
                        case PlyScalarType.Int32: BytesRead += 4; return m_Reader.ReadInt32();
                        case PlyScalarType.UInt32: BytesRead += 4; return m_Reader.ReadUInt32();
                        case PlyScalarType.Float32: BytesRead += 4; return m_Reader.ReadSingle();
                        case PlyScalarType.Float64: BytesRead += 8; return m_Reader.ReadDouble();
                        default: throw new InvalidOperationException();
                    }
                }
                catch (EndOfStreamException exception)
                {
                    throw new InvalidDataException("PLY payload ended in the middle of a scalar value.", exception);
                }
            }

            public byte ReadByte()
            {
                return (byte)ReadScalar(PlyScalarType.UInt8);
            }

            public uint ReadUInt32()
            {
                return (uint)ReadScalar(PlyScalarType.UInt32);
            }

            public float ReadSingle()
            {
                return (float)ReadScalar(PlyScalarType.Float32);
            }

            public void SkipExactly(
                long byteCount,
                ProgressReporter reporter,
                string phase)
            {
                while (byteCount > 0)
                {
                    int requested = (int)Math.Min(m_SkipBuffer.Length, byteCount);
                    int read = m_Reader.Read(m_SkipBuffer, 0, requested);
                    if (read <= 0)
                        throw new InvalidDataException("PLY payload ended while skipping an unneeded element.");
                    byteCount -= read;
                    BytesRead += read;
                    reporter.Report(phase, BytesRead);
                }
            }

            public void Dispose()
            {
                m_Reader.Dispose();
            }
        }
    }
}
