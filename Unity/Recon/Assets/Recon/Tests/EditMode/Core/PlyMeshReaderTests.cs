using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using Recon.Colliders;

namespace Recon.Tests.Core
{
    /// <summary>
    /// Tests for the collider-mesh PLY reader. The file this reads is produced by
    /// tools/splat_to_mesh.py (PR #20), which writes whatever Open3D's write_triangle_mesh emits
    /// (binary little endian, float x y z nx ny nz, faces as "property list uchar int
    /// vertex_indices") and then patches frame comments into the header afterwards.
    ///
    /// That header is NOT pinned by a test on the tool side, so this reader is deliberately
    /// tolerant about what it accepts and deliberately strict about what it asserts: properties in
    /// any order, extra properties skipped by their declared size, ascii or binary little endian,
    /// polygons fan-triangulated, and a loud refusal for big endian, for a file with no faces, and
    /// for one that does not declare the Unity frame.
    /// </summary>
    public class PlyMeshReaderTests
    {
        // ------------------------------------------------------------------ the shape under test

        // Unit cube, corners at 0 and 1. Written either as 12 triangles or as 6 quads that must
        // fan-triangulate to exactly the same 12 triangles.
        static readonly Vector3[] CubeVertices =
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 1f, 1f), new Vector3(0f, 1f, 1f),
        };

        static readonly int[][] CubeQuads =
        {
            new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 },
            new[] { 1, 2, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 },
        };

        static int[] ExpectedTriangles()
        {
            var tris = new List<int>();
            foreach (var q in CubeQuads)
                for (int k = 1; k + 1 < q.Length; k++) { tris.Add(q[0]); tris.Add(q[k]); tris.Add(q[k + 1]); }
            return tris.ToArray();
        }

        static PlyMesh Read(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes)) return PlyMeshReader.Read(ms);
        }

        // ------------------------------------------------------------------ ascii and binary

        [Test]
        public void AsciiCubeWithNormalsReadsBackExactly()
        {
            var mesh = Read(new PlyFileBuilder { Binary = false }.Build());

            Assert.That(mesh.Format, Is.EqualTo("ascii"));
            Assert.That(mesh.Vertices, Has.Length.EqualTo(8));
            Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()));
            Assert.That(mesh.Vertices[6], Is.EqualTo(new Vector3(1f, 1f, 1f)));
            Assert.That(mesh.Normals, Is.Not.Null);
            Assert.That(mesh.Normals, Has.Length.EqualTo(8));
            Assert.That(mesh.Normals[6].Length(), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(mesh.TriangleCount, Is.EqualTo(12));
        }

        [Test]
        public void BinaryLittleEndianCubeWithNormalsReadsBackExactly()
        {
            var mesh = Read(new PlyFileBuilder { Binary = true }.Build());

            Assert.That(mesh.Format, Is.EqualTo("binary_little_endian"));
            Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices));
            Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()));
            Assert.That(mesh.HasNormals, Is.True);
        }

        [Test]
        public void MeshWithNoNormalsReadsWithNullNormals()
        {
            foreach (bool binary in new[] { false, true })
            {
                var mesh = Read(new PlyFileBuilder { Binary = binary, Normals = false }.Build());
                Assert.That(mesh.Normals, Is.Null, "binary = " + binary);
                Assert.That(mesh.HasNormals, Is.False);
                Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices));
                Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()));
            }
        }

        /// <summary>Six quads must fan-triangulate to the same 12 triangles the triangle file has.</summary>
        [Test]
        public void QuadsAreFanTriangulated()
        {
            foreach (bool binary in new[] { false, true })
            {
                var mesh = Read(new PlyFileBuilder { Binary = binary, Quads = true }.Build());
                Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()), "binary = " + binary);
                Assert.That(mesh.TriangleCount, Is.EqualTo(12));
            }
        }

        // ------------------------------------------------------------------ tolerance

        /// <summary>An unrelated colour property between the coordinates must be skipped by its size.</summary>
        [Test]
        public void ExtraUcharPropertyBetweenCoordinatesIsSkipped()
        {
            foreach (bool binary in new[] { false, true })
            {
                var mesh = Read(new PlyFileBuilder { Binary = binary, RedBetweenCoordinates = true }.Build());
                Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices), "binary = " + binary);
                Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()));
            }
        }

        /// <summary>x, y, z and the normals may be declared in any order; the reader follows the header.</summary>
        [Test]
        public void VertexPropertiesInAnyOrderAreReadByName()
        {
            foreach (bool binary in new[] { false, true })
            {
                var mesh = Read(new PlyFileBuilder { Binary = binary, ShuffledPropertyOrder = true }.Build());
                Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices), "binary = " + binary);
                Assert.That(mesh.Normals[6].Length(), Is.EqualTo(1f).Within(1e-4f));
            }
        }

        [Test]
        public void DoublePrecisionCoordinatesAreAccepted()
        {
            foreach (bool binary in new[] { false, true })
            {
                var mesh = Read(new PlyFileBuilder { Binary = binary, CoordinateType = "double" }.Build());
                Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices), "binary = " + binary);
            }
        }

        [Test]
        public void Float32AndFloat64SpellingsAreAccepted()
        {
            var mesh = Read(new PlyFileBuilder { Binary = true, CoordinateType = "float32" }.Build());
            Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices));
            mesh = Read(new PlyFileBuilder { Binary = true, CoordinateType = "float64" }.Build());
            Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices));
        }

        [Test]
        public void Int32AndUint32IndicesBothWork()
        {
            foreach (var indexType in new[] { "int", "int32", "uint", "uint32", "uchar" })
                foreach (bool binary in new[] { false, true })
                {
                    var mesh = Read(new PlyFileBuilder { Binary = binary, IndexType = indexType }.Build());
                    Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()),
                        indexType + ", binary = " + binary);
                }
        }

        [Test]
        public void VertexIndexSingularIsAcceptedAsWellAsVertexIndices()
        {
            var mesh = Read(new PlyFileBuilder { Binary = true, FaceListName = "vertex_index" }.Build());
            Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()));
        }

        /// <summary>
        /// Unknown elements before and after the ones we want must be skipped by their computed
        /// size, or every byte after them is misread as geometry.
        /// </summary>
        [Test]
        public void UnknownExtraElementsAreSkipped()
        {
            foreach (bool binary in new[] { false, true })
            {
                var mesh = Read(new PlyFileBuilder
                {
                    Binary = binary,
                    ExtraElementBeforeVertex = true,
                    ExtraElementAfterFace = true,
                }.Build());
                Assert.That(mesh.Vertices, Is.EqualTo(CubeVertices), "binary = " + binary);
                Assert.That(mesh.Triangles, Is.EqualTo(ExpectedTriangles()));
            }
        }

        [Test]
        public void CommentsAndObjInfoArePreservedAndDoNotBreakParsing()
        {
            var mesh = Read(new PlyFileBuilder
            {
                Binary = true,
                Comments = { "Collider mesh from tools/splat_to_mesh.py", "Method: poisson" },
                ObjInfo = "made up by a test",
            }.Build());

            Assert.That(mesh.Comments, Has.Count.EqualTo(2));
            Assert.That(mesh.Comments[0], Does.Contain("splat_to_mesh"));
            Assert.That(mesh.Vertices, Has.Length.EqualTo(8));
        }

        // ------------------------------------------------------------------ refusals

        /// <summary>
        /// Big endian is refused rather than read wrong: every coordinate would come out as a
        /// meaningless number and the mesh would look like noise, which is a slow bug to find.
        /// </summary>
        [Test]
        public void BigEndianHeaderIsRefusedWithTheFix()
        {
            var e = Assert.Throws<PlyFormatException>(() =>
                Read(new PlyFileBuilder { Binary = true, BigEndianHeader = true }.Build()));
            Assert.That(e.Message, Does.Contain("big_endian"));
            Assert.That(e.Message, Does.Contain("little"));
        }

        /// <summary>A point cloud is not a collider: PhysX needs triangles, so this is refused.</summary>
        [Test]
        public void FileWithNoFacesIsRefused()
        {
            var noElement = Assert.Throws<PlyFormatException>(() =>
                Read(new PlyFileBuilder { Binary = true, NoFaceElement = true }.Build()));
            Assert.That(noElement.Message, Does.Contain("face"));

            var zero = Assert.Throws<PlyFormatException>(() =>
                Read(new PlyFileBuilder { Binary = true, ZeroFaces = true }.Build()));
            Assert.That(zero.Message, Does.Contain("point cloud"));
        }

        [Test]
        public void MissingCoordinatePropertyIsRefused()
        {
            var e = Assert.Throws<PlyFormatException>(() =>
                Read(new PlyFileBuilder { Binary = true, DropZProperty = true }.Build()));
            Assert.That(e.Message, Does.Contain("z"));
        }

        [Test]
        public void NotAPlyFileIsRefused()
        {
            Assert.Throws<PlyFormatException>(() => Read(Encoding.ASCII.GetBytes("this is not a ply\n")));
            Assert.Throws<PlyFormatException>(() => Read(new byte[0]));
        }

        [Test]
        public void TruncatedBinaryBodyIsRefusedRatherThanSilentlyShort()
        {
            var full = new PlyFileBuilder { Binary = true }.Build();
            var cut = new byte[full.Length - 40];
            Array.Copy(full, cut, cut.Length);

            var e = Assert.Throws<PlyFormatException>(() => Read(cut));
            Assert.That(e.Message, Does.Contain("ended"));
        }

        [Test]
        public void IndexOutOfRangeIsRefused()
        {
            var e = Assert.Throws<PlyFormatException>(() =>
                Read(new PlyFileBuilder { Binary = true, CorruptOneIndex = true }.Build()));
            Assert.That(e.Message, Does.Contain("index"));
        }

        [Test]
        public void NullStreamIsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => PlyMeshReader.Read(null));
        }

        // ------------------------------------------------------------------ the frame gate

        /// <summary>
        /// docs/FRAMES.md: the client asserts the frame instead of guessing. A mesh that was not
        /// converted would be mirrored, and a mirrored collider is worse than no collider because
        /// physics still runs. Both comments must be present, and case must not matter, because
        /// tools/splat_to_mesh.py writes "Vertical Axis: y" and "Handedness: left" capitalised.
        /// </summary>
        [Test]
        public void DeclaresUnityFrameOnlyWhenBothCommentsArePresent()
        {
            Assert.That(Read(new PlyFileBuilder
            {
                Comments = { "Vertical Axis: y", "Handedness: left" }
            }.Build()).DeclaresUnityFrame, Is.True, "as tools/splat_to_mesh.py writes them");

            Assert.That(Read(new PlyFileBuilder
            {
                Comments = { "handedness: left", "vertical axis: y" }
            }.Build()).DeclaresUnityFrame, Is.True, "lower case");

            Assert.That(Read(new PlyFileBuilder { Comments = { "Handedness: left" } }.Build())
                .DeclaresUnityFrame, Is.False, "handedness alone is not enough");

            Assert.That(Read(new PlyFileBuilder { Comments = { "Vertical Axis: y" } }.Build())
                .DeclaresUnityFrame, Is.False, "up axis alone is not enough");

            Assert.That(Read(new PlyFileBuilder
            {
                Comments = { "Handedness: right", "Vertical Axis: y" }
            }.Build()).DeclaresUnityFrame, Is.False, "right handed must not pass");

            Assert.That(Read(new PlyFileBuilder
            {
                Comments = { "Handedness: left", "Vertical Axis: z" }
            }.Build()).DeclaresUnityFrame, Is.False, "z up must not pass");

            Assert.That(Read(new PlyFileBuilder().Build()).DeclaresUnityFrame, Is.False, "no comments at all");
        }

        // ------------------------------------------------------------------ the real file

        /// <summary>
        /// The file the pipeline actually produces: Open3D's write_triangle_mesh(write_ascii=False,
        /// write_vertex_normals=True) with the five comment lines tools/splat_to_mesh.py injects
        /// after the format line. Scene 1 came out at 25,127 vertices / 50,000 triangles; this uses
        /// the same header with a small mesh so the test stays fast.
        /// </summary>
        [Test]
        public void ReadsTheHeaderToolsSplatToMeshActuallyWrites()
        {
            var header = new StringBuilder();
            header.Append("ply\n");
            header.Append("format binary_little_endian 1.0\n");
            header.Append("comment Collider mesh from tools/splat_to_mesh.py\n");
            header.Append("comment Method: poisson\n");
            header.Append("comment Vertical Axis: y\n");
            header.Append("comment Handedness: left\n");
            header.Append("comment Units: scene units, NOT metres. Apply unitScale from metadata.json\n");
            header.Append("element vertex 4\n");
            header.Append("property float x\nproperty float y\nproperty float z\n");
            header.Append("property float nx\nproperty float ny\nproperty float nz\n");
            header.Append("element face 2\n");
            header.Append("property list uchar int vertex_indices\n");
            header.Append("end_header\n");

            using (var ms = new MemoryStream())
            {
                var bytes = Encoding.ASCII.GetBytes(header.ToString());
                ms.Write(bytes, 0, bytes.Length);
                using (var bw = new BinaryWriter(ms, Encoding.ASCII, true))
                {
                    // A 2 m x 3 m quad on the floor, in SCENE UNITS, split into two triangles.
                    var quad = new[]
                    {
                        new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f),
                        new Vector3(2f, 0f, 3f), new Vector3(0f, 0f, 3f),
                    };
                    foreach (var v in quad)
                    {
                        bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z);
                        bw.Write(0f); bw.Write(1f); bw.Write(0f);
                    }
                    foreach (var tri in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } })
                    {
                        bw.Write((byte)3);
                        foreach (var i in tri) bw.Write(i);
                    }
                }
                ms.Position = 0;

                var mesh = PlyMeshReader.Read(ms);

                Assert.That(mesh.Format, Is.EqualTo("binary_little_endian"));
                Assert.That(mesh.Comments, Has.Count.EqualTo(5));
                Assert.That(mesh.DeclaresUnityFrame, Is.True);
                Assert.That(mesh.Vertices, Has.Length.EqualTo(4));
                Assert.That(mesh.Vertices[2], Is.EqualTo(new Vector3(2f, 0f, 3f)));
                Assert.That(mesh.Normals[0], Is.EqualTo(new Vector3(0f, 1f, 0f)));
                Assert.That(mesh.Triangles, Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));
                Assert.That(mesh.TriangleCount, Is.EqualTo(2));
                Assert.That(mesh.ToString(), Does.Contain("4 vertices"));
            }
        }
    }

    /// <summary>
    /// Writes small PLY files the way the tools in this repo and Open3D do, so the reader is tested
    /// against bytes rather than against a mock. Every flag corresponds to one thing a real exporter
    /// is known to vary.
    /// </summary>
    internal sealed class PlyFileBuilder
    {
        public bool Binary = true;
        public bool Normals = true;
        public bool Quads;
        public bool RedBetweenCoordinates;
        public bool ShuffledPropertyOrder;
        public string CoordinateType = "float";
        public string IndexType = "int";
        public string CountType = "uchar";
        public string FaceListName = "vertex_indices";
        public bool BigEndianHeader;
        public bool NoFaceElement;
        public bool ZeroFaces;
        public bool DropZProperty;
        public bool CorruptOneIndex;
        public bool ExtraElementBeforeVertex;
        public bool ExtraElementAfterFace;
        public string ObjInfo;
        public List<string> Comments = new List<string>();

        static readonly Vector3[] Vertices =
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 1f, 1f), new Vector3(0f, 1f, 1f),
        };

        static readonly int[][] Quadrilaterals =
        {
            new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 },
            new[] { 1, 2, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 },
        };

        static Vector3 NormalOf(Vector3 v) => Vector3.Normalize(v - new Vector3(0.5f, 0.5f, 0.5f));

        List<string[]> VertexProperties()
        {
            var props = new List<string[]>();
            void Add(string type, string name) => props.Add(new[] { type, name });

            if (ShuffledPropertyOrder)
            {
                Add(CoordinateType, "z");
                if (Normals) Add("float", "nx");
                Add(CoordinateType, "y");
                Add("uchar", "red");
                Add(CoordinateType, "x");
                if (Normals) { Add("float", "nz"); Add("float", "ny"); }
                return props;
            }

            Add(CoordinateType, "x");
            Add(CoordinateType, "y");
            if (RedBetweenCoordinates) Add("uchar", "red");
            if (!DropZProperty) Add(CoordinateType, "z");
            if (Normals) { Add("float", "nx"); Add("float", "ny"); Add("float", "nz"); }
            return props;
        }

        int[][] Faces()
        {
            if (ZeroFaces) return new int[0][];
            if (Quads) return Quadrilaterals;
            var tris = new List<int[]>();
            foreach (var q in Quadrilaterals)
                for (int k = 1; k + 1 < q.Length; k++) tris.Add(new[] { q[0], q[k], q[k + 1] });
            return tris.ToArray();
        }

        public byte[] Build()
        {
            var props = VertexProperties();
            var faces = Faces();
            string format = Binary ? (BigEndianHeader ? "binary_big_endian" : "binary_little_endian") : "ascii";

            var header = new StringBuilder();
            header.Append("ply\n").Append("format ").Append(format).Append(" 1.0\n");
            foreach (var c in Comments) header.Append("comment ").Append(c).Append('\n');
            if (ObjInfo != null) header.Append("obj_info ").Append(ObjInfo).Append('\n');
            if (ExtraElementBeforeVertex)
                header.Append("element camera 1\nproperty float view_px\nproperty float view_py\nproperty uchar flag\n");
            header.Append("element vertex ").Append(Vertices.Length).Append('\n');
            foreach (var p in props) header.Append("property ").Append(p[0]).Append(' ').Append(p[1]).Append('\n');
            if (!NoFaceElement)
            {
                header.Append("element face ").Append(faces.Length).Append('\n');
                header.Append("property list ").Append(CountType).Append(' ').Append(IndexType)
                      .Append(' ').Append(FaceListName).Append('\n');
            }
            if (ExtraElementAfterFace)
                header.Append("element edge 2\nproperty int vertex1\nproperty int vertex2\nproperty uchar red\n");
            header.Append("end_header\n");

            var ms = new MemoryStream();
            var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            ms.Write(headerBytes, 0, headerBytes.Length);

            if (Binary) WriteBinaryBody(ms, props, faces);
            else WriteAsciiBody(ms, props, faces);
            return ms.ToArray();
        }

        double ValueOf(string name, Vector3 v, Vector3 n)
        {
            switch (name)
            {
                case "x": return v.X;
                case "y": return v.Y;
                case "z": return v.Z;
                case "nx": return n.X;
                case "ny": return n.Y;
                case "nz": return n.Z;
                default: return 200;                 // the unrelated property, e.g. red
            }
        }

        void WriteAsciiBody(Stream s, List<string[]> props, int[][] faces)
        {
            var sb = new StringBuilder();
            if (ExtraElementBeforeVertex) sb.Append("0.5 0.25 7\n");
            foreach (var v in Vertices)
            {
                var n = NormalOf(v);
                for (int i = 0; i < props.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    double value = ValueOf(props[i][1], v, n);
                    sb.Append(props[i][0] == "uchar"
                        ? ((int)value).ToString(CultureInfo.InvariantCulture)
                        : value.ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }
            for (int f = 0; f < faces.Length; f++)
            {
                sb.Append(faces[f].Length);
                for (int k = 0; k < faces[f].Length; k++)
                {
                    int index = faces[f][k];
                    if (CorruptOneIndex && f == 0 && k == 0) index = 99;
                    sb.Append(' ').Append(index);
                }
                sb.Append('\n');
            }
            if (ExtraElementAfterFace) sb.Append("0 1 3\n1 2 4\n");
            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            s.Write(bytes, 0, bytes.Length);
        }

        void WriteBinaryBody(Stream s, List<string[]> props, int[][] faces)
        {
            using (var bw = new BinaryWriter(s, Encoding.ASCII, true))
            {
                if (ExtraElementBeforeVertex) { bw.Write(0.5f); bw.Write(0.25f); bw.Write((byte)7); }
                foreach (var v in Vertices)
                {
                    var n = NormalOf(v);
                    foreach (var p in props) WriteScalar(bw, p[0], ValueOf(p[1], v, n));
                }
                for (int f = 0; f < faces.Length; f++)
                {
                    WriteScalar(bw, CountType, faces[f].Length);
                    for (int k = 0; k < faces[f].Length; k++)
                    {
                        int index = faces[f][k];
                        if (CorruptOneIndex && f == 0 && k == 0) index = 99;
                        WriteScalar(bw, IndexType, index);
                    }
                }
                if (ExtraElementAfterFace)
                {
                    bw.Write(0); bw.Write(1); bw.Write((byte)3);
                    bw.Write(1); bw.Write(2); bw.Write((byte)4);
                }
            }
        }

        static void WriteScalar(BinaryWriter bw, string type, double value)
        {
            switch (type)
            {
                case "float":
                case "float32": bw.Write((float)value); break;
                case "double":
                case "float64": bw.Write(value); break;
                case "uchar":
                case "uint8": bw.Write((byte)value); break;
                case "char":
                case "int8": bw.Write((sbyte)value); break;
                case "ushort":
                case "uint16": bw.Write((ushort)value); break;
                case "short":
                case "int16": bw.Write((short)value); break;
                case "uint":
                case "uint32": bw.Write((uint)value); break;
                case "int":
                case "int32": bw.Write((int)value); break;
                default: throw new NotSupportedException(type);
            }
        }
    }
}
