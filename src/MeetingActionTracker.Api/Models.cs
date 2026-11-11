using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace MeetingActionTracker.Api;

public sealed class Committee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Membership> Memberships { get; set; } = [];
}

public sealed class Member
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Membership
{
    public int CommitteeId { get; set; }
    public Committee Committee { get; set; } = null!;
    public int MemberId { get; set; }
    public Member Member { get; set; } = null!;
}

public sealed class Meeting
{
    public int Id { get; set; }
    public int CommitteeId { get; set; }
    public Committee Committee { get; set; } = null!;
    public string Title { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public string Location { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<AgendaItem> Agenda { get; set; } = [];
}

public sealed class AgendaItem
{
    public int Id { get; set; }
    public int MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Position { get; set; }
    public List<AgendaDocument> Documents { get; set; } = [];
}

public sealed class AgendaDocument
{
    public int Id { get; set; }
    public int AgendaItemId { get; set; }
    public AgendaItem AgendaItem { get; set; } = null!;
    public string FileName { get; set; } = "";
    public string StorageName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public DateTime UploadedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ActionStatus>))]
public enum ActionStatus { Open, InProgress, Done }

public sealed class MeetingAction
{
    public int Id { get; set; }
    public int MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int AssigneeId { get; set; }
    public Member Assignee { get; set; } = null!;
    public DateOnly DueDate { get; set; }
    public ActionStatus Status { get; set; }
}

public sealed class TrackerDb(DbContextOptions<TrackerDb> options) : DbContext(options)
{
    public DbSet<Committee> Committees => Set<Committee>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<AgendaItem> AgendaItems => Set<AgendaItem>();
    public DbSet<AgendaDocument> Documents => Set<AgendaDocument>();
    public DbSet<MeetingAction> Actions => Set<MeetingAction>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Membership>().HasKey(x => new { x.CommitteeId, x.MemberId });
        model.Entity<Committee>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<Member>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<AgendaItem>().HasIndex(x => new { x.MeetingId, x.Position }).IsUnique();
        model.Entity<AgendaDocument>().HasIndex(x => x.StorageName).IsUnique();
        model.Entity<MeetingAction>().Property(x => x.Status).HasConversion<string>();
        model.Entity<MeetingAction>().HasIndex(x => new { x.DueDate, x.Status });
        foreach (var foreignKey in model.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
    }
}

public sealed record CommitteeInput(
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: Required(AllowEmptyStrings = true), StringLength(4000)] string Description);
public sealed record MemberInput([property: Required, StringLength(200, MinimumLength = 1)] string Name);
public sealed record MeetingInput(
    [property: Range(1, int.MaxValue)] int CommitteeId,
    [property: Required, StringLength(200, MinimumLength = 1)] string Title,
    [property: Required] string ScheduledAt,
    [property: Required(AllowEmptyStrings = true), StringLength(500)] string Location);
public sealed record SummaryInput([property: Required(AllowEmptyStrings = true), StringLength(50000)] string Summary);
public sealed record AgendaInput(
    [property: Required, StringLength(200, MinimumLength = 1)] string Title,
    [property: Required(AllowEmptyStrings = true), StringLength(4000)] string Description);
public sealed record OrderInput([property: Required] int[] ItemIds);
public sealed record ActionInput(
    [property: Range(1, int.MaxValue)] int MeetingId,
    [property: Required, StringLength(200, MinimumLength = 1)] string Title,
    [property: Required(AllowEmptyStrings = true), StringLength(4000)] string Description,
    [property: Range(1, int.MaxValue)] int AssigneeId,
    [property: Required] string DueDate,
    [property: Required] string Status);

public sealed record CommitteeDto(int Id, string Name, string Description, int[] MemberIds);
public sealed record MemberDto(int Id, string Name);
public record MeetingDto(int Id, int CommitteeId, string CommitteeName, string Title, DateTime ScheduledAt, string Location, string Summary);
public sealed record MeetingDetailDto(int Id, int CommitteeId, string CommitteeName, string Title, DateTime ScheduledAt, string Location, string Summary, AgendaDto[] Agenda);
public sealed record AgendaDto(int Id, string Title, string Description, int Position, DocumentDto[] Documents);
public sealed record DocumentDto(int Id, string FileName, string ContentType, long Size, DateTime UploadedAt);
public sealed record ActionDto(int Id, int MeetingId, string Title, string Description, int AssigneeId, DateOnly DueDate,
    ActionStatus Status, int CommitteeId, string CommitteeName, string MeetingTitle, string AssigneeName);

public static class Dto
{
    public static CommitteeDto Map(Committee x) => new(x.Id, x.Name, x.Description, x.Memberships.Select(m => m.MemberId).Order().ToArray());
    public static MeetingDto Map(Meeting x) => new(x.Id, x.CommitteeId, x.Committee.Name, x.Title, DateTime.SpecifyKind(x.ScheduledAt, DateTimeKind.Utc), x.Location, x.Summary);
    public static AgendaDto Map(AgendaItem x) => new(x.Id, x.Title, x.Description, x.Position, x.Documents.OrderBy(d => d.Id).Select(Map).ToArray());
    public static DocumentDto Map(AgendaDocument x) => new(x.Id, x.FileName, x.ContentType, x.Size, DateTime.SpecifyKind(x.UploadedAt, DateTimeKind.Utc));
    public static ActionDto Map(MeetingAction x) => new(x.Id, x.MeetingId, x.Title, x.Description, x.AssigneeId, x.DueDate, x.Status,
        x.Meeting.CommitteeId, x.Meeting.Committee.Name, x.Meeting.Title, x.Assignee.Name);
}
