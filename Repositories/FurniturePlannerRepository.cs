using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.EF;
using CheapHelpers.EF.Repositories;
using CheapHelpers.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace CheapFurniturePlanner.Repositories;

/// <summary>
/// Repository for managing furniture planner data, extending CheapHelpers BaseRepo
/// </summary>
public class FurniturePlannerRepository
{
    private readonly IDbContextFactory<FurniturePlannerContext> _contextFactory;

    public FurniturePlannerRepository(IDbContextFactory<FurniturePlannerContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    #region Furniture Catalog Management

    /// <summary>
    /// Gets all active furniture items from the catalog
    /// </summary>
    public async Task<List<FurnitureItem>> GetActiveFurnitureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.FurnitureItems
                .Where(f => f.IsActive)
                .OrderBy(f => f.Type)
                .ThenBy(f => f.Name)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetActiveFurnitureAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets furniture items by type
    /// </summary>
    public async Task<List<FurnitureItem>> GetFurnitureByTypeAsync(FurnitureType type, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.FurnitureItems
                .Where(f => f.IsActive && f.Type == type)
                .OrderBy(f => f.Name)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetFurnitureByTypeAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets a furniture item by its code
    /// </summary>
    public async Task<FurnitureItem?> GetFurnitureByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.FurnitureItems
                .FirstOrDefaultAsync(f => f.Code == code, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetFurnitureByCodeAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Adds a new furniture item to the catalog
    /// </summary>
    public async Task<FurnitureItem> AddFurnitureItemAsync(FurnitureItem furnitureItem, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            context.FurnitureItems.Add(furnitureItem);
            await context.SaveChangesAsync(cancellationToken);
            return furnitureItem;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in AddFurnitureItemAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing furniture item
    /// </summary>
    public async Task<FurnitureItem> UpdateFurnitureItemAsync(FurnitureItem furnitureItem, CancellationToken cancellationToken = default)
    {
        try
        {
            furnitureItem.UpdatedAt = DateTime.UtcNow;
            using var context = _contextFactory.CreateDbContext();
            context.FurnitureItems.Update(furnitureItem);
            await context.SaveChangesAsync(cancellationToken);
            return furnitureItem;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in UpdateFurnitureItemAsync: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Room Plan Management

    /// <summary>
    /// Gets all room plans
    /// </summary>
    public async Task<List<RoomPlan>> GetRoomPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.RoomPlans
                .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetRoomPlansAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets a room plan by ID with all furniture items
    /// </summary>
    public async Task<RoomPlan?> GetRoomPlanWithFurnitureAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.RoomPlans
                .Include(r => r.FurnitureItems)
                    .ThenInclude(f => f.FurnitureItem)
                .FirstOrDefaultAsync(r => r.Id == roomPlanId, cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetRoomPlanWithFurnitureAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Creates a new room plan
    /// </summary>
    public async Task<RoomPlan> CreateRoomPlanAsync(RoomPlan roomPlan, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            context.RoomPlans.Add(roomPlan);
            await context.SaveChangesAsync(cancellationToken);
            return roomPlan;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in CreateRoomPlanAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing room plan
    /// </summary>
    public async Task<RoomPlan> UpdateRoomPlanAsync(RoomPlan roomPlan, CancellationToken cancellationToken = default)
    {
        try
        {
            roomPlan.UpdatedAt = DateTime.UtcNow;
            using var context = _contextFactory.CreateDbContext();
            context.RoomPlans.Update(roomPlan);
            await context.SaveChangesAsync(cancellationToken);
            return roomPlan;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in UpdateRoomPlanAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Deletes a room plan and all its furniture items
    /// </summary>
    public async Task<bool> DeleteRoomPlanAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();

            // First delete all furniture items in this room plan
            var furnitureItems = await context.PlannerFurnitureItems
                .Where(f => f.RoomPlanId == roomPlanId)
                .ToListAsync(cancellationToken);

            if (furnitureItems.Any())
            {
                context.PlannerFurnitureItems.RemoveRange(furnitureItems);
            }

            // Then delete the room plan
            var roomPlan = await context.RoomPlans
                .FirstOrDefaultAsync(r => r.Id == roomPlanId, cancellationToken);

            if (roomPlan != null)
            {
                context.RoomPlans.Remove(roomPlan);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in DeleteRoomPlanAsync: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Planner Furniture Items Management

    /// <summary>
    /// Gets all furniture items for a specific room plan
    /// </summary>
    public async Task<List<PlannerFurnitureItem>> GetPlannerFurnitureItemsAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.PlannerFurnitureItems
                .Include(p => p.FurnitureItem)
                .Where(p => p.RoomPlanId == roomPlanId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetPlannerFurnitureItemsAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Adds a furniture item to a room plan
    /// </summary>
    public async Task<PlannerFurnitureItem> AddPlannerFurnitureItemAsync(PlannerFurnitureItem plannerItem, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            context.PlannerFurnitureItems.Add(plannerItem);
            await context.SaveChangesAsync(cancellationToken);
            return plannerItem;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in AddPlannerFurnitureItemAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Updates the position/rotation of a furniture item in the planner
    /// </summary>
    public async Task<PlannerFurnitureItem> UpdatePlannerFurnitureItemAsync(PlannerFurnitureItem plannerItem, CancellationToken cancellationToken = default)
    {
        try
        {
            plannerItem.UpdatedAt = DateTime.UtcNow;
            using var context = _contextFactory.CreateDbContext();
            context.PlannerFurnitureItems.Update(plannerItem);
            await context.SaveChangesAsync(cancellationToken);
            return plannerItem;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in UpdatePlannerFurnitureItemAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Removes a furniture item from the room plan
    /// </summary>
    public async Task<bool> RemovePlannerFurnitureItemAsync(int roomPlanId, int uiId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var item = await context.PlannerFurnitureItems
                .FirstOrDefaultAsync(p => p.RoomPlanId == roomPlanId && p.UIId == uiId, cancellationToken);

            if (item != null)
            {
                context.PlannerFurnitureItems.Remove(item);
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in RemovePlannerFurnitureItemAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Bulk update furniture item positions (for efficient dragging operations)
    /// </summary>
    public async Task<bool> BulkUpdatePositionsAsync(List<PlannerFurnitureItem> items, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();

            foreach (var item in items)
            {
                var existingItem = await context.PlannerFurnitureItems
                    .FirstOrDefaultAsync(p => p.Id == item.Id, cancellationToken);

                if (existingItem != null)
                {
                    existingItem.X = item.X;
                    existingItem.Y = item.Y;
                    existingItem.Rotation = item.Rotation;
                    existingItem.UpdatedAt = DateTime.UtcNow;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in BulkUpdatePositionsAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Clears all furniture from a room plan
    /// </summary>
    public async Task<bool> ClearRoomPlanFurnitureAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var furnitureItems = await context.PlannerFurnitureItems
                .Where(f => f.RoomPlanId == roomPlanId)
                .ToListAsync(cancellationToken);

            if (furnitureItems.Any())
            {
                context.PlannerFurnitureItems.RemoveRange(furnitureItems);
                await context.SaveChangesAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in ClearRoomPlanFurnitureAsync: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Grouping Operations

    /// <summary>
    /// Groups multiple furniture items together
    /// </summary>
    public async Task<bool> GroupFurnitureItemsAsync(int roomPlanId, List<int> uiIds, int groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var items = await context.PlannerFurnitureItems
                .Where(p => p.RoomPlanId == roomPlanId && uiIds.Contains(p.UIId))
                .ToListAsync(cancellationToken);

            foreach (var item in items)
            {
                item.GroupId = groupId;
                item.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GroupFurnitureItemsAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Ungroups furniture items
    /// </summary>
    public async Task<bool> UngroupFurnitureItemsAsync(int roomPlanId, int groupId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            var items = await context.PlannerFurnitureItems
                .Where(p => p.RoomPlanId == roomPlanId && p.GroupId == groupId)
                .ToListAsync(cancellationToken);

            foreach (var item in items)
            {
                item.GroupId = null;
                item.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in UngroupFurnitureItemsAsync: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Statistics and Analytics

    /// <summary>
    /// Gets usage statistics for furniture types
    /// </summary>
    public async Task<Dictionary<FurnitureType, int>> GetFurnitureTypeUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.PlannerFurnitureItems
                .Include(p => p.FurnitureItem)
                .GroupBy(p => p.FurnitureItem.Type)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetFurnitureTypeUsageAsync: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets the most popular furniture items
    /// </summary>
    public async Task<List<(FurnitureItem Furniture, int UsageCount)>> GetPopularFurnitureAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateDbContext();
            return await context.PlannerFurnitureItems
                .Include(p => p.FurnitureItem)
                .GroupBy(p => p.FurnitureItem)
                .Select(g => new { Furniture = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(limit)
                .Select(x => ValueTuple.Create(x.Furniture, x.Count))
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetPopularFurnitureAsync: {ex.Message}");
            throw;
        }
    }

    #endregion
}