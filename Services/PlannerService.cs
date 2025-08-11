using MapsterMapper;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.ViewModels;
using Microsoft.Extensions.Logging;

namespace CheapFurniturePlanner.Services;

/// <summary>
/// Main service for planner operations
/// </summary>
public class PlannerService
{
    private readonly FurniturePlannerRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<PlannerService> _logger;

    public PlannerService(
        FurniturePlannerRepository repository,
        IMapper mapper,
        ILogger<PlannerService> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Adds a furniture item to a room plan
    /// </summary>
    public async Task<FurniturePlannerViewModel> AddFurnitureToRoomAsync(
        int roomPlanId,
        FurniturePlannerViewModel furnitureViewModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plannerItem = new PlannerFurnitureItem
            {
                RoomPlanId = roomPlanId,
                FurnitureItemId = furnitureViewModel.FurnitureItemId,
                UIId = furnitureViewModel.UIId,
                X = furnitureViewModel.X,
                Y = furnitureViewModel.Y,
                Rotation = furnitureViewModel.Rotation,
                GroupId = furnitureViewModel.GroupID,
                CustomName = furnitureViewModel.CustomName,
                Notes = furnitureViewModel.Notes,
                CreatedAt = DateTime.UtcNow
            };

            var addedItem = await _repository.AddPlannerFurnitureItemAsync(plannerItem, cancellationToken);

            _logger.LogInformation("Added furniture {FurnitureId} to room plan {RoomPlanId}",
                furnitureViewModel.FurnitureItemId, roomPlanId);

            // Return the updated view model with the database ID
            furnitureViewModel.UIId = addedItem.UIId;
            return furnitureViewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding furniture to room plan");
            throw;
        }
    }

    /// <summary>
    /// Updates furniture position/rotation in room plan
    /// </summary>
    public async Task<bool> UpdateFurniturePositionAsync(
        int roomPlanId,
        FurniturePlannerViewModel furnitureViewModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existingItems = await _repository.GetPlannerFurnitureItemsAsync(roomPlanId, cancellationToken);
            var existingItem = existingItems.FirstOrDefault(x => x.UIId == furnitureViewModel.UIId);

            if (existingItem == null)
            {
                _logger.LogWarning("Furniture item with UIId {UIId} not found in room plan {RoomPlanId}",
                    furnitureViewModel.UIId, roomPlanId);
                return false;
            }

            existingItem.X = furnitureViewModel.X;
            existingItem.Y = furnitureViewModel.Y;
            existingItem.Rotation = furnitureViewModel.Rotation;
            existingItem.GroupId = furnitureViewModel.GroupID;
            existingItem.CustomName = furnitureViewModel.CustomName;
            existingItem.Notes = furnitureViewModel.Notes;

            await _repository.UpdatePlannerFurnitureItemAsync(existingItem, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating furniture position");
            throw;
        }
    }

