using System;
using System.Collections.Generic;
using System.Linq;

namespace TickerQ.Utilities
{
    public static class TickerCronExpressionHelper
    {
        private static readonly Dictionary<string, string> DaysMap = new Dictionary<string, string>()
        {
            { "0", "Sun" },
            { "1", "Mon" },
            { "2", "Tue" },
            { "3", "Wed" },
            { "4", "Thu" },
            { "5", "Fri" },
            { "6", "Sat" },
            { "7", "Sun" },
        };

        /// <summary>
        /// Transforms cron expression into a human-readable format.
        /// </summary>
        /// <param name="cronExpression"></param>
        /// <param name="timeZone"></param>
        /// <returns></returns>
        public static string ToHumanReadable(this string cronExpression, TimeZoneInfo timeZone = null)
        {
            string cronosCompatible = ToCronosExpression(cronExpression);
            string[] parts = cronosCompatible.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
                return "Invalid cron expression";

            string minute = parts[0];
            string hour = parts[1];
            string dayOfMonth = parts[2];
            string month = parts[3];
            string dayOfWeek = parts[4];

            string time = FormatTime(hour, minute, timeZone);

            // Handle every minute
            if (minute.StartsWith("*/") && hour == "*")
            {
                return $"Every {minute.Replace("*/", "")} minutes";
            }

            // Handle specific hours
            if (hour.StartsWith("*/") && minute == "0")
            {
                string interval = hour.Replace("*/", "");
                if (dayOfWeek != "*" && !IsAllDays(dayOfWeek))
                    return $"Every {interval} hours on {JoinDays(dayOfWeek)}";

                return $"Every {interval} hours";
            }

            // Handle specific time of day
            if ((dayOfWeek == "*" || dayOfWeek == "?") && dayOfMonth == "*")
            {
                return $"Every day at {time}";
            }

            // Handle specific days of the week
            if (dayOfWeek != "*" && dayOfWeek != "?" && dayOfMonth == "*")
            {
                return $"Every {JoinDays(dayOfWeek)} at {time}";
            }

            // Handle specific day of month
            if (dayOfMonth != "*" && (dayOfWeek == "*" || dayOfWeek == "?"))
            {
                string monthDesc = month.StartsWith("*/") ? $"Every {month.Replace("*/", "")} months" : "Every month";
                return $"{monthDesc} on the {Ordinal(dayOfMonth)} at {time}";
            }

            // Handle specific day of month and day of week combinations
            if (dayOfMonth != "*" && dayOfWeek != "*")
            {
                return $"On {Ordinal(dayOfMonth)} and {JoinDays(dayOfWeek)} at {time}";
            }

            return cronExpression;
        }

        #region [Private Methods]

        /// <summary>
        /// Converts a cron expression to a Cronos-compatible format.
        /// </summary>
        /// <param name="cron"></param>
        /// <returns></returns>
        private static string ToCronosExpression(string cron)
        {
            string[] parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 6 || parts.Length == 7)
            {
                string minute = parts[1];
                string hour = parts[2];
                string day = parts[3];
                string month = parts[4];
                string dayOfWeek = parts[5] == "?" ? "*" : parts[5];

                return $"{minute} {hour} {day} {month} {dayOfWeek}";
            }
            return cron;
        }

        /// <summary>
        /// Formats the time based on the hour and minute, adjusting for the specified time zone.
        /// </summary>
        /// <param name="hour"></param>
        /// <param name="minute"></param>
        /// <param name="timeZone"></param>
        /// <returns></returns>
        private static string FormatTime(string hour, string minute, TimeZoneInfo timeZone)
        {
            int h = int.TryParse(hour.Replace("*/", "0"), out int hParsed) ? hParsed : 0;
            int m = int.TryParse(minute.Replace("*/", "0"), out int mParsed) ? mParsed : 0;
            DateTime utcTime = new DateTime(2000, 1, 1, h, m, 0, DateTimeKind.Utc);
            DateTime localTime = timeZone != null ? TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone) : utcTime;
            return localTime.ToString("HH:mm");
        }

        /// <summary>
        /// Converts a number to its ordinal representation (1st, 2nd, 3rd, etc.).
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        private static string Ordinal(string number)
        {
            if (number.Contains("/"))
                number = number.Split('/')[0];

            if (!int.TryParse(number, out int n))
                return number;

            return n switch
            {
                int _ when (n % 100 >= 11 && n % 100 <= 13) => $"{n}th",
                int _ when n % 10 == 1 => $"{n}st",
                int _ when n % 10 == 2 => $"{n}nd",
                int _ when n % 10 == 3 => $"{n}rd",
                _ => $"{n}th",
            };
        }

        /// <summary>
        /// Checks if the provided day of the week string represents all days of the week.
        /// </summary>
        /// <param name="dayOfWeek"></param>
        /// <returns></returns>
        private static bool IsAllDays(string dayOfWeek)
        {
            HashSet<string> all = new HashSet<string> { "0", "1", "2", "3", "4", "5", "6" };
            HashSet<string> input = dayOfWeek.Split(',').ToHashSet();
            return input.SetEquals(all);
        }

        /// <summary>
        /// Joins the days of the week into a human-readable format.
        /// </summary>
        /// <param name="dayOfWeek"></param>
        /// <returns></returns>
        private static string JoinDays(string dayOfWeek)
        {
            List<string> days = dayOfWeek
                .Split(',')
                .Select(d => DaysMap.ContainsKey(d) ? DaysMap[d] : $"Day {d}")
                .Distinct()
                .ToList();

            if (days.Count > 2)
            {
                return string.Join(", ", days.Take(days.Count - 1)) + " and " + days.Last();
            }
            else if (days.Count == 2)
            {
                return days[0] + " and " + days[1];
            }
            else if (days.Count == 1)
            {
                return days[0];
            }
            else
            {
                return "days";
            }
        }

        #endregion [Private Methods]
    }
}
