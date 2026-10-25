using System.Collections.Generic;
using System.Text;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// CSV(RFC 4180)を読む。区切りは `,`。`"` で囲んだ値の中の `,`・改行・`""`(`"` そのもの)を扱う。
/// 改行は CRLF と LF のどちらでもよい。
/// </summary>
public static class CsvParser
{
    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var quoted = false;
        var quoteLine = 0;
        // 直前の文字が値の区切り(値の先頭)か。引用符は値の先頭でだけ特別な意味を持つ
        var atFieldStart = true;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when atFieldStart:
                    quoted = true;
                    quoteLine = line;
                    atFieldStart = false;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    atFieldStart = true;
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(new CsvRecord(recordLine, fields));
                    fields = new List<string>();
                    line++;
                    recordLine = line;
                    atFieldStart = true;
                    break;
                default:
                    field.Append(c);
                    atFieldStart = false;
                    break;
            }
        }

        if (quoted)
        {
            throw new CsvFormatException(quoteLine, "引用符(\")が閉じていません。");
        }

        // 最後の行に改行がない場合
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new CsvRecord(recordLine, fields));
        }

        return records;
    }
}
