using Microsoft.EntityFrameworkCore;
using Needly.Domain;

namespace Needly.Infrastructure;

/// <summary>
/// Provides the EF Core unit of work for Needly's durable data.
/// </summary>
public sealed class NeedlyDbContext(DbContextOptions<NeedlyDbContext> options) : DbContext(options)
{
    /// <summary>Gets the GitHub App installations.</summary>
    public DbSet<Installation> Installations => Set<Installation>();

    /// <summary>Gets the repositories visible to installations.</summary>
    public DbSet<Repository> Repositories => Set<Repository>();

    /// <summary>Gets the GitHub user identities.</summary>
    public DbSet<GitHubUser> GitHubUsers => Set<GitHubUser>();

    /// <summary>Gets the Needly user accounts.</summary>
    public DbSet<NeedlyUser> NeedlyUsers => Set<NeedlyUser>();

    /// <summary>Gets links between Needly users and GitHub App installations.</summary>
    public DbSet<UserInstallation> UserInstallations => Set<UserInstallation>();

    /// <summary>Gets the GitHub teams.</summary>
    public DbSet<Team> Teams => Set<Team>();

    /// <summary>Gets organization memberships scoped to installations.</summary>
    public DbSet<InstallationMember> InstallationMembers => Set<InstallationMember>();

    /// <summary>Gets memberships in installation-scoped teams.</summary>
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    /// <summary>Gets immutable raw webhook deliveries.</summary>
    public DbSet<RawEvent> RawEvents => Set<RawEvent>();

    /// <summary>Gets the derived attention actions.</summary>
    public DbSet<NeedlyAction> Actions => Set<NeedlyAction>();

    /// <summary>Gets per-user muted-subject suppressions.</summary>
    public DbSet<ActionSuppression> ActionSuppressions => Set<ActionSuppression>();

    /// <summary>Gets durable action lifecycle undo records.</summary>
    public DbSet<ActionLifecycleUndo> ActionLifecycleUndos => Set<ActionLifecycleUndo>();

    /// <summary>Gets durable action-detector processing receipts.</summary>
    public DbSet<ActionEventReceipt> ActionEventReceipts => Set<ActionEventReceipt>();

    /// <summary>Gets user-defined Saved Views.</summary>
    public DbSet<SavedView> SavedViews => Set<SavedView>();

    /// <summary>Gets ordered per-user automation rules.</summary>
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();

    /// <summary>Gets per-user action states created by automation.</summary>
    public DbSet<ActionDisposition> ActionDispositions => Set<ActionDisposition>();

    /// <summary>Gets durable idempotent automation execution records.</summary>
    public DbSet<RuleExecution> RuleExecutions => Set<RuleExecution>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NeedlyDbContext).Assembly);
    }
}