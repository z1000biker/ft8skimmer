namespace Skimmer.Core;

public static class WavWriter
{
    /// <summary>Γράφει mono 16 bit. Κανονικοποιεί σε RMS ~2000 ώστε το jt9 να βλέπει λογική στάθμη.</summary>
    public static void WriteNormalized(string path, ReadOnlySpan<float> samples, int rate)
    {
        double sum = 0; int cnt = 0;
        foreach (var s in samples) if (s != 0) { sum += s * s; cnt++; }
        double rms = cnt > 0 ? Math.Sqrt(sum / cnt) : 1;
        float g = rms > 0 ? (float)(2000 / rms) : 1;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        int data = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * g, -32767f, 32767f));
    }

    public static float[] ReadMono16(string path)
    {
        var b = File.ReadAllBytes(path);
        int p = 12;
        while (p + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
            int len = BitConverter.ToInt32(b, p + 4);
            if (id == "data")
            {
                var r = new float[len / 2];
                for (int i = 0; i < r.Length; i++) r[i] = BitConverter.ToInt16(b, p + 8 + 2 * i);
                return r;
            }
            p += 8 + len;
        }
        throw new InvalidDataException("No data chunk found");
    }
}
