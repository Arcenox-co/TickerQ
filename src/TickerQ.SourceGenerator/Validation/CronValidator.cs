using System;
using System.Collections.Generic;

namespace TickerQ.SourceGenerator.Validation
{
    public static class CronValidator
    {
        // Format with seconds (6 parts): seconds, minutes, hours, day, month, day-of-week
        private static readonly int[] MinValuesArray6Part = { 0, 0, 0, 1, 1, 0 };
        private static readonly int[] MaxValuesArray6Part = { 59, 59, 23, 31, 12, 6 };
        
        // Format without seconds (5 parts): minutes, hours, day, month, day-of-week
        private static readonly int[] MinValuesArray5Part = { 0, 0, 1, 1, 0 };
        private static readonly int[] MaxValuesArray5Part = { 59, 23, 31, 12, 6 };
        
        // Performance constants
        private const int MaxPartsCount = 6;

        public static bool IsValidCronExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return false;

            // Use Span for efficient string splitting without allocations
            var expressionSpan = expression.AsSpan();
            var parts = new string[MaxPartsCount];
            var partCount = SplitIntoSpan(expressionSpan, parts);
            
            // Support both 5-part (without seconds) and 6-part (with seconds) cron expressions
            if (partCount != 5 && partCount != 6) 
                return false;

            // Select appropriate min/max values based on part count
            ReadOnlySpan<int> minSpan = partCount == 6 
                ? MinValuesArray6Part 
                : MinValuesArray5Part;
            
            ReadOnlySpan<int> maxSpan = partCount == 6 
                ? MaxValuesArray6Part 
                : MaxValuesArray5Part;
            
            for (int i = 0; i < partCount; i++)
            {
                if (!ValidatePart(parts[i], minSpan[i], maxSpan[i]))
                    return false;
            }
            
            return true;
        }

        /// <summary>
        /// Efficiently splits a ReadOnlySpan into parts without allocating intermediate arrays.
        /// </summary>
        private static int SplitIntoSpan(ReadOnlySpan<char> input, Span<string> output)
        {
            var partIndex = 0;
            var start = 0;
            
            for (int i = 0; i < input.Length && partIndex < output.Length; i++)
            {
                if (input[i] == ' ')
                {
                    if (i > start) // Avoid empty parts
                    {
                        output[partIndex++] = input.Slice(start, i - start).ToString();
                    }
                    start = i + 1;
                }
            }
            
            // Add the last part if there's remaining content
            if (start < input.Length && partIndex < output.Length)
            {
                output[partIndex++] = input.Slice(start).ToString();
            }
            
            return partIndex;
        }

        private static bool ValidatePart(string part, int min, int max)
        {
            // Fast path for wildcard
            if (part.Length == 1 && part[0] == '*') 
                return true; // wildcard

            var span = part.AsSpan();
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
                else if (span[i] >= '0' && span[i] <= '9')
                {
                    num1 = ReadNumber(span, ref i);
                    if (num1 == -1) return false; // Invalid number
                }
                else
                {
                    return false;
                }

                // optional range "‑"
                if (i < span.Length && span[i] == '-')
                {
                    i++;
                    if (i < span.Length && span[i] >= '0' && span[i] <= '9')
                    {
                        num2 = ReadNumber(span, ref i);
                        if (num2 == -1) return false; // Invalid number
                    }
                    else
                        return false;
                }

                // optional step "/"
                if (i < span.Length && span[i] == '/')
                {
                    i++;
                    if (i < span.Length && span[i] >= '0' && span[i] <= '9')
                    {
                        step = ReadNumber(span, ref i);
                        if (step == -1) return false; // Invalid number
                    }
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

        /// <summary>
        /// Efficiently reads a number from a ReadOnlySpan with bounds checking.
        /// Optimized to avoid char.IsDigit() calls by using direct ASCII range checks.
        /// </summary>
        private static int ReadNumber(ReadOnlySpan<char> span, ref int index)
        {
            int num = 0;
            var startIndex = index;
            
            // Use direct ASCII range check instead of char.IsDigit() for better performance
            while (index < span.Length && span[index] >= '0' && span[index] <= '9')
            {
                var digit = span[index] - '0';
                
                // Overflow protection for cron values (max expected is 59)
                if (num > (int.MaxValue - digit) / 10)
                    break;
                    
                num = num * 10 + digit;
                index++;
            }
            
            // Return -1 if no digits were read (invalid number)
            return index > startIndex ? num : -1;
        }
    }
}