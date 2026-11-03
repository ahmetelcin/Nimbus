namespace Nimbus;

internal sealed record GoogleState(string Link, long CreatedUtcTicks, long ModifiedUtcTicks);

internal enum SyncAction
{
    None,
    Export,
    Conflict,
}

internal static class SyncRules
{
    // v1.1 öncesi kayıtlarda son eşitleme zamanı yoktur; oluşturma zamanına dönüştürme payı eklenerek karşılaştırılır.
    private static readonly TimeSpan UnknownBaselineMargin = TimeSpan.FromMinutes(1);

    // Düzenleme sürerken yarım hâli yazmamak için kopya bir süre durulmalı.
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMinutes(2);

    public static bool GoogleChanged(UploadRecord record, GoogleState state)
    {
        var baseline = record.SyncedGoogleTicks > 0
            ? record.SyncedGoogleTicks
            : state.CreatedUtcTicks + UnknownBaselineMargin.Ticks;
        return state.ModifiedUtcTicks > baseline;
    }

    public static SyncAction Decide(UploadRecord record, GoogleState state, bool localChanged, DateTime utcNow)
    {
        if (!GoogleChanged(record, state) || utcNow.Ticks - state.ModifiedUtcTicks < QuietPeriod.Ticks)
            return SyncAction.None;

        return localChanged ? SyncAction.Conflict : SyncAction.Export;
    }
}
