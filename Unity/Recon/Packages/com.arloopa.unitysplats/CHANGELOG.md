# Changelog

All notable changes to UnitySplats are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-08-18

### Fixed

- Fixed incorrect splat compositing on Direct3D 11 when a large camera-depth range caused nearby splats to collide in the former 16-bit CPU sort. The fallback now preserves full 32-bit float depth order.
- Fixed Direct3D 11 Scene views consuming an order sorted from the Main camera. The CPU fallback now follows the active Scene/Game viewport and discards in-flight results when the selected viewport changes.
- Matched the GPU sort's stable handling of equal view depths in the CPU fallback, preventing localized ordering artifacts at camera angles where SPARK half-float centers produce tied depth keys.

## [1.1.0] - 2026-07-22

### Added

- Added Scene-view selection of `GsplatRenderer` GameObjects through transformed splat bounds without requiring a collider.
- Added multi-object editing to the renderer inspector, including mixed values and batch `BoxCollider` fitting.
- Added cross-renderer sorting controls and compatibility guidance to `Project Settings > Gsplat`.
- Added hover tooltips for every editable `GsplatRenderer` inspector control.

### Changed

- Enabled cross-renderer per-splat sorting by default for compatible Spark renderers; existing project settings migrate once while preserving subsequent manual changes.

## [1.0.1] - 2026-07-22

### Fixed

- Fixed Unity 6.5 compilation by replacing deprecated 32-bit `GetInstanceID()` cache keys with direct Unity object references, compatible with Unity's 64-bit `EntityId` transition.

### Documentation

- Added an OpenUPM installation option that lets Unity resolve the declared Unity.WebP dependency automatically.

## [1.0.0] - 2026-07-22

### Added

- Added PlayCanvas standard/compressed PLY, SOG v1/v2, SPZ v1-v4, and Gaussian-splat GLB decoding.
- Added editor import and runtime byte/stream loading with automatic coordinate conversion.
- Added asynchronous CPU sorting for D3D11, OpenGL Core, and OpenGL ES 3.1 while retaining GPU radix sorting on supported D3D12, Vulkan, and Metal devices.
- Added a texture-backed OpenGL draw path for devices that report zero vertex-stage storage-buffer slots, preventing `Gsplat/Standard` GLSL link failures on affected GLES drivers.
- Added Unity 6 URP RenderGraph and compatibility-mode integration.
- Added 3DGS antialiasing opacity compensation and corrected degree-4 spherical-harmonic evaluation.
- Added SPZ coordinate-system extension parsing, including rotated Adobe coordinate frames.
- Added exact per-renderer active source ranges, including async-upload prefix safety and mapped CPU/GPU sorting. Explicit ranges bypass compute cutouts until cleared.
- Added Project-to-Hierarchy and Project-to-Scene drag-and-drop creation of configured `GsplatRenderer` GameObjects.
- Set Project-to-Hierarchy and Project-to-Scene drag-created splats to 180 degrees local Z rotation.
- Added an experimental WebGL 2 renderer using sampled splat/order textures and main-thread CPU sorting, with no compute or structured-buffer allocation.
- Added optional global depth sorting across multiple `GsplatRenderer` instances. Global sorting supports Spark compression and falls back to per-renderer sorting when its compatibility requirements are not met. ([upstream #28](https://github.com/wuyize25/gsplat-unity/pull/28))
- Added runtime PLY loading from byte arrays. ([upstream #34](https://github.com/wuyize25/gsplat-unity/pull/34))
- Added automatic `BoxCollider` fitting from imported splat bounds, with an inspector action for manual refitting.
- Added a per-file Gaussian-splat GLB importer override and `Assets > UnitySplats > Choose GLB Importer...` so UnitySplats can coexist with general-purpose GLB importers.
- Added an optional Package Manager sample demonstrating runtime PLY byte loading and asset cleanup.

### Changed

- Rebranded the package as UnitySplats and changed its Unity Package Manager identifier from `wu.yize.gsplat` to `com.arloopa.unitysplats`.
- Changed the OpenGL packed-data fallback to RGBA8 byte textures so Mali devices that reject sampled `R32_UInt` textures can render.
- Enabled the texture-backed draw path on every OpenGL ES device because some Honor/Mali drivers advertise vertex SSBO slots but still reject Unity's generated buffer bindings.
- Enabled `GammaToLinear` by default and added a one-time serialized migration that preserves later manual changes.

### Fixed

- Hardened mobile portability with 256-thread compute kernels, non-subnormal mesh instance IDs, and cross-API-safe structured-buffer layouts.
- Made asynchronous GPU uploads resource-owned and cancellation-safe, and fixed renderer/global-buffer release paths.
- Prevented global-merge buffer aliasing for four or more renderers and preserved layer, queue, and packed-index constraints through automatic fallback.
- Released partially created GPU resources when runtime capability checks fail.
- Added compatibility decoding for logarithmic GLB scales emitted by `splat-transform` 2.6.0 and older while preserving strict `KHR_gaussian_splatting` validation for current exporters.
- Fixed PLY header parsing to count only vertex element properties, including files exported by Apple's ml-sharp. ([upstream #33](https://github.com/wuyize25/gsplat-unity/pull/33))
- Fixed URP compilation on Unity 6000.4 and newer, where the legacy `ScriptableRenderPass.Execute` override was removed in favor of RenderGraph.
- Fixed Project-to-Hierarchy drag-and-drop on Unity 6000.4 and newer by using the 64-bit `EntityId` handler instead of the obsolete 32-bit `InstanceID` delegate.

[unreleased]: https://github.com/arloopa/UnitySplats/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/arloopa/UnitySplats/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/arloopa/UnitySplats/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/arloopa/UnitySplats/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/arloopa/UnitySplats/releases/tag/v1.0.0
