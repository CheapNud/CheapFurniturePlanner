using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace CheapFurniturePlanner.Services;

public sealed record ServiceLineInput(int? OrderLineId, string Description);
public sealed record ServiceTicketSummary(int Id, string TicketNumber, string ConsumerName, ServiceFlow Flow, ServiceTicketState State, string? AssignedUserName, DateTime CreatedAt);

// The service module's single mutation boundary: the one state machine
// (New -> InProgress -> Resolved, Cancelled from New/InProgress) is enforced here and only
// here, every change writes a log row stamped with the acting user, and the role rules hold
// no matter which page calls in. Reads (GetAsync/ListAsync) are open to any signed-in caller.
public sealed class ServiceTicketService(IDbContextFactory<FurniturePlannerContext> factory, ICurrentUser currentUser)
{
    public async Task<ServiceTicket> CreateTicketAsync(int consumerId, int? orderId, string problemDescription, string? visitAddress, ServiceFlow flow, IReadOnlyList<ServiceLineInput> lines, string? supplierRef = null, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        if (string.IsNullOrWhiteSpace(problemDescription)) { throw new InvalidOperationException("Problem description is required."); }

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Consumers.AnyAsync(c => c.Id == consumerId, ct)) { throw new InvalidOperationException($"Consumer {consumerId} not found."); }
        if (orderId is int linkedOrderId && !await db.Orders.AnyAsync(o => o.Id == linkedOrderId, ct)) { throw new InvalidOperationException($"Order {linkedOrderId} not found."); }

        var prefix = $"SRV-{DateTime.UtcNow.Year}-";
        var countThisYear = await db.ServiceTickets.CountAsync(t => t.TicketNumber.StartsWith(prefix), ct);
        var userId = await RequireUserIdAsync();

