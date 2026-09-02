namespace Recon.Colliders
{
    /// <summary>
    /// Which collision geometry a shot was fired at. Handbook Section 09 wants both paths to exist
    /// because comparing them IS the research contribution: "fire identical shots into both collider
    /// sets, measure the impact point difference, and plot it against mesh decimation level"
    /// (Challenge 1). So the collider set is part of every logged shot, not a UI detail.
    ///
    /// Pure C#: the enum is shared by the Unity ShotController and by the shot log the study reads.
    /// </summary>
    public enum ColliderSet
    {
        /// <summary>Detected AR planes. The robust baseline, available with no reconstruction at all.</summary>
        ArPlanes = 0,

        /// <summary>The Poisson or voxel mesh from tools/splat_to_mesh.py. The research path.</summary>
        SplatMesh = 1,

        /// <summary>Both layers at once: what the app should feel like, and useless as a comparison.</summary>
        Both = 2,
    }
}
