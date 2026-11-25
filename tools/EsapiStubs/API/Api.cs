// Compile-check stub. NOT the real ESAPI. Signatures copied from the v18 API probe; bodies throw.
using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using VMS.TPS.Common.Model.Types;

namespace VMS.TPS.Common.Model.API
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ESAPIScriptAttribute : Attribute
    {
        public bool IsWriteable { get; set; }
    }

    /// <summary>Exists in real ESAPI (Application.PatientSummaries); included so name clashes with Core show up here.</summary>
    public class PatientSummary { public string Id => throw new NotImplementedException(); }

    public class User { public string Id => throw new NotImplementedException(); }

    public class Application : IDisposable
    {
        public static Application CreateApplication() => throw new NotImplementedException();
        public User CurrentUser => throw new NotImplementedException();
        public IEnumerable<PatientSummary> PatientSummaries => throw new NotImplementedException();
        public Patient OpenPatientById(string id) => throw new NotImplementedException();
        public void ClosePatient() => throw new NotImplementedException();
        public void SaveModifications() => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
    }

    public class Patient
    {
        public string Id => throw new NotImplementedException();
        public string Name => throw new NotImplementedException();
        public IEnumerable<Course> Courses => throw new NotImplementedException();
        public IEnumerable<Study> Studies => throw new NotImplementedException();
        public IEnumerable<StructureSet> StructureSets => throw new NotImplementedException();
        public bool CanModifyData() => throw new NotImplementedException();
        public StructureSet CopyImageFromOtherPatient(string otherPatientId, string otherPatientStudyId, string otherPatient3DImageId) => throw new NotImplementedException();
        public void BeginModifications() => throw new NotImplementedException();
        public bool CanAddCourse() => throw new NotImplementedException();
        public Course AddCourse() => throw new NotImplementedException();
    }

    public class Study
    {
        public string Id => throw new NotImplementedException();
        public IEnumerable<Image> Images3D => throw new NotImplementedException();
    }

    public class Course
    {
        public string Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IEnumerable<PlanSetup> PlanSetups => throw new NotImplementedException();
        public IEnumerable<ExternalPlanSetup> ExternalPlanSetups => throw new NotImplementedException();
        public bool IsCompleted() => throw new NotImplementedException();
        public ExternalPlanSetup AddExternalPlanSetupAsVerificationPlan(StructureSet structureSet, ExternalPlanSetup verifiedPlan) => throw new NotImplementedException();
        public bool CanRemovePlanSetup(PlanSetup planSetup) => throw new NotImplementedException();
        public void RemovePlanSetup(PlanSetup planSetup) => throw new NotImplementedException();
    }

    public class PlanSetup
    {
        public string Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string UID => throw new NotImplementedException();
        public string Comment { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public PlanningItemDose Dose => throw new NotImplementedException();
        public bool IsDoseValid => throw new NotImplementedException();
        public IEnumerable<Beam> Beams => throw new NotImplementedException();
        public IEnumerable<Beam> BeamsInTreatmentOrder => throw new NotImplementedException();
        public DoseValue DosePerFraction => throw new NotImplementedException();
        public double TreatmentPercentage => throw new NotImplementedException();
        public Dictionary<string, string> PhotonCalculationOptions => throw new NotImplementedException();
        public void SetPrescription(int numberOfFractions, DoseValue dosePerFraction, double treatmentPercentage) => throw new NotImplementedException();
        public void SetCalculationModel(CalculationType calculationType, string model) => throw new NotImplementedException();
        public Dictionary<string, string> GetCalculationOptions(string calculationModel) => throw new NotImplementedException();
        public bool SetCalculationOption(string calculationModel, string optionName, string optionValue) => throw new NotImplementedException();
        public void SetTreatmentOrder(IEnumerable<Beam> orderedBeams) => throw new NotImplementedException();
        public StructureSet StructureSet => throw new NotImplementedException();
        public PatientOrientation TreatmentOrientation => throw new NotImplementedException();
        public DoseValue TotalDose => throw new NotImplementedException();
        public int? NumberOfFractions => throw new NotImplementedException();
        public DoseValuePresentation DoseValuePresentation { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string PhotonCalculationModel => throw new NotImplementedException();
        public string GetCalculationModel(CalculationType calculationType) => throw new NotImplementedException();
        public IEnumerable<string> GetModelsForCalculationType(CalculationType calculationType) => throw new NotImplementedException();
    }

    public class ExternalPlanSetup : PlanSetup
    {
        public Beam AddVMATBeam(ExternalBeamMachineParameters machineParameters, IEnumerable<double> metersetWeights, double collimatorAngle, double gantryAngle, double gantryStop, GantryDirection gantryDirection, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddConformalArcBeam(ExternalBeamMachineParameters machineParameters, double collimatorAngle, int controlPointCount, double gantryAngle, double gantryStop, GantryDirection gantryDirection, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddMLCArcBeam(ExternalBeamMachineParameters machineParameters, float[,] leafPositions, VRect<double> jawPositions, double collimatorAngle, double gantryAngle, double gantryStop, GantryDirection gantryDirection, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddMLCBeam(ExternalBeamMachineParameters machineParameters, float[,] leafPositions, VRect<double> jawPositions, double collimatorAngle, double gantryAngle, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddArcBeam(ExternalBeamMachineParameters machineParameters, VRect<double> jawPositions, double collimatorAngle, double gantryAngle, double gantryStop, GantryDirection gantryDirection, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddStaticBeam(ExternalBeamMachineParameters machineParameters, VRect<double> jawPositions, double collimatorAngle, double gantryAngle, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddSlidingWindowBeam(ExternalBeamMachineParameters machineParameters, IEnumerable<double> metersetWeights, double collimatorAngle, double gantryAngle, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public Beam AddMultipleStaticSegmentBeam(ExternalBeamMachineParameters machineParameters, IEnumerable<double> metersetWeights, double collimatorAngle, double gantryAngle, double patientSupportAngle, VVector isocenter) => throw new NotImplementedException();
        public void RemoveBeam(Beam beam) => throw new NotImplementedException();
        public CalculationResult CalculateDoseWithPresetValues(List<KeyValuePair<string, MetersetValue>> presetValues) => throw new NotImplementedException();
    }

    public class CalculationResult { public bool Success => throw new NotImplementedException(); }
    public class ExternalBeamTreatmentUnit { public string Id => throw new NotImplementedException(); }
    public class Technique { public string Id => throw new NotImplementedException(); }
    public class MLC { public string Id => throw new NotImplementedException(); }

    public class ControlPoint
    {
        public double CollimatorAngle => throw new NotImplementedException();
        public double GantryAngle => throw new NotImplementedException();
        public VRect<double> JawPositions => throw new NotImplementedException();
        public float[,] LeafPositions => throw new NotImplementedException();
        public double MetersetWeight => throw new NotImplementedException();
        public double PatientSupportAngle => throw new NotImplementedException();
    }

    public class ControlPointCollection : IEnumerable<ControlPoint>
    {
        public IEnumerator<ControlPoint> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }

    public class BeamParameters
    {
        public IEnumerable<ControlPointParameters> ControlPoints => throw new NotImplementedException();
        public VVector Isocenter { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public double WeightFactor { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }

    public class ControlPointParameters
    {
        public VRect<double> JawPositions { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public float[,] LeafPositions { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public double MetersetWeight { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }

    public class Beam
    {
        public string Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool IsSetupField => throw new NotImplementedException();
        public VVector IsocenterPosition => throw new NotImplementedException();
        public string EnergyModeDisplayName => throw new NotImplementedException();
        public MLCPlanType MLCPlanType => throw new NotImplementedException();
        public MetersetValue Meterset => throw new NotImplementedException();
        public double WeightFactor => throw new NotImplementedException();
        public int DoseRate => throw new NotImplementedException();
        public GantryDirection GantryDirection => throw new NotImplementedException();
        public ExternalBeamTreatmentUnit TreatmentUnit => throw new NotImplementedException();
        public Technique Technique => throw new NotImplementedException();
        public MLC MLC => throw new NotImplementedException();
        public ControlPointCollection ControlPoints => throw new NotImplementedException();
        public BeamParameters GetEditableParameters() => throw new NotImplementedException();
        public void ApplyParameters(BeamParameters beamParams) => throw new NotImplementedException();
    }

    public class Dose
    {
        public int XSize => throw new NotImplementedException();
        public int YSize => throw new NotImplementedException();
        public int ZSize => throw new NotImplementedException();
        public double XRes => throw new NotImplementedException();
        public double YRes => throw new NotImplementedException();
        public double ZRes => throw new NotImplementedException();
        public VVector Origin => throw new NotImplementedException();
        public VVector XDirection => throw new NotImplementedException();
        public VVector YDirection => throw new NotImplementedException();
        public VVector ZDirection => throw new NotImplementedException();
        public DoseValue DoseMax3D => throw new NotImplementedException();
        public void GetVoxels(int planeIndex, int[,] preallocatedBuffer) => throw new NotImplementedException();
        public DoseValue VoxelToDoseValue(int voxelValue) => throw new NotImplementedException();
    }

    public class PlanningItemDose : Dose { }

    public class StructureSet
    {
        public string Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Image Image => throw new NotImplementedException();
        public string UID => throw new NotImplementedException();
        public IEnumerable<Structure> Structures => throw new NotImplementedException();
    }

    public class Structure
    {
        public string Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string DicomType => throw new NotImplementedException();
        public bool IsEmpty => throw new NotImplementedException();
        public double Volume => throw new NotImplementedException();
        public MeshGeometry3D MeshGeometry => throw new NotImplementedException();
        public VVector[][] GetContoursOnImagePlane(int z) => throw new NotImplementedException();
        public bool GetAssignedHU(out double huValue) => throw new NotImplementedException();
    }

    public class Image
    {
        public string Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int XSize => throw new NotImplementedException();
        public int YSize => throw new NotImplementedException();
        public int ZSize => throw new NotImplementedException();
        public double XRes => throw new NotImplementedException();
        public double YRes => throw new NotImplementedException();
        public double ZRes => throw new NotImplementedException();
        public VVector Origin => throw new NotImplementedException();
        public VVector UserOrigin { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool HasUserOrigin => throw new NotImplementedException();
        public PatientOrientation ImagingOrientation => throw new NotImplementedException();
    }
}
