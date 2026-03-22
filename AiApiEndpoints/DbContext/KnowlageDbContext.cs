using AiApiEndpoints.Models;
using Microsoft.EntityFrameworkCore;

namespace AiApiEndpoints.DbContext;

//Is not used right now as no database is used. here for future functions.
public class KnowlageDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public DbSet<RawData> RawData { get; set; }
}