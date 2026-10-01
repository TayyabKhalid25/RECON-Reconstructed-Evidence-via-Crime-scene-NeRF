# Runtime PLY loading

This sample loads a binary little-endian Gaussian-splat PLY from a byte array at runtime.

1. Import the sample from the Unity Package Manager.
2. Add `GsplatRenderer` and `RuntimePlyBytesLoaderExample` to a GameObject.
3. Set `Ply Path` to an absolute path or a path relative to `Application.streamingAssetsPath`.
4. Enter Play mode.

On Android, WebGL, and URI-based sources, fetch the bytes with `UnityWebRequest` and pass them to the runtime loader instead of using `File.ReadAllBytes`.
