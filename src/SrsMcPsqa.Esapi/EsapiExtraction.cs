using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Prescription;
using SrsMcPsqa.Core.Services;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SrsMcPsqa.Esapi
{
    /// <summary>Converts ESAPI objects to Core DTOs. ESAPI thread only.</summary>
    internal static class EsapiExtraction
    {
        public static Vec3 ToVec3(this VVector v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// Dose value in Gy. Relative (%) values are converted with the plan's total dose (100% = TotalDose).
        /// VERIFY: DoseValue.UnitAsString values ("Gy", "cGy", "%").
        /// </summary>
        public static double ToGy(DoseValue dv, double? totalGy)
        {
            var unit = (dv.UnitAsString ?? "").Trim();
            if (unit.Equals("Gy", StringComparison.OrdinalIgnoreCase)) return dv.Dose;
            if (unit.Equals("cGy", StringComparison.OrdinalIgnoreCase)) return dv.Dose / 100.0;
            if (unit == "%")
            {
                if (totalGy is double t && t > 0) return dv.Dose / 100.0 * t;
                throw new InvalidOperationException("Dose is relative (%) and the plan has no total dose to convert it.");
            }
            throw new InvalidOperationException($"Unknown dose unit '{dv.UnitAsString}'.");
        }

        public static double? TotalDoseGy(PlanSetup plan)
        {
            try
            {
                var t = plan.TotalDose;
                if (double.IsNaN(t.Dose) || t.Dose <= 0) return null;
                return ToGy(t, null);
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ validation
        /// <summary>Checks from section 4.3: calculated dose, single isocentre, HFS, photon beams.</summary>
        public static List<ValidationMessage> Validate(ExternalPlanSetup plan)
        {
            var v = new List<ValidationMessage>();
            void Add(ValidationSeverity s, string t) => v.Add(new ValidationMessage(s, t));

            if (plan.Dose == null || !plan.IsDoseValid) Add(ValidationSeverity.Error, "The plan has no valid calculated dose.");
            else Add(ValidationSeverity.Info, "Dose calculated.");

            var tx = plan.Beams.Where(b => !b.IsSetupField).ToList();
            if (tx.Count == 0) Add(ValidationSeverity.Error, "The plan has no treatment beams.");
            else
            {
                var iso = tx[0].IsocenterPosition.ToVec3();
                bool single = tx.All(b => b.IsocenterPosition.ToVec3().ApproxEquals(iso, 0.01));
                if (single) Add(ValidationSeverity.Info, $"Single isocentre {iso} for {tx.Count} treatment beams.");
                else Add(ValidationSeverity.Error, "Treatment beams do not share one isocentre (only single-isocentre plans are supported).");

                var nonPhoton = tx.Where(b => !(b.EnergyModeDisplayName ?? "").ToUpperInvariant().Contains("X")).Select(b => b.Id).ToList();
                if (nonPhoton.Count > 0) Add(ValidationSeverity.Error, $"Only photon beams are supported (not: {string.Join(", ", nonPhoton)}).");
                var kinds = string.Join(", ", tx.GroupBy(b => b.MLCPlanType).Select(g => $"{g.Count()} × {g.Key}"));
                Add(ValidationSeverity.Info, $"Beams: {kinds}; energy {string.Join("/", tx.Select(b => b.EnergyModeDisplayName).Distinct())}.");
            }

            if (plan.TreatmentOrientation != PatientOrientation.HeadFirstSupine)
                Add(ValidationSeverity.Error, $"Treatment orientation is {plan.TreatmentOrientation}; this version supports head first supine only.");
            else Add(ValidationSeverity.Info, "Treatment orientation HFS.");

            if (plan.StructureSet?.Image == null) Add(ValidationSeverity.Error, "The plan has no structure set / image.");
            return v;
        }

        // ------------------------------------------------------------------ PTVs
        /// <summary>Non-empty structures with "PTV" in the ID: mask (0.5 mm in-plane, CT slice spacing), mesh, Rx guess.</summary>
        public static List<PtvInfo> ExtractPtvs(StructureSet ss, double? planTotalGy, IProgress<string>? progress, CancellationToken ct, List<ValidationMessage> notes)
        {
            var img = ss.Image;
            var list = new List<PtvInfo>();
            foreach (var s in ss.Structures.Where(s => PtvFilter.IsPtv(s.Id)).OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (s.IsEmpty) { notes.Add(new ValidationMessage(ValidationSeverity.Info, $"{s.Id} is empty; skipped.")); continue; }
                progress?.Report($"Reading contours of {s.Id}...");
                var sw = Stopwatch.StartNew();
                var slices = new List<ContourSlice>();
                for (int k = 0; k < img.ZSize; k++)
                {
                    var contours = s.GetContoursOnImagePlane(k);
                    if (contours == null || contours.Length == 0) continue;
                    var polys = contours.Where(c => c.Length >= 3)
                        .Select(c => (IReadOnlyList<Point2>)c.Select(p => new Point2(p.x, p.y)).ToList()).ToList();
                    if (polys.Count == 0) continue;
                    slices.Add(new ContourSlice(contours.First(c => c.Length > 0)[0].z, polys));
                }
                if (slices.Count == 0) { notes.Add(new ValidationMessage(ValidationSeverity.Warning, $"{s.Id} has no contours on image planes; skipped.")); continue; }
                var mask = ContourRasterizer.Rasterize(slices, img.ZRes, 0.5, 2.0);
                var (rx, src) = PrescriptionResolver.Resolve(s.Id, null, planTotalGy);
                list.Add(new PtvInfo
                {
                    Id = s.Id, VolumeCc = s.Volume, RxGy = rx, RxSource = src, Mask = mask, Mesh = ToMesh(s),
                });
                Trace($"{s.Id}: {slices.Count} slices rasterised in {sw.ElapsedMilliseconds} ms", progress);
            }
            return list;
        }

        private static TriangleMesh? ToMesh(Structure s)
        {
            try
            {
                var m = s.MeshGeometry;
                if (m == null || m.Positions.Count == 0) return null;
                return new TriangleMesh(m.Positions.Select(p => new Vec3(p.X, p.Y, p.Z)).ToList(), m.TriangleIndices.ToList());
            }
            catch
            {
                return null; // mesh is display-only
            }
        }

        // ------------------------------------------------------------------ dose
        /// <summary>
        /// Absolute dose (Gy) cropped to <paramref name="region"/>. Only the z-planes in the region are read
        /// (GetVoxels reads whole planes, ~0.2 s each on Citrix for a 468×241 grid).
        /// Voxel→Gy is linear; it is calibrated from VoxelToDoseValue at three points and checked.
        /// </summary>
        public static DoseGrid ExtractDose(Dose dose, Box3 region, double? totalGy, IProgress<string>? progress, CancellationToken ct, List<ValidationMessage> notes)
        {
            CheckAxis(dose.XDirection, 0); CheckAxis(dose.YDirection, 1); CheckAxis(dose.ZDirection, 2);

            double g0 = ToGy(dose.VoxelToDoseValue(0), totalGy);
            double g1 = ToGy(dose.VoxelToDoseValue(1_000_000), totalGy);
            double scale = (g1 - g0) / 1_000_000.0, offset = g0;
            double check = ToGy(dose.VoxelToDoseValue(123_457), totalGy);
            if (Math.Abs(scale * 123_457 + offset - check) > 1e-6 * Math.Max(1, Math.Abs(check)))
                throw new InvalidOperationException("Voxel-to-dose conversion is not linear; cannot extract dose.");
            if ((dose.VoxelToDoseValue(0).UnitAsString ?? "").Trim() == "%")
                notes.Add(new ValidationMessage(ValidationSeverity.Warning, "Plan dose is shown as relative (%); converted to Gy with the plan total dose."));

            var o = dose.Origin.ToVec3();
            double xr = dose.XRes, yr = dose.YRes, zr = dose.ZRes;
            int Lo(double v, double org, double res) => (int)Math.Floor((v - org) / res);
            int Hi(double v, double org, double res) => (int)Math.Ceiling((v - org) / res);
            int i0 = Math.Max(0, Lo(region.Min.X, o.X, xr)), i1 = Math.Min(dose.XSize - 1, Hi(region.Max.X, o.X, xr));
            int j0 = Math.Max(0, Lo(region.Min.Y, o.Y, yr)), j1 = Math.Min(dose.YSize - 1, Hi(region.Max.Y, o.Y, yr));
            int k0 = Math.Max(0, Lo(region.Min.Z, o.Z, zr)), k1 = Math.Min(dose.ZSize - 1, Hi(region.Max.Z, o.Z, zr));
            if (i1 < i0 || j1 < j0 || k1 < k0) throw new InvalidOperationException("The PTVs are outside the dose grid.");

            int nx = i1 - i0 + 1, ny = j1 - j0 + 1, nz = k1 - k0 + 1;
            var values = new float[nx * ny * nz];
            var buf = new int[dose.XSize, dose.YSize];
            var sw = Stopwatch.StartNew();
            for (int k = k0; k <= k1; k++)
            {
                ct.ThrowIfCancellationRequested();
                if ((k - k0) % 10 == 0) progress?.Report($"Reading dose plane {k - k0 + 1} of {nz}...");
                dose.GetVoxels(k, buf);
                int baseK = nx * ny * (k - k0);
                for (int j = j0; j <= j1; j++)
                {
                    int row = baseK + nx * (j - j0);
                    for (int i = i0; i <= i1; i++) values[row + (i - i0)] = (float)(buf[i, j] * scale + offset);
                }
            }
            Trace($"Dose: {nz} planes of {dose.XSize}×{dose.YSize} read in {sw.Elapsed.TotalSeconds:F1} s; crop {nx}×{ny}×{nz} at {xr:0.##}/{yr:0.##}/{zr:0.##} mm", progress);
            var geometry = new GridGeometry(new Vec3(o.X + i0 * xr, o.Y + j0 * yr, o.Z + k0 * zr), new Vec3(xr, yr, zr), nx, ny, nz);
            return new DoseGrid(geometry, values);
        }

        private static void CheckAxis(VVector d, int axis)
        {
            var v = d.ToVec3();
            var expected = Vec3.Zero.With(axis, 1);
            if (!v.ApproxEquals(expected, 1e-3))
                throw new InvalidOperationException($"Dose grid axis {axis} direction is {v}; only axis-aligned (HFS) grids are supported.");
        }

        private static void Trace(string m, IProgress<string>? progress) => progress?.Report(m);
    }
}
