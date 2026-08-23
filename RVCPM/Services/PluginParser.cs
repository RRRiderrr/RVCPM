using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    internal sealed class PluginParser
    {
        private static readonly Regex NameRegex = new Regex(@"definePlugin\s*\(\s*\{\s*(?:['""])?name(?:['""])?\s*:\s*(['""`])(?<v>.*?)\1", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex DescriptionRegex = new Regex(@"\bdescription\s*:\s*(['""`])(?<v>(?:\\.|(?!\1).)*?)\1", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new Regex(@"\bversion\s*:\s*(['""`])(?<v>(?:\\.|(?!\1).)*?)\1", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex AuthorNameRegex = new Regex(@"authors\s*:\s*\[\s*\{[^\}]*?name\s*:\s*(['""`])(?<v>.*?)\1", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RequiresRestartRegex = new Regex(@"\brequiresRestart\s*:\s*(?<v>true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EnabledDefaultRegex = new Regex(@"\benabledByDefault\s*:\s*(?<v>true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RequiredRegex = new Regex(@"\brequired\s*:\s*(?<v>true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DependenciesRegex = new Regex(@"\bdependencies\s*:\s*\[(?<v>.*?)\]", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex StringLiteralRegex = new Regex(@"(['""`])(?<v>(?:\\.|(?!\1).)*?)\1", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RelativeImportRegex = new Regex(@"(?:from\s*|import\s*\(\s*|import\s+)(['""])(?<v>\.{1,2}/[^'""]+)\1", RegexOptions.Singleline | RegexOptions.Compiled);

        public PluginCandidate ParseCandidate(string sourcePath, string relativePath, bool isFile)
        {
            var entry = isFile ? sourcePath : ResolveEntry(sourcePath);
            if (entry == null || !File.Exists(entry)) return null;

            var text = File.ReadAllText(entry);
            var nameMatch = NameRegex.Match(text);
            if (!nameMatch.Success) return null;

            var candidate = new PluginCandidate
            {
                Name = UnescapeString(nameMatch.Groups["v"].Value),
                Description = MatchValue(DescriptionRegex, text),
                Version = MatchValue(VersionRegex, text),
                Author = MatchValue(AuthorNameRegex, text),
                SourcePath = sourcePath,
                RelativePath = relativePath ?? "",
                IsFile = isFile,
                Extension = Path.GetExtension(entry).ToLowerInvariant(),
                EnabledByDefault = MatchBool(EnabledDefaultRegex, text),
                Required = MatchBool(RequiredRegex, text),
                RequiresRestart = MatchBool(RequiresRestartRegex, text),
                Dependencies = ParseDependencies(text),
                Settings = ParseSettings(text)
            };

            candidate.TargetSuffix = DetectTargetSuffix(isFile ? Path.GetFileNameWithoutExtension(sourcePath) : Path.GetFileName(sourcePath));
            if (candidate.TargetSuffix == "web" || candidate.TargetSuffix == "browser" || candidate.TargetSuffix == "vesktop")
                candidate.Warnings.Add("This plugin targets " + candidate.TargetSuffix + " and is not intended for Discord Desktop.");
            if (candidate.TargetSuffix == "dev")
                candidate.Warnings.Add("This plugin is a dev-target plugin and requires RVCPM Dev Build mode.");

            if (isFile)
            {
                var imports = FindRelativeImports(entry);
                if (imports.Any(x => x.StartsWith("../", StringComparison.Ordinal)))
                    candidate.Warnings.Add("The single-file plugin imports files from a parent directory. Import the whole plugin folder instead.");
            }

            return candidate;
        }

        public static string ResolveEntry(string directory)
        {
            var ts = Path.Combine(directory, "index.ts");
            if (File.Exists(ts)) return ts;
            var tsx = Path.Combine(directory, "index.tsx");
            if (File.Exists(tsx)) return tsx;
            return null;
        }

        public static bool LooksLikePluginFile(string file)
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                var text = File.ReadAllText(file);
                return NameRegex.IsMatch(text);
            }
            catch { return false; }
        }

        public static string DetectTargetSuffix(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName)) return "";
            var parts = baseName.Split('.');
            if (parts.Length < 2) return "";
            var suffix = parts[parts.Length - 1];
            var allowed = new[] { "dev", "web", "browser", "desktop", "discordDesktop", "vesktop" };
            return allowed.FirstOrDefault(x => x.Equals(suffix, StringComparison.OrdinalIgnoreCase)) ?? "";
        }

        public static List<string> FindRelativeImports(string file)
        {
            try
            {
                var text = File.ReadAllText(file);
                return RelativeImportRegex.Matches(text).Cast<Match>().Select(m => m.Groups["v"].Value).Distinct().ToList();
            }
            catch { return new List<string>(); }
        }

        private static string MatchValue(Regex regex, string text)
        {
            var m = regex.Match(text);
            return m.Success ? UnescapeString(m.Groups["v"].Value) : "";
        }

        private static bool MatchBool(Regex regex, string text)
        {
            var m = regex.Match(text);
            bool b;
            return m.Success && bool.TryParse(m.Groups["v"].Value, out b) && b;
        }

        private static List<string> ParseDependencies(string text)
        {
            var result = new List<string>();
            var m = DependenciesRegex.Match(text);
            if (!m.Success) return result;
            foreach (Match sm in StringLiteralRegex.Matches(m.Groups["v"].Value))
                result.Add(UnescapeString(sm.Groups["v"].Value));
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<PluginSettingSchema> ParseSettings(string text)
        {
            var result = new List<PluginSettingSchema>();
            var idx = text.IndexOf("definePluginSettings", StringComparison.Ordinal);
            if (idx < 0) return result;
            var paren = text.IndexOf('(', idx);
            if (paren < 0) return result;
            var brace = FindNextNonSpace(text, paren + 1, '{');
            if (brace < 0) return result;
            var objectText = ExtractBalanced(text, brace, '{', '}');
            if (string.IsNullOrWhiteSpace(objectText)) return result;

            var inner = objectText.Substring(1, objectText.Length - 2);
            foreach (var property in SplitTopLevel(inner, ','))
            {
                var colon = FindTopLevelColon(property);
                if (colon <= 0) continue;
                var key = property.Substring(0, colon).Trim().Trim('\'', '"');
                if (!Regex.IsMatch(key, @"^[A-Za-z_$][\w$]*$")) continue;
                var value = property.Substring(colon + 1).Trim();
                if (!value.StartsWith("{", StringComparison.Ordinal)) continue;

                var setting = ParseSettingObject(key, value);
                if (setting != null) result.Add(setting);
            }
            return result;
        }

        private static PluginSettingSchema ParseSettingObject(string key, string objectText)
        {
            var schema = new PluginSettingSchema { Key = key, DisplayName = Humanize(key) };
            var typeMatch = Regex.Match(objectText, @"\btype\s*:\s*OptionType\.(?<v>[A-Z]+)");
            if (!typeMatch.Success)
            {
                schema.Type = PluginSettingType.Unknown;
                schema.UnsupportedOutsideDiscord = true;
                return schema;
            }

            switch (typeMatch.Groups["v"].Value)
            {
                case "STRING": schema.Type = PluginSettingType.String; break;
                case "NUMBER": schema.Type = PluginSettingType.Number; break;
                case "BOOLEAN": schema.Type = PluginSettingType.Boolean; break;
                case "SELECT": schema.Type = PluginSettingType.Select; break;
                case "SLIDER": schema.Type = PluginSettingType.Slider; break;
                case "BIGINT": schema.Type = PluginSettingType.BigInt; schema.UnsupportedOutsideDiscord = true; break;
                case "COMPONENT": schema.Type = PluginSettingType.Component; schema.UnsupportedOutsideDiscord = true; break;
                case "CUSTOM": schema.Type = PluginSettingType.Custom; break;
                default: schema.Type = PluginSettingType.Unknown; schema.UnsupportedOutsideDiscord = true; break;
            }

            schema.DisplayName = ExtractStringProperty(objectText, "displayName") ?? Humanize(key);
            schema.Description = ExtractStringProperty(objectText, "description") ?? "";
            schema.RestartNeeded = ExtractBoolProperty(objectText, "restartNeeded");
            schema.Multiline = ExtractBoolProperty(objectText, "multiline");
            schema.DefaultValue = ExtractLiteralProperty(objectText, "default");

            if (schema.Type == PluginSettingType.Select)
                schema.Options = ParseSelectOptions(objectText);
            if (schema.Type == PluginSettingType.Slider)
                schema.Markers = ParseNumberArrayProperty(objectText, "markers");

            if (schema.Type == PluginSettingType.Custom && schema.DefaultValue == null)
                schema.UnsupportedOutsideDiscord = true;
            return schema;
        }

        private static List<PluginSettingOption> ParseSelectOptions(string objectText)
        {
            var result = new List<PluginSettingOption>();
            var idx = Regex.Match(objectText, @"\boptions\s*:").Index;
            if (idx <= 0) return result;
            var arrStart = objectText.IndexOf('[', idx);
            if (arrStart < 0) return result;
            var arr = ExtractBalanced(objectText, arrStart, '[', ']');
            if (string.IsNullOrWhiteSpace(arr)) return result;
            var inner = arr.Substring(1, arr.Length - 2);
            foreach (var part in SplitTopLevel(inner, ','))
            {
                var t = part.Trim();
                if (!t.StartsWith("{", StringComparison.Ordinal)) continue;
                var label = ExtractStringProperty(t, "label") ?? "";
                var value = ExtractLiteralProperty(t, "value");
                if (value == null) continue;
                result.Add(new PluginSettingOption { Label = label, Value = value, IsDefault = ExtractBoolProperty(t, "default") });
            }
            return result;
        }

        private static List<double> ParseNumberArrayProperty(string objectText, string name)
        {
            var result = new List<double>();
            var m = Regex.Match(objectText, @"\b" + Regex.Escape(name) + @"\s*:");
            if (!m.Success) return result;
            var start = objectText.IndexOf('[', m.Index + m.Length);
            if (start < 0) return result;
            var arr = ExtractBalanced(objectText, start, '[', ']');
            if (string.IsNullOrWhiteSpace(arr)) return result;
            foreach (var item in arr.Substring(1, arr.Length - 2).Split(','))
            {
                double d;
                if (double.TryParse(item.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) result.Add(d);
            }
            return result;
        }

        private static string ExtractStringProperty(string objectText, string property)
        {
            var m = Regex.Match(objectText, @"\b" + Regex.Escape(property) + @"\s*:\s*(['""`])(?<v>(?:\\.|(?!\1).)*?)\1", RegexOptions.Singleline);
            return m.Success ? UnescapeString(m.Groups["v"].Value) : null;
        }

        private static bool ExtractBoolProperty(string objectText, string property)
        {
            var m = Regex.Match(objectText, @"\b" + Regex.Escape(property) + @"\s*:\s*(?<v>true|false)", RegexOptions.IgnoreCase);
            bool v;
            return m.Success && bool.TryParse(m.Groups["v"].Value, out v) && v;
        }

        private static JToken ExtractLiteralProperty(string objectText, string property)
        {
            var m = Regex.Match(objectText, @"\b" + Regex.Escape(property) + @"\s*:\s*");
            if (!m.Success) return null;
            var i = m.Index + m.Length;
            while (i < objectText.Length && char.IsWhiteSpace(objectText[i])) i++;
            if (i >= objectText.Length) return null;

            if (objectText[i] == '\'' || objectText[i] == '"' || objectText[i] == '`')
            {
                var q = objectText[i++];
                var sb = new StringBuilder();
                var esc = false;
                for (; i < objectText.Length; i++)
                {
                    var ch = objectText[i];
                    if (esc) { sb.Append(ch); esc = false; continue; }
                    if (ch == '\\') { esc = true; sb.Append(ch); continue; }
                    if (ch == q) break;
                    sb.Append(ch);
                }
                return new JValue(UnescapeString(sb.ToString()));
            }

            var tail = objectText.Substring(i);
            var token = Regex.Match(tail, @"^(true|false|null|-?\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase);
            if (!token.Success) return null;
            var raw = token.Value;
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return new JValue(true);
            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return new JValue(false);
            if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) return JValue.CreateNull();
            double d;
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return new JValue(d);
            return null;
        }

        private static int FindNextNonSpace(string text, int start, char expected)
        {
            for (var i = start; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) continue;
                return text[i] == expected ? i : -1;
            }
            return -1;
        }

        private static string ExtractBalanced(string text, int start, char open, char close)
        {
            var depth = 0;
            var quote = '\0';
            var escaped = false;
            var lineComment = false;
            var blockComment = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (lineComment)
                {
                    if (c == '\n') lineComment = false;
                    continue;
                }
                if (blockComment)
                {
                    if (c == '*' && next == '/') { blockComment = false; i++; }
                    continue;
                }
                if (quote != '\0')
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '/' && next == '/') { lineComment = true; i++; continue; }
                if (c == '/' && next == '*') { blockComment = true; i++; continue; }
                if (c == '\'' || c == '"' || c == '`') { quote = c; continue; }
                if (c == open) depth++;
                if (c == close)
                {
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var result = new List<string>();
            var start = 0;
            var curly = 0; var square = 0; var paren = 0;
            var quote = '\0'; var escaped = false; var lineComment = false; var blockComment = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i]; var next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (lineComment) { if (c == '\n') lineComment = false; continue; }
                if (blockComment) { if (c == '*' && next == '/') { blockComment = false; i++; } continue; }
                if (quote != '\0') { if (escaped) { escaped = false; continue; } if (c == '\\') { escaped = true; continue; } if (c == quote) quote = '\0'; continue; }
                if (c == '/' && next == '/') { lineComment = true; i++; continue; }
                if (c == '/' && next == '*') { blockComment = true; i++; continue; }
                if (c == '\'' || c == '"' || c == '`') { quote = c; continue; }
                if (c == '{') curly++; else if (c == '}') curly--; else if (c == '[') square++; else if (c == ']') square--; else if (c == '(') paren++; else if (c == ')') paren--;
                else if (c == separator && curly == 0 && square == 0 && paren == 0)
                {
                    result.Add(text.Substring(start, i - start)); start = i + 1;
                }
            }
            if (start <= text.Length) result.Add(text.Substring(start));
            return result;
        }

        private static int FindTopLevelColon(string text)
        {
            var quote = '\0'; var escaped = false; var curly = 0; var square = 0; var paren = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0') { if (escaped) { escaped = false; continue; } if (c == '\\') { escaped = true; continue; } if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"' || c == '`') { quote = c; continue; }
                if (c == '{') curly++; else if (c == '}') curly--; else if (c == '[') square++; else if (c == ']') square--; else if (c == '(') paren++; else if (c == ')') paren--;
                else if (c == ':' && curly == 0 && square == 0 && paren == 0) return i;
            }
            return -1;
        }

        private static string Humanize(string key)
        {
            return Regex.Replace(key, "([a-z0-9])([A-Z])", "$1 $2").Replace("_", " ");
        }

        private static string UnescapeString(string value)
        {
            return value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\`", "`").Replace("\\\\", "\\");
        }
    }
}
