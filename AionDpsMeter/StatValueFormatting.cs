using System.Globalization;
using System.Numerics;

namespace AionDpsMeter.Local;
internal static class StatValueFormatting
{
    internal static string Format(long raw, bool percent, bool hundredths)
    {
        // Do not silently discard integer precision to imitate a JS Number.
        if (raw is < -9007199254740991L or > 9007199254740991L)
            return raw.ToString(CultureInfo.InvariantCulture) + " (raw)";
        double value = raw;
        if (hundredths) value *= .01;
        if (percent) value /= 100;
        double magnitude = Math.Abs(value);
        int digits = magnitude < 10 ? 2 : magnitude < 100 ? 1 : 0;
        // JS toFixed rounds the actual binary value, not a decimal approximation.
        // Exact rational rounding also avoids intermediate multiply/round ties.
        long bits = BitConverter.DoubleToInt64Bits(magnitude);
        int exponent = (int)((bits >> 52) & 2047);
        BigInteger numerator = bits & 0x000fffffffffffffL;
        if (exponent != 0) numerator += BigInteger.One << 52;
        int shift = (exponent == 0 ? -1022 : exponent - 1023) - 52;
        int scale = digits == 2 ? 100 : digits == 1 ? 10 : 1;
        numerator *= scale;
        BigInteger denominator = BigInteger.One;
        if (shift >= 0) numerator <<= shift; else denominator <<= -shift;
        BigInteger rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder * 2 >= denominator) rounded++;
        decimal display = (decimal)rounded / scale;
        return (value < 0 ? "-" : "") + display.ToString("#,0.##", CultureInfo.InvariantCulture) + (percent ? "%" : "");
    }
}
