// PlayCanvas lod-meta.json compatibility.
// PlayCanvas engine copyright (c) 2011-2026 PlayCanvas Ltd; MIT license.
// Unity port copyright (c) 2026 ARLOOPA.
// SPDX-License-Identifier: MIT

#if false // Temporarily disabled: lod-meta.json support is not part of the current public release.

using System;
using System.Collections.Generic;
using System.Globalization;
using Gsplat.Formats;
using UnityEngine;

namespace Gsplat
{
    /// <summary>A validated half-open splat slice referenced by one octree leaf and LOD.</summary>
    public readonly struct GsplatLodSlice
    {
        public readonly int FileIndex;
        public readonly int Offset;
        public readonly int Count;

        public bool IsPresent => FileIndex >= 0 && Count > 0;
        public int EndExclusive => checked(Offset + Count);

        public GsplatLodSlice(int fileIndex, int offset, int count)
        {
            FileIndex = fileIndex;
            Offset = offset;
            Count = count;
        }

        public static GsplatLodSlice Missing => new(-1, 0, 0);
    }

    /// <summary>One depth-first leaf from a PlayCanvas Gaussian-splat LOD tree.</summary>
    public sealed class GsplatLodLeaf
    {
        public Bounds Bounds { get; }
        public GsplatLodSlice[] Lods { get; }

        internal GsplatLodLeaf(Bounds bounds, GsplatLodSlice[] lods)
        {
            Bounds = bounds;
            Lods = lods;
        }
    }

    /// <summary>
    /// Immutable, rigorously validated representation of a PlayCanvas <c>lod-meta.json</c>.
    /// Bounds are converted to Unity coordinates while file intervals remain exact splat indices.
    /// </summary>
    public sealed class GsplatLodManifest
    {
        const int MaxLodLevels = 64;
        const int MaxFiles = 1_000_000;
        const int MaxTreeNodes = 1_000_000;
        const int MaxTreeDepth = 256;

        public int LodLevels { get; }
        public string[] Filenames { get; }
        public string Environment { get; }
        public Bounds Bounds { get; }
        public GsplatLodLeaf[] Leaves { get; }
        public int[] RequiredSplatCounts { get; }
        public SourceCoordinates SourceCoordinates { get; }

        GsplatLodManifest(int lodLevels, string[] filenames, string environment, Bounds bounds,
            GsplatLodLeaf[] leaves, int[] requiredSplatCounts, SourceCoordinates sourceCoordinates)
        {
            LodLevels = lodLevels;
            Filenames = filenames;
            Environment = environment;
            Bounds = bounds;
            Leaves = leaves;
            RequiredSplatCounts = requiredSplatCounts;
            SourceCoordinates = sourceCoordinates;
        }

        /// <summary>Parses a PlayCanvas manifest. Unspecified coordinates resolve to standard 3DGS RUB.</summary>
        public static GsplatLodManifest Parse(string json,
            SourceCoordinates sourceCoordinates = SourceCoordinates.Unspecified)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("LOD manifest JSON is empty.", nameof(json));
            if (!Enum.IsDefined(typeof(SourceCoordinates), sourceCoordinates))
                throw new ArgumentOutOfRangeException(nameof(sourceCoordinates));
            if (sourceCoordinates == SourceCoordinates.Unspecified)
                sourceCoordinates = SourceCoordinates.RUB;

            var root = JsonValues.Object(MiniJson.Deserialize(json), "root");
            int lodLevels = root.RequiredInt("lodLevels");
            if (lodLevels <= 0 || lodLevels > MaxLodLevels)
                throw new FormatException($"lodLevels must be between 1 and {MaxLodLevels}.");

            string[] filenames = root.StringArray("filenames");
            if (filenames.Length == 0 || filenames.Length > MaxFiles)
                throw new FormatException($"filenames must contain between 1 and {MaxFiles:N0} entries.");
            for (int i = 0; i < filenames.Length; ++i)
                filenames[i] = ValidateResourceName(filenames[i], $"filenames[{i}]");

            string environment = null;
            if (root.TryGetValue("environment", out object environmentValue) && environmentValue != null)
                environment = ValidateResourceName(JsonValues.String(environmentValue, "environment"), "environment");

            if (!root.TryGetValue("tree", out object treeValue))
                throw new FormatException("Required JSON member 'tree' is missing.");
            var tree = JsonValues.Object(treeValue, "tree");
            Bounds rootSourceBounds = ParseSourceBounds(tree, "tree.bound");
            Bounds convertedRootBounds = ConvertBounds(rootSourceBounds, sourceCoordinates);

            var leaves = new List<GsplatLodLeaf>();
            var requiredCounts = new int[filenames.Length];
            var stack = new Stack<NodeWork>();
            stack.Push(new NodeWork(tree, 0, "tree"));
            int visited = 0;

