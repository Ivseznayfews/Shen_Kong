using System;
using BedCheck;
static class Test
{
    static int n;
    static void Equal<T>(T actual, T expected, string name)
    {
        n++;
        if (!Equals(actual, expected)) throw new Exception(name + ": expected " + expected + ", got " + actual);
    }
    static void Main()
    {
        Equal(DecisionRules.Reactor(18, 18), ReactorRisk.Critical, "reactor reaches zero");
        Equal(DecisionRules.Reactor(19, 18), ReactorRisk.NoPower, "reactor already below power threshold");
        Equal(DecisionRules.Reactor(26, 1), ReactorRisk.PowerRisk, "reactor crosses power threshold");
        Equal(DecisionRules.Reactor(26, 0), ReactorRisk.None, "reactor without decay");
        Equal(DecisionRules.Reactor(25, 0), ReactorRisk.NoPower, "reactor threshold inclusive");
        Equal(DecisionRules.FurnaceNeedsInput(1, false, 0, false), true, "furnace short one");
        Equal(DecisionRules.FurnaceNeedsInput(2, false, 0, false), false, "furnace enough");
        Equal(DecisionRules.FurnaceNeedsInput(2, true, 0, false), true, "blast needs three");
        Equal(DecisionRules.FurnaceNeedsInput(0, false, 6, true), false, "junk mode complete");
        Equal(DecisionRules.FurnaceNeedsInput(0, false, 5, true), true, "junk mode short");
        Equal(DecisionRules.FurnaceNeedsInput(0, false, 6, false), true, "junk without module");
        Equal(DecisionRules.InsufficientEnergy(8, 8), false, "energy exact");
        Equal(DecisionRules.InsufficientEnergy(7, 8), true, "energy short");
        Equal(DecisionRules.AgeableWine(true, false, 2), true, "finished genuine wine");
        Equal(DecisionRules.AgeableWine(true, true, 2), false, "counterfeit wine");
        Equal(DecisionRules.AgeableWine(true, false, 1), false, "unfinished wine");
        Equal(DecisionRules.AgeableWine(false, false, 2), false, "non wine");
        Console.WriteLine(n + " decision checks passed");
    }
}
