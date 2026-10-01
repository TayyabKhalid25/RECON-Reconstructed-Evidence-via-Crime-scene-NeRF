// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gsplat.Formats;
using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public class PlayCanvasGlbReaderTests
    {
        [Test]
        public void ReadsCurrentLinearScaleGlb()
        {
            GsplatDecodedData decoded = PlayCanvasGlbReader.Read(
                BuildOneSplatGlb("current-exporter 1.0", new Vector3(0.25f, 0.5f, 2f)),
                SourceCoordinates.RUF);

            Assert.That(decoded.Scales[0].x, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(decoded.Scales[0].y, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(decoded.Scales[0].z, Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void ConvertsSplatTransform260LogScales()
        {
            var logScale = new Vector3(-3.625f, -4.125f, -4.875f);
            GsplatDecodedData decoded = PlayCanvasGlbReader.Read(
                BuildOneSplatGlb("splat-transform 2.6.0", logScale),
                SourceCoordinates.RUF);

            Assert.That(decoded.Scales[0].x, Is.EqualTo(Mathf.Exp(logScale.x)).Within(1e-6f));
            Assert.That(decoded.Scales[0].y, Is.EqualTo(Mathf.Exp(logScale.y)).Within(1e-6f));
            Assert.That(decoded.Scales[0].z, Is.EqualTo(Mathf.Exp(logScale.z)).Within(1e-6f));
        }

        [Test]
        public void RejectsNegativeScaleFromNonLegacyGenerator()
        {
            Assert.Throws<InvalidDataException>(() => PlayCanvasGlbReader.Read(
                BuildOneSplatGlb("current-exporter 1.0", new Vector3(-1f, 0.5f, 0.25f)),
                SourceCoordinates.RUF));
        }

        static byte[] BuildOneSplatGlb(string generator, Vector3 scale)
        {
            var binary = new List<byte>(56);
            Add(binary, 1f, 2f, 3f);             // POSITION
            Add(binary, 0f, 0f, 0f, 1f);         // ROTATION xyzw
            Add(binary, scale.x, scale.y, scale.z);
            Add(binary, 0.75f);                   // OPACITY
            Add(binary, 0.1f, 0.2f, 0.3f);       // degree-zero SH

            string escapedGenerator = generator.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string json = "{" +
                          "\"asset\":{\"version\":\"2.0\",\"generator\":\"" + escapedGenerator + "\"}," +
                          "\"extensionsUsed\":[\"KHR_gaussian_splatting\"]," +
                          "\"buffers\":[{\"byteLength\":56}]," +
                          "\"bufferViews\":[" +
                          "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":12}," +
                          "{\"buffer\":0,\"byteOffset\":12,\"byteLength\":16}," +
                          "{\"buffer\":0,\"byteOffset\":28,\"byteLength\":12}," +
                          "{\"buffer\":0,\"byteOffset\":40,\"byteLength\":4}," +
                          "{\"buffer\":0,\"byteOffset\":44,\"byteLength\":12}]," +
                          "\"accessors\":[" +
                          "{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"VEC3\"}," +
                          "{\"bufferView\":1,\"componentType\":5126,\"count\":1,\"type\":\"VEC4\"}," +
                          "{\"bufferView\":2,\"componentType\":5126,\"count\":1,\"type\":\"VEC3\"}," +
                          "{\"bufferView\":3,\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}," +
                          "{\"bufferView\":4,\"componentType\":5126,\"count\":1,\"type\":\"VEC3\"}]," +
                          "\"meshes\":[{\"primitives\":[{" +
                          "\"mode\":0," +
                          "\"attributes\":{" +
                          "\"POSITION\":0," +
                          "\"KHR_gaussian_splatting:ROTATION\":1," +
                          "\"KHR_gaussian_splatting:SCALE\":2," +
                          "\"KHR_gaussian_splatting:OPACITY\":3," +
                          "\"KHR_gaussian_splatting:SH_DEGREE_0_COEF_0\":4}," +
                          "\"extensions\":{\"KHR_gaussian_splatting\":{" +
                          "\"kernel\":\"ellipse\",\"colorSpace\":\"srgb_rec709_display\"}}}]}]}";

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int paddedJsonLength = (jsonBytes.Length + 3) & ~3;
            int paddedBinLength = (binary.Count + 3) & ~3;
            int totalLength = 12 + 8 + paddedJsonLength + 8 + paddedBinLength;

            using var stream = new MemoryStream(totalLength);
            using var writer = new BinaryWriter(stream);
            writer.Write(0x46546c67u);
            writer.Write(2u);
            writer.Write((uint)totalLength);
            writer.Write((uint)paddedJsonLength);
            writer.Write(0x4e4f534au);
            writer.Write(jsonBytes);
            for (int i = jsonBytes.Length; i < paddedJsonLength; ++i)
                writer.Write((byte)' ');
            writer.Write((uint)paddedBinLength);
            writer.Write(0x004e4942u);
            writer.Write(binary.ToArray());
            while (stream.Length < totalLength)
                writer.Write((byte)0);
            return stream.ToArray();
        }

        static void Add(List<byte> destination, params float[] values)
        {
            foreach (float value in values)
                destination.AddRange(BitConverter.GetBytes(value));
        }
    }
}
