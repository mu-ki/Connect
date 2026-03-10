namespace TeamApp.API.Models;

public class User
{
    public Guid Id { get; set; }
    
    // Active Directory User Principal Name (e.g., user@company.local)
    public string AdUpn { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? LastSeen { get; set; }
    public string? AvatarUrl { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<WorkspaceUser> WorkspaceUsers { get; set; } = new List<WorkspaceUser>();
}
