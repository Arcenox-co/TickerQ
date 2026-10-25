using System;
using System.Runtime.ExceptionServices;

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

        /// <summary>
        /// Throws <see cref="Exception"/>, with its original stack trace, when the operation failed; otherwise
        /// returns this result so calls can be chained: <c>(await manager.AddAsync(job)).EnsureSuccess()</c>.
        /// A failure without an exception throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public TickerResult<TEntity> EnsureSuccess()
        {
            if (IsSucceeded)
                return this;
            if (Exception != null)
                ExceptionDispatchInfo.Capture(Exception).Throw();
            throw new InvalidOperationException("The TickerQ operation failed.");
        }
    }
}
