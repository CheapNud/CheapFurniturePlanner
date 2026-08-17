namespace CheapFurniturePlanner.Ui;

// UX-2 Task 2: the one humanizer StatusChip (and any other status label renderer) calls -
// turns enum-ish tokens into short readable labels without a general-purpose "humanizer"
// dependency. Two shapes seen in this codebase's enums: PascalCase ("InProgress") and
// underscore-shouty ("RMA_Created"); either way, only the first word keeps its case.
public static class StatusText
{
    public static string Humanize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var words = value.Contains('_') ? SplitOnUnderscore(value) : SplitOnPascalCase(value);
        if (words.Count == 0)
        {
            return value;
        }

        return string.Join(' ', words.Select((word, index) => index == 0 ? word : word.ToLowerInvariant()));
    }

    private static List<string> SplitOnUnderscore(string value) =>
        value.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static List<string> SplitOnPascalCase(string value)
    {
        var words = new List<string>();
        var start = 0;
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
            {
                words.Add(value[start..i]);
                start = i;
            }
        }
        words.Add(value[start..]);
        return words;
    }
}