            while (stack.Count != 0)
            {
                NodeWork work = stack.Pop();
                if (++visited > MaxTreeNodes)
                    throw new FormatException($"LOD tree exceeds {MaxTreeNodes:N0} nodes.");
                if (work.Depth > MaxTreeDepth)
                    throw new FormatException($"LOD tree exceeds maximum depth {MaxTreeDepth} at {work.Path}.");

                Bounds sourceBounds = ParseSourceBounds(work.Node, $"{work.Path}.bound");
                bool hasLods = work.Node.TryGetValue("lods", out object lodsValue) && lodsValue != null;
                bool hasChildren = work.Node.TryGetValue("children", out object childrenValue) && childrenValue != null;
                if (hasLods == hasChildren)
                    throw new FormatException($"{work.Path} must contain exactly one of 'lods' or 'children'.");

                if (hasLods)
                {
                    var lodObject = JsonValues.Object(lodsValue, $"{work.Path}.lods");
                    var lods = new GsplatLodSlice[lodLevels];
                    for (int i = 0; i < lods.Length; ++i) lods[i] = GsplatLodSlice.Missing;

                    foreach (var pair in lodObject)
                    {
                        if (!int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int lod) ||
                            lod < 0 || lod >= lodLevels || pair.Key != lod.ToString(CultureInfo.InvariantCulture))
                            throw new FormatException($"{work.Path}.lods key '{pair.Key}' is not a canonical LOD index in [0,{lodLevels - 1}].");

                        var value = JsonValues.Object(pair.Value, $"{work.Path}.lods.{pair.Key}");
                        int fileIndex = value.RequiredInt("file");
                        int offset = value.OptionalInt("offset", 0);
                        int count = value.OptionalInt("count", 0);
                        if ((uint)fileIndex >= (uint)filenames.Length)
                            throw new FormatException($"{work.Path}.lods.{lod}.file {fileIndex} is outside filenames.");
                        if (offset < 0 || count < 0)
                            throw new FormatException($"{work.Path}.lods.{lod} offset and count must be non-negative.");

                        int end;
                        try { end = checked(offset + count); }
                        catch (OverflowException)
                        {
                            throw new FormatException($"{work.Path}.lods.{lod} interval overflows Int32.");
                        }

                        var slice = new GsplatLodSlice(fileIndex, offset, count);
                        lods[lod] = slice;
                        if (end > requiredCounts[fileIndex]) requiredCounts[fileIndex] = end;
                    }

                    if (lodObject.Count == 0)
                        throw new FormatException($"{work.Path}.lods must contain at least one entry.");
                    leaves.Add(new GsplatLodLeaf(ConvertBounds(sourceBounds, sourceCoordinates), lods));
                    continue;
                }

                List<object> children = JsonValues.Array(childrenValue, $"{work.Path}.children");
                if (children.Count == 0)
                    throw new FormatException($"{work.Path}.children must not be empty.");
                for (int i = children.Count - 1; i >= 0; --i)
                {
                    var child = JsonValues.Object(children[i], $"{work.Path}.children[{i}]");
                    stack.Push(new NodeWork(child, work.Depth + 1,
                        $"{work.Path}.children[{i}]"));
                }
            }

            if (leaves.Count == 0)
                throw new FormatException("LOD tree contains no leaves.");
            return new GsplatLodManifest(lodLevels, filenames, environment, convertedRootBounds,
                leaves.ToArray(), requiredCounts, sourceCoordinates);
        }

        /// <summary>Validates all manifest intervals for a file against its decoded splat count.</summary>
        public void ValidateFileSplatCount(int fileIndex, uint splatCount)
        {
            if ((uint)fileIndex >= (uint)Filenames.Length)
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            if (RequiredSplatCounts[fileIndex] > splatCount)
                throw new FormatException(
                    $"LOD file '{Filenames[fileIndex]}' has {splatCount:N0} splats, but its manifest references index {RequiredSplatCounts[fileIndex] - 1:N0}.");
        }

        static string ValidateResourceName(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException($"{name} must not be empty.");
            if (value.IndexOf('\0') >= 0)
                throw new FormatException($"{name} contains a null character.");
            return value.Replace('\\', '/');
        }

        static Bounds ParseSourceBounds(Dictionary<string, object> node, string name)
        {
            if (!node.TryGetValue("bound", out object value))
                throw new FormatException($"Required JSON member '{name}' is missing.");
            var bound = JsonValues.Object(value, name);
            float[] min = bound.FloatArray("min", 3);
            float[] max = bound.FloatArray("max", 3);
            for (int axis = 0; axis < 3; ++axis)
                if (min[axis] > max[axis])
                    throw new FormatException($"{name}.min[{axis}] exceeds max[{axis}].");
            var minVector = new Vector3(min[0], min[1], min[2]);
            var maxVector = new Vector3(max[0], max[1], max[2]);
            return new Bounds((minVector + maxVector) * 0.5f, maxVector - minVector);
        }

        static Bounds ConvertBounds(Bounds source, SourceCoordinates coordinates)
        {
            var (xs, ys, zs) = GsplatUtils.AxisSigns(coordinates);
            Vector3 a = Vector3.Scale(source.min, new Vector3(xs, ys, zs));
            Vector3 b = Vector3.Scale(source.max, new Vector3(xs, ys, zs));
            Vector3 min = Vector3.Min(a, b);
            Vector3 max = Vector3.Max(a, b);
            return new Bounds((min + max) * 0.5f, max - min);
        }

        readonly struct NodeWork
        {
            public readonly Dictionary<string, object> Node;
            public readonly int Depth;
            public readonly string Path;

            public NodeWork(Dictionary<string, object> node, int depth, string path)
            {
                Node = node;
                Depth = depth;
                Path = path;
            }
        }
    }
}
#endif
