using System;

namespace TickerQ.Utilities.Models
{
    public class TickerResult<TEntity> where TEntity : class
    {
        internal TickerResult(Exception exception) : this(false)
            => Exception = exception;
        internal TickerResult(TEntity result) : this(true)
            => Result = result;
        internal TickerResult(int affectedRows) : this(true)
            => AffectedRows = affectedRows;
        private TickerResult(bool isSucceeded)
            => IsSucceeded = isSucceeded;

        internal TickerResult(TEntity result, int affectedRows) : this(true)
        {
            Result = result;
            AffectedRows = affectedRows;
        }

        public readonly bool IsSucceeded;
        public readonly int AffectedRows;
        public readonly TEntity Result;
        public readonly Exception Exception;
    }
}
