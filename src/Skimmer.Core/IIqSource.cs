namespace Skimmer.Core;

/// <summary>Πηγή δειγμάτων IQ 8 bit χωρίς πρόσημο (μορφή RTL-SDR).</summary>
public interface IIqSource : IDisposable
{
    int SampleRate { get; }

    /// <summary>true για πραγματικό δέκτη· false για συνθετική πηγή με εικονικό χρόνο.</summary>
    bool IsRealtime { get; }

    /// <summary>Για μη πραγματικές πηγές: ο χρόνος UTC του πρώτου δείγματος.</summary>
    DateTime VirtualStartUtc { get; }

    void Open();
    void SetCenterFrequency(long hz);

    /// <summary>Γεμίζει το buffer με ζεύγη I,Q. Επιστρέφει πλήθος bytes ή 0 στο τέλος.</summary>
    int Read(byte[] buffer);
}
