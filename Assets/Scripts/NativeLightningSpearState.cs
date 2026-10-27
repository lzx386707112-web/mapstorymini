// CloseRangeDamageHandler.advanceLightningSpearCombo: times are monotonic milliseconds.
// A lifecycle reset is required on death, map replacement, or actor replacement.
public sealed class NativeLightningSpearState
{
    public int PressCount { get; private set; }
    public bool Finishing => PressCount == 12;
    long started, lastPress;
    public bool TryPress(long now)
    {
        if (Finishing) return false;
        if (PressCount == 0 || now - started >= 60000)
        {
            PressCount = 0;
            started = now;
        }
        else if (now - lastPress < 180) return false;
        lastPress = now;
        PressCount++;
        return true;
    }
    public void Reset() { PressCount = 0; started = lastPress = 0; }
}
