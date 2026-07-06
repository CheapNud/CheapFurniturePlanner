using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CheapFurniturePlanner.Data;

public class FurniturePlannerContextFactory : IDesignTimeDbContextFactory<FurniturePlannerContext>
{
    public FurniturePlannerContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>()
            .UseSqlite("Data Source=design_time.db")
            .Options;
        return new FurniturePlannerContext(options);
    }
}
