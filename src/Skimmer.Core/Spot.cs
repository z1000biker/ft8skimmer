namespace Skimmer.Core;

public sealed record Spot(
    DateTime SlotUtc,
    Band Band,
    int Snr,
    double Dt,
    int AudioHz,
    string Message,
    string? Sender,
    string? Grid,
    double? DistanceKm)
{
    public long FrequencyHz => Band.DialHz + AudioHz;
}
