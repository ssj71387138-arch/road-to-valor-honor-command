using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoadToValor.Core
{
    /// <summary>
    /// Small CSV reader for designer-editable tables (Excel / Google Sheets export friendly).
    /// Lines starting with '#' are comments. The first non-comment line is the header.
    /// </summary>
    public static class Csv
    {
        public static List<Dictionary<string, string>> Parse(string text)
        {
            var rows = new List<Dictionary<string, string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            string[] header = null;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.TrimStart('﻿');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                List<string> cells = SplitLine(line);
                if (header == null)
                {
                    header = cells.ToArray();
                    for (int i = 0; i < header.Length; i++) header[i] = header[i].Trim();
                    continue;
                }
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < header.Length; i++) row[header[i]] = i < cells.Count ? cells[i].Trim() : "";
                rows.Add(row);
            }
            return rows;
        }

        private static List<string> SplitLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { cells.Add(sb.ToString()); sb.Length = 0; }
                    else sb.Append(c);
                }
            }
            cells.Add(sb.ToString());
            return cells;
        }

        public static float F(Dictionary<string, string> row, string key, float def = 0f)
        {
            if (row.TryGetValue(key, out var s) && s.Length > 0 &&
                float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            return def;
        }

        public static int I(Dictionary<string, string> row, string key, int def = 0)
        {
            if (row.TryGetValue(key, out var s) && s.Length > 0 &&
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            return def;
        }

        public static string S(Dictionary<string, string> row, string key, string def = "")
        {
            return row.TryGetValue(key, out var s) ? s : def;
        }
    }
}
