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

    public struct MetersetValue
    {
        public double Value => throw new NotImplementedException();
    }

    public enum CalculationType { PhotonVolumeDose }
    public enum DoseValuePresentation { Relative, Absolute }
    public enum PatientOrientation { NoOrientation, HeadFirstSupine, HeadFirstProne, FeetFirstSupine, FeetFirstProne }
    public enum MLCPlanType { Static, DoseDynamic, ArcDynamic, VMAT, NotDefined }
    public enum GantryDirection { None, Clockwise, CounterClockwise }
}
