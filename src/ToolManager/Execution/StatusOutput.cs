namespace ToolManager.Execution;

using System.Globalization;
using ToolManager.Models;

internal sealed class StatusOutput(CustomStatusSource source)
{
    internal const string Prefix = "##TM[STATUS]:";
    private const int MaximumLineCharacters = 4096;
    private const int MaximumTextElements = 120;
    private readonly StringBuilder line = new();

    public void Append(string text, RunRecord record)
    {
        if (source == CustomStatusSource.None)
        {
            return;
        }

        foreach (var character in text)
        {
            if (character == '\n')
            {
                this.Publish(record);
            }
            else if (this.line.Length < MaximumLineCharacters)
            {
                this.line.Append(character);
            }
        }
    }

    public void Complete(RunRecord record) => this.Publish(record);

    private static string Sanitize(string text)
    {
        var clean = new StringBuilder();
        bool space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                space = clean.Length > 0;
            }
            else if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                if (space)
                {
                    clean.Append(' ');
                    space = false;
                }

                clean.Append(rune.ToString());
            }
        }

        var result = clean.ToString();
        var elements = StringInfo.ParseCombiningCharacters(result);
        return elements.Length > MaximumTextElements ? result[..elements[MaximumTextElements]] : result;
    }

    private void Publish(RunRecord record)
    {
        var text = this.line.ToString();
        this.line.Clear();
        if (source == CustomStatusSource.ExplicitStatusLine)
        {
            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return;
            }

            record.CustomStatus = Sanitize(text[Prefix.Length..]);
        }
        else if (source == CustomStatusSource.LastOutputLine)
        {
            var status = Sanitize(text);
            if (status.Length > 0)
            {
                record.CustomStatus = status;
            }
        }
    }
}
