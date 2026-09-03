using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Needly.Domain;
using Needly.Infrastructure.GitHub;

namespace Needly.Infrastructure;

internal sealed class InstallationConfiguration : IEntityTypeConfiguration<Installation>
{
    public void Configure(EntityTypeBuilder<Installation> builder)
    {
        builder.ToTable("Installations");
        builder.HasKey(installation => installation.Id);
        builder.Property(installation => installation.AccountLogin).HasMaxLength(100).IsRequired();
        builder.HasIndex(installation => installation.GitHubInstallationId).IsUnique();
    }
}

internal sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.ToTable("Repositories");
        builder.HasKey(repository => repository.Id);
        builder.Property(repository => repository.Owner).HasMaxLength(100).IsRequired();
        builder.Property(repository => repository.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(repository => new { repository.InstallationId, repository.GitHubRepositoryId }).IsUnique();
        builder.HasIndex(repository => new { repository.Owner, repository.Name }).IsUnique();
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(repository => repository.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitHubUserConfiguration : IEntityTypeConfiguration<GitHubUser>
{
    public void Configure(EntityTypeBuilder<GitHubUser> builder)
    {
        builder.ToTable("GitHubUsers");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Login).HasMaxLength(100).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(200);
        builder.Property(user => user.AvatarUrl).HasMaxLength(2048);
        builder.HasIndex(user => user.GitHubUserId).IsUnique();
        builder.HasIndex(user => user.Login).IsUnique();
    }
}

internal sealed class NeedlyUserConfiguration : IEntityTypeConfiguration<NeedlyUser>
{
    public void Configure(EntityTypeBuilder<NeedlyUser> builder)
    {
        builder.ToTable("NeedlyUsers");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(user => user.GitHubUserId).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasOne<GitHubUser>()
            .WithOne()
            .HasForeignKey<NeedlyUser>(user => user.GitHubUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserInstallationConfiguration : IEntityTypeConfiguration<UserInstallation>
{
    public void Configure(EntityTypeBuilder<UserInstallation> builder)
    {
        builder.ToTable("UserInstallations");
        builder.HasKey(link => link.Id);
        builder.HasIndex(link => new { link.NeedlyUserId, link.GitHubInstallationId }).IsUnique();
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(link => link.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.HasKey(team => team.Id);
        builder.Property(team => team.Slug).HasMaxLength(100).IsRequired();
        builder.Property(team => team.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(team => new { team.InstallationId, team.GitHubTeamId }).IsUnique();
        builder.HasIndex(team => new { team.InstallationId, team.Slug }).IsUnique();
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(team => team.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InstallationMemberConfiguration : IEntityTypeConfiguration<InstallationMember>
{
    public void Configure(EntityTypeBuilder<InstallationMember> builder)
    {
        builder.ToTable("InstallationMembers");
        builder.HasKey(member => member.Id);
        builder.HasIndex(member => new { member.InstallationId, member.GitHubUserId }).IsUnique();
        builder.HasIndex(member => new { member.GitHubUserId, member.IsActive });
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(member => member.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitHubUser>()
            .WithMany()
            .HasForeignKey(member => member.GitHubUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("TeamMembers");
        builder.HasKey(member => member.Id);
        builder.HasIndex(member => new { member.TeamId, member.GitHubUserId }).IsUnique();
        builder.HasIndex(member => new { member.GitHubUserId, member.IsActive });
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitHubUser>()
            .WithMany()
            .HasForeignKey(member => member.GitHubUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RawEventConfiguration : IEntityTypeConfiguration<RawEvent>
{
    public void Configure(EntityTypeBuilder<RawEvent> builder)
    {
        builder.ToTable("RawEvents");
        builder.HasKey(rawEvent => rawEvent.Id);
        builder.Property(rawEvent => rawEvent.DeliveryId).HasMaxLength(100).IsRequired();
        builder.Property(rawEvent => rawEvent.EventName).HasMaxLength(100).IsRequired();
        builder.Property(rawEvent => rawEvent.EventAction).HasMaxLength(100);
        builder.Property(rawEvent => rawEvent.PayloadJson).IsRequired();
        builder.Property(rawEvent => rawEvent.LastError).HasMaxLength(2000);
        builder.HasIndex(rawEvent => rawEvent.DeliveryId).IsUnique();
        builder.HasIndex(rawEvent => new { rawEvent.GitHubInstallationId, rawEvent.GitHubRepositoryId, rawEvent.ReceivedAt });
        builder.HasIndex(rawEvent => new { rawEvent.Status, rawEvent.NextAttemptAt });
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(rawEvent => rawEvent.InstallationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(rawEvent => rawEvent.RepositoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NeedlyActionConfiguration : IEntityTypeConfiguration<NeedlyAction>
{
    private const string ActiveActionFilter = "\"State\" IN (0, 1)";

    public void Configure(EntityTypeBuilder<NeedlyAction> builder)
    {
        builder.ToTable("Actions");
        builder.HasKey(action => action.Id);
        builder.Property(action => action.Key)
            .HasConversion(key => key.Value, value => ActionKey.Parse(value))
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(action => action.SubjectUrl)
            .HasConversion(link => link.Value, value => GitHubDeepLink.Parse(value))
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(action => action.Title).HasMaxLength(500).IsRequired();
        builder.Property(action => action.Context).HasMaxLength(4000);
        builder.Property(action => action.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(action => action.RiskReason).HasMaxLength(1000);
        builder.Property(action => action.AuthorLogin).HasMaxLength(100);

        builder.HasIndex(action => action.Key)
            .HasFilter(ActiveActionFilter)
            .IsUnique();
        builder.HasIndex(action => new
            {
                action.Type,
                action.RepositoryId,
                action.SubjectType,
                action.SubjectNumber,
                action.AssigneeType,
                action.AssigneeId
            })
            .HasFilter(ActiveActionFilter)
            .IsUnique();
        builder.HasIndex(action => new
            {
                action.InstallationId,
                action.AssigneeType,
                action.AssigneeId,
                action.State,
                action.UpdatedAt
            });
            builder.HasIndex(action => new { action.State, action.IsAtRisk, action.LastActivityAt });

        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(action => action.InstallationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(action => action.RepositoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActionSuppressionConfiguration : IEntityTypeConfiguration<ActionSuppression>
{
    public void Configure(EntityTypeBuilder<ActionSuppression> builder)
    {
        builder.ToTable("ActionSuppressions");
        builder.HasKey(suppression => suppression.Id);
        builder.HasIndex(suppression => new
            {
                suppression.NeedlyUserId,
                suppression.InstallationId,
                suppression.RepositoryId,
                suppression.SubjectType,
                suppression.SubjectNumber,
                suppression.AssigneeType,
                suppression.AssigneeId
            })
            .HasFilter("\"IsActive\" = 1")
            .IsUnique();
        builder.HasIndex(suppression => new
        {
            suppression.InstallationId,
            suppression.RepositoryId,
            suppression.SubjectType,
            suppression.SubjectNumber,
            suppression.AssigneeType,
            suppression.AssigneeId,
            suppression.IsActive
        });
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(suppression => suppression.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Installation>()
            .WithMany()
            .HasForeignKey(suppression => suppression.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(suppression => suppression.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ActionLifecycleUndoConfiguration : IEntityTypeConfiguration<ActionLifecycleUndo>
{
    public void Configure(EntityTypeBuilder<ActionLifecycleUndo> builder)
    {
        builder.ToTable("ActionLifecycleUndos");
        builder.HasKey(undo => undo.Id);
        builder.HasIndex(undo => new { undo.NeedlyUserId, undo.CreatedAt });
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(undo => undo.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<NeedlyAction>()
            .WithMany()
            .HasForeignKey(undo => undo.ActionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ActionSuppression>()
            .WithMany()
            .HasForeignKey(undo => undo.SuppressionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ActionEventReceiptConfiguration : IEntityTypeConfiguration<ActionEventReceipt>
{
    public void Configure(EntityTypeBuilder<ActionEventReceipt> builder)
    {
        builder.ToTable("ActionEventReceipts");
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.DetectorKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(receipt => new { receipt.EventId, receipt.DetectorKey }).IsUnique();
        builder.HasOne<RawEvent>()
            .WithMany()
            .HasForeignKey(receipt => receipt.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitHubPullRequestStateConfiguration : IEntityTypeConfiguration<GitHubPullRequestStateEntity>
{
    public void Configure(EntityTypeBuilder<GitHubPullRequestStateEntity> builder)
    {
        builder.ToTable("GitHubPullRequestStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.AuthorLogin).HasMaxLength(100).IsRequired();
        builder.Property(state => state.HeadSha).HasMaxLength(100).IsRequired();
        builder.Property(state => state.Title).HasMaxLength(500).IsRequired();
        builder.Property(state => state.Url).HasMaxLength(2048).IsRequired();
        builder.HasIndex(state => new { state.RepositoryId, state.PullRequestNumber }).IsUnique();
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(state => state.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitHubReviewRequestStateConfiguration : IEntityTypeConfiguration<GitHubReviewRequestStateEntity>
{
    public void Configure(EntityTypeBuilder<GitHubReviewRequestStateEntity> builder)
    {
        builder.ToTable("GitHubReviewRequestStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.AssigneeLogin).HasMaxLength(100).IsRequired();
        builder.HasIndex(state => new
        {
            state.RepositoryId,
            state.PullRequestNumber,
            state.AssigneeType,
            state.GitHubAssigneeId
        }).IsUnique();
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(state => state.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitHubReviewerFeedbackStateConfiguration : IEntityTypeConfiguration<GitHubReviewerFeedbackStateEntity>
{
    public void Configure(EntityTypeBuilder<GitHubReviewerFeedbackStateEntity> builder)
    {
        builder.ToTable("GitHubReviewerFeedbackStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.ReviewerLogin).HasMaxLength(100).IsRequired();
        builder.HasIndex(state => new
        {
            state.RepositoryId,
            state.PullRequestNumber,
            state.ReviewerGitHubUserId
        }).IsUnique();
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(state => state.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitHubCheckFailureStateConfiguration : IEntityTypeConfiguration<GitHubCheckFailureStateEntity>
{
    public void Configure(EntityTypeBuilder<GitHubCheckFailureStateEntity> builder)
    {
        builder.ToTable("GitHubCheckFailureStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.HeadSha).HasMaxLength(100).IsRequired();
        builder.Property(state => state.CheckKey).HasMaxLength(200).IsRequired();
        builder.Property(state => state.Name).HasMaxLength(500).IsRequired();
        builder.Property(state => state.Url).HasMaxLength(2048);
        builder.HasIndex(state => new
        {
            state.RepositoryId,
            state.PullRequestNumber,
            state.HeadSha,
            state.CheckKey
        }).IsUnique();
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(state => state.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitHubResponseStateConfiguration : IEntityTypeConfiguration<GitHubResponseStateEntity>
{
    public void Configure(EntityTypeBuilder<GitHubResponseStateEntity> builder)
    {
        builder.ToTable("GitHubResponseStates");
        builder.HasKey(state => state.Id);
        builder.HasIndex(state => new
        {
            state.RepositoryId,
            state.SubjectType,
            state.SubjectNumber,
            state.GitHubAssigneeId
        }).IsUnique();
        builder.HasOne<Repository>()
            .WithMany()
            .HasForeignKey(state => state.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SavedViewConfiguration : IEntityTypeConfiguration<SavedView>
{
    public void Configure(EntityTypeBuilder<SavedView> builder)
    {
        builder.ToTable("SavedViews");
        builder.HasKey(view => view.Id);
        builder.Property(view => view.Name).HasMaxLength(100).IsRequired();
        builder.Property(view => view.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(view => view.FilterJson).HasMaxLength(16000).IsRequired();
        builder.HasIndex(view => new { view.NeedlyUserId, view.NormalizedName }).IsUnique();
        builder.HasIndex(view => new { view.NeedlyUserId, view.SortOrder });
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(view => view.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> builder)
    {
        builder.ToTable("AutomationRules");
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Name).HasMaxLength(100).IsRequired();
        builder.Property(rule => rule.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(rule => rule.FilterJson).HasMaxLength(16000).IsRequired();
        builder.HasIndex(rule => new { rule.NeedlyUserId, rule.NormalizedName }).IsUnique();
        builder.HasIndex(rule => new { rule.NeedlyUserId, rule.SortOrder });
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(rule => rule.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ActionDispositionConfiguration : IEntityTypeConfiguration<ActionDisposition>
{
    public void Configure(EntityTypeBuilder<ActionDisposition> builder)
    {
        builder.ToTable("ActionDispositions");
        builder.HasKey(disposition => disposition.Id);
        builder.HasIndex(disposition => new { disposition.NeedlyUserId, disposition.ActionId }).IsUnique();
        builder.HasIndex(disposition => new
        {
            disposition.NeedlyUserId,
            disposition.IsArchived,
            disposition.IsMuted,
            disposition.SnoozedUntil,
            disposition.IsPinned
        });
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(disposition => disposition.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<NeedlyAction>()
            .WithMany()
            .HasForeignKey(disposition => disposition.ActionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RuleExecutionConfiguration : IEntityTypeConfiguration<RuleExecution>
{
    public void Configure(EntityTypeBuilder<RuleExecution> builder)
    {
        builder.ToTable("RuleExecutions");
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.RuleName).HasMaxLength(100).IsRequired();
        builder.Property(execution => execution.Explanation).HasMaxLength(1000).IsRequired();
        builder.Property(execution => execution.IdempotencyKey).HasMaxLength(140).IsRequired();
        builder.HasIndex(execution => execution.IdempotencyKey).IsUnique();
        builder.HasIndex(execution => new { execution.NeedlyUserId, execution.ExecutedAt });
        builder.HasOne<NeedlyUser>()
            .WithMany()
            .HasForeignKey(execution => execution.NeedlyUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<NeedlyAction>()
            .WithMany()
            .HasForeignKey(execution => execution.ActionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}