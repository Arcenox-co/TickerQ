using System;

namespace TickerQ.Utilities
{
    /// <summary>
    /// A validated cron expression. Validates on construction — invalid expressions throw immediately.
    /// Supports 6-part format (with seconds): "seconds minutes hours day month day-of-week".
    /// </summary>
    public readonly struct CronExpression : IEquatable<CronExpression>
    {
        /// <summary>
        /// The validated cron expression string.
        /// </summary>
        public string Value { get; }

        private CronExpression(string value) => Value = value;

        /// <summary>
        /// Parses and validates a cron expression. Throws if invalid.
        /// </summary>
        public static CronExpression Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Cron expression cannot be null or empty.", nameof(expression));

            // Config placeholders (e.g. %AppSettings:CronSchedule%) are deferred — skip validation
            if (expression.StartsWith("%") && expression.EndsWith("%"))
                return new CronExpression(expression);

            // Auto-upgrade 5-part (standard) to 6-part (with seconds) by prepending "0 "
            var normalized = NormalizeToSixPart(expression);

            if (CronScheduleCache.Get(normalized) == null)
                throw new ArgumentException($"Invalid cron expression: '{expression}'.", nameof(expression));

            return new CronExpression(normalized);
        }

        /// <summary>
        /// Tries to parse a cron expression. Returns false if invalid.
        /// </summary>
        public static bool TryParse(string expression, out CronExpression result)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                result = default;
                return false;
            }

            if (expression.StartsWith("%") && expression.EndsWith("%"))
            {
                result = new CronExpression(expression);
                return true;
            }

            var normalized = NormalizeToSixPart(expression);
            if (CronScheduleCache.Get(normalized) != null)
            {
                result = new CronExpression(normalized);
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// If the expression has 5 parts (standard cron), prepends "0 " to add seconds.
        /// </summary>
        private static string NormalizeToSixPart(string expression)
        {
            var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 5 ? "0 " + expression.Trim() : expression.Trim();
        }

        public static implicit operator CronExpression(string expression) => Parse(expression);
        public static implicit operator string(CronExpression cron) => cron.Value;

        public override string ToString() => Value;
        public bool Equals(CronExpression other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CronExpression other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;

        public static bool operator ==(CronExpression left, CronExpression right) => left.Equals(right);
        public static bool operator !=(CronExpression left, CronExpression right) => !left.Equals(right);
    }
}
