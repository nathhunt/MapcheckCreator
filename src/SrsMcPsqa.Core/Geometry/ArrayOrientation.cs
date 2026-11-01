using System;

namespace SrsMcPsqa.Core.Geometry
{
    /// <summary>
    /// Orientation of the SRS MapCHECK array in the StereoPHAN. Rotation is about the patient's longitudinal axis.
    /// </summary>
    public enum ArrayOrientation
    {
        /// <summary>Array horizontal: coronal plane, normal along patient AP (Y). In-plane axes (X, Z).</summary>
        Deg0 = 0,

        /// <summary>Array vertical: sagittal plane, normal along patient LR (X). In-plane axes (Y, Z).</summary>
        Deg270 = 270,
    }

    /// <summary>
    /// Plane coordinates for an array orientation: (u, v) are the in-plane axes and n is the coordinate along the normal
    /// ("depth"). All are plain patient coordinates in mm (no sign flips), so mapping is a component permutation:
    /// <list type="bullet">
    /// <item>0°:   u = X, v = Z, n = Y</item>
    /// <item>270°: u = Y, v = Z, n = X</item>
    /// </list>
    /// The detector lattice is symmetric under 90° rotation, so the in-plane rotation between orientations is irrelevant.
    /// </summary>
    public static class PlaneAxes
    {
        /// <summary>Patient axis index (0 = X, 1 = Y, 2 = Z) of the array normal.</summary>
        public static int NormalAxis(this ArrayOrientation o) => o == ArrayOrientation.Deg0 ? 1 : 0;

        /// <summary>Patient axis index of the in-plane u axis.</summary>
        public static int UAxis(this ArrayOrientation o) => o == ArrayOrientation.Deg0 ? 0 : 1;

        /// <summary>Patient axis index of the in-plane v axis (always Z, the longitudinal axis).</summary>
        public const int VAxis = 2;

        public static double U(this ArrayOrientation o, Vec3 p) => o == ArrayOrientation.Deg0 ? p.X : p.Y;
        public static double V(this ArrayOrientation o, Vec3 p) => p.Z;
        public static double N(this ArrayOrientation o, Vec3 p) => o == ArrayOrientation.Deg0 ? p.Y : p.X;

        /// <summary>Patient point from plane coordinates.</summary>
        public static Vec3 FromPlane(this ArrayOrientation o, double u, double v, double n) =>
            o == ArrayOrientation.Deg0 ? new Vec3(u, n, v) : new Vec3(n, u, v);

        /// <summary>"0°" or "270°".</summary>
        public static string Label(this ArrayOrientation o) => o == ArrayOrientation.Deg0 ? "0°" : "270°";

        /// <summary>"0" or "270", used in plan IDs.</summary>
        public static string ShortLabel(this ArrayOrientation o) => ((int)o).ToString();

        public static ArrayOrientation[] All { get; } = { ArrayOrientation.Deg0, ArrayOrientation.Deg270 };
    }
}
