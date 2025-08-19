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
            // Handle null or empty input
            if (string.IsNullOrWhiteSpace(cronExpression))
                return "Invalid cron expression";

            string cronosCompatible = ToCronosExpression(cronExpression);
            string[] parts = cronosCompatible.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
                return "Invalid cron expression";

            string minute = parts[0];
            string hour = parts[1];
            string dayOfMonth = parts[2];
            string month = parts[3];
            string dayOfWeek = parts[4];

            // Handle every minute (* * * * *)
            if (minute == "*" && hour == "*" && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                return "Every minute";
            }

            // Handle every X minutes (*/X * * * *)
            if (minute.StartsWith("*/") && hour == "*" && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                string interval = minute.Replace("*/", "");
                if (int.TryParse(interval, out int intervalValue) && intervalValue > 0)
                {
                    return $"Every {interval} minutes";
                }
            }

            // Handle every hour (* 0 * * *)
            if (minute == "0" && hour == "*" && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                return "Every hour";
            }

            // Handle every X hours (0 */X * * *)
            if (minute == "0" && hour.StartsWith("*/") && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                string interval = hour.Replace("*/", "");
                if (int.TryParse(interval, out int intervalValue) && intervalValue > 0)
                {
                    return $"Every {interval} hours";
                }
            }

            // Handle specific minute every hour (X * * * *)
            if (minute != "*" && !minute.StartsWith("*/") && hour == "*" && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                if (int.TryParse(minute, out int minuteValue) && minuteValue >= 0 && minuteValue <= 59)
                {
                    return $"Every hour at minute {minuteValue}";
                }
            }

            // Handle specific hour every minute (* X * * *)
            if (minute == "*" && hour != "*" && !hour.StartsWith("*/") && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                if (int.TryParse(hour, out int hourValue) && hourValue >= 0 && hourValue <= 23)
                {
                    return $"Every minute of the {hourValue}th hour";
                }
            }

            // Handle specific day of month every minute (* * X * *)
            if (minute == "*" && hour == "*" && dayOfMonth != "*" && !dayOfMonth.StartsWith("*/") && month == "*" && dayOfWeek == "*")
            {
                return $"Every minute of every hour on the {FormatDayOfMonth(dayOfMonth)}";
            }

            // Handle specific month every minute (* * * X *)
            if (minute == "*" && hour == "*" && dayOfMonth == "*" && month != "*" && !month.StartsWith("*/") && dayOfWeek == "*")
            {
                return $"Every minute of every hour of every day in {FormatMonth(month)}";
            }

            // Handle specific day of week every minute (* * * * X)
            if (minute == "*" && hour == "*" && dayOfMonth == "*" && month == "*" && dayOfWeek != "*" && dayOfWeek != "?")
            {
                return $"Every minute of every hour of every day on {JoinDays(dayOfWeek)}";
            }

            // Handle specific time every day (X Y * * *)
            if (minute != "*" && !minute.StartsWith("*/") && hour != "*" && !hour.StartsWith("*/") && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every day at {time}";
                }
            }

            // Handle specific time of day (X Y * * *)
            if (minute != "*" && hour != "*" && dayOfMonth == "*" && month == "*" && dayOfWeek == "*")
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every day at {time}";
                }
            }

            // Handle specific days of the week (* * * * X)
            if (minute != "*" && hour != "*" && dayOfMonth == "*" && month == "*" && dayOfWeek != "*" && dayOfWeek != "?")
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every {JoinDays(dayOfWeek)} at {time}";
                }
            }

            // Handle specific day of month (* * X * *)
            if (minute != "*" && hour != "*" && dayOfMonth != "*" && month == "*" && (dayOfWeek == "*" || dayOfWeek == "?"))
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    string monthDesc = month.StartsWith("*/") ? $"Every {month.Replace("*/", "")} months" : "Every month";
                    return $"{monthDesc} on the {FormatDayOfMonth(dayOfMonth)} at {time}";
                }
            }

            // Handle specific day of month and day of week combinations
            if (minute != "*" && hour != "*" && dayOfMonth != "*" && month == "*" && dayOfWeek != "*" && dayOfWeek != "?")
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"On {FormatDayOfMonth(dayOfMonth)} and {JoinDays(dayOfWeek)} at {time}";
                }
            }

            // Handle monthly patterns (* * * X *)
            if (minute != "*" && hour != "*" && dayOfMonth != "*" && month.StartsWith("*/") && (dayOfWeek == "*" || dayOfWeek == "?"))
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    string interval = month.Replace("*/", "");
                    if (int.TryParse(interval, out int intervalValue) && intervalValue > 0)
                    {
                        return $"Every {interval} months on the {FormatDayOfMonth(dayOfMonth)} at {time}";
                    }
                }
            }

            // Handle yearly patterns (* * * X *)
            if (minute != "*" && hour != "*" && dayOfMonth != "*" && month != "*" && month != "*/" && (dayOfWeek == "*" || dayOfWeek == "?"))
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every {FormatMonth(month)} on the {FormatDayOfMonth(dayOfMonth)} at {time}";
                }
            }

            // Handle special cases with L (last day of month)
            if (dayOfMonth.Contains("L"))
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every month on the last day at {time}";
                }
            }

            // Handle special cases with W (nearest weekday)
            if (dayOfMonth.Contains("W"))
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every month on the nearest weekday at {time}";
                }
            }

            // Handle special cases with # (nth day of week)
            if (dayOfWeek.Contains("#"))
            {
                string time = FormatTime(hour, minute, timeZone);
                if (time != "Invalid time")
                {
                    return $"Every month on the {FormatNthDayOfWeek(dayOfWeek)} at {time}";
                }
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
            try
            {
                if (string.IsNullOrWhiteSpace(cron))
                {
                    return cron;
                }

                string[] parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                // NCrontab supports only 5 parts: minute hour day month dayofweek
                if (parts.Length == 5)
                {
                    return cron;
                }
                
                // If 6 or 7 parts (with seconds), convert to 5 parts by removing seconds
                if (parts.Length == 6 || parts.Length == 7)
                {
                    string minute = parts[1];
                    string hour = parts[2];
                    string day = parts[3];
                    string month = parts[4];
                    string dayOfWeek = parts[5] == "?" ? "*" : parts[5];

                    return $"{minute} {hour} {day} {month} {dayOfWeek}";
                }

                // Handle malformed expressions with wrong number of parts
                if (parts.Length < 5)
                {
                    // Pad with wildcards to make it 5 parts
                    var paddedParts = new List<string>(parts);
                    while (paddedParts.Count < 5)
                    {
                        paddedParts.Add("*");
                    }
                    return string.Join(" ", paddedParts);
                }
                
                return cron;
            }
            catch
            {
                return cron;
            }
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
            try
            {
                // Handle wildcards and intervals
                string hourValue = hour.Replace("*/", "0").Replace("*", "0");
                string minuteValue = minute.Replace("*/", "0").Replace("*", "0");

                // Handle ranges (e.g., "1-5")
                if (hourValue.Contains("-"))
                {
                    string[] range = hourValue.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0], out int startHour))
                    {
                        hourValue = startHour.ToString();
                    }
                }

                if (minuteValue.Contains("-"))
                {
                    string[] range = minuteValue.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0], out int startMinute))
                    {
                        minuteValue = startMinute.ToString();
                    }
                }

                // Handle lists (e.g., "1,3,5")
                if (hourValue.Contains(","))
                {
                    string[] hours = hourValue.Split(',');
                    if (hours.Length > 0 && int.TryParse(hours[0], out int firstHour))
                    {
                        hourValue = firstHour.ToString();
                    }
                }

                if (minuteValue.Contains(","))
                {
                    string[] minutes = minuteValue.Split(',');
                    if (minutes.Length > 0 && int.TryParse(minutes[0], out int firstMinute))
                    {
                        minuteValue = firstMinute.ToString();
                    }
                }

                // Parse hour and minute
                if (!int.TryParse(hourValue, out int h) || !int.TryParse(minuteValue, out int m))
                {
                    return "Invalid time";
                }

                // Validate ranges
                if (h < 0 || h > 23 || m < 0 || m > 59)
                {
                    return "Invalid time";
                }

                DateTime utcTime = new DateTime(2000, 1, 1, h, m, 0, DateTimeKind.Utc);
                DateTime localTime = timeZone != null ? TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone) : utcTime;
                return localTime.ToString("HH:mm");
            }
            catch
            {
                return "Invalid time";
            }
        }

        /// <summary>
        /// Formats the month expression into a human-readable format.
        /// </summary>
        /// <param name="month"></param>
        /// <returns></returns>
        private static string FormatMonth(string month)
        {
            try
            {
                // Handle special characters
                if (month.Contains("?") || month.Contains("*"))
                {
                    return "every month";
                }

                // Handle intervals (e.g., "*/2")
                if (month.StartsWith("*/"))
                {
                    string interval = month.Replace("*/", "");
                    if (int.TryParse(interval, out int intervalValue) && intervalValue > 0 && intervalValue <= 12)
                    {
                        return $"Every {intervalValue} months";
                    }
                }

                // Handle ranges (e.g., "1-6")
                if (month.Contains("-"))
                {
                    string[] range = month.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                    {
                        if (start >= 1 && start <= 12 && end >= 1 && end <= 12 && start <= end)
                        {
                            return $"months {start} through {end}";
                        }
                    }
                }

                // Handle lists (e.g., "1,3,5")
                if (month.Contains(","))
                {
                    string[] months = month.Split(',');
                    var validMonths = new List<string>();
                    
                    foreach (string m in months)
                    {
                        if (int.TryParse(m, out int monthValue) && monthValue >= 1 && monthValue <= 12)
                        {
                            validMonths.Add(monthValue.ToString());
                        }
                    }

                    if (validMonths.Count == 1)
                    {
                        return $"month {validMonths[0]}";
                    }
                    else if (validMonths.Count == 2)
                    {
                        return $"months {validMonths[0]} and {validMonths[1]}";
                    }
                    else if (validMonths.Count > 2)
                    {
                        return $"months {string.Join(", ", validMonths.Take(validMonths.Count - 1))} and {validMonths.Last()}";
                    }
                }

                // Handle single month
                if (int.TryParse(month, out int singleMonth) && singleMonth >= 1 && singleMonth <= 12)
                {
                    return $"month {month}";
                }

                return month;
            }
            catch
            {
                return month;
            }
        }

        /// <summary>
        /// Formats the day of month expression into a human-readable format.
        /// </summary>
        /// <param name="dayOfMonth"></param>
        /// <returns></returns>
        private static string FormatDayOfMonth(string dayOfMonth)
        {
            try
            {
                // Handle special characters
                if (dayOfMonth.Contains("L"))
                {
                    return "last day";
                }

                if (dayOfMonth.Contains("W"))
                {
                    return "nearest weekday";
                }

                if (dayOfMonth.Contains("?"))
                {
                    return "any day";
                }

                // Handle ranges (e.g., "1-5")
                if (dayOfMonth.Contains("-"))
                {
                    string[] range = dayOfMonth.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                    {
                        if (start >= 1 && start <= 31 && end >= 1 && end <= 31 && start <= end)
                        {
                            return $"days {start} through {end}";
                        }
                    }
                }

                // Handle lists (e.g., "1,3,5")
                if (dayOfMonth.Contains(","))
                {
                    string[] days = dayOfMonth.Split(',');
                    var validDays = new List<string>();
                    
                    foreach (string day in days)
                    {
                        if (int.TryParse(day, out int dayValue) && dayValue >= 1 && dayValue <= 31)
                        {
                            validDays.Add(Ordinal(day));
                        }
                    }

                    if (validDays.Count == 1)
                    {
                        return validDays[0];
                    }
                    else if (validDays.Count == 2)
                    {
                        return $"{validDays[0]} and {validDays[1]}";
                    }
                    else if (validDays.Count > 2)
                    {
                        return string.Join(", ", validDays.Take(validDays.Count - 1)) + " and " + validDays.Last();
                    }
                }

                // Handle single day
                if (int.TryParse(dayOfMonth, out int singleDay) && singleDay >= 1 && singleDay <= 31)
                {
                    return Ordinal(dayOfMonth);
                }

                // Handle intervals (e.g., "*/5")
                if (dayOfMonth.StartsWith("*/"))
                {
                    string interval = dayOfMonth.Replace("*/", "");
                    if (int.TryParse(interval, out int intervalValue) && intervalValue > 0 && intervalValue <= 31)
                    {
                        return $"every {intervalValue} days";
                    }
                }

                return $"day {dayOfMonth}";
            }
            catch
            {
                return $"day {dayOfMonth}";
            }
        }

        /// <summary>
        /// Joins the days of the week into a human-readable format.
        /// </summary>
        /// <param name="dayOfWeek"></param>
        /// <returns></returns>
        private static string JoinDays(string dayOfWeek)
        {
            try
            {
                // Handle special characters
                if (dayOfWeek.Contains("?") || dayOfWeek.Contains("*"))
                {
                    return "any day";
                }

                // Handle ranges (e.g., "1-5")
                if (dayOfWeek.Contains("-"))
                {
                    string[] range = dayOfWeek.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                    {
                        if (start >= 0 && start <= 7 && end >= 0 && end <= 7 && start <= end)
                        {
                            var days = new List<string>();
                            for (int i = start; i <= end; i++)
                            {
                                string dayKey = i.ToString();
                                if (DaysMap.ContainsKey(dayKey))
                                {
                                    days.Add(DaysMap[dayKey]);
                                }
                            }
                            return string.Join(", ", days);
                        }
                    }
                }

                // Handle lists (e.g., "1,3,5")
                if (dayOfWeek.Contains(","))
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

                // Handle intervals (e.g., "*/2")
                if (dayOfWeek.StartsWith("*/"))
                {
                    string interval = dayOfWeek.Replace("*/", "");
                    if (int.TryParse(interval, out int intervalValue) && intervalValue > 0 && intervalValue <= 7)
                    {
                        return $"every {intervalValue} days";
                    }
                }

                // Handle single day
                if (DaysMap.ContainsKey(dayOfWeek))
                {
                    return DaysMap[dayOfWeek];
                }

                // Handle numeric day
                if (int.TryParse(dayOfWeek, out int dayValue) && dayValue >= 0 && dayValue <= 7)
                {
                    string dayKey = dayValue.ToString();
                    if (DaysMap.ContainsKey(dayKey))
                    {
                        return DaysMap[dayKey];
                    }
                }

                return $"Day {dayOfWeek}";
            }
            catch
            {
                return $"Day {dayOfWeek}";
            }
        }

        /// <summary>
        /// Formats the nth day of week expression into a human-readable format.
        /// </summary>
        /// <param name="dayOfWeek"></param>
        /// <returns></returns>
        private static string FormatNthDayOfWeek(string dayOfWeek)
        {
            try
            {
                if (dayOfWeek.Contains("#"))
                {
                    string[] parts = dayOfWeek.Split('#');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int nth) && int.TryParse(parts[1], out int dayOfWeekValue))
                    {
                        if (nth >= 1 && nth <= 5 && dayOfWeekValue >= 0 && dayOfWeekValue <= 7)
                        {
                            string dayOfWeekDesc = JoinDays(dayOfWeekValue.ToString());
                            return $"{Ordinal(nth.ToString())} {dayOfWeekDesc}";
                        }
                    }
                }

                // Handle other special patterns
                if (dayOfWeek.Contains("L"))
                {
                    return "last day of week";
                }

                if (dayOfWeek.Contains("?"))
                {
                    return "any day of week";
                }

                return dayOfWeek;
            }
            catch
            {
                return dayOfWeek;
            }
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

        #endregion [Private Methods]
    }
}

