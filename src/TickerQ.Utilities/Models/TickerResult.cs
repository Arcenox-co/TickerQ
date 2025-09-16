using System;

namespace TickerQ.Utilities.Models
{
    public class TickerResult<TEntity> where TEntity : class
    {
        public TickerResult(Exception exception) : this(false)
            => Exception = exception;
        public TickerResult(TEntity result) : this(true)
            => Result = result;
        public TickerResult(int affectedRows) : this(true)
            => AffectedRows = affectedRows;

        public TickerResult(TEntity result, int affectedRows) : this(true)
        {
            Result = result;
            AffectedRows = affectedRows;
        }
        
        private TickerResult(bool isSucceeded)
            => isSucceeded = isSucceeded;

        public readonly bool isSucceeded;
        public int AffectedRows;
        public readonly TEntity Result;
        public readonly Exception Exception;
    }
}
