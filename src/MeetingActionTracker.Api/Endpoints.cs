using Microsoft.EntityFrameworkCore;

namespace MeetingActionTracker.Api;

public static class Endpoints
{
    public static void MapTrackerEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/committees", async (TrackerDb db) =>
            (await db.Committees.Include(x => x.Memberships).OrderBy(x => x.Name).ToListAsync()).Select(Dto.Map));
        api.MapPost("/committees", async (CommitteeInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var item = new Committee { Name = input.Name.Trim(), Description = input.Description.Trim() };
            db.Committees.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/committees/{item.Id}", Dto.Map(item));
        });
        api.MapPut("/committees/{id:int}", async (int id, CommitteeInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var item = Check.Found(await db.Committees.Include(x => x.Memberships).SingleOrDefaultAsync(x => x.Id == id), "Committee");
            item.Name = input.Name.Trim();
            item.Description = input.Description.Trim();
            await db.SaveChangesAsync();
            return Dto.Map(item);
        });
        api.MapGet("/members", async (TrackerDb db) =>
            await db.Members.OrderBy(x => x.Name).Select(x => new MemberDto(x.Id, x.Name)).ToListAsync());
        api.MapPost("/members", async (MemberInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var item = new Member { Name = input.Name.Trim() };
            db.Members.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/members/{item.Id}", new MemberDto(item.Id, item.Name));
        });
        api.MapPut("/members/{id:int}", async (int id, MemberInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var item = Check.Found(await db.Members.FindAsync(id), "Member");
            item.Name = input.Name.Trim();
            await db.SaveChangesAsync();
            return new MemberDto(item.Id, item.Name);
        });
        api.MapDelete("/members/{id:int}", async (int id, TrackerDb db) =>
        {
            var item = Check.Found(await db.Members.FindAsync(id), "Member");
            Check.Require(!await db.Memberships.AnyAsync(x => x.MemberId == id)
                && !await db.Actions.AnyAsync(x => x.AssigneeId == id),
                "Remove this member's committee memberships first. Members with assigned actions cannot be deleted.", 409);
            db.Members.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPut("/committees/{id:int}/members/{memberId:int}", async (int id, int memberId, TrackerDb db) =>
        {
            Check.Found(await db.Committees.FindAsync(id), "Committee");
            Check.Found(await db.Members.FindAsync(memberId), "Member");
            if (!await db.Memberships.AnyAsync(x => x.CommitteeId == id && x.MemberId == memberId))
            {
                db.Memberships.Add(new Membership { CommitteeId = id, MemberId = memberId });
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });
        api.MapDelete("/committees/{id:int}/members/{memberId:int}", async (int id, int memberId, TrackerDb db) =>
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            var membership = Check.Found(await db.Memberships.FindAsync(id, memberId), "Membership");
            Check.Require(!await db.Actions.AnyAsync(x => x.Meeting.CommitteeId == id && x.AssigneeId == memberId),
                "This member has assigned actions in this committee and cannot be removed.", 409);
            db.Memberships.Remove(membership);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });

        api.MapGet("/meetings", async (int? committeeId, TrackerDb db) =>
        {
            Check.Require(committeeId is null or > 0, "Committee ID must be positive.");
            var query = db.Meetings.Include(x => x.Committee).AsQueryable();
            if (committeeId.HasValue) query = query.Where(x => x.CommitteeId == committeeId);
            return (await query.OrderByDescending(x => x.ScheduledAt).ThenByDescending(x => x.Id).ToListAsync()).Select(Dto.Map);
        });
        api.MapPost("/meetings", async (MeetingInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var scheduledAt = Check.Timestamp(input.ScheduledAt);
            var committee = Check.Found(await db.Committees.FindAsync(input.CommitteeId), "Committee");
            var item = new Meeting { Committee = committee, Title = input.Title.Trim(), ScheduledAt = scheduledAt, Location = input.Location.Trim() };
            db.Meetings.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/meetings/{item.Id}", Dto.Map(item));
        });
        api.MapGet("/meetings/{id:int}", async (int id, TrackerDb db) =>
        {
            var item = Check.Found(await db.Meetings.Include(x => x.Committee).Include(x => x.Agenda)
                .ThenInclude(x => x.Documents).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id), "Meeting");
            return new MeetingDetailDto(item.Id, item.CommitteeId, item.Committee.Name, item.Title,
                DateTime.SpecifyKind(item.ScheduledAt, DateTimeKind.Utc), item.Location, item.Summary, item.Agenda.OrderBy(x => x.Position).Select(Dto.Map).ToArray());
        });
        api.MapPut("/meetings/{id:int}", async (int id, MeetingInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var scheduledAt = Check.Timestamp(input.ScheduledAt);
            var item = Check.Found(await db.Meetings.Include(x => x.Committee).SingleOrDefaultAsync(x => x.Id == id), "Meeting");
            Check.Require(item.CommitteeId == input.CommitteeId, "A meeting cannot be moved to another committee.", 409);
            item.Title = input.Title.Trim();
            item.ScheduledAt = scheduledAt;
            item.Location = input.Location.Trim();
            await db.SaveChangesAsync();
            return Dto.Map(item);
        });
        api.MapPut("/meetings/{id:int}/summary", async (int id, SummaryInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var item = Check.Found(await db.Meetings.FindAsync(id), "Meeting");
            item.Summary = input.Summary;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPost("/meetings/{id:int}/agenda", async (int id, AgendaInput input, TrackerDb db) =>
        {
            Check.Input(input);
            using var transaction = await db.Database.BeginTransactionAsync();
            Check.Found(await db.Meetings.FindAsync(id), "Meeting");
            var position = (await db.AgendaItems.Where(x => x.MeetingId == id).MaxAsync(x => (int?)x.Position) ?? 0) + 1;
            var item = new AgendaItem { MeetingId = id, Title = input.Title.Trim(), Description = input.Description.Trim(), Position = position };
            db.AgendaItems.Add(item);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/v1/agenda/{item.Id}", Dto.Map(item));
        });
        api.MapPut("/agenda/{id:int}", async (int id, AgendaInput input, TrackerDb db) =>
        {
            Check.Input(input);
            var item = Check.Found(await db.AgendaItems.FindAsync(id), "Agenda item");
            item.Title = input.Title.Trim();
            item.Description = input.Description.Trim();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapDelete("/agenda/{id:int}", async (int id, TrackerDb db) =>
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            var item = Check.Found(await db.AgendaItems.FindAsync(id), "Agenda item");
            Check.Require(!await db.Documents.AnyAsync(x => x.AgendaItemId == id), "Remove the agenda item's documents first.", 409);
            db.AgendaItems.Remove(item);
            await db.SaveChangesAsync();
            var items = await db.AgendaItems.Where(x => x.MeetingId == item.MeetingId).OrderBy(x => x.Position).ToListAsync();
            await SaveOrder(db, items);
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        api.MapPut("/meetings/{id:int}/agenda/order", async (int id, OrderInput input, TrackerDb db) =>
        {
            Check.Input(input);
            using var transaction = await db.Database.BeginTransactionAsync();
            Check.Found(await db.Meetings.FindAsync(id), "Meeting");
            var items = await db.AgendaItems.Where(x => x.MeetingId == id).ToListAsync();
            Check.Require(input.ItemIds.Length == items.Count && input.ItemIds.Distinct().Count() == items.Count
                && input.ItemIds.ToHashSet().SetEquals(items.Select(x => x.Id)), "Provide every current agenda item ID exactly once.", 409);
            await SaveOrder(db, input.ItemIds.Select(itemId => items.Single(x => x.Id == itemId)).ToList());
            await transaction.CommitAsync();
            return Results.NoContent();
        });

        api.MapPost("/agenda/{id:int}/documents", async (int id, HttpRequest request, TrackerDb db, DocumentStore store) =>
        {
            Check.Found(await db.AgendaItems.FindAsync(id), "Agenda item");
            Check.Require(request.HasFormContentType, "A multipart file upload is required.", 415);
            IFormCollection form;
            try { form = await request.ReadFormAsync(); }
            catch (InvalidDataException exception)
            {
                throw new ApiException(400, $"Invalid multipart upload: {exception.Message}");
            }
            Check.Require(form.Files.Count == 1 && form.Files[0].Name == "file", "Upload exactly one file using the 'file' field.");
            var document = await store.Upload(form.Files[0], id, db);
            return Results.Created($"/api/v1/documents/{document.Id}", Dto.Map(document));
        });
        api.MapGet("/documents/{id:int}", async (int id, TrackerDb db, DocumentStore store) =>
        {
            var document = Check.Found(await db.Documents.FindAsync(id), "Document");
            return store.Download(document);
        });
        api.MapDelete("/documents/{id:int}", async (int id, TrackerDb db, DocumentStore store) =>
        {
            var document = Check.Found(await db.Documents.FindAsync(id), "Document");
            await store.Delete(document, db);
            return Results.NoContent();
        });

        api.MapGet("/actions", async (HttpRequest request, TrackerDb db) =>
        {
            var today = Check.Date(request.Query["today"].ToString());
            var query = ActionQuery(db);
            var committeeId = QueryId(request, "committeeId");
            var meetingId = QueryId(request, "meetingId");
            var assigneeId = QueryId(request, "assigneeId");
            if (committeeId.HasValue) query = query.Where(x => x.Meeting.CommitteeId == committeeId);
            if (meetingId.HasValue) query = query.Where(x => x.MeetingId == meetingId);
            if (assigneeId.HasValue) query = query.Where(x => x.AssigneeId == assigneeId);
            if (request.Query.ContainsKey("status"))
            {
                var status = ParseStatus(request.Query["status"].ToString());
                query = query.Where(x => x.Status == status);
            }
            var outstanding = QueryBool(request, "outstanding");
            var overdue = QueryBool(request, "overdue");
            if (outstanding.HasValue) query = outstanding.Value ? query.Where(x => x.Status != ActionStatus.Done) : query.Where(x => x.Status == ActionStatus.Done);
            if (overdue.HasValue) query = overdue.Value ? query.Where(x => x.Status != ActionStatus.Done && x.DueDate < today)
                : query.Where(x => x.Status == ActionStatus.Done || x.DueDate >= today);
            return (await query.OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToListAsync()).Select(Dto.Map);
        });
        api.MapPost("/actions", async (ActionInput input, TrackerDb db) =>
        {
            Check.Input(input);
            using var transaction = await db.Database.BeginTransactionAsync();
            var item = new MeetingAction();
            await ApplyAction(input, item, db);
            db.Actions.Add(item);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await db.Entry(item).Reference(x => x.Assignee).LoadAsync();
            return Results.Created($"/api/v1/actions/{item.Id}", Dto.Map(item));
        });
        api.MapPut("/actions/{id:int}", async (int id, ActionInput input, TrackerDb db) =>
        {
            Check.Input(input);
            using var transaction = await db.Database.BeginTransactionAsync();
            var item = Check.Found(await db.Actions.FindAsync(id), "Action");
            Check.Require(item.MeetingId == input.MeetingId, "An action cannot be moved to another meeting.", 409);
            await ApplyAction(input, item, db);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await db.Entry(item).Reference(x => x.Assignee).LoadAsync();
            return Dto.Map(item);
        });
    }

    private static async Task SaveOrder(TrackerDb db, List<AgendaItem> items)
    {
        // Use temporary negative positions to avoid unique-index collisions during swaps.
        foreach (var item in items) item.Position = -item.Id;
        await db.SaveChangesAsync();
        for (var index = 0; index < items.Count; index++) items[index].Position = index + 1;
        await db.SaveChangesAsync();
    }

    private static IQueryable<MeetingAction> ActionQuery(TrackerDb db) =>
        db.Actions.Include(x => x.Meeting).ThenInclude(x => x.Committee).Include(x => x.Assignee);

    private static async Task ApplyAction(ActionInput input, MeetingAction item, TrackerDb db)
    {
        var date = Check.Date(input.DueDate);
        var status = ParseStatus(input.Status);
        var meeting = Check.Found(await db.Meetings.Include(x => x.Committee).SingleOrDefaultAsync(x => x.Id == input.MeetingId), "Meeting");
        Check.Require(await db.Memberships.AnyAsync(x => x.CommitteeId == meeting.CommitteeId && x.MemberId == input.AssigneeId),
            "The assignee must be a member of this meeting's committee.");
        item.Meeting = meeting;
        item.Title = input.Title.Trim();
        item.Description = input.Description.Trim();
        item.AssigneeId = input.AssigneeId;
        item.DueDate = date;
        item.Status = status;
    }

    private static ActionStatus ParseStatus(string value)
    {
        Check.Require(value is "Open" or "InProgress" or "Done", "Status must be Open, InProgress, or Done.");
        return Enum.Parse<ActionStatus>(value);
    }

    private static int? QueryId(HttpRequest request, string key)
    {
        if (!request.Query.ContainsKey(key)) return null;
        Check.Require(int.TryParse(request.Query[key], out var value) && value > 0, $"{key} must be a positive integer.");
        return value;
    }

    private static bool? QueryBool(HttpRequest request, string key)
    {
        if (!request.Query.ContainsKey(key)) return null;
        Check.Require(bool.TryParse(request.Query[key], out var value), $"{key} must be true or false.");
        return value;
    }
}
