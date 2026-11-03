using System;

public static class PercentageDefense
{
    // DamageCalc.checkDamage; additive IED is an explicit user decision.
    public static double Multiplier(double defensePercent,double ignorePercent)
    {
        if(double.IsNaN(defensePercent)||double.IsNaN(ignorePercent))
            throw new ArgumentException("Invalid defense percentage");
        double remaining=Math.Max(0,defensePercent)*(1-Math.Max(0,Math.Min(100,ignorePercent))/100);
        return Math.Max(0,1-remaining/100);
    }
}
