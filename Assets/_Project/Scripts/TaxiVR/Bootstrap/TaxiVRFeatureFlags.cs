using System;

namespace TaxiVR.Bootstrap
{
    [Serializable]
    public sealed class TaxiVRFeatureFlags
    {
        public bool Cockpit;
        public bool FootTracker;
        public bool Driving;

        public bool DeferredSystemsAbsent => !Cockpit && !FootTracker && !Driving;
    }
}
