using MapsterMapper;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Repositories;
using CheapFurniturePlanner.ViewModels;
using Microsoft.Extensions.Logging;

namespace CheapFurniturePlanner.Services;

/// <summary>
/// Service for managing furniture catalog operations
/// </summary>
public class FurnitureCatalogService
{
    private readonly FurniturePlannerRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<FurnitureCatalogService> _logger;

    public FurnitureCatalogService(
        FurniturePlannerRepository repository,
        IMapper mapper,
        ILogger<FurnitureCatalogService> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Gets all active furniture items for display in catalog
    /// </summary>
    public async Task<List<FurnitureCatalogViewModel>> GetActiveFurnitureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var furnitureItems = await _repository.GetActiveFurnitureAsync(cancellationToken);
            return _mapper.Map<List<FurnitureCatalogViewModel>>(furnitureItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active furniture");
            throw;
        }
    }

    /// <summary>
    /// Gets furniture items by type
    /// </summary>
    public async Task<List<FurnitureCatalogViewModel>> GetFurnitureByTypeAsync(FurnitureType type, CancellationToken cancellationToken = default)
    {
        try
        {
            var furnitureItems = await _repository.GetFurnitureByTypeAsync(type, cancellationToken);
            return _mapper.Map<List<FurnitureCatalogViewModel>>(furnitureItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving furniture by type {Type}", type);
            throw;
        }
    }

    /// <summary>
    /// Searches furniture by name or code
    /// </summary>
    public async Task<List<FurnitureCatalogViewModel>> SearchFurnitureAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        try
        {
            var allFurniture = await _repository.GetActiveFurnitureAsync(cancellationToken);
            var filtered = allFurniture.Where(f =>
                f.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                f.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (f.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();

            return _mapper.Map<List<FurnitureCatalogViewModel>>(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching furniture with term {SearchTerm}", searchTerm);
            throw;
        }
    }

    /// <summary>
    /// Adds a new furniture item to the catalog
    /// </summary>
    public async Task<FurnitureCatalogViewModel> AddFurnitureItemAsync(FurnitureCatalogViewModel viewModel, CancellationToken cancellationToken = default)
    {
        try
        {
            var furnitureItem = _mapper.Map<FurnitureItem>(viewModel);
            furnitureItem.CreatedAt = DateTime.UtcNow;

            var addedItem = await _repository.AddFurnitureItemAsync(furnitureItem, cancellationToken);

            _logger.LogInformation("Added new furniture item: {Code} - {Name}", addedItem.Code, addedItem.Name);

            return _mapper.Map<FurnitureCatalogViewModel>(addedItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding furniture item {Code}", viewModel.Code);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing furniture item
    /// </summary>
    public async Task<FurnitureCatalogViewModel> UpdateFurnitureItemAsync(FurnitureCatalogViewModel viewModel, CancellationToken cancellationToken = default)
    {
        try
        {
            var furnitureItem = _mapper.Map<FurnitureItem>(viewModel);
            var updatedItem = await _repository.UpdateFurnitureItemAsync(furnitureItem, cancellationToken);

            _logger.LogInformation("Updated furniture item: {Code} - {Name}", updatedItem.Code, updatedItem.Name);

            return _mapper.Map<FurnitureCatalogViewModel>(updatedItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating furniture item {Code}", viewModel.Code);
            throw;
        }
    }

    /// <summary>
    /// Gets furniture usage statistics
    /// </summary>
    public async Task<Dictionary<FurnitureType, int>> GetUsageStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetFurnitureTypeUsageAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving usage statistics");
            throw;
        }
    }

    /// <summary>
    /// Gets the most popular furniture items
    /// </summary>
    public async Task<List<(FurnitureCatalogViewModel Furniture, int UsageCount)>> GetPopularFurnitureAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var popularItems = await _repository.GetPopularFurnitureAsync(limit, cancellationToken);
            return popularItems.Select(x => (_mapper.Map<FurnitureCatalogViewModel>(x.Furniture), x.UsageCount)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving popular furniture");
            throw;
        }
    }
}
