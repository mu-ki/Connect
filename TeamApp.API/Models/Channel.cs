namespace TeamApp.API.Models;

public class Channel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsPrivate { get; set; } = false;
    
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
