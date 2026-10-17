using System;
using System.IO;

namespace ZonerInspiredViewer
{
    internal sealed class FaceCropOptions
    {
        internal int Surroundings = 30;
        internal double Aspect = 1;
        internal FaceCropOptions Copy() { return (FaceCropOptions)MemberwiseClone(); }
        internal bool SameAs(FaceCropOptions other) { return other != null && Surroundings == other.Surroundings && Math.Abs(Aspect - other.Aspect) < .000001; }
        internal void Validate()
        {
            if (Surroundings < 0 || Surroundings > 200 || Double.IsNaN(Aspect) || Double.IsInfinity(Aspect) || Aspect < .5 || Aspect > 2)
                throw new InvalidDataException("Use face surroundings of 0-200% and a crop ratio between 1:2 and 2:1.");
        }
    }
}