        var ticket = new ServiceTicket
        {
            TicketNumber = $"{prefix}{countThisYear + 1:D4}",
            ConsumerId = consumerId,
            OrderId = orderId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            ProblemDescription = problemDescription.Trim(),
            VisitAddress = string.IsNullOrWhiteSpace(visitAddress) ? null : visitAddress.Trim(),
        };
        ticket.Lines.AddRange(lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Description))
            .Select(l => new ServiceTicketLine { OrderLineId = l.OrderLineId, Description = l.Description.Trim() }));
        db.ServiceTickets.Add(ticket);
        await db.SaveChangesAsync(ct); // ticket.Id needed for the log + flow rows

        AddLog(db, ticket.Id, userId, $"Ticket {ticket.TicketNumber} created");
        if (flow != ServiceFlow.Undecided)
        {
            await ApplyFlowAsync(db, ticket, flow, supplierRef, ct);
        }
        await db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<int> OpenTicketCountForOrderAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServiceTickets.CountAsync(
            t => t.OrderId == orderId && t.State != ServiceTicketState.Resolved && t.State != ServiceTicketState.Cancelled, ct);
    }

    public async Task<ServiceTicket?> GetAsync(int ticketId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServiceTickets.AsNoTracking()
            .Include(t => t.Consumer).Include(t => t.Order)
            .Include(t => t.Lines).Include(t => t.Logs.OrderBy(l => l.At)).Include(t => t.Photos)
            .Include(t => t.InternalRepair).Include(t => t.SupplierReport)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);
    }

    public async Task<List<ServiceTicketSummary>> ListAsync(string? assignedUserId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ticketsQuery = db.ServiceTickets.AsNoTracking()
            .Include(t => t.Consumer).Include(t => t.InternalRepair).AsQueryable();
        if (assignedUserId is not null)
        {
            ticketsQuery = ticketsQuery.Where(t => t.InternalRepair != null && t.InternalRepair.AssignedUserId == assignedUserId);
        }
        var tickets = await ticketsQuery.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

        var assignedIds = tickets.Select(t => t.InternalRepair?.AssignedUserId).Where(id => id != null).Distinct().ToList();
        var names = await db.Users.Where(u => assignedIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName!, ct);

        return tickets
            .Select(t => new ServiceTicketSummary(
                t.Id, t.TicketNumber, t.Consumer!.Name, t.Flow, t.State,
                t.InternalRepair?.AssignedUserId is string assignedId ? names.GetValueOrDefault(assignedId) : null,
                t.CreatedAt))
            .ToList();
    }

    public async Task SetFlowAsync(int ticketId, ServiceFlow flow, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        if (flow == ServiceFlow.Undecided) { throw new InvalidOperationException("Choose Internal or External."); }

        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await RequireTicketAsync(db, ticketId, ct);
        if (ticket.State != ServiceTicketState.New) { throw new InvalidOperationException($"Ticket {ticket.TicketNumber}: flow is locked once work has started."); }
        if (ticket.Flow == flow) { return; }

        await ApplyFlowAsync(db, ticket, flow, null, ct);
        AddLog(db, ticket.Id, await RequireUserIdAsync(), $"Flow set to {flow}");
        await db.SaveChangesAsync(ct);
    }

    public async Task StartAsync(int ticketId, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await RequireTicketAsync(db, ticketId, ct);
        if (ticket.Flow == ServiceFlow.Undecided) { throw new InvalidOperationException($"Ticket {ticket.TicketNumber}: choose a flow before starting."); }
        Transition(ticket, ServiceTicketState.InProgress);
        AddLog(db, ticket.Id, await RequireUserIdAsync(), "Work started");
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(int ticketId, string reason, CancellationToken ct = default)
    {
        await RequireAdminOrOfficeAsync();
        if (string.IsNullOrWhiteSpace(reason)) { throw new InvalidOperationException("A cancel reason is required."); }

        await using var db = await factory.CreateDbContextAsync(ct);
        var ticket = await RequireTicketAsync(db, ticketId, ct);
        Transition(ticket, ServiceTicketState.Cancelled);
        ticket.CancelReason = reason.Trim();
        AddLog(db, ticket.Id, await RequireUserIdAsync(), "Ticket cancelled");
        await db.SaveChangesAsync(ct);
    }

    // ----- shared internals (Tasks 3-5 add the flow-specific mutations below) -----

    private async Task ApplyFlowAsync(FurniturePlannerContext db, ServiceTicket ticket, ServiceFlow flow, string? supplierRef, CancellationToken ct)
    {
        ticket.Flow = flow;
        if (flow == ServiceFlow.Internal)
        {
            if (ticket.SupplierReport is not null) { db.SupplierReports.Remove(ticket.SupplierReport); ticket.SupplierReport = null; }
            ticket.InternalRepair ??= new InternalRepair { TicketId = ticket.Id };
        }
        else
        {
            if (ticket.InternalRepair is not null) { db.InternalRepairs.Remove(ticket.InternalRepair); ticket.InternalRepair = null; }
            if (ticket.SupplierReport is null)
            {
                // Dropship payoff: default the supplier reference from the first linked order
                // line that carries one, unless the caller already provided it.
                if (supplierRef is null)
                {
                    var lineIds = ticket.Lines.Where(l => l.OrderLineId != null).Select(l => l.OrderLineId!.Value).ToList();
                    var refsByOrderLineId = await db.OrderLines
                        .Where(ol => lineIds.Contains(ol.Id) && ol.SupplierRef != null)
                        .Select(ol => new { ol.Id, ol.SupplierRef })
                        .ToDictionaryAsync(x => x.Id, x => x.SupplierRef, ct);
                    supplierRef = lineIds
                        .Where(id => refsByOrderLineId.ContainsKey(id))
                        .Select(id => refsByOrderLineId[id])
                        .FirstOrDefault();
                }
                ticket.SupplierReport = new SupplierReport { TicketId = ticket.Id, SupplierRef = supplierRef ?? "" };
            }
        }
    }

    private static void Transition(ServiceTicket ticket, ServiceTicketState to)
    {
        var legal = (ticket.State, to) switch
        {
            (ServiceTicketState.New, ServiceTicketState.InProgress) => true,
            (ServiceTicketState.InProgress, ServiceTicketState.Resolved) => true,
            (ServiceTicketState.New, ServiceTicketState.Cancelled) => true,
            (ServiceTicketState.InProgress, ServiceTicketState.Cancelled) => true,
            _ => false,
        };
        if (!legal) { throw new InvalidOperationException($"Ticket {ticket.TicketNumber} cannot go from {ticket.State} to {to}."); }
        ticket.State = to;
        if (to == ServiceTicketState.Resolved) { ticket.ResolvedAt = DateTime.UtcNow; }
    }

    private static void RequireOpen(ServiceTicket ticket)
    {
        if (ticket.State is ServiceTicketState.Resolved or ServiceTicketState.Cancelled)
        {
            throw new InvalidOperationException($"Ticket {ticket.TicketNumber} is closed.");
        }
    }

    private static void AddLog(FurniturePlannerContext db, int ticketId, string userId, string message) =>
        db.ServiceTicketLogs.Add(new ServiceTicketLog { TicketId = ticketId, At = DateTime.UtcNow, UserId = userId, Message = message });

    private static async Task<ServiceTicket> RequireTicketAsync(FurniturePlannerContext db, int ticketId, CancellationToken ct) =>
        await db.ServiceTickets
            .Include(t => t.Lines.OrderBy(l => l.Id))
            .Include(t => t.InternalRepair).Include(t => t.SupplierReport)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
        ?? throw new InvalidOperationException($"Ticket {ticketId} not found.");

    private async Task RequireAdminOrOfficeAsync()
    {
        if (await currentUser.IsInRoleAsync(Roles.Admin) || await currentUser.IsInRoleAsync(Roles.Office)) { return; }
        throw new InvalidOperationException("Only Admin or Office can do this.");
    }

    private async Task<string> RequireUserIdAsync() =>
        await currentUser.UserIdAsync() ?? throw new InvalidOperationException("No signed-in user.");
}
