using System.Text.RegularExpressions;

namespace RemotenetTrace
{
    internal class CSharpStringsConverter
    {
        public string ConvertToFormat(string input)
        {
            Regex entireInputIsStringInterpo = new Regex("^\\$\".*\"$");

            if (entireInputIsStringInterpo.IsMatch(input))
                return ConvertToFormatInner(input);

            Regex stringInterpo = new Regex("\\$\".*\"");
            string output = input;
            var matches = stringInterpo.Matches(input);
            foreach (Match match in matches.OrderByDescending(mtch => mtch.Index))
            {
                var val = match.Value;
                var replacement = ConvertToFormatInner(val);

                string prefix = match.Index > 0 ? output.Substring(0, match.Index - 1) : "";
                string suffix = output.Substring(match.Index + match.Length);
                output = prefix +
                         replacement +
                         suffix;
            }

            return output;
        }
        private string ConvertToFormatInner(string stringInterpolation)
        {
            if (stringInterpolation[0] != '$')
            {
                throw new Exception("Expected a $ at index 0");
            }

            if (stringInterpolation[1] != '"' || stringInterpolation[^1] != '"')
            {
                throw new Exception("Expected the string to start with '$\"' and end with '\"'");
            }

            // Get rid of the $
            string output = stringInterpolation[1..];

            Regex paranthesisFinder = new Regex(@"\{[^}]*\}");
            var matches = paranthesisFinder.Matches(stringInterpolation);

            // We're going to go over the matches from the end of the string towards the start
            // this way, changes that we apply to the string do not change the indexes of the matches that
            // we did not process yet
            List<Match> orderedMatches = matches.OrderByDescending(mtch => mtch.Index).ToList();
            int index = orderedMatches.Count - 1;
            List<string> variablesToFormat = new List<string>();
            foreach (Match match in orderedMatches)
            {
                string val = match.Value;
                string replacement = "{" + index + "}";
                string variable = val.TrimStart('{').TrimEnd('}');

                bool hasFormat = val.Contains(":");
                if (hasFormat)
                {
                    string format = val.Substring(val.IndexOf(":") + 1).TrimEnd('}');
                    // remove last '}:
                    replacement = $"{{{index}:{format}}}";
                    variable = variable.Substring(0, variable.IndexOf(':'));
                }
                index--;

                string prefix = match.Index > 0 ? output.Substring(0, match.Index - 1) : "";
                string suffix = output.Substring(match.Index + match.Length - 1);
                output = prefix +
                         replacement +
                         suffix;
                variablesToFormat.Insert(0, variable);
            }

            return variablesToFormat.Any() ? $"String.Format({output}, {string.Join(", ", variablesToFormat)})" : output;
        }
    }
}
