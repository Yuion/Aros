using Microsoft.EntityFrameworkCore;

namespace Aros.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Add DbSet<YourEntity> properties here as you create entities
    // Example: public DbSet<User> Users => Set<User>();
}
