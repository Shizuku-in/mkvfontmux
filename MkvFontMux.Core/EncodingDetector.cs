using System.Text;

namespace MkvFontMux;

internal static class EncodingDetector
{
    private static bool _providerRegistered;

    private static readonly int[] CjkCodePages = [932, 936, 54936, 950, 951];

    private static readonly string[] AssMarkers =
        ["[Script Info]", "[V4", "Style:", "Dialogue:", "Format:", "[Events]"];

    public static Encoding Detect(string filePath)
    {
        var raw = File.ReadAllBytes(filePath);
        return Detect(raw);
    }

    public static Encoding Detect(byte[] raw)
    {
        EnsureProvider();

        if (TryDetectBom(raw, out var bomEncoding))
            return bomEncoding;

        var utf8Text = Decode(raw, Encoding.UTF8);
        if (utf8Text != null && !utf8Text.Contains('�'))
            return Encoding.UTF8;

        Encoding? bestEncoding = null;
        var bestScore = -1;

        foreach (var codePage in CjkCodePages)
        {
            try
            {
                var enc = Encoding.GetEncoding(codePage);
                var text = Decode(raw, enc);
                if (text == null) continue;

                var score = Score(text);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEncoding = enc;
                }
            }
            catch
            {
            }
        }

        return bestEncoding ?? Encoding.UTF8;
    }

    private static bool TryDetectBom(byte[] raw, out Encoding encoding)
    {
        encoding = Encoding.UTF8;

        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
            return true;
        }

        if (raw.Length >= 4 && raw[0] == 0xFF && raw[1] == 0xFE && raw[2] == 0x00 && raw[3] == 0x00)
        {
            encoding = Encoding.UTF32;
            return true;
        }

        if (raw.Length >= 4 && raw[0] == 0x00 && raw[1] == 0x00 && raw[2] == 0xFE && raw[3] == 0xFF)
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            return true;
        }

        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            return true;
        }

        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        return false;
    }

    private static string? Decode(byte[] raw, Encoding encoding)
    {
        try
        {
            return encoding.GetString(raw);
        }
        catch
        {
            return null;
        }
    }

    private static int Score(string text)
    {
        var score = 0;

        foreach (var marker in AssMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                score += 50;
        }

        var replacementCount = 0;
        var cjkCount = 0;

        foreach (var ch in text)
        {
            if (ch == '�')
                replacementCount++;
            else if (IsCjkCodepoint(ch))
                cjkCount++;
        }

        score -= replacementCount * 10;
        score += Math.Min(cjkCount / 10, 20);

        return score;
    }

    private static bool IsCjkCodepoint(char ch)
    {
        return ch switch
        {
            >= '　' and <= '〿' => true,  // CJK punctuation
            >= '぀' and <= 'ゟ' => true,  // Hiragana
            >= '゠' and <= 'ヿ' => true,  // Katakana
            >= '㐀' and <= '䶿' => true,  // CJK Extension A
            >= '一' and <= '鿿' => true,  // CJK Unified
            >= '＀' and <= '￯' => true,  // Fullwidth forms
            _ => false
        };
    }

    private static void EnsureProvider()
    {
        if (_providerRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _providerRegistered = true;
    }
}
