# Third Party Notices

UnitySplats is an MIT-licensed derivative work that includes, adapts, or is informed by the open-source projects listed below. Copyright notices in individual source files remain in effect.

## gsplat-unity

UnitySplats is a modified and substantially extended fork of `gsplat-unity`.

- Project: https://github.com/wuyize25/gsplat-unity
- License: MIT
- Copyright: Copyright (c) 2025 Yize Wu

## PlayCanvas Engine

Parts of the Gaussian-splat renderer architecture, PLY, SOG, GLB, LOD behavior, and proxy-mesh screen-space relighting design are adapted from or based on PlayCanvas Engine Gaussian-splat code. The relighting reference is `scripts/esm/gsplat/gsplat-relighting.mjs`. The reference checkout used for this Unity implementation was PlayCanvas Engine `v2.21.0-beta.14`, commit `d5fe88878e338936fe763bbce1a58bc315e89cbe`.

- Project: https://github.com/playcanvas/engine
- License: MIT
- Copyright: Copyright (c) 2011-2026 PlayCanvas Ltd.

## UnityGaussianSplatting

Parts of the original Unity renderer lineage, including rendering-pipeline integration and cutout-related work, are derived from or informed by UnityGaussianSplatting.

- Project: https://github.com/aras-p/UnityGaussianSplatting
- License: MIT
- Copyright: Copyright (c) 2023 Aras Pranckevičius

## GPUSorting

The GPU radix-sort implementation includes code from GPUSorting. The relevant shader files retain the upstream notice and license text.

- Project: https://github.com/b0nes164/GPUSorting
- License: MIT
- Copyright: Copyright Thomas Smith 2024

## Spark

The packed Gaussian representation and related implementation concepts are derived from or informed by Spark.

- Project: https://github.com/sparkjsdev/spark
- License: MIT
- Copyright: Copyright (c) 2025 WORLD LABS TECHNOLOGIES, INC.

## Niantic SPZ

SPZ container and attribute decoding follows the Niantic SPZ specification and Unity SPZ implementation lineage.

- Project: https://github.com/nianticlabs/spz
- License: MIT
- Copyright: Copyright (c) 2024 Niantic Labs; portions Copyright (c) 2025 Niantic Spatial

## ZstdSharp

ZstdSharp is used to decompress SPZ v4 attribute streams.

- Project: https://github.com/oleg-st/ZstdSharp
- NuGet package: `ZstdSharp.Port`
- License: MIT
- Copyright: Copyright (c) 2021 Oleg Stepanischev

## Unity.WebP and libwebp

SOG image decoding depends on `com.netpyoung.webp` and its platform libwebp binaries. Unity.WebP is installed as a separate Unity package and carries its own complete notices.

- Unity wrapper: https://github.com/netpyoung/unity.webp
- libwebp: https://chromium.googlesource.com/webm/libwebp
- Licenses: MIT for the Unity wrapper; BSD-3-Clause for libwebp

## MIT License text

The following MIT terms apply to the MIT-licensed components above together with each component's copyright notice.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

