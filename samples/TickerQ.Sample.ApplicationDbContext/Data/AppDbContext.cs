using Microsoft.EntityFrameworkCore;

namespace TickerQ.Sample.ApplicationDbContext.Data;

public class AppDbContext : DbContext
{
    public DbSet<Person> Persons { get; set; }

    public AppDbContext(DbContextOptions options) : base(options)
    {

    }
}

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
}
