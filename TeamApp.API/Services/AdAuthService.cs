using System.Runtime.Versioning;
using System.DirectoryServices.AccountManagement; // Use this when running on Windows for real AD validation

namespace TeamApp.API.Services;

public class AdAuthService : IAdAuthService
{
    public bool ValidateCredentials(string username, string password)
    {
        // Mock implementation for development.
        // In a real environment on a Windows domain, you would use:
        using (var context = new PrincipalContext(ContextType.Domain, "claysys.com"))
        {
            return context.ValidateCredentials(username, password);
        }
        
        // For now, accept any non-empty password for any user format (domain\user or user@domain)
        return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
    }

    public IEnumerable<TeamApp.API.Models.UserDto> SearchUsers(string searchTerm)
    {
#pragma warning disable CA1416 // Validate platform compatibility
        var users = new List<TeamApp.API.Models.UserDto>();
        try
        {
            using (var context = new PrincipalContext(ContextType.Domain, "claysys.com"))
            {
                var userPrincipal = new UserPrincipal(context);
                userPrincipal.DisplayName = $"*{searchTerm}*"; // Search by DisplayName wildcard
                using (var searcher = new PrincipalSearcher(userPrincipal))
                {
                    foreach (var result in searcher.FindAll().Cast<UserPrincipal>().Take(20))
                    {
                        if (result != null && !string.IsNullOrEmpty(result.UserPrincipalName))
                        {
                            users.Add(new TeamApp.API.Models.UserDto
                            {
                                AdUpn = result.UserPrincipalName,
                                DisplayName = result.DisplayName ?? result.Name
                            });
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // If AD search fails (e.g. not on VPN), return an empty list or gracefully degrade
        }
        return users;
#pragma warning restore CA1416
    }

    public TeamApp.API.Models.UserDto? GetUserByUpn(string upn)
    {
#pragma warning disable CA1416
        try
        {
            using (var context = new PrincipalContext(ContextType.Domain, "claysys.com"))
            {
                var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.UserPrincipalName, upn);
                if (userPrincipal != null)
                {
                    return new TeamApp.API.Models.UserDto
                    {
                        AdUpn = userPrincipal.UserPrincipalName ?? upn,
                        DisplayName = userPrincipal.DisplayName ?? userPrincipal.Name ?? upn
                    };
                }
            }
        }
        catch (Exception)
        {
            // Fallback gracefully
        }
        return null;
#pragma warning restore CA1416
    }
}
