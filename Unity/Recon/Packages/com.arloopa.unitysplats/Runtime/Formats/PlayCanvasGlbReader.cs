// Port of the PlayCanvas KHR_gaussian_splatting glTF decode rules.
// PlayCanvas engine copyright (c) 2011-2026 PlayCanvas Ltd; MIT license.
// Unity port copyright (c) 2026 ARLOOPA.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Gsplat.Formats
{
    /// <summary>
    /// Reads Gaussian splats from a GLB 2.0 container using KHR_gaussian_splatting.
    /// Primitive data is concatenated in mesh/primitive order and converted from the source
    /// coordinate frame to Unity RUF coordinates. glTF stores activated linear scale and
    /// post-sigmoid opacity, so those values are normally copied without applying exp/sigmoid.
    /// GLBs written by splat-transform 2.6.0 and older used logarithmic scales despite declaring
    /// the current extension; those explicitly identified legacy exports are converted on load.
    /// </summary>
    public static class PlayCanvasGlbReader
    {
        const uint GlbMagic = 0x46546c67;       // "glTF"
        const uint JsonChunk = 0x4e4f534a;      // "JSON"
        const uint BinChunk = 0x004e4942;       // "BIN\0"
        const uint GlbVersion = 2;
        const int MaxJsonBytes = 64 * 1024 * 1024;
        const string ExtensionName = "KHR_gaussian_splatting";

        static readonly int[] ShDegreeCoefficientCounts = { 0, 3, 5, 7 };

        /// <summary>Decodes a complete GLB byte array. The input array is not retained.</summary>
        public static GsplatDecodedData Read(
            byte[] bytes,
            SourceCoordinates sourceCoordinates = SourceCoordinates.LUF,
            ProgressCallback progress = null)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            using var stream = new MemoryStream(bytes, writable: false);
            return Read(stream, sourceCoordinates, progress);
        }

        /// <summary>Decodes a GLB from the stream's current position. The stream remains open.</summary>
        public static GsplatDecodedData Read(
            Stream stream,
            SourceCoordinates sourceCoordinates = SourceCoordinates.LUF,
            ProgressCallback progress = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("The GLB stream must be readable.", nameof(stream));
            if (!Enum.IsDefined(typeof(SourceCoordinates), sourceCoordinates))
                throw new ArgumentOutOfRangeException(nameof(sourceCoordinates));

            progress?.Invoke("Reading GLB container", 0f);
            GlbPayload payload = ReadContainer(stream, progress);

            string json = Encoding.UTF8.GetString(payload.Json).TrimEnd('\0', ' ', '\t', '\r', '\n');
            if (json.Length > 0 && json[0] == '\ufeff')
                json = json.Substring(1);

            Dictionary<string, object> root;
            try
            {
                root = JsonValues.Object(MiniJson.Deserialize(json), "root");
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
            {
                throw new InvalidDataException("The GLB JSON chunk is invalid.", exception);
            }

            var context = new GltfContext(root, payload.Bin);
            bool legacyLogScales = UsesLegacySplatTransformLogScales(root);
            List<PrimitiveInfo> primitives = CollectPrimitives(root, context);

            int totalCount = 0;
            byte maxShBands = 0;
            try
            {
                for (int i = 0; i < primitives.Count; ++i)
                {
                    totalCount = checked(totalCount + primitives[i].Count);
                    if (primitives[i].ShBands > maxShBands)
                        maxShBands = primitives[i].ShBands;
                }
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("The total GLB splat count exceeds the supported range.", exception);
            }

            if (totalCount == 0)
                throw new InvalidDataException(
                    $"The GLB does not contain a non-empty {ExtensionName} primitive.");

            var result = new GsplatDecodedData(totalCount, maxShBands);
            DecodePrimitives(context, primitives, sourceCoordinates, legacyLogScales, result, progress);
            result.Validate();
            progress?.Invoke("GLB decoded", 1f);
            return result;
        }

        static GlbPayload ReadContainer(Stream stream, ProgressCallback progress)
        {
            byte[] header = ReadBytes(stream, 12, "GLB header");
            uint magic = ReadUInt32(header, 0);
            uint version = ReadUInt32(header, 4);
            uint declaredLength = ReadUInt32(header, 8);

            if (magic != GlbMagic)
                throw new InvalidDataException("The stream is not a GLB file (invalid glTF magic).");
            if (version != GlbVersion)
                throw new NotSupportedException($"GLB version {version} is not supported; expected version 2.");
            if (declaredLength < 20 || (declaredLength & 3u) != 0)
                throw new InvalidDataException($"GLB declared length {declaredLength} is invalid.");
            if (stream.CanSeek && stream.Length - stream.Position < declaredLength - 12)
                throw new InvalidDataException(
                    $"GLB is truncated: {declaredLength - 12} payload bytes are declared, but only " +
                    $"{stream.Length - stream.Position} remain.");

            long consumed = 12;
            byte[] json = null;
            byte[] bin = null;
            bool firstChunk = true;

            while (consumed < declaredLength)
            {
                if (declaredLength - consumed < 8)
                    throw new InvalidDataException("GLB ends with a partial chunk header.");

                byte[] chunkHeader = ReadBytes(stream, 8, "GLB chunk header");
                consumed += 8;
                uint chunkLengthValue = ReadUInt32(chunkHeader, 0);
                uint chunkType = ReadUInt32(chunkHeader, 4);
                if ((chunkLengthValue & 3u) != 0)
                    throw new InvalidDataException($"GLB chunk length {chunkLengthValue} is not 4-byte aligned.");
                if (chunkLengthValue > int.MaxValue)
                    throw new NotSupportedException("GLB chunks larger than 2 GiB are not supported.");
                if (chunkLengthValue > declaredLength - consumed)
                    throw new InvalidDataException("A GLB chunk extends beyond the declared file length.");

                int chunkLength = (int)chunkLengthValue;
                byte[] chunk = ReadBytes(stream, chunkLength, "GLB chunk payload");
                consumed += chunkLength;

                if (firstChunk && chunkType != JsonChunk)
                    throw new InvalidDataException("The first GLB chunk must be JSON.");
                firstChunk = false;

                if (chunkType == JsonChunk)
                {
                    if (json != null)
                        throw new InvalidDataException("GLB contains more than one JSON chunk.");
                    if (chunkLength == 0 || chunkLength > MaxJsonBytes)
                        throw new InvalidDataException(
                            $"GLB JSON chunk length {chunkLength} is outside the supported range.");
                    json = chunk;
                }
                else if (chunkType == BinChunk)
                {
                    if (bin != null)
                        throw new InvalidDataException("GLB contains more than one BIN chunk.");
                    bin = chunk;
                }
                // Unknown extension chunks are permitted by the GLB container and ignored.

                progress?.Invoke("Reading GLB container",
                    Mathf.Clamp01(consumed / (float)declaredLength) * 0.1f);
            }

            if (consumed != declaredLength)
                throw new InvalidDataException(
                    $"GLB layout consumed {consumed} bytes, but the header declares {declaredLength}.");
            if (json == null)
                throw new InvalidDataException("GLB does not contain a JSON chunk.");
            if (bin == null)
                throw new InvalidDataException("GLB does not contain the BIN chunk required for splat data.");

            return new GlbPayload(json, bin);
        }

        static List<PrimitiveInfo> CollectPrimitives(
            Dictionary<string, object> root,
            GltfContext context)
        {
            List<object> meshes = RequiredArray(root, "meshes", "GLB root");
            var result = new List<PrimitiveInfo>();

            for (int meshIndex = 0; meshIndex < meshes.Count; ++meshIndex)
            {
                Dictionary<string, object> mesh = JsonValues.Object(meshes[meshIndex], $"meshes[{meshIndex}]");
                List<object> meshPrimitives = RequiredArray(mesh, "primitives", $"meshes[{meshIndex}]");

                for (int primitiveIndex = 0; primitiveIndex < meshPrimitives.Count; ++primitiveIndex)
                {
                    string label = $"meshes[{meshIndex}].primitives[{primitiveIndex}]";
                    Dictionary<string, object> primitive =
                        JsonValues.Object(meshPrimitives[primitiveIndex], label);

                    if (!TryGetExtension(primitive, label, out _))
                        continue;

                    int mode = primitive.OptionalInt("mode", 4);
                    if (mode != 0)
                        throw new FormatException(
                            $"{label} uses {ExtensionName} but its mode is {mode}; POINTS mode 0 is required.");

                    Dictionary<string, object> attributes = RequiredObject(primitive, "attributes", label);
                    RejectUnsupportedShDegrees(attributes, label);

                    int positionIndex = RequiredAccessorIndex(attributes, "POSITION", label);
                    Accessor position = context.GetAccessor(positionIndex, $"{label}.attributes.POSITION");
                    position.RequireShape(3, $"{label}.attributes.POSITION");
                    if (position.Count <= 0)
                        throw new FormatException($"{label} contains no splats.");

                    var info = new PrimitiveInfo(label, position.Count)
                    {
                        Position = positionIndex,
                        Rotation = RequiredAccessorIndex(attributes, Attribute("ROTATION"), label),
                        Scale = RequiredAccessorIndex(attributes, Attribute("SCALE"), label),
                        Opacity = RequiredAccessorIndex(attributes, Attribute("OPACITY"), label),
                        Sh0 = RequiredAccessorIndex(attributes, Attribute("SH_DEGREE_0_COEF_0"), label),
                    };

                    context.GetAccessor(info.Rotation, $"{label}.attributes.{Attribute("ROTATION")}")
                        .RequireShapeAndCount(4, info.Count, $"{label} rotation");
                    context.GetAccessor(info.Scale, $"{label}.attributes.{Attribute("SCALE")}")
                        .RequireShapeAndCount(3, info.Count, $"{label} scale");
                    context.GetAccessor(info.Opacity, $"{label}.attributes.{Attribute("OPACITY")}")
                        .RequireShapeAndCount(1, info.Count, $"{label} opacity");
                    context.GetAccessor(info.Sh0, $"{label}.attributes.{Attribute("SH_DEGREE_0_COEF_0")}")
                        .RequireShapeAndCount(3, info.Count, $"{label} degree-zero SH");

                    bool encounteredMissingDegree = false;
                    for (int degree = 1; degree <= 3; ++degree)
                    {
                        int coefficientCount = ShDegreeCoefficientCounts[degree];
                        bool any = false;
                        bool all = true;
                        int bandStart = degree * degree - 1;

                        for (int coefficient = 0; coefficient < coefficientCount; ++coefficient)
                        {
                            string name = Attribute($"SH_DEGREE_{degree}_COEF_{coefficient}");
                            if (attributes.TryGetValue(name, out object raw) && raw != null)
                            {
                                any = true;
                                int accessorIndex = JsonValues.Int(raw, $"{label}.attributes.{name}");
                                info.ShAccessors[bandStart + coefficient] = accessorIndex;
                                context.GetAccessor(accessorIndex, $"{label}.attributes.{name}")
                                    .RequireShapeAndCount(3, info.Count, $"{label} {name}");
                            }
                            else
                            {
                                all = false;
                            }
                        }

                        if (any && !all)
                            throw new FormatException(
                                $"{label} contains an incomplete degree-{degree} SH band; all " +
                                $"{coefficientCount} coefficient attributes are required together.");
                        if (all)
                        {
                            if (encounteredMissingDegree)
                                throw new FormatException(
                                    $"{label} contains degree-{degree} SH data after a missing lower degree.");
                            info.ShBands = (byte)degree;
                        }
                        else
                        {
                            encounteredMissingDegree = true;
                        }
                    }

                    result.Add(info);
                }
            }

            return result;
        }

        static void DecodePrimitives(
            GltfContext context,
            List<PrimitiveInfo> primitives,
            SourceCoordinates sourceCoordinates,
            bool legacyLogScales,
            GsplatDecodedData result,
            ProgressCallback progress)
        {
            var (positionX, positionY, positionZ) = GsplatUtils.AxisSigns(sourceCoordinates);
            float rotationX = positionY * positionZ;
            float rotationY = positionX * positionZ;
            float rotationZ = positionX * positionY;
            int destinationOffset = 0;
            int destinationShStride = GsplatUtils.SHBandsToCoefficientCount(result.SHBands);
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            long totalWork = 0;
            for (int i = 0; i < primitives.Count; ++i)
                totalWork += (long)primitives[i].Count *
                             (5 + GsplatUtils.SHBandsToCoefficientCount(primitives[i].ShBands));
            long completedWork = 0;

            for (int primitiveIndex = 0; primitiveIndex < primitives.Count; ++primitiveIndex)
            {
                PrimitiveInfo primitive = primitives[primitiveIndex];
                int count = primitive.Count;

                float[] values = context.GetAccessor(primitive.Position, primitive.Label + " position").ReadAll();
                for (int i = 0; i < count; ++i)
                {
                    Vector3 position = new Vector3(
                        values[i * 3] * positionX,
                        values[i * 3 + 1] * positionY,
                        values[i * 3 + 2] * positionZ);
                    result.Positions[destinationOffset + i] = position;
                    if (!hasBounds)
                    {
                        bounds = new Bounds(position, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(position);
                    }
                }
                ReportProgress(progress, "Decoding GLB positions", completedWork += count, totalWork);

                values = context.GetAccessor(primitive.Rotation, primitive.Label + " rotation").ReadAll();
                for (int i = 0; i < count; ++i)
                {
                    int source = i * 4;
                    float x = values[source] * rotationX;
                    float y = values[source + 1] * rotationY;
                    float z = values[source + 2] * rotationZ;
                    float w = values[source + 3];
                    float lengthSquared = x * x + y * y + z * z + w * w;
                    if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-20f)
                        throw new InvalidDataException(
                            $"{primitive.Label} rotation {i} is zero-length or non-finite.");
                    float inverseLength = 1f / Mathf.Sqrt(lengthSquared);
                    result.Rotations[destinationOffset + i] =
                        new Vector4(w * inverseLength, x * inverseLength, y * inverseLength, z * inverseLength);
                }
                ReportProgress(progress, "Decoding GLB rotations", completedWork += count, totalWork);

                values = context.GetAccessor(primitive.Scale, primitive.Label + " scale").ReadAll();
                for (int i = 0; i < count; ++i)
                {
                    int source = i * 3;
                    result.Scales[destinationOffset + i] =
                        new Vector3(
                            DecodeScale(values[source], legacyLogScales),
                            DecodeScale(values[source + 1], legacyLogScales),
                            DecodeScale(values[source + 2], legacyLogScales));
                }
                ReportProgress(progress, "Decoding GLB scales", completedWork += count, totalWork);

                values = context.GetAccessor(primitive.Opacity, primitive.Label + " opacity").ReadAll();
                for (int i = 0; i < count; ++i)
                    result.Colors[destinationOffset + i].w = values[i];
                ReportProgress(progress, "Decoding GLB opacities", completedWork += count, totalWork);

                values = context.GetAccessor(primitive.Sh0, primitive.Label + " degree-zero SH").ReadAll();
                for (int i = 0; i < count; ++i)
                {
                    int source = i * 3;
                    int destination = destinationOffset + i;
                    result.Colors[destination] = new Vector4(
                        values[source], values[source + 1], values[source + 2], result.Colors[destination].w);
                }
                ReportProgress(progress, "Decoding GLB degree-zero SH", completedWork += count, totalWork);

                int primitiveShCount = GsplatUtils.SHBandsToCoefficientCount(primitive.ShBands);
                for (int coefficient = 0; coefficient < primitiveShCount; ++coefficient)
                {
                    int accessorIndex = primitive.ShAccessors[coefficient];
                    values = context.GetAccessor(accessorIndex,
                        $"{primitive.Label} SH coefficient {coefficient}").ReadAll();
                    int degree = Mathf.FloorToInt(Mathf.Sqrt(coefficient + 1));
                    int bandStart = degree * degree - 1;
                    float sign = GsplatUtils.ShSign(sourceCoordinates, degree, coefficient - bandStart);

                    for (int i = 0; i < count; ++i)
                    {
                        int source = i * 3;
                        result.SHs[(destinationOffset + i) * destinationShStride + coefficient] =
                            sign * new Vector3(values[source], values[source + 1], values[source + 2]);
                    }
                    ReportProgress(progress, $"Decoding GLB SH coefficient {coefficient}",
                        completedWork += count, totalWork);
                }

                destinationOffset += count;
            }

            result.Bounds = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);
        }

        static bool UsesLegacySplatTransformLogScales(Dictionary<string, object> root)
        {
            if (!root.TryGetValue("asset", out object assetRaw) || assetRaw == null)
                return false;

            Dictionary<string, object> asset = JsonValues.Object(assetRaw, "asset");
            if (!asset.TryGetValue("generator", out object generatorRaw) || generatorRaw is not string generator)
                return false;

            const string prefix = "splat-transform ";
            if (!generator.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string versionText = generator.Substring(prefix.Length).Trim();
            int separator = versionText.IndexOfAny(new[] { ' ', '-', '+' });
            if (separator >= 0)
                versionText = versionText.Substring(0, separator);

            return Version.TryParse(versionText, out Version version) && version <= new Version(2, 6, 0);
        }

        static float DecodeScale(float value, bool legacyLogScales)
        {
            if (!legacyLogScales)
                return value;
            if (float.IsNegativeInfinity(value))
                return 0f;
            return (float)Math.Exp(value);
        }

        static void ReportProgress(
            ProgressCallback progress,
            string phase,
            long completed,
            long total)
        {
            if (progress == null)
                return;
            float decoded = total > 0 ? completed / (float)total : 1f;
            progress(phase, 0.1f + Mathf.Clamp01(decoded) * 0.9f);
        }

        static bool TryGetExtension(
            Dictionary<string, object> primitive,
            string label,
            out Dictionary<string, object> extension)
        {
            extension = null;
            if (!primitive.TryGetValue("extensions", out object extensionsRaw) || extensionsRaw == null)
                return false;

            Dictionary<string, object> extensions =
                JsonValues.Object(extensionsRaw, label + ".extensions");
            if (!extensions.TryGetValue(ExtensionName, out object extensionRaw) || extensionRaw == null)
                return false;

            extension = JsonValues.Object(extensionRaw, label + ".extensions." + ExtensionName);
            return true;
        }

        static void RejectUnsupportedShDegrees(Dictionary<string, object> attributes, string label)
        {
            string prefix = Attribute("SH_DEGREE_");
            foreach (string name in attributes.Keys)
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                int degreeStart = prefix.Length;
                int degreeEnd = name.IndexOf("_COEF_", degreeStart, StringComparison.Ordinal);
                if (degreeEnd <= degreeStart ||
                    !int.TryParse(name.Substring(degreeStart, degreeEnd - degreeStart), out int degree))
                    continue;
                if (degree > 3)
                    throw new NotSupportedException(
                        $"{label} uses SH degree {degree}; only degrees 0 through 3 are supported.");
            }
        }

        static int RequiredAccessorIndex(
            Dictionary<string, object> attributes,
            string name,
            string label)
        {
            if (!attributes.TryGetValue(name, out object raw) || raw == null)
                throw new FormatException($"{label} is missing required attribute '{name}'.");
            return JsonValues.Int(raw, $"{label}.attributes.{name}");
        }

        static Dictionary<string, object> RequiredObject(
            Dictionary<string, object> parent,
            string name,
            string label)
        {
            if (!parent.TryGetValue(name, out object raw) || raw == null)
                throw new FormatException($"{label} is missing required object '{name}'.");
            return JsonValues.Object(raw, label + "." + name);
        }

        static List<object> RequiredArray(
            Dictionary<string, object> parent,
            string name,
            string label)
        {
            if (!parent.TryGetValue(name, out object raw) || raw == null)
                throw new FormatException($"{label} is missing required array '{name}'.");
            return JsonValues.Array(raw, label + "." + name);
        }

        static string RequiredString(
            Dictionary<string, object> parent,
            string name,
            string label)
        {
            if (!parent.TryGetValue(name, out object raw) || raw == null)
                throw new FormatException($"{label} is missing required string '{name}'.");
            return JsonValues.String(raw, label + "." + name);
        }

        static string Attribute(string suffix) => ExtensionName + ":" + suffix;

        static byte[] ReadBytes(Stream stream, int count, string section)
        {
            var result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(result, offset, count - offset);
                if (read <= 0)
                    throw new InvalidDataException(
                        $"Unexpected end of stream while reading {section}; needed {count - offset} more bytes.");
                offset += read;
            }
            return result;
        }

        static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] |
                          bytes[offset + 1] << 8 |
                          bytes[offset + 2] << 16 |
                          bytes[offset + 3] << 24);
        }

        readonly struct GlbPayload
        {
            public readonly byte[] Json;
            public readonly byte[] Bin;

            public GlbPayload(byte[] json, byte[] bin)
            {
                Json = json;
                Bin = bin;
            }
        }

        sealed class PrimitiveInfo
        {
            public readonly string Label;
            public readonly int Count;
            public int Position;
            public int Rotation;
            public int Scale;
            public int Opacity;
            public int Sh0;
            public byte ShBands;
            public readonly int[] ShAccessors = new int[15];

            public PrimitiveInfo(string label, int count)
            {
                Label = label;
                Count = count;
                for (int i = 0; i < ShAccessors.Length; ++i)
                    ShAccessors[i] = -1;
            }
        }

        sealed class GltfContext
        {
            readonly byte[] m_bin;
            readonly int m_bufferLength;
            readonly List<object> m_accessors;
            readonly List<object> m_bufferViews;
            readonly Dictionary<int, Accessor> m_accessorCache = new();
            readonly Dictionary<int, BufferView> m_bufferViewCache = new();

            public GltfContext(Dictionary<string, object> root, byte[] bin)
            {
                Dictionary<string, object> asset = RequiredObject(root, "asset", "GLB root");
                string assetVersion = RequiredString(asset, "version", "asset");
                if (assetVersion != "2.0")
                    throw new NotSupportedException(
                        $"glTF asset version '{assetVersion}' is not supported; expected '2.0'.");

                List<object> buffers = RequiredArray(root, "buffers", "GLB root");
                if (buffers.Count == 0)
                    throw new FormatException("GLB root.buffers is empty.");
                Dictionary<string, object> buffer = JsonValues.Object(buffers[0], "buffers[0]");
                m_bufferLength = buffer.RequiredInt("byteLength");
                if (m_bufferLength < 0)
                    throw new FormatException("buffers[0].byteLength cannot be negative.");
                if (buffer.TryGetValue("uri", out object uri) && uri != null)
                    throw new NotSupportedException(
                        "External/data-URI buffers are not supported by the GLB reader.");
                if (m_bufferLength > bin.Length)
                    throw new InvalidDataException(
                        $"GLB BIN chunk is truncated: buffer declares {m_bufferLength} bytes, " +
                        $"but the chunk contains {bin.Length}.");

                m_bin = bin;
                m_accessors = RequiredArray(root, "accessors", "GLB root");
                m_bufferViews = RequiredArray(root, "bufferViews", "GLB root");
            }

            public Accessor GetAccessor(int index, string label)
            {
                if ((uint)index >= (uint)m_accessors.Count)
                    throw new FormatException(
                        $"{label} references accessor {index}, but only {m_accessors.Count} accessors exist.");
                if (!m_accessorCache.TryGetValue(index, out Accessor accessor))
                {
                    accessor = new Accessor(this,
                        JsonValues.Object(m_accessors[index], $"accessors[{index}]"), index);
                    m_accessorCache.Add(index, accessor);
                }
                return accessor;
            }

            public BufferView GetBufferView(int index, string label)
            {
                if ((uint)index >= (uint)m_bufferViews.Count)
                    throw new FormatException(
                        $"{label} references bufferView {index}, but only {m_bufferViews.Count} exist.");
                if (!m_bufferViewCache.TryGetValue(index, out BufferView view))
                {
                    Dictionary<string, object> json =
                        JsonValues.Object(m_bufferViews[index], $"bufferViews[{index}]");
                    int buffer = json.OptionalInt("buffer", 0);
                    if (buffer != 0)
                        throw new NotSupportedException(
                            $"bufferViews[{index}] references buffer {buffer}; only embedded GLB buffer 0 is supported.");
                    int byteOffset = json.OptionalInt("byteOffset", 0);
                    int byteLength = json.RequiredInt("byteLength");
                    int byteStride = json.OptionalInt("byteStride", 0);
                    if (byteOffset < 0 || byteLength < 0)
                        throw new FormatException($"bufferViews[{index}] has a negative byte range.");
                    if ((long)byteOffset + byteLength > m_bufferLength)
                        throw new InvalidDataException(
                            $"bufferViews[{index}] extends beyond buffers[0].byteLength.");
                    if (byteStride != 0 && (byteStride < 4 || byteStride > 252 || (byteStride & 3) != 0))
                        throw new FormatException(
                            $"bufferViews[{index}].byteStride must be a multiple of 4 from 4 through 252.");
                    view = new BufferView(index, byteOffset, byteLength, byteStride);
                    m_bufferViewCache.Add(index, view);
                }
                return view;
            }

            public float ReadComponent(int byteOffset, int componentType, bool normalized)
            {
                switch (componentType)
                {
                    case 5120:
                    {
                        sbyte value = unchecked((sbyte)m_bin[byteOffset]);
                        return normalized ? Math.Max(value / 127f, -1f) : value;
                    }
                    case 5121:
                    {
                        byte value = m_bin[byteOffset];
                        return normalized ? value / 255f : value;
                    }
                    case 5122:
                    {
                        short value = unchecked((short)(m_bin[byteOffset] | m_bin[byteOffset + 1] << 8));
                        return normalized ? Math.Max(value / 32767f, -1f) : value;
                    }
                    case 5123:
                    {
                        ushort value = (ushort)(m_bin[byteOffset] | m_bin[byteOffset + 1] << 8);
                        return normalized ? value / 65535f : value;
                    }
                    case 5125:
                    {
                        uint value = ReadUInt32(m_bin, byteOffset);
                        return normalized ? (float)(value / (double)uint.MaxValue) : value;
                    }
                    case 5126:
                        return BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32(m_bin, byteOffset)));
                    default:
                        throw new NotSupportedException(
                            $"glTF accessor componentType {componentType} is not supported.");
                }
            }

            public uint ReadSparseIndex(int byteOffset, int componentType)
            {
                return componentType switch
                {
                    5121 => m_bin[byteOffset],
                    5123 => (uint)(m_bin[byteOffset] | m_bin[byteOffset + 1] << 8),
                    5125 => ReadUInt32(m_bin, byteOffset),
                    _ => throw new FormatException(
                        $"Sparse accessor index componentType {componentType} must be 5121, 5123, or 5125."),
                };
            }
        }

        readonly struct BufferView
        {
            public readonly int Index;
            public readonly int ByteOffset;
            public readonly int ByteLength;
            public readonly int ByteStride;

            public BufferView(int index, int byteOffset, int byteLength, int byteStride)
            {
                Index = index;
                ByteOffset = byteOffset;
                ByteLength = byteLength;
                ByteStride = byteStride;
            }

            public void RequireRange(long relativeOffset, long byteCount, string label)
            {
                if (relativeOffset < 0 || byteCount < 0 || relativeOffset + byteCount > ByteLength)
                    throw new InvalidDataException(
                        $"{label} byte range [{relativeOffset}, {relativeOffset + byteCount}) " +
                        $"is outside bufferViews[{Index}] length {ByteLength}.");
            }
        }

        sealed class Accessor
        {
            readonly GltfContext m_context;
            readonly int m_index;
            readonly int m_bufferView;
            readonly int m_byteOffset;
            readonly int m_componentType;
            readonly bool m_normalized;
            readonly SparseInfo m_sparse;

            public readonly int Count;
            public readonly int Components;

            public Accessor(GltfContext context, Dictionary<string, object> json, int index)
            {
                m_context = context;
                m_index = index;
                m_bufferView = json.OptionalInt("bufferView", -1);
                m_byteOffset = json.OptionalInt("byteOffset", 0);
                m_componentType = json.RequiredInt("componentType");
                Count = json.RequiredInt("count");
                string type = RequiredString(json, "type", $"accessors[{index}]");
                Components = ComponentCount(type, index);
                m_normalized = json.OptionalBool("normalized", false);

                if (Count < 0 || m_byteOffset < 0)
                    throw new FormatException($"accessors[{index}] has a negative count or byteOffset.");
                ComponentSize(m_componentType, index); // validates the component type
                if (m_byteOffset % ComponentSize(m_componentType, index) != 0)
                    throw new FormatException(
                        $"accessors[{index}].byteOffset is not aligned to its component size.");

                if (json.TryGetValue("sparse", out object sparseRaw) && sparseRaw != null)
                    m_sparse = new SparseInfo(JsonValues.Object(sparseRaw, $"accessors[{index}].sparse"), index);

                if (m_bufferView < 0 && m_sparse == null)
                    throw new FormatException(
                        $"accessors[{index}] has neither a bufferView nor sparse data.");
                if (m_bufferView < 0 && m_byteOffset != 0)
                    throw new FormatException(
                        $"accessors[{index}] cannot have byteOffset without a bufferView.");
            }

            public void RequireShape(int components, string label)
            {
                if (Components != components)
                    throw new FormatException(
                        $"{label} has {Components} components; expected {components}.");
            }

            public void RequireShapeAndCount(int components, int count, string label)
            {
                RequireShape(components, label);
                if (Count != count)
                    throw new FormatException(
                        $"{label} contains {Count} elements; expected {count}.");
            }

            public float[] ReadAll()
            {
                int valueCount;
                int componentSize = ComponentSize(m_componentType, m_index);
                int elementSize;
                try
                {
                    valueCount = checked(Count * Components);
                    elementSize = checked(componentSize * Components);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException(
                        $"accessors[{m_index}] is too large to decode.", exception);
                }

                var result = new float[valueCount];
                if (m_bufferView >= 0 && Count > 0)
                {
                    BufferView view = m_context.GetBufferView(m_bufferView, $"accessors[{m_index}]");
                    int stride = view.ByteStride == 0 ? elementSize : view.ByteStride;
                    if (stride < elementSize || stride % componentSize != 0)
                        throw new FormatException(
                            $"accessors[{m_index}] element size {elementSize} is incompatible with " +
                            $"bufferViews[{view.Index}].byteStride {stride}.");
                    long required = (long)(Count - 1) * stride + elementSize;
                    view.RequireRange(m_byteOffset, required, $"accessors[{m_index}]");

                    int destination = 0;
                    for (int element = 0; element < Count; ++element)
                    {
                        int source = checked(view.ByteOffset + m_byteOffset + element * stride);
                        for (int component = 0; component < Components; ++component)
                        {
                            result[destination++] = m_context.ReadComponent(
                                source + component * componentSize, m_componentType, m_normalized);
                        }
                    }
                }

                m_sparse?.Apply(m_context, result, Count, Components, m_componentType, m_normalized, m_index);
                return result;
            }

            static int ComponentCount(string type, int accessor)
            {
                return type switch
                {
                    "SCALAR" => 1,
                    "VEC2" => 2,
                    "VEC3" => 3,
                    "VEC4" => 4,
                    _ => throw new NotSupportedException(
                        $"accessors[{accessor}] type '{type}' is not supported for Gaussian splat attributes."),
                };
            }

            static int ComponentSize(int componentType, int accessor)
            {
                return componentType switch
                {
                    5120 => 1,
                    5121 => 1,
                    5122 => 2,
                    5123 => 2,
                    5125 => 4,
                    5126 => 4,
                    _ => throw new NotSupportedException(
                        $"accessors[{accessor}] componentType {componentType} is not supported; " +
                        "expected 5120, 5121, 5122, 5123, 5125, or 5126."),
                };
            }
        }

        sealed class SparseInfo
        {
            readonly int m_count;
            readonly int m_indicesBufferView;
            readonly int m_indicesByteOffset;
            readonly int m_indicesComponentType;
            readonly int m_valuesBufferView;
            readonly int m_valuesByteOffset;

            public SparseInfo(Dictionary<string, object> json, int accessor)
            {
                m_count = json.RequiredInt("count");
                if (m_count <= 0)
                    throw new FormatException($"accessors[{accessor}].sparse.count must be positive.");

                Dictionary<string, object> indices =
                    RequiredObject(json, "indices", $"accessors[{accessor}].sparse");
                m_indicesBufferView = indices.RequiredInt("bufferView");
                m_indicesByteOffset = indices.OptionalInt("byteOffset", 0);
                m_indicesComponentType = indices.RequiredInt("componentType");
                if (m_indicesComponentType != 5121 &&
                    m_indicesComponentType != 5123 &&
                    m_indicesComponentType != 5125)
                    throw new FormatException(
                        $"accessors[{accessor}].sparse.indices.componentType must be 5121, 5123, or 5125.");

                Dictionary<string, object> values =
                    RequiredObject(json, "values", $"accessors[{accessor}].sparse");
                m_valuesBufferView = values.RequiredInt("bufferView");
                m_valuesByteOffset = values.OptionalInt("byteOffset", 0);
                if (m_indicesByteOffset < 0 || m_valuesByteOffset < 0)
                    throw new FormatException(
                        $"accessors[{accessor}].sparse byte offsets cannot be negative.");
            }

            public void Apply(
                GltfContext context,
                float[] destination,
                int accessorCount,
                int components,
                int componentType,
                bool normalized,
                int accessor)
            {
                if (m_count > accessorCount)
                    throw new FormatException(
                        $"accessors[{accessor}].sparse.count {m_count} exceeds accessor count {accessorCount}.");

                BufferView indicesView = context.GetBufferView(
                    m_indicesBufferView, $"accessors[{accessor}].sparse.indices");
                BufferView valuesView = context.GetBufferView(
                    m_valuesBufferView, $"accessors[{accessor}].sparse.values");
                if (indicesView.ByteStride != 0 || valuesView.ByteStride != 0)
                    throw new FormatException(
                        $"accessors[{accessor}] sparse indices and values must be tightly packed.");

                int indexSize = m_indicesComponentType == 5121 ? 1 :
                    m_indicesComponentType == 5123 ? 2 : 4;
                int valueSize = componentType == 5120 || componentType == 5121 ? 1 :
                    componentType == 5122 || componentType == 5123 ? 2 : 4;
                int elementSize = checked(valueSize * components);
                indicesView.RequireRange(m_indicesByteOffset, (long)m_count * indexSize,
                    $"accessors[{accessor}].sparse.indices");
                valuesView.RequireRange(m_valuesByteOffset, (long)m_count * elementSize,
                    $"accessors[{accessor}].sparse.values");

                uint previous = 0;
                for (int sparse = 0; sparse < m_count; ++sparse)
                {
                    uint index = context.ReadSparseIndex(
                        indicesView.ByteOffset + m_indicesByteOffset + sparse * indexSize,
                        m_indicesComponentType);
                    if (index >= accessorCount)
                        throw new InvalidDataException(
                            $"accessors[{accessor}] sparse index {index} is outside count {accessorCount}.");
                    if (sparse > 0 && index <= previous)
                        throw new InvalidDataException(
                            $"accessors[{accessor}] sparse indices must be strictly increasing.");
                    previous = index;

                    int source = valuesView.ByteOffset + m_valuesByteOffset + sparse * elementSize;
                    int target = checked((int)index * components);
                    for (int component = 0; component < components; ++component)
                    {
                        destination[target + component] = context.ReadComponent(
                            source + component * valueSize, componentType, normalized);
                    }
                }
            }
        }
    }
}
