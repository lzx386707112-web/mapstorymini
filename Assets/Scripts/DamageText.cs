using UnityEngine;

// Numeric and Miss paths both use the verified native bitmap/timing rules.
public static class DamageText
{
    public static void Show(Vector3 worldPos, int amount, string skin = "red", float scale = 1f)
        => NativeDamageNumber.Show(worldPos, System.Math.Max(0,amount), skin, scale);
    public static void Show(Vector3 worldPos, float amount, string skin = "red", float scale = 1f)
    {
        int value = (int)System.Math.Max(0, System.Math.Min(int.MaxValue, System.Math.Round((double)amount)));
        NativeDamageNumber.Show(worldPos, value, skin, scale);
    }
    public static void Miss(Vector3 worldPos, float scale = 1f, string skin = "red")
        => NativeDamageNumber.Miss(worldPos,skin,scale);
}
