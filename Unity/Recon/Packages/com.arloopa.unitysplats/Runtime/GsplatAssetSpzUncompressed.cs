// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Gsplat.Formats;
using UnityEngine;

namespace Gsplat
{
    // Loads SPZ files into GsplatAssetUncompressed
    public class GsplatAssetSpzUncompressed : GsplatAssetUncompressed
    {
        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
            => throw new System.NotSupportedException("GsplatAssetSpzUncompressed loads SPZ files, not PLY.");

        public override void LoadFromPlyBytes(byte[] plyBytes, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
            => throw new System.NotSupportedException("GsplatAssetSpzUncompressed loads SPZ files, not PLY.");

        public SpzPhaseTimings LoadFromSpz(string spzPath,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUB,
            ProgressCallback progressCallback = null)
        {
            var swDecompress = Stopwatch.StartNew();
            var data = SpzLoader.Load(spzPath);
            swDecompress.Stop();
            var h = data.Header;

            if (h.ShDegree > 4)
                throw new System.NotSupportedException($"SPZ SH degree {h.ShDegree} is not supported (max 4)");

            SplatCount = h.NumPoints;
            SHBands = h.ShDegree;
            Antialiased = (h.Flags & 1) != 0;
            bool float16Pos = h.Version == 1;
            bool smallestThree = h.Version >= 3;
            int shDim = SpzLoader.ShDim(h.ShDegree);
            var transform = new PlayCanvasSpzReader.CoordinateTransform(data, sourceCoordinates);

            Allocate();

            int splatCount = (int)SplatCount;
            var shBand = new Vector3[9];

            var swPack = Stopwatch.StartNew();
            for (int i = 0; i < splatCount; i++)
            {
                var rawPos = float16Pos
                    ? SpzLoader.DecodePositionFloat16(data.Positions, i)
                    : SpzLoader.DecodePosition(data.Positions, i, h.FractionalBits);
                Positions[i] = transform.Position(rawPos);

                if (i == 0) Bounds = new Bounds(Positions[i], Vector3.zero);
                else Bounds.Encapsulate(Positions[i]);

                // Color: raw SH DC coefficients + post-sigmoid alpha.
                // The uncompressed shader applies (* SH_C0 + 0.5) at render time.
                var rgb = SpzLoader.DecodeColor(data.Colors, i);
                Colors[i] = new Vector4(rgb.x, rgb.y, rgb.z,
                    SpzLoader.DecodeAlphaLinear(data.Alphas, i));

                // Scale: uncompressed expects linear scale (exp of log-scale).
                var logScale = SpzLoader.DecodeScaleLog(data.Scales, i);
                Scales[i] = new Vector3(
                    Mathf.Exp(logScale.x),
                    Mathf.Exp(logScale.y),
                    Mathf.Exp(logScale.z));

                // Quaternion stored as Vector4(w_real, x_imag, y_imag, z_imag).
                var rawRot = SpzLoader.DecodeRotation(data.Rotations, i, smallestThree);
                var rotation = transform.Rotation(rawRot);
                Rotations[i] = new Vector4(
                    rotation.w, rotation.x, rotation.y, rotation.z).normalized;

                // SH: SPZ stores per-point as [R0,G0,B0, R1,G1,B1, ...] (interleaved by coeff).
                // Uncompressed SHs[i*shDim+j] = Vector3(Rj, Gj, Bj).
                for (int band = 1, j = 0; band <= SHBands; band++)
                {
                    int bandSize = band * 2 + 1;
                    for (int k = 0; k < bandSize; k++)
                    {
                        int off = i * shDim * 3 + (j + k) * 3;
                        shBand[k] = new Vector3(
                            SpzLoader.UnquantizeSH(data.SH, off + 0),
                            SpzLoader.UnquantizeSH(data.SH, off + 1),
                            SpzLoader.UnquantizeSH(data.SH, off + 2));
                    }
                    transform.SHBand(shBand, 0, band);
                    for (int k = 0; k < bandSize; k++)
                        SHs[i * shDim + j + k] = shBand[k];
                    j += bandSize;
                }

                if ((i & 0xFFFF) == 0)
                    progressCallback?.Invoke("Reading splats", i / (float)splatCount);
            }
            swPack.Stop();

            return new SpzPhaseTimings
            {
                DecompressMs = swDecompress.ElapsedMilliseconds,
                PackMs = swPack.ElapsedMilliseconds,
            };
        }
    }
}
