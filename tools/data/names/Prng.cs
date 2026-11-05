using System.Text;

namespace OneContact.Data.Names;

/// <summary>
/// A small deterministic random source (SplitMix64 seeding an xoshiro256** generator). Every example gets its own generator seeded
/// from the run seed and the example's identifier, so an example renders the same whatever order the registers are read in.
/// </summary>
internal sealed class Prng
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    internal Prng(ulong seed)
    {
        _s0 = Mix(ref seed);
        _s1 = Mix(ref seed);
        _s2 = Mix(ref seed);
        _s3 = Mix(ref seed);
    }

    /// <summary>FNV-1a over the UTF-8 bytes of <paramref name="text"/>, finished with a SplitMix64 round: stable across runs and machines.</summary>
    internal static ulong Hash(string text, ulong salt)
    {
        ulong h = 14695981039346656037UL ^ salt;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            h ^= b;
            h *= 1099511628211UL;
        }

        return Mix(ref h);
    }

    internal static Prng For(ulong seed, string id) => new(Hash(id, seed));

    internal ulong Next()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>A uniform number in [0, 1).</summary>
    internal double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>A uniform integer in [0, <paramref name="n"/>).</summary>
    internal int Int(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);

    /// <summary>True with probability <paramref name="p"/>.</summary>
    internal bool Chance(double p) => Unit() < p;

    internal T Pick<T>(IReadOnlyList<T> items) => items[Int(items.Count)];

    /// <summary>Picks an index by weight.</summary>
    internal int Weighted(IReadOnlyList<double> weights)
    {
        double total = 0;
        foreach (double w in weights)
        {
            total += w;
        }

        double x = Unit() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            x -= weights[i];
            if (x < 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }

    private static ulong Mix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
}
