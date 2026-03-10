namespace TeamApp.API.Models;

public class UserDto
{
    public string AdUpn { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
}
