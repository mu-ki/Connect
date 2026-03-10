using Microsoft.EntityFrameworkCore;
using TeamApp.API.Models;

namespace TeamApp.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Workspace> Workspaces { get; set; }
    public DbSet<Channel> Channels { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<WorkspaceUser> WorkspaceUsers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkspaceUser>()
            .HasKey(wu => new { wu.WorkspaceId, wu.UserId });

        modelBuilder.Entity<WorkspaceUser>()
            .HasOne(wu => wu.Workspace)
            .WithMany(w => w.WorkspaceUsers)
            .HasForeignKey(wu => wu.WorkspaceId);

        modelBuilder.Entity<WorkspaceUser>()
            .HasOne(wu => wu.User)
            .WithMany(u => u.WorkspaceUsers)
            .HasForeignKey(wu => wu.UserId);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Sender)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.SenderId);
            
        modelBuilder.Entity<Message>()
            .HasOne(m => m.Channel)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ChannelId);
    }
}
