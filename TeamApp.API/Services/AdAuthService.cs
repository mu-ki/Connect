using System.Runtime.Versioning;
using System.DirectoryServices.AccountManagement; // Use this when running on Windows for real AD validation

using Microsoft.Extensions.Caching.Memory;

namespace TeamApp.API.Services;

public class AdAuthService : IAdAuthService
{
    private readonly IMemoryCache _cache;

    public AdAuthService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool ValidateCredentials(string username, string password)
    {
        // Attempt real AD validation when running on Windows + domain access.
        try
        {
            using (var context = new PrincipalContext(ContextType.Domain, "claysys.com"))
            {
                return context.ValidateCredentials(username, password);
            }
        }
        catch
        {
            // If AD validation fails (e.g., not on domain / no access), fall back to a simple check.
            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
        }
    }

    public IEnumerable<TeamApp.API.Models.UserDto> SearchUsers(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return Array.Empty<TeamApp.API.Models.UserDto>();

        var cacheKey = $"SearchUsers:{searchTerm.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out List<TeamApp.API.Models.UserDto> cached))
        {
            return cached;
        }

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
        finally
        {
            // Cache results for short-term to avoid excessive AD hits when user types
            _cache.Set(cacheKey, users, TimeSpan.FromSeconds(10));
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
