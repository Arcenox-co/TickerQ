using System;
using System.Collections.Generic;

namespace TickerQ.SourceGenerator
{
    public static class CronValidator
    {
        // C# 8 doesn’t support target‑typed array initializers or file‑scoped namespaces,
        // so we use the classic form:
        private static readonly int[] MinValues = new int[] { 0, 0, 1, 1, 0 };
        private static readonly int[] MaxValues = new int[] { 59, 23, 31, 12, 6 };

        public static bool IsValidCronExpression(string expression)
        {
            if (expression == null) return false;

            var parts = expression.Split(' ');
            if (parts.Length != MinValues.Length) 
                return false; // must have exactly 5 parts

            for (int i = 0; i < MinValues.Length; i++)
            {
                if (!ValidatePart(parts[i], MinValues[i], MaxValues[i]))
                    return false;
            }

            return true;
        }

        private static bool ValidatePart(string part, int min, int max)
        {
            if (part == "*") 
                return true; // wildcard

            ReadOnlySpan<char> span = part.AsSpan();
            var values = new HashSet<int>();
            int i = 0;

            while (i < span.Length)
            {
                int num1;
                int num2 = -1;
                int step = 1;

                // first number or '*'
                if (span[i] == '*')
                {
                    num1 = min;
                    num2 = max;
                    i++;
                }
                else if (char.IsDigit(span[i]))
                {
                    num1 = ReadNumber(span, ref i);
                }
                else
                {
                    return false;
                }

                // optional range “‑”
                if (i < span.Length && span[i] == '-')
                {
                    i++;
                    if (i < span.Length && char.IsDigit(span[i]))
                        num2 = ReadNumber(span, ref i);
                    else
                        return false;
                }

                // optional step “/”
                if (i < span.Length && span[i] == '/')
                {
                    i++;
                    if (i < span.Length && char.IsDigit(span[i]))
                        step = ReadNumber(span, ref i);
                    else
                        return false;
                }

                if (num2 == -1) 
                    num2 = num1;

                // validate bounds
                if (num1 < min || num2 > max || num1 > num2 || step < 1 || step > max)
                    return false;

                // collect values
                for (int v = num1; v <= num2; v += step)
                    values.Add(v);

                // skip comma
                if (i < span.Length && span[i] == ',')
                    i++;
            }

            return values.Count > 0;
        }

        private static int ReadNumber(ReadOnlySpan<char> span, ref int index)
        {
            int num = 0;
            while (index < span.Length && char.IsDigit(span[index]))
            {
                num = num * 10 + (span[index] - '0');
                index++;
            }
            return num;
        }
    }
}