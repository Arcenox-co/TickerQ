using System;

namespace TickerQ.Utilities.Models
{
    public class TickerResult<TEntity>
    {
        public TickerResult(Exception exception) : this(false)
            => Exception = exception;

        public TickerResult(TEntity result) : this(true)
            => Result = result;
        private TickerResult(bool isSucceded)
            => IsSucceded = isSucceded;

        public readonly bool IsSucceded;
        public readonly TEntity Result;
        public readonly Exception Exception;
    }
}
