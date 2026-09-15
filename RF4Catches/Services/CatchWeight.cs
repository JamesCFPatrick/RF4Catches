namespace RF4Catches.Services;

public static class CatchWeight
{
    public static decimal NormalizeKg(decimal weightKg) =>
        weightKg >= 1000m ? weightKg / 1000m : weightKg;
}
