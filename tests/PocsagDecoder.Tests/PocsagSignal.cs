namespace PocsagDecoder.Tests;

/// <summary>
/// Erzeugt ein synthetisches POCSAG-Basisbandsignal (22050 Hz, s16le), wie es
/// rtl_fm liefert (C#-Gegenstück zu tools/pocsag_gen.py).
/// </summary>
public static class PocsagSignal
{
    private const uint Sync = 0x7CD215D8, Idle = 0x7A89C197;
    private const int Rate = 22050;

    public static uint Bch(uint cw21)
    {
        const uint gen = 0x769;
        var reg = cw21 << 10;
        for (var i = 30; i >= 10; i--)
            if ((reg & (1u << i)) != 0) reg ^= gen << (i - 10);
        var cw = ((cw21 << 10) | (reg & 0x3FF)) << 1;
        return cw | (uint)(System.Numerics.BitOperations.PopCount(cw) & 1);
    }

    public static byte[] Generate(int baud, params (int Ric, int Func, string Text)[] pages)
    {
        var words = new List<uint>();
        foreach (var (ric, func, text) in pages)
        {
            while (words.Count % 16 != (ric & 7) * 2) words.Add(Idle);
            words.Add(Bch((uint)(((ric >> 3) << 2) | func)));
            var bits = new List<int>();
            foreach (var ch in text)
                for (var i = 0; i < 7; i++) bits.Add((ch >> i) & 1);
            while (bits.Count % 20 != 0) bits.Add(0);
            for (var i = 0; i < bits.Count; i += 20)
            {
                uint v = 0;
                for (var j = 0; j < 20; j++) v = (v << 1) | (uint)bits[i + j];
                words.Add(Bch((1u << 20) | v));
            }
            words.Add(Idle);
        }
        while (words.Count % 16 != 0) words.Add(Idle);

        var stream = new List<int>();
        for (var i = 0; i < 576; i++) stream.Add(1 - i % 2);
        for (var b = 0; b < words.Count; b += 16)
            foreach (var w in new[] { Sync }.Concat(words.Skip(b).Take(16)))
                for (var i = 31; i >= 0; i--) stream.Add((int)((w >> i) & 1));
        for (var i = 0; i < 64; i++) stream.Add(1 - i % 2);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        for (var i = 0; i < Rate; i++) bw.Write((short)0);
        double acc = 0;
        foreach (var bit in stream)
        {
            acc += (double)Rate / baud;
            var n = (int)acc;
            acc -= n;
            for (var i = 0; i < n; i++) bw.Write((short)(bit == 1 ? -12000 : 12000));
        }
        for (var i = 0; i < Rate; i++) bw.Write((short)0);
        bw.Flush();
        return ms.ToArray();
    }
}
