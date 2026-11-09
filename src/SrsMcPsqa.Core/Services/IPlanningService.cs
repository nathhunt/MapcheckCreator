using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;

namespace SrsMcPsqa.Core.Services
{
    // Draft contract between the UI and Eclipse. Implemented by SrsMcPsqa.Esapi (real) and SrsMcPsqa.Fake (synthetic).
    // It will be revised after the Phase 0 spike results. Only Core DTOs cross this boundary, never ESAPI objects.

    public sealed class PlanSummary
    {
        public string CourseId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public bool IsQaPlan { get; set; }

        /// <summary>Parsed app tag from the plan Comment (QA plans created by this app only).</summary>
        public QaPlanTag? Tag { get; set; }
    }

    public sealed class CourseSummary
    {
        public string Id { get; set; } = "";
        public bool IsCompleted { get; set; }
        public IReadOnlyList<PlanSummary> Plans { get; set; } = Array.Empty<PlanSummary>();
    }

    public sealed class PatientSummary
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool CanModify { get; set; }
        public string? ModifyError { get; set; }
        public IReadOnlyList<CourseSummary> Courses { get; set; } = Array.Empty<CourseSummary>();
    }

    public enum ValidationSeverity { Info, Warning, Error }

    public sealed class ValidationMessage
    {
        public ValidationMessage(ValidationSeverity severity, string text) { Severity = severity; Text = text; }
        public ValidationSeverity Severity { get; }
        public string Text { get; }
    }

    public sealed class ClinicalPlanData
    {
        public string CourseId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public string PlanUid { get; set; } = "";
        public Vec3 Isocentre { get; set; }
        public double? PlanTotalDoseGy { get; set; }
        public IReadOnlyDictionary<string, double> PrescriptionTargetsGy { get; set; } = new Dictionary<string, double>();
        public IReadOnlyList<PtvInfo> Ptvs { get; set; } = Array.Empty<PtvInfo>();

        /// <summary>Clinical dose cropped to the PTVs + 45 mm (Gy).</summary>
        public DoseGrid? Dose { get; set; }

        public IReadOnlyList<ValidationMessage> Validation { get; set; } = Array.Empty<ValidationMessage>();
    }

    public sealed class PhantomImageInfo
    {
        public ArrayOrientation Orientation { get; set; }
        public string ImageId { get; set; } = "";
        public string StructureSetId { get; set; } = "";
        /// <summary>UID of the exact structure set (several copies of the phantom can share the same IDs).</summary>
        public string StructureSetUid { get; set; } = "";
        /// <summary>User origin = array centre (DICOM).</summary>
        public Vec3 UserOrigin { get; set; }
        public bool WasCopied { get; set; }
    }

    public sealed class QaPlanRequest
    {
        public string QaCourseId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public ArrayOrientation Orientation { get; set; }
        /// <summary>Phantom structure set to plan on (<see cref="PhantomImageInfo.StructureSetUid"/>).</summary>
        public string StructureSetUid { get; set; } = "";
        /// <summary>PTVs this plane is for (written to the plan tag).</summary>
        public IReadOnlyList<string> AssignedPtvIds { get; set; } = Array.Empty<string>();
        /// <summary>QA isocentre in phantom DICOM coordinates (<see cref="IsoGeometry.QaIsocentre"/>).</summary>
        public Vec3 Isocentre { get; set; }
        /// <summary>Region (phantom coordinates) to return dose for, e.g. array plane ± 15 mm.</summary>
        public Box3 DoseRegion { get; set; }
    }

    public sealed class QaPlanResult
    {
        public string CourseId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public ArrayOrientation Orientation { get; set; }
        public Vec3 Isocentre { get; set; }
        public bool CalculationSucceeded { get; set; }
        public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
        /// <summary>Calculated QA dose in phantom coordinates (Gy), cropped to the requested region.</summary>
        public DoseGrid? Dose { get; set; }
    }

    public sealed class TemplatePhantomSettings
    {
        public string PatientId { get; set; } = "";
        /// <summary>Study holding the 0° image; empty = search all studies of the template patient.</summary>
        public string Study0Id { get; set; } = "";
        /// <summary>Study holding the 270° image; empty = search all studies of the template patient.</summary>
        public string Study270Id { get; set; } = "";
        public string Image0Id { get; set; } = "";
        public string Image270Id { get; set; } = "";
        public bool CopyTemplateStructures { get; set; }

        public string ImageId(ArrayOrientation o) => o == ArrayOrientation.Deg0 ? Image0Id : Image270Id;
        public string StudyId(ArrayOrientation o) => o == ArrayOrientation.Deg0 ? Study0Id : Study270Id;
    }

    /// <summary>
    /// Everything the app needs from Eclipse. Implementations must run all ESAPI work on the ESAPI thread
    /// and convert results to Core DTOs. Write operations save the patient when they complete.
    /// </summary>
    public interface IPlanningService
    {
        /// <summary>True when this implementation can create/modify plans (false for read-only builds).</summary>
        bool SupportsWriting { get; }

        Task<PatientSummary> OpenPatientAsync(string patientId, CancellationToken ct = default);

        Task<ClinicalPlanData> LoadClinicalPlanAsync(string courseId, string planId, IProgress<string>? progress = null, CancellationToken ct = default);

        Task<IReadOnlyList<PhantomImageInfo>> EnsurePhantomImagesAsync(TemplatePhantomSettings settings, IReadOnlyCollection<ArrayOrientation> orientations,
            IProgress<string>? progress = null, CancellationToken ct = default, IProgress<QaProgressEvent>? steps = null);

        /// <summary>Opens the template patient read-only and checks the configured study/image IDs exist.</summary>
        Task<IReadOnlyList<ValidationMessage>> ValidateTemplateAsync(TemplatePhantomSettings settings, CancellationToken ct = default);

        Task<QaPlanResult> CreateQaPlanAsync(QaPlanRequest request, IProgress<string>? progress = null, CancellationToken ct = default,
            IProgress<QaProgressEvent>? steps = null);

        Task<QaPlanResult> MoveIsocentreAsync(string courseId, string planId, Vec3 newIsocentre, Box3 doseRegion,
            IProgress<string>? progress = null, CancellationToken ct = default, IProgress<QaProgressEvent>? steps = null);

        /// <summary>Lists plans in the course that carry the app's tag.</summary>
        Task<IReadOnlyList<PlanSummary>> ListQaPlansAsync(string courseId, CancellationToken ct = default);

        /// <summary>Deletes tagged QA plans only.</summary>
        Task DeleteQaPlansAsync(string courseId, IReadOnlyCollection<string> planIds, IProgress<string>? progress = null, CancellationToken ct = default);
    }
}
