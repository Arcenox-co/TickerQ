using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure;

public static class PaginationExtensions
{
    /// <summary>
    /// Converts an IQueryable to a paginated list
    /// </summary>
    public static async Task<PaginationResult<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> source,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 1000); // Max 1000 items per page
        
        // Get total count efficiently
        var count = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        
        // Apply pagination
        var items = await source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        
        return new PaginationResult<T>(items, count, pageNumber, pageSize);
    }
    
    /// <summary>
    /// Converts an IQueryable to a paginated list with a projection
    /// </summary>
    public static async Task<PaginationResult<TResult>> ToPaginatedListAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        int pageNumber,
        int pageSize,
        Func<IQueryable<TSource>, IQueryable<TResult>> projection,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 1000); // Max 1000 items per page
        
        // Get total count from the source query
        var count = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        
        // Apply pagination to the source
        var paginatedSource = source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);
            
        // Apply the projection
        var projectedQuery = projection(paginatedSource);
        var items = await projectedQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        
        return new PaginationResult<TResult>(items, count, pageNumber, pageSize);
    }
    
    /// <summary>
    /// Creates a paginated result without pagination (returns all items) 
    /// Useful for backward compatibility
    /// </summary>
    public static async Task<PaginationResult<T>> ToUnpaginatedListAsync<T>(
        this IQueryable<T> source,
        CancellationToken cancellationToken = default)
    {
        var items = await source.ToListAsync(cancellationToken).ConfigureAwait(false);
        var count = items.Count;
        
        return new PaginationResult<T>(items, count, 1, count > 0 ? count : 1);
    }
}