    /// <summary>
    /// Removes furniture from room plan
    /// </summary>
    public async Task<bool> RemoveFurnitureFromRoomAsync(
        int roomPlanId,
        int uiId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _repository.RemovePlannerFurnitureItemAsync(roomPlanId, uiId, cancellationToken);

            if (success)
            {
                _logger.LogInformation("Removed furniture with UIId {UIId} from room plan {RoomPlanId}",
                    uiId, roomPlanId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing furniture from room plan");
            throw;
        }
    }

    /// <summary>
    /// Clears all furniture from a room plan
    /// </summary>
    public async Task<bool> ClearRoomPlanAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _repository.ClearRoomPlanFurnitureAsync(roomPlanId, cancellationToken);

            if (success)
            {
                _logger.LogInformation("Cleared all furniture from room plan {RoomPlanId}", roomPlanId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing room plan furniture");
            throw;
        }
    }

    /// <summary>
    /// Groups multiple furniture items together
    /// </summary>
    public async Task<bool> GroupFurnitureAsync(
        int roomPlanId,
        List<int> uiIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate a new group ID
            var existingItems = await _repository.GetPlannerFurnitureItemsAsync(roomPlanId, cancellationToken);
            var maxGroupId = existingItems.Where(x => x.GroupId.HasValue).Max(x => x.GroupId) ?? 0;
            var newGroupId = maxGroupId + 1;

            var success = await _repository.GroupFurnitureItemsAsync(roomPlanId, uiIds, newGroupId, cancellationToken);

            if (success)
            {
                _logger.LogInformation("Grouped {Count} furniture items in room plan {RoomPlanId} with group ID {GroupId}",
                    uiIds.Count, roomPlanId, newGroupId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error grouping furniture items");
            throw;
        }
    }

    /// <summary>
    /// Ungroups furniture items
    /// </summary>
    public async Task<bool> UngroupFurnitureAsync(
        int roomPlanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _repository.UngroupFurnitureItemsAsync(roomPlanId, groupId, cancellationToken);

            if (success)
            {
                _logger.LogInformation("Ungrouped furniture items in group {GroupId} from room plan {RoomPlanId}",
                    groupId, roomPlanId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ungrouping furniture items");
            throw;
        }
    }

    /// <summary>
    /// Saves the current state of all furniture in a room plan
    /// </summary>
    public async Task<bool> SaveRoomPlanStateAsync(
        int roomPlanId,
        List<FurniturePlannerViewModel> furnitureItems,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get existing items from database
            var existingItems = await _repository.GetPlannerFurnitureItemsAsync(roomPlanId, cancellationToken);

            var itemsToUpdate = new List<PlannerFurnitureItem>();

            foreach (var viewModel in furnitureItems)
            {
                var existingItem = existingItems.FirstOrDefault(x => x.UIId == viewModel.UIId);
                if (existingItem != null)
                {
                    existingItem.X = viewModel.X;
                    existingItem.Y = viewModel.Y;
                    existingItem.Rotation = viewModel.Rotation;
                    existingItem.GroupId = viewModel.GroupID;
                    existingItem.CustomName = viewModel.CustomName;
                    existingItem.Notes = viewModel.Notes;
                    itemsToUpdate.Add(existingItem);
                }
            }

            if (itemsToUpdate.Any())
            {
                await _repository.BulkUpdatePositionsAsync(itemsToUpdate, cancellationToken);
                _logger.LogInformation("Saved state for {Count} furniture items in room plan {RoomPlanId}",
                    itemsToUpdate.Count, roomPlanId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving room plan state");
            throw;
        }
    }

    /// <summary>
    /// Validates furniture placement (checks for overlaps, boundaries, etc.)
    /// </summary>
    public ValidationResult ValidateFurniturePlacement(
        FurniturePlannerViewModel furniture,
        RoomPlanViewModel roomPlan,
        List<FurniturePlannerViewModel> existingFurniture)
    {
        var result = new ValidationResult();

        try
        {
            // Check boundaries
            bool isRotated = ((int)Math.Round(furniture.Rotation / 90) % 2) == 1;
            double width = isRotated ? furniture.FurnitureLength.GetValueOrDefault() : furniture.FurnitureWidth.GetValueOrDefault();
            double height = isRotated ? furniture.FurnitureWidth.GetValueOrDefault() : furniture.FurnitureLength.GetValueOrDefault();

            if (furniture.X < 0 || furniture.Y < 0 ||
                furniture.X + width > roomPlan.Width ||
                furniture.Y + height > roomPlan.Height)
            {
                result.AddError("Furniture is outside room boundaries");
            }

            // Check overlaps if enabled
            if (roomPlan.PreventOverlap)
            {
                foreach (var existing in existingFurniture.Where(f => f.UIId != furniture.UIId))
                {
                    if (CheckOverlap(furniture, existing))
                    {
                        result.AddError($"Furniture overlaps with {existing.Name}");
                        break;
                    }
                }
            }

            result.IsValid = !result.Errors.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating furniture placement");
            result.AddError("Validation error occurred");
        }

        return result;
    }

    private static bool CheckOverlap(FurniturePlannerViewModel item1, FurniturePlannerViewModel item2)
    {
        // Calculate actual dimensions based on rotation
        bool isRotated1 = ((int)Math.Round(item1.Rotation / 90) % 2) == 1;
        bool isRotated2 = ((int)Math.Round(item2.Rotation / 90) % 2) == 1;

        double width1 = isRotated1 ? item1.FurnitureLength.GetValueOrDefault() : item1.FurnitureWidth.GetValueOrDefault();
        double height1 = isRotated1 ? item1.FurnitureWidth.GetValueOrDefault() : item1.FurnitureLength.GetValueOrDefault();

        double width2 = isRotated2 ? item2.FurnitureLength.GetValueOrDefault() : item2.FurnitureWidth.GetValueOrDefault();
        double height2 = isRotated2 ? item2.FurnitureWidth.GetValueOrDefault() : item2.FurnitureLength.GetValueOrDefault();

        // Check for rectangle overlap
        return !(item1.X + width1 <= item2.X ||
                 item2.X + width2 <= item1.X ||
                 item1.Y + height1 <= item2.Y ||
                 item2.Y + height2 <= item1.Y);
    }
}
