namespace Nimbus;

internal static class UpdatePrompt
{
    private static readonly long SnoozeTicks = TimeSpan.FromHours(24).Ticks;

    // Saat geri alınırsa erteleme süresiz uzamasın diye "gelecekteki" kayıt geçersiz sayılır.
    public static bool IsDue(string version, string? snoozedVersion, long snoozedUtcTicks, long nowUtcTicks) =>
        version != snoozedVersion
        || nowUtcTicks < snoozedUtcTicks
        || nowUtcTicks - snoozedUtcTicks >= SnoozeTicks;
}
