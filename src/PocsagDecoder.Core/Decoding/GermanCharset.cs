namespace PocsagDecoder.Core.Decoding;

/// <summary>
/// POCSAG überträgt 7-Bit-ASCII. In Deutschland (DIN 66003) stehen die Zeichen
/// [ \ ] { | } ~ für Ä Ö Ü ä ö ü ß.
/// </summary>
public static class GermanCharset
{
    public static string Convert(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                '[' => 'Ä',
                '\\' => 'Ö',
                ']' => 'Ü',
                '{' => 'ä',
                '|' => 'ö',
                '}' => 'ü',
                '~' => 'ß',
                _ => chars[i],
            };
        }
        return new string(chars);
    }
}
