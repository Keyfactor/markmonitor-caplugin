namespace TestConsole.Helpers;

using System.Globalization;

public abstract class TitleCaseConverter
{
    public static string ToTitleCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Get the current culture's TextInfo and convert to title case
        var textInfo = CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(input.ToLower());
    }
}