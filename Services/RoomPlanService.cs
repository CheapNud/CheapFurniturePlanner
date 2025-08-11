using MapsterMapper;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.ViewModels;
using Microsoft.Extensions.Logging;

namespace CheapFurniturePlanner.Services;

/// <summary>
/// Service for managing room plan operations
/// </summary>
public class RoomPlanService
{
    private readonly FurniturePlannerRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<RoomPlanService> _logger;

    public RoomPlanService(
        FurniturePlannerRepository repository,
        IMapper mapper,
        ILogger<RoomPlanService> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Gets all room plans
    /// </summary>
    public async Task<List<RoomPlanViewModel>> GetAllRoomPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var roomPlans = await _repository.GetRoomPlansAsync(cancellationToken);
            return _mapper.Map<List<RoomPlanViewModel>>(roomPlans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving room plans");
            throw;
        }
    }

    /// <summary>
    /// Gets a room plan with all its furniture
    /// </summary>
    public async Task<RoomPlanViewModel?> GetRoomPlanWithFurnitureAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var roomPlan = await _repository.GetRoomPlanWithFurnitureAsync(roomPlanId, cancellationToken);
            if (roomPlan == null) return null;

            var viewModel = _mapper.Map<RoomPlanViewModel>(roomPlan);

            // Map furniture items
            viewModel.FurnitureItems = roomPlan.FurnitureItems.Select(pfi =>
            {
                var furnitureVm = _mapper.Map<FurniturePlannerViewModel>(pfi.FurnitureItem);
                furnitureVm.UIId = pfi.UIId;
                furnitureVm.X = pfi.X;
                furnitureVm.Y = pfi.Y;
                furnitureVm.Rotation = pfi.Rotation;
                furnitureVm.GroupID = pfi.GroupId;
                furnitureVm.CustomName = pfi.CustomName;
                furnitureVm.Notes = pfi.Notes;
                return furnitureVm;
            }).ToList();

            return viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving room plan {RoomPlanId}", roomPlanId);
            throw;
        }
    }

    /// <summary>
    /// Creates a new room plan
    /// </summary>
    public async Task<RoomPlanViewModel> CreateRoomPlanAsync(RoomPlanViewModel viewModel, CancellationToken cancellationToken = default)
    {
        try
        {
            var roomPlan = _mapper.Map<RoomPlan>(viewModel);
            roomPlan.CreatedAt = DateTime.UtcNow;

            var createdPlan = await _repository.CreateRoomPlanAsync(roomPlan, cancellationToken);

            _logger.LogInformation("Created new room plan: {Name}", createdPlan.Name);

            return _mapper.Map<RoomPlanViewModel>(createdPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room plan {Name}", viewModel.Name);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing room plan
    /// </summary>
    public async Task<RoomPlanViewModel> UpdateRoomPlanAsync(RoomPlanViewModel viewModel, CancellationToken cancellationToken = default)
    {
        try
        {
            var roomPlan = _mapper.Map<RoomPlan>(viewModel);
            var updatedPlan = await _repository.UpdateRoomPlanAsync(roomPlan, cancellationToken);

            _logger.LogInformation("Updated room plan: {Name}", updatedPlan.Name);

            return _mapper.Map<RoomPlanViewModel>(updatedPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating room plan {Id}", viewModel.Id);
            throw;
        }
    }

    /// <summary>
    /// Deletes a room plan and all its furniture
    /// </summary>
    public async Task<bool> DeleteRoomPlanAsync(int roomPlanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _repository.DeleteRoomPlanAsync(roomPlanId, cancellationToken);

            if (success)
            {
                _logger.LogInformation("Deleted room plan {RoomPlanId}", roomPlanId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting room plan {RoomPlanId}", roomPlanId);
            throw;
        }
    }
}
