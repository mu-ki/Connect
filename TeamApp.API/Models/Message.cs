namespace TeamApp.API.Models;

public class Message
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;

    // Nullable because it might be a direct message (future addition) or in a channel.
    public Guid? ChannelId { get; set; }
    public Channel? Channel { get; set; }
}
