// Compile-check stub. NOT the real ESAPI. Members limited to those used by SrsMcPsqa.
using System;

namespace VMS.TPS.Common.Model.Types
{
    public struct VVector
    {
        public VVector(double xi, double yi, double zi) { x = xi; y = yi; z = zi; }
        public double x { get; set; }
        public double y { get; set; }
        public double z { get; set; }
        public double Length => throw new NotImplementedException();
        public static VVector operator +(VVector a, VVector b) => throw new NotImplementedException();
        public static VVector operator -(VVector a, VVector b) => throw new NotImplementedException();
    }

    public struct DoseValue
    {
        public double Dose => throw new NotImplementedException();
        public string UnitAsString => throw new NotImplementedException();
    }

    public enum DosimeterUnit { Null, MU, Minute, Second }

    public struct MetersetValue
    {
        public MetersetValue(double value, DosimeterUnit unit) { Value = value; Unit = unit; }
        public double Value { get; }
        public DosimeterUnit Unit { get; }
    }

    public struct VRect<T>
    {
        public VRect(T x1, T y1, T x2, T y2) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
        public T X1 { get; }
        public T Y1 { get; }
        public T X2 { get; }
        public T Y2 { get; }
    }

    public class ExternalBeamMachineParameters
    {
        public ExternalBeamMachineParameters(string machineId, string energyModeId, int doseRate, string techniqueId, string primaryFluenceModeId) { }
        public ExternalBeamMachineParameters(string machineId) { }
        public string MLCId { get; set; }
    }

    public enum CalculationType { PhotonVolumeDose }
    public enum DoseValuePresentation { Relative, Absolute }
    public enum PatientOrientation { NoOrientation, HeadFirstSupine, HeadFirstProne, FeetFirstSupine, FeetFirstProne }
    public enum MLCPlanType { Static, DoseDynamic, ArcDynamic, VMAT, NotDefined }
    public enum GantryDirection { None, Clockwise, CounterClockwise }
}
