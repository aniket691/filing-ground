namespace FinQuery.Application.Documents;

public static class MetricNames
{
    public static string Normalize(string name)
    {
        var cleaned = name.Trim().ToLowerInvariant();
        var output = new System.Text.StringBuilder();
        var previousUnderscore = false;
        foreach (var character in cleaned)
        {
            if (char.IsLetterOrDigit(character))
            {
                output.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore && output.Length > 0)
            {
                output.Append('_');
                previousUnderscore = true;
            }
            else
            {
                previousUnderscore = true;
            }
        }

        return output.ToString().Trim('_');
    }
}
