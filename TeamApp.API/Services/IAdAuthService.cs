using System.Runtime.Versioning;

namespace TeamApp.API.Services;

using TeamApp.API.Models;

public interface IAdAuthService
{
    bool ValidateCredentials(string username, string password);
    IEnumerable<UserDto> SearchUsers(string searchTerm);
    UserDto? GetUserByUpn(string upn);
}
