# Implementation Details

## Import and decoded data

`GsplatImporter` handles supported Gaussian-splat files through Unity's `ScriptedImporter` pipeline. It is the default importer for PLY, SOG, and SPZ. GLB is registered as a per-asset importer override because general-purpose glTF packages often handle the same extension; users choose UnitySplats for a Gaussian-splat GLB from the Inspector importer dropdown or `Assets > UnitySplats > Choose GLB Importer...`. Runtime loading uses the same format readers through `GsplatRuntimeLoader`, so editor imports and runtime byte, stream, and file-path loading share decoding behavior.

PLY, SOG, SPZ, and Gaussian-splat GLB readers produce a common `GsplatDecodedData` representation containing positions, linear scales, rotations, opacity/color, spherical harmonics, antialiasing metadata, and local bounds. Source coordinate systems are converted to Unity RUF during decoding. The decoded data is then stored in one of two asset representations:

- **Uncompressed**: `GsplatAssetUncompressed` stores positions, colors, scales, rotations, and optional spherical harmonics in separate managed arrays.
- **Spark**: `GsplatAssetSpark` stores each base splat in a packed 16-byte `uint4`, with separate packed arrays for spherical-harmonic bands. Positions use float16, scale is logarithmically encoded, color/opacity use RGBA8, and rotation uses octahedral axis-angle encoding.

SPZ v1-v4 decompression is implemented by `SpzLoader`; v4 uses the bundled Zstandard decoder. SOG images are decoded from WebP before their attributes are converted into the common representation.

## Shared resources and lifetime

`GsplatSettings` owns the package-wide materials, compute shaders, render-order limits, and procedurally generated instance mesh. The instance mesh contains batches of quads; the vertex z value encodes the intra-instance splat index used by the vertex shader.

`GsplatResourceManager` reference-counts uploaded resources by asset, allowing multiple renderers to share the same splat data. `GsplatRendererImpl` owns only renderer-specific state such as its order resource, active selection, cutout buffers, and current bounds.

Uploads may be synchronous or asynchronous. Asynchronous work is owned by the resource and is cancellation-safe, preventing a released resource from being written after disposal. `RenderBeforeUploadComplete` allows the resident prefix to render while upload is still in progress.

Native buffer-capable paths upload splat attributes to `GraphicsBuffer`s. The portable OpenGL/WebGL path instead uploads attributes and draw order to sampled textures. Spark integer data uses RGBA8 byte packing so it does not depend on sampled integer-texture support on mobile drivers.

## Frame orchestration and sorting

`GsplatSorter` registers active `IGsplat` renderers and selects the sorting path from runtime graphics capabilities.

### GPU radix path

Compatible Direct3D 12, Vulkan, and Metal devices use the compute path:

1. An asset-specific depth kernel calculates view-space depth keys.
2. `DeviceRadixSort.hlsl` performs upsweep, scan, and downsweep passes.
3. The same radix operations reorder the payload containing original splat IDs.
4. The resulting back-to-front IDs become the renderer's order buffer.

Wave/subgroup operations and compatible compute workgroup limits are required. The renderer automatically falls back when those requirements are unavailable.

Cross-renderer sorting is enabled by default. When every active renderer satisfies the merge constraints, `GsplatGlobalRenderer` packs renderer and source IDs into one global order, sorts them together, and draws the merged result. The setting is available in `Project Settings > Gsplat` and remains project-wide so the renderer set cannot enter a partially merged state. Incompatible compression, layer, queue, or packed-index conditions return the scene to per-renderer drawing.

### CPU fallback

Direct3D 11, OpenGL Core, OpenGL ES, and native devices without the required GPU features use an asynchronous stable, full-precision radix sort. Native CPU-fallback platforms upload the order to a graphics buffer while the portable texture path uploads it as RGBA8 sampled data.

WebGL 2 cannot use the worker-based native implementation, compute shaders, or structured buffers, so it performs the same radix sort on Unity's main thread and renders entirely from sampled textures.

Global merge and compute cutouts are disabled during CPU fallback. Each renderer retains one current order; in the Editor, the active Scene or Game viewport selects the camera used for that order.

## Cutouts and active source ranges

On the compute path, `GsplatRendererImpl.DispatchInitOrder` rebuilds sequential source IDs, applies active cutouts, writes the surviving count, and can update bounds. The resulting order is then depth-sorted.

`GsplatRenderer.SetActiveRanges` accepts sorted or unsorted half-open source ranges. It validates, merges, and expands them into exact source IDs. Explicit active ranges take precedence over compute cutouts until `ClearActiveRanges` restores the dense asset path. During asynchronous upload, only selected IDs that are already resident are exposed for sorting and drawing.

## Render pass

The Built-in Render Pipeline uses the camera callback integration in `GsplatSorter`. URP uses `GsplatURPFeature`, and HDRP uses `GsplatHDRPPass`. Unity 6000.0-6000.3 URP supports both the RenderGraph callback and the legacy compatibility-mode `Execute` callback; Unity 6000.4 and newer compile only the RenderGraph callback because URP removed the legacy override. These hooks coordinate sorting and submit draws for the current camera.

`GsplatRendererImpl.Render` issues an instanced procedural-mesh draw. Its material is selected by compression mode, spherical-harmonic degree, and render order. Per-renderer buffers, textures, transforms, brightness, scale factor, and color-conversion state are supplied through a `MaterialPropertyBlock`.

The vertex shader:

1. Combines the instance ID with the mesh's encoded intra-instance index.
2. Resolves that entry through the sorted order buffer or order texture.
3. Fetches position, rotation, scale, color, and spherical-harmonic data.
4. Evaluates view-dependent spherical-harmonic color when enabled.
5. Projects the 3D Gaussian covariance into a screen-space ellipse.
6. Culls off-screen or insignificant splats and outputs the quad position, ellipse coordinates, and color.

The fragment shader evaluates the Gaussian exponential falloff, discards pixels outside the ellipse or below the alpha threshold, and outputs premultiplied alpha. `GammaToLinear` converts splat RGB before output and is enabled by default. A hidden serialized migration marker upgrades renderers created by earlier package versions once, while still allowing the option to be disabled afterward.

## Bounds and editor integration

Every imported `GsplatAsset` stores local-space bounds. `GsplatScenePicker` renders those transformed bounds into Unity's editor-only GPU picking buffer and retains a ray/bounds fallback for classic `PickGameObject` calls. This allows an otherwise geometry-less splat GameObject to be selected without a collider while preserving Unity's filtering, depth testing, cycling, and multi-selection behavior.

`GsplatRenderer.FitBoxColliderToAssetBounds` copies the stored center and size to a `BoxCollider` on the same GameObject. The custom renderer inspector detects newly added default `BoxCollider` components, fits them automatically, and provides a manual **Fit Box Collider to Splat Bounds** button. The editor supports mixed-value multi-object editing and applies collider fitting to every eligible selected renderer. `OnValidate` keeps colliders aligned when assigned assets change or are reimported. GameObject rotation and scale continue to be handled by Unity's transform and collider systems.

Project-to-Hierarchy and Project-to-Scene drag-and-drop create a configured `GsplatRenderer`, assign the imported asset, and apply the package's 180-degree local Z rotation convention. Unity 6000.3 and newer use the V2 Hierarchy handler and resolve its 64-bit `EntityId`; Unity 6000.0-6000.2 retain the legacy `InstanceID` handler.
