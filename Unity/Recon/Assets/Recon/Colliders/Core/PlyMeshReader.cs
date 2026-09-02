using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace Recon.Colliders
{
    /// <summary>A PLY that cannot be turned into a collider, with the fix in the message.</summary>
    public sealed class PlyFormatException : Exception
    {
        public PlyFormatException(string message) : base(message) { }
        public PlyFormatException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A triangle mesh read from a .ply, in SCENE UNITS. Nothing here is scaled or flipped:
    /// docs/FRAMES.md puts the one conversion on the GPU side before export, and SceneRoot applies
    /// unitScale exactly once. A client-side flip would cancel the exporter's and cost a day.
    /// </summary>
    public sealed class PlyMesh
    {
        public Vector3[] Vertices { get; set; }

        /// <summary>Per-vertex normals, or null when the file declared none.</summary>
        public Vector3[] Normals { get; set; }

        /// <summary>Flat triangle index list, three entries per triangle.</summary>
        public int[] Triangles { get; set; }

        public List<string> Comments { get; } = new List<string>();

        /// <summary>"ascii" or "binary_little_endian".</summary>
        public string Format { get; set; }

        public bool HasNormals => Normals != null;
        public int VertexCount => Vertices?.Length ?? 0;
        public int TriangleCount => (Triangles?.Length ?? 0) / 3;

        /// <summary>
        /// True when the header comments declare BOTH "handedness: left" and "vertical axis: y",
        /// case and spacing insensitive, which is what tools/convert_ply_to_unity.py and
        /// tools/splat_to_mesh.py write (capitalised, as "Vertical Axis: y" / "Handedness: left").
        ///
        /// This is the client half of docs/FRAMES.md. A mesh that never went through the converter
        /// is mirrored, and a mirrored collider is the dangerous failure: physics still runs, so
        /// nothing looks broken until the impact points are quietly wrong. The loader refuses.
        /// </summary>
        public bool DeclaresUnityFrame
        {
            get
            {
                var joined = new StringBuilder();
                foreach (var c in Comments)
                {
                    if (c == null) continue;
                    foreach (var ch in c) if (!char.IsWhiteSpace(ch)) joined.Append(char.ToLowerInvariant(ch));
                    joined.Append('\n');
                }
                var text = joined.ToString();
                return text.Contains("handedness:left") && text.Contains("verticalaxis:y");
            }
        }

        public override string ToString() =>
            $"{VertexCount} vertices, {TriangleCount} triangles, {Format}, " +
            $"normals {(HasNormals ? "yes" : "no")}, unity frame {(DeclaresUnityFrame ? "declared" : "NOT declared")}";
    }

    /// <summary>
    /// Reads the collider mesh produced by tools/splat_to_mesh.py (PR #20). That tool writes
    /// whatever Open3D's write_triangle_mesh(write_ascii=False, write_vertex_normals=True) emits,
    /// which is binary little endian with float x y z nx ny nz and faces as
    /// "property list uchar int vertex_indices", and then patches its frame comments into the header
    /// afterwards.
    ///
    /// That header is not pinned by a test on the tool side, so this reader is tolerant on purpose:
    /// ascii or binary little endian, vertex properties in any order with unknown ones skipped by
    /// their declared size, unknown elements skipped, polygons fan-triangulated. It is strict where
    /// being wrong is silent: big endian, a file with no faces, an out-of-range index and a
    /// truncated body are all refused with the fix in the message, and the caller must check
    /// <see cref="PlyMesh.DeclaresUnityFrame"/> before using the mesh as collision geometry.
    ///
    /// Pure C#, no UnityEngine: parsed and tested under `dotnet test Unity/Recon.Core.Tests`.
    /// </summary>
    public static class PlyMeshReader
    {
        enum PlyType { Float32, Float64, Int8, UInt8, Int16, UInt16, Int32, UInt32 }

        sealed class Property
        {
            public string Name;
            public PlyType Type;          // scalar type, or the index type of a list
            public bool IsList;
            public PlyType CountType;
        }

        sealed class Element
        {
            public string Name;
            public int Count;
            public List<Property> Properties = new List<Property>();
        }

        public static PlyMesh ReadFile(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                return Read(fs);
        }

        public static PlyMesh Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var mesh = new PlyMesh();
            var elements = ParseHeader(stream, mesh);

            var vertex = Find(elements, "vertex");
            if (vertex == null)
                throw new PlyFormatException("No 'element vertex' in the PLY header, so there is no geometry to read.");

            var face = Find(elements, "face");
            if (face == null)
                throw new PlyFormatException(
                    "No 'element face' in the PLY header. This is a point cloud, and a point cloud is " +
                    "not a collider: PhysX needs triangles. Run tools/splat_to_mesh.py to build a mesh first.");
            if (face.Count <= 0)
                throw new PlyFormatException(
                    "The PLY declares 0 faces, so it is a point cloud rather than a mesh, and a point " +
                    "cloud is not a collider. Run tools/splat_to_mesh.py to build a mesh first.");

            if (mesh.Format == "ascii") ReadAsciiBody(stream, elements, vertex, face, mesh);
            else ReadBinaryBody(stream, elements, vertex, face, mesh);

            return mesh;
        }

        // ------------------------------------------------------------------ header

        static List<Element> ParseHeader(Stream stream, PlyMesh mesh)
        {
            var lines = ReadHeaderLines(stream);
            if (lines.Count == 0 || lines[0].Trim() != "ply")
                throw new PlyFormatException("Not a PLY file: the first line is not 'ply'.");

            var elements = new List<Element>();
            Element current = null;

            for (int i = 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "comment":
                        mesh.Comments.Add(line.Length > 8 ? line.Substring(8).Trim() : string.Empty);
                        break;

                    case "obj_info":
                        break;                       // recorded by nothing, but must not break parsing

                    case "format":
                        if (parts.Length < 2) throw new PlyFormatException("Malformed 'format' line in the PLY header.");
                        if (parts[1] == "binary_big_endian")
                            throw new PlyFormatException(
                                "PLY is binary_big_endian, which this reader does not support. Re-export it " +
                                "little endian: Open3D's write_triangle_mesh (what tools/splat_to_mesh.py uses) " +
                                "writes binary little endian, so a big endian file did not come from the pipeline.");
                        if (parts[1] != "ascii" && parts[1] != "binary_little_endian")
                            throw new PlyFormatException($"Unknown PLY format '{parts[1]}'; expected ascii or binary_little_endian.");
                        mesh.Format = parts[1];
                        break;

                    case "element":
                        if (parts.Length < 3) throw new PlyFormatException($"Malformed 'element' line: {line}");
                        current = new Element { Name = parts[1], Count = ParseCount(parts[2], line) };
                        elements.Add(current);
                        break;

                    case "property":
                        if (current == null) throw new PlyFormatException($"'property' before any 'element': {line}");
                        current.Properties.Add(ParseProperty(parts, line));
                        break;

                    case "end_header":
                        break;

                    default:
                        break;                       // unknown header keyword: ignore, do not refuse
                }
            }

            if (mesh.Format == null)
                throw new PlyFormatException("The PLY header has no 'format' line.");
            return elements;
        }

        static List<string> ReadHeaderLines(Stream stream)
        {
            var lines = new List<string>();
            var sb = new StringBuilder();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    if (sb.Length > 0) lines.Add(sb.ToString());
                    throw new PlyFormatException(
                        lines.Count == 0
                            ? "The file is empty; a PLY starts with 'ply' and a header."
                            : "The PLY header ended before 'end_header'; the file is truncated.");
                }
                if (b == '\n')
                {
                    var line = sb.ToString().TrimEnd('\r');
                    sb.Length = 0;
                    lines.Add(line);
                    if (line.Trim() == "end_header") return lines;
                    // A header this long is not a header; refuse rather than read a whole binary file.
                    if (lines.Count > 4096)
                        throw new PlyFormatException("No 'end_header' in the first 4096 lines; this is not a PLY header.");
                }
                else
                {
                    sb.Append((char)b);
                    if (sb.Length > 8192) throw new PlyFormatException("A PLY header line is over 8 KB; refusing to read further.");
                }
            }
        }

        static int ParseCount(string token, string line)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 0)
                throw new PlyFormatException($"Element count '{token}' is not a non-negative integer: {line}");
            return count;
        }

        static Property ParseProperty(string[] parts, string line)
        {
            if (parts.Length >= 5 && parts[1] == "list")
            {
                // property list <count type> <index type> <name>
                return new Property
                {
                    IsList = true,
                    CountType = ParseType(parts[2], line),
                    Type = ParseType(parts[3], line),
                    Name = parts[4],
                };
            }
            if (parts.Length < 3) throw new PlyFormatException($"Malformed 'property' line: {line}");
            return new Property { Name = parts[2], Type = ParseType(parts[1], line) };
        }

        static PlyType ParseType(string token, string line)
        {
            switch (token)
            {
                case "float": case "float32": return PlyType.Float32;
                case "double": case "float64": return PlyType.Float64;
                case "char": case "int8": return PlyType.Int8;
                case "uchar": case "uint8": return PlyType.UInt8;
                case "short": case "int16": return PlyType.Int16;
                case "ushort": case "uint16": return PlyType.UInt16;
                case "int": case "int32": return PlyType.Int32;
                case "uint": case "uint32": return PlyType.UInt32;
                default:
                    throw new PlyFormatException($"Unknown PLY property type '{token}': {line}");
            }
        }

        static int SizeOf(PlyType t)
        {
            switch (t)
            {
                case PlyType.Int8: case PlyType.UInt8: return 1;
                case PlyType.Int16: case PlyType.UInt16: return 2;
                case PlyType.Float64: return 8;
                default: return 4;
            }
        }

        static Element Find(List<Element> elements, string name)
        {
            foreach (var e in elements) if (e.Name == name) return e;
            return null;
        }

        static bool IsFloat(PlyType t) => t == PlyType.Float32 || t == PlyType.Float64;

        // ------------------------------------------------------------------ property lookup

        sealed class VertexLayout
        {
            public int X = -1, Y = -1, Z = -1, Nx = -1, Ny = -1, Nz = -1;
            public bool HasNormals => Nx >= 0 && Ny >= 0 && Nz >= 0;
        }

        static VertexLayout LayoutOf(Element vertex)
        {
            var layout = new VertexLayout();
            for (int i = 0; i < vertex.Properties.Count; i++)
            {
                switch (vertex.Properties[i].Name)
                {
                    case "x": layout.X = i; break;
                    case "y": layout.Y = i; break;
                    case "z": layout.Z = i; break;
                    case "nx": layout.Nx = i; break;
                    case "ny": layout.Ny = i; break;
                    case "nz": layout.Nz = i; break;
                }
            }
            var missing = new List<string>();
            if (layout.X < 0) missing.Add("x");
            if (layout.Y < 0) missing.Add("y");
            if (layout.Z < 0) missing.Add("z");
            if (missing.Count > 0)
                throw new PlyFormatException(
                    $"The vertex element is missing {string.Join(", ", missing)}; a mesh needs x, y and z.");
            return layout;
        }

        static Property FaceIndexProperty(Element face)
        {
            foreach (var p in face.Properties)
                if (p.IsList && (p.Name == "vertex_indices" || p.Name == "vertex_index")) return p;
            foreach (var p in face.Properties)
                if (p.IsList) return p;              // some exporters name it something else entirely
            throw new PlyFormatException(
                "The face element has no 'property list ... vertex_indices', so there are no triangles to read.");
        }

        // ------------------------------------------------------------------ ascii body

        static void ReadAsciiBody(Stream stream, List<Element> elements, Element vertex, Element face, PlyMesh mesh)
        {
            string body;
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1 << 16, true))
                body = reader.ReadToEnd();
            var tokens = body.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int cursor = 0;

            double NextNumber()
            {
                if (cursor >= tokens.Length)
                    throw new PlyFormatException("The PLY body ended early: the header declares more data than the file holds.");
                var token = tokens[cursor++];
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw new PlyFormatException($"'{token}' in the PLY body is not a number.");
                return value;
            }

            foreach (var element in elements)
            {
                if (element == vertex)
                {
                    var layout = LayoutOf(vertex);
                    mesh.Vertices = new Vector3[vertex.Count];
                    if (layout.HasNormals) mesh.Normals = new Vector3[vertex.Count];
                    var values = new double[vertex.Properties.Count];
                    for (int v = 0; v < vertex.Count; v++)
                    {
                        for (int p = 0; p < vertex.Properties.Count; p++)
                        {
                            if (vertex.Properties[p].IsList)
                            {
                                int n = (int)NextNumber();
                                for (int k = 0; k < n; k++) NextNumber();
                                values[p] = 0;
                            }
                            else values[p] = NextNumber();
                        }
                        mesh.Vertices[v] = new Vector3((float)values[layout.X], (float)values[layout.Y], (float)values[layout.Z]);
                        if (layout.HasNormals)
                            mesh.Normals[v] = new Vector3((float)values[layout.Nx], (float)values[layout.Ny], (float)values[layout.Nz]);
                    }
                }
                else if (element == face)
                {
                    var indexProperty = FaceIndexProperty(face);
                    var triangles = new List<int>(face.Count * 3);
                    var polygon = new List<int>(8);
                    for (int f = 0; f < face.Count; f++)
                    {
                        foreach (var p in face.Properties)
                        {
                            if (p != indexProperty)
                            {
                                if (p.IsList) { int n = (int)NextNumber(); for (int k = 0; k < n; k++) NextNumber(); }
                                else NextNumber();
                                continue;
                            }
                            int count = (int)NextNumber();
                            if (count < 0) throw new PlyFormatException($"Face {f} declares {count} vertices.");
                            polygon.Clear();
                            for (int k = 0; k < count; k++) polygon.Add((int)NextNumber());
                            FanTriangulate(polygon, triangles, mesh.Vertices?.Length ?? 0, f);
                        }
                    }
                    mesh.Triangles = triangles.ToArray();
                }
                else
                {
                    // Unknown element: consume exactly its declared values so the next element still lines up.
                    for (int i = 0; i < element.Count; i++)
                        foreach (var p in element.Properties)
                        {
                            if (p.IsList) { int n = (int)NextNumber(); for (int k = 0; k < n; k++) NextNumber(); }
                            else NextNumber();
                        }
                }
            }

            RequireTriangles(mesh);
        }

        // ------------------------------------------------------------------ binary body

        static void ReadBinaryBody(Stream stream, List<Element> elements, Element vertex, Element face, PlyMesh mesh)
        {
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                try
                {
                    foreach (var element in elements)
                    {
                        if (element == vertex) ReadBinaryVertices(reader, vertex, mesh);
                        else if (element == face) ReadBinaryFaces(reader, face, mesh);
                        else SkipBinaryElement(reader, element);
                    }
                }
                catch (EndOfStreamException e)
                {
                    throw new PlyFormatException(
                        "The PLY body ended before the header said it would; the file is truncated or the " +
                        "header does not describe it. Re-export it with tools/splat_to_mesh.py.", e);
                }
            }
            RequireTriangles(mesh);
        }

        static void ReadBinaryVertices(BinaryReader reader, Element vertex, PlyMesh mesh)
        {
            var layout = LayoutOf(vertex);
            mesh.Vertices = new Vector3[vertex.Count];
            if (layout.HasNormals) mesh.Normals = new Vector3[vertex.Count];
            var values = new double[vertex.Properties.Count];

            for (int v = 0; v < vertex.Count; v++)
            {
                for (int p = 0; p < vertex.Properties.Count; p++)
                {
                    var property = vertex.Properties[p];
                    if (property.IsList)
                    {
                        long n = (long)ReadScalar(reader, property.CountType);
                        for (long k = 0; k < n; k++) ReadScalar(reader, property.Type);
                        values[p] = 0;
                    }
                    else values[p] = ReadScalar(reader, property.Type);
                }
                mesh.Vertices[v] = new Vector3((float)values[layout.X], (float)values[layout.Y], (float)values[layout.Z]);
                if (layout.HasNormals)
                    mesh.Normals[v] = new Vector3((float)values[layout.Nx], (float)values[layout.Ny], (float)values[layout.Nz]);
            }
        }

        static void ReadBinaryFaces(BinaryReader reader, Element face, PlyMesh mesh)
        {
            var indexProperty = FaceIndexProperty(face);
            if (IsFloat(indexProperty.Type))
                throw new PlyFormatException($"Face indices are declared as a floating point type; that cannot be an index.");

            var triangles = new List<int>(face.Count * 3);
            var polygon = new List<int>(8);
            int vertexCount = mesh.Vertices?.Length ?? 0;

            for (int f = 0; f < face.Count; f++)
            {
                foreach (var property in face.Properties)
                {
                    if (property != indexProperty)
                    {
                        if (property.IsList)
                        {
                            long n = (long)ReadScalar(reader, property.CountType);
                            for (long k = 0; k < n; k++) ReadScalar(reader, property.Type);
                        }
                        else ReadScalar(reader, property.Type);
                        continue;
                    }

                    long count = (long)ReadScalar(reader, property.CountType);
                    if (count < 0 || count > 1024)
                        throw new PlyFormatException(
                            $"Face {f} declares {count} vertices, which is not a polygon. The count type in the " +
                            "header ('property list <count type> ...') probably does not match the file.");
                    polygon.Clear();
                    for (long k = 0; k < count; k++) polygon.Add((int)ReadScalar(reader, property.Type));
                    FanTriangulate(polygon, triangles, vertexCount, f);
                }
            }
            mesh.Triangles = triangles.ToArray();
        }

        static void SkipBinaryElement(BinaryReader reader, Element element)
        {
            // Fixed-size elements are one seek; a list property has to be walked, because its length
            // lives in the data and not in the header.
            bool anyList = false;
            int fixedSize = 0;
            foreach (var p in element.Properties)
            {
                if (p.IsList) { anyList = true; break; }
                fixedSize += SizeOf(p.Type);
            }

            if (!anyList)
            {
                long bytes = (long)fixedSize * element.Count;
                for (long i = 0; i < bytes; i++)
                    if (reader.BaseStream.ReadByte() < 0) throw new EndOfStreamException();
                return;
            }

            for (int i = 0; i < element.Count; i++)
                foreach (var p in element.Properties)
                {
                    if (p.IsList)
                    {
                        long n = (long)ReadScalar(reader, p.CountType);
                        for (long k = 0; k < n; k++) ReadScalar(reader, p.Type);
                    }
                    else ReadScalar(reader, p.Type);
                }
        }

        static double ReadScalar(BinaryReader reader, PlyType type)
        {
            switch (type)
            {
                case PlyType.Float32: return reader.ReadSingle();
                case PlyType.Float64: return reader.ReadDouble();
                case PlyType.Int8: return reader.ReadSByte();
                case PlyType.UInt8: return reader.ReadByte();
                case PlyType.Int16: return reader.ReadInt16();
                case PlyType.UInt16: return reader.ReadUInt16();
                case PlyType.Int32: return reader.ReadInt32();
                default: return reader.ReadUInt32();
            }
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        /// Splits a polygon into a triangle fan around its first vertex. Open3D writes triangles, but
        /// a mesh that went through another tool can carry quads, and a quad silently dropped is a
        /// hole in the collider that shots pass through.
        /// </summary>
        static void FanTriangulate(List<int> polygon, List<int> triangles, int vertexCount, int faceIndex)
        {
            if (polygon.Count < 3) return;           // a point or an edge is not a surface
            foreach (var index in polygon)
                if (index < 0 || index >= vertexCount)
                    throw new PlyFormatException(
                        $"Face {faceIndex} refers to vertex index {index}, outside the {vertexCount} vertices " +
                        "the header declares. The file is corrupt or the index type in the header is wrong.");

            for (int k = 1; k + 1 < polygon.Count; k++)
            {
                triangles.Add(polygon[0]);
                triangles.Add(polygon[k]);
                triangles.Add(polygon[k + 1]);
            }
        }

        static void RequireTriangles(PlyMesh mesh)
        {
            if (mesh.Triangles == null || mesh.Triangles.Length == 0)
                throw new PlyFormatException(
                    "The PLY produced no triangles, so it is a point cloud as far as physics is concerned " +
                    "and cannot be a collider.");
        }
    }
}
