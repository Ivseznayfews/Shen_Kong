namespace BedCheck
{
    internal enum ReactorRisk { None, Critical, NoPower, PowerRisk }

    // Pure decisions: no game state is read or modified here.
    internal static class DecisionRules
    {
        internal static ReactorRisk Reactor(int integrity, int decay)
        {
            if (decay > 0 && integrity <= decay) return ReactorRisk.Critical;
            if (integrity <= 25) return ReactorRisk.NoPower;
            if (decay > 0 && integrity - decay <= 25) return ReactorRisk.PowerRisk;
            return ReactorRisk.None;
        }
        internal static bool FurnaceNeedsInput(int metal, bool blast, int junk, bool recycling)
        {
            return metal < (blast ? 3 : 2) && (!recycling || junk < 6);
        }
        internal static bool InsufficientEnergy(int available, int required) => available < required;
        internal static bool AgeableWine(bool wine, bool counterfeit, int state) => wine && !counterfeit && state == 2;
    }
}
