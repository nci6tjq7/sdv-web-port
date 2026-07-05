using System.Globalization;

namespace SdvWebPort.Vfs.Content;

public sealed class BmFontFile
{
    public int LineHeight { get; set; }
    public int BaseHeight { get; set; }
    public int ScaleWidth { get; set; }
    public int ScaleHeight { get; set; }
    public List<BmFontPage> Pages { get; } = new();
    public Dictionary<int, BmFontChar> Characters { get; } = new();

    public static BmFontFile Parse(string text)
    {
        var font = new BmFontFile();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.StartsWith("common ")) { var p = ParseProps(t); font.LineHeight = I(p, "lineHeight"); font.BaseHeight = I(p, "base"); font.ScaleWidth = I(p, "scaleW"); font.ScaleHeight = I(p, "scaleH"); }
            else if (t.StartsWith("page ")) { var p = ParseProps(t); font.Pages.Add(new BmFontPage(I(p, "id"), S(p, "file"))); }
            else if (t.StartsWith("char ")) { var p = ParseProps(t); font.Characters[I(p, "id")] = new BmFontChar(I(p,"id"), I(p,"x"), I(p,"y"), I(p,"width"), I(p,"height"), I(p,"xoffset"), I(p,"yoffset"), I(p,"xadvance"), I(p,"page")); }
        }
        return font;
    }

    public BmFontChar? GetChar(int charCode) => Characters.TryGetValue(charCode, out var c) ? c : null;
    public int MeasureString(string text) { int w = 0; foreach (char c in text) w += Characters.TryGetValue(c, out var g) ? g.XAdvance : 8; return w; }

    private static Dictionary<string, string> ParseProps(string line)
    {
        var props = new Dictionary<string, string>();
        var parts = line.Split(' ', 2);
        if (parts.Length < 2) return props;
        var rest = parts[1];
        int i = 0;
        while (i < rest.Length)
        {
            while (i < rest.Length && rest[i] == ' ') i++;
            if (i >= rest.Length) break;
            int ks = i;
            while (i < rest.Length && rest[i] != '=') i++;
            if (i >= rest.Length) break;
            string key = rest[ks..i]; i++;
            string value;
            if (i < rest.Length && rest[i] == '"') { i++; int vs = i; while (i < rest.Length && rest[i] != '"') i++; value = rest[vs..i]; i++; }
            else { int vs = i; while (i < rest.Length && rest[i] != ' ') i++; value = rest[vs..i]; }
            props[key] = value;
        }
        return props;
    }

    private static int I(Dictionary<string, string> p, string k) => int.TryParse(p.GetValueOrDefault(k, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static string S(Dictionary<string, string> p, string k) => p.GetValueOrDefault(k, "");
}

public record BmFontPage(int Id, string File);
public record BmFontChar(int Id, int X, int Y, int Width, int Height, int XOffset, int YOffset, int XAdvance, int Page);
