namespace TeamApp.API.Models;

public class Channel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsPrivate { get; set; } = false;
    public string ConversationType { get; set; } = "Group";
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
}
