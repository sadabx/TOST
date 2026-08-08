using System.Text;

namespace Trionine.TOST.Core.Steam;

public sealed record SteamDepotKeyPreview(
    bool ChangesFile,
    string UpdatedText,
    IReadOnlyList<string> AddedDepotIds,
    IReadOnlyList<string> Conflicts);

public sealed record SteamDepotKeyWriteResult(bool Changed, string? BackupPath, string ConfigPath);

public sealed class SteamDepotKeyService
{
    public const long MaximumConfigBytes = 16L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public SteamDepotKeyPreview Preview(string configPath, IReadOnlyDictionary<string, string> depotKeys)
    {
        var text = Read(configPath);
        var root = VdfParser.Parse(text);
        var depots = Find(root, "InstallConfigStore", "Software", "Valve", "Steam", "depots")
            ?? throw new InvalidDataException("Steam config.vdf has no InstallConfigStore/Software/Valve/Steam/depots section.");
        var added = new List<string>();
        var conflicts = new List<string>();
        var additions = new StringBuilder();
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        foreach (var pair in depotKeys.OrderBy(item => ulong.Parse(item.Key)))
        {
            if (!ulong.TryParse(pair.Key, out _) || pair.Value.Length == 0 || pair.Value.Length % 2 != 0 ||
                pair.Value.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"Depot {pair.Key} has an invalid hexadecimal key.");
            var existing = depots.Children.FirstOrDefault(child => child.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            var existingKey = existing?.Properties.FirstOrDefault(property => property.Key.Equals("DecryptionKey", StringComparison.OrdinalIgnoreCase)).Value;
            if (existingKey is not null)
            {
                if (!existingKey.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                    conflicts.Add($"Depot {pair.Key} already has a different DecryptionKey; overwrite is disabled.");
                continue;
            }
            if (existing is not null)
            {
                conflicts.Add($"Depot {pair.Key} exists without a DecryptionKey; automatic restructuring is disabled.");
                continue;
            }

            added.Add(pair.Key);
            additions.Append('\t', depots.Depth + 1).Append('"').Append(pair.Key).Append('"').Append(newline);
            additions.Append('\t', depots.Depth + 1).Append('{').Append(newline);
            additions.Append('\t', depots.Depth + 2).Append("\"DecryptionKey\"\t\t\"").Append(pair.Value.ToLowerInvariant()).Append('"').Append(newline);
            additions.Append('\t', depots.Depth + 1).Append('}').Append(newline);
        }

        var updated = additions.Length == 0 ? text : text.Insert(depots.CloseBraceIndex, additions.ToString());
        return new SteamDepotKeyPreview(added.Count > 0, updated, added, conflicts);
    }

    public SteamDepotKeyWriteResult Apply(string configPath, IReadOnlyDictionary<string, string> depotKeys, string backupDirectory)
    {
        var preview = Preview(configPath, depotKeys);
        if (preview.Conflicts.Count > 0) throw new InvalidDataException(string.Join(" ", preview.Conflicts));
        var fullPath = Path.GetFullPath(configPath);
        if (!preview.ChangesFile) return new SteamDepotKeyWriteResult(false, null, fullPath);
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"config-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.vdf");
        File.Copy(fullPath, backupPath, overwrite: false);
        WriteAtomic(fullPath, StrictUtf8.GetBytes(preview.UpdatedText));
        return new SteamDepotKeyWriteResult(true, backupPath, fullPath);
    }

    private static VdfNode? Find(VdfNode root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = current.Children.FirstOrDefault(child => child.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (current is null) return null;
        }
        return current;
    }

    private static string Read(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists || info.LinkTarget is not null || info.Length is <= 0 or > MaximumConfigBytes)
            throw new InvalidDataException("Steam config.vdf must be a bounded regular file.");
        try { return StrictUtf8.GetString(File.ReadAllBytes(info.FullName)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Steam config.vdf is not valid UTF-8.", ex); }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Invalid Steam config path.");
        var temporary = Path.Combine(directory, $".config.vdf.tost-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record VdfNode(string Name, int Depth, int CloseBraceIndex, List<VdfNode> Children, List<KeyValuePair<string, string>> Properties);

    private static class VdfParser
    {
        public static VdfNode Parse(string text)
        {
            var tokens = Tokenize(text);
            var index = 0;
            var children = ParseEntries(tokens, ref index, 0, out _);
            if (index != tokens.Count) throw new InvalidDataException("Steam config.vdf contains unexpected trailing syntax.");
            return new VdfNode(string.Empty, -1, text.Length, children.Nodes, children.Properties);
        }

        private static (List<VdfNode> Nodes, List<KeyValuePair<string, string>> Properties) ParseEntries(
            List<Token> tokens, ref int index, int depth, out int closeIndex)
        {
            var nodes = new List<VdfNode>();
            var properties = new List<KeyValuePair<string, string>>();
            closeIndex = -1;
            while (index < tokens.Count)
            {
                if (tokens[index].Kind == TokenKind.Close)
                {
                    closeIndex = tokens[index++].Start;
                    return (nodes, properties);
                }
                if (tokens[index].Kind != TokenKind.String) throw new InvalidDataException("Expected a quoted VDF key.");
                var key = tokens[index++].Value;
                if (index >= tokens.Count) throw new InvalidDataException("VDF key has no value.");
                if (tokens[index].Kind == TokenKind.String)
                {
                    properties.Add(new KeyValuePair<string, string>(key, tokens[index++].Value));
                    continue;
                }
                if (tokens[index++].Kind != TokenKind.Open) throw new InvalidDataException("Expected a VDF object.");
                var entries = ParseEntries(tokens, ref index, depth + 1, out var childClose);
                if (childClose < 0) throw new InvalidDataException("VDF object is missing its closing brace.");
                nodes.Add(new VdfNode(key, depth, childClose, entries.Nodes, entries.Properties));
            }
            return (nodes, properties);
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            for (var i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i += 2;
                    while (i < text.Length && text[i] is not '\r' and not '\n') i++;
                    continue;
                }
                if (text[i] == '{') { tokens.Add(new Token(TokenKind.Open, i++, string.Empty)); continue; }
                if (text[i] == '}') { tokens.Add(new Token(TokenKind.Close, i++, string.Empty)); continue; }
                if (text[i] != '"') throw new InvalidDataException($"Unexpected unquoted VDF content at offset {i}.");
                var start = i++;
                var value = new StringBuilder();
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '"') { i++; closed = true; break; }
                    if (text[i] == '\\' && i + 1 < text.Length) { value.Append(text[i + 1]); i += 2; }
                    else value.Append(text[i++]);
                }
                if (!closed) throw new InvalidDataException("VDF quoted string is not terminated.");
                tokens.Add(new Token(TokenKind.String, start, value.ToString()));
            }
            return tokens;
        }

        private enum TokenKind { String, Open, Close }
        private sealed record Token(TokenKind Kind, int Start, string Value);
    }
}
