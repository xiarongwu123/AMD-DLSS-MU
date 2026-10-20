using System.Text;
using System.Text.RegularExpressions;

namespace AmdNrAssistant;

public sealed record ManagedIniKey(string Section, string Key, string? Before, string After);

// Preserve all untouched lines, line endings, comments and UTF-8/UTF-16 BOMs.
// Ambiguous duplicate sections/keys are errors, not silently rewritten settings.
public static class ManagedIni
{
    sealed record Line(string Text, string End);
    static List<Line> Lines(string text) => Regex.Matches(text, @"[^\r\n]*(?:\r\n|\n|\r|$)")
        .Cast<Match>().Where(m => m.Length > 0).Select(m =>
        { var value = m.Value; var raw = value.TrimEnd('\r', '\n'); return new Line(raw, value[raw.Length..]); }).ToList();
    static (int Start, int End, int Key) Find(List<Line> lines, string section, string key)
    {
        var start = -1; var end = lines.Count; var found = -1; var active = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i].Text.Trim();
            var header = Regex.Match(raw, @"^\[([^\]]+)\]\s*(?:[;#].*)?$");
            if (header.Success)
            {
                if (active) end = i;
                active = header.Groups[1].Value.Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                if (active) { if (start >= 0) throw new IOException("配置含重复节：[" + section + "]"); start = i; end = lines.Count; }
            }
            else if (active && !raw.StartsWith(';') && !raw.StartsWith('#') && raw.Contains('='))
            {
                if (raw[..raw.IndexOf('=')].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                { if (found >= 0) throw new IOException("配置含重复键：" + key); found = i; }
            }
        }
        return (start, end, found);
    }
    static string Value(string line) => new Regex(@"\s+[;#]").Split(line[(line.IndexOf('=') + 1)..], 2)[0].Trim();
    public static string? Get(string text, string section, string key)
    { var lines = Lines(text); var at = Find(lines, section, key).Key; return at < 0 ? null : Value(lines[at].Text); }
    public static string Set(string text, string section, string key, string? value)
    {
        if (!Regex.IsMatch(section, "^[A-Za-z][A-Za-z0-9_]{0,60}$") || !Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9_]{0,60}$") ||
            value?.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new IOException("配置键值无效。");
        var lines = Lines(text); var (start, end, at) = Find(lines, section, key);
        if (at >= 0)
        {
            if (value == null) lines.RemoveAt(at);
            else
            {
                var raw = lines[at].Text; var equal = raw.IndexOf('='); var rest = raw[(equal + 1)..];
                var prefix = Regex.Match(rest, @"^\s*").Value;
                var comment = Regex.Match(rest, @"\s+[;#].*$");
                lines[at] = lines[at] with { Text = raw[..(equal + 1)] + prefix + value + (comment.Success ? comment.Value : "") };
            }
        }
        else if (value != null)
        {
            var nl = lines.FirstOrDefault(l => l.End.Length > 0)?.End ?? "\r\n";
            if (lines.Count > 0 && lines[^1].End.Length == 0) lines[^1] = lines[^1] with { End = nl };
            if (start < 0) { lines.Add(new("[" + section + "]", nl)); end = lines.Count; }
            lines.Insert(end, new(key + "=" + value, nl));
        }
        return string.Concat(lines.Select(l => l.Text + l.End));
    }
    public static (string Text, Encoding Encoding) Decode(byte[] data)
    {
        if (data.Length > 2 * 1024 * 1024) throw new IOException("INI 配置过大。");
        Encoding encoding = data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? new UnicodeEncoding(false, true, true)
            : data.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? new UnicodeEncoding(true, true, true)
            : new UTF8Encoding(data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), true);
        var bom = encoding.GetPreamble(); var offset = data.AsSpan().StartsWith(bom) ? bom.Length : 0;
        return (encoding.GetString(data, offset, data.Length - offset), encoding);
    }
    public static byte[] Encode(string text, Encoding encoding) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
}
