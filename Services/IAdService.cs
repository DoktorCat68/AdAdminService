using AdAdminPortal.Models;
using System.Security.Cryptography;

namespace AdAdminPortal.Services
{
    public interface IAdService
    {
        IEnumerable<AdUserDto> FindUsers(string filter);
        AdUserDto? GetUser(string sam);
        IEnumerable<string> GetUserGroups(string sam);
        void DisableUser(string sam);
        void EnableUser(string sam);

        IEnumerable<string> GetAllGroups(string? filter = null);
        void AddUserToGroup(string sam, string groupSam);
        void RemoveUserFromGroup(string sam, string groupSam);
        // --- Группы ---
        void CreateGroup(string sam, string? description, string? ouDn);
        void DeleteGroup(string sam);

        IEnumerable<GroupMemberDto> GetGroupMembers(string groupSam);
        void AddGroupToGroup(string childGroupSam, string parentGroupSam);
        void RemoveGroupFromGroup(string childGroupSam, string parentGroupSam);
        IEnumerable<AdGroupDto> FindGroups(string? filter = null);
        void CreateUser(
    string sam,
    string password,
    string? displayName,
    string? email,
    string? ouDn,
    string? firstName,
    string? middleName,
    string? lastName,
    string? jobTitle = null,
    string? telephone = null);
        IEnumerable<string> GetOrganizationalUnits();
        void ResetPassword(string sam, string newPassword, bool mustChangeAtLogon);
        void UnlockUser(string sam);
        void UpdateUser(string sam, string? displayName, string? email);
        string? GetUserOuDn(string sam);
        string? GetUserJobTitle(string sam);
        void CopyGroups(string fromSam, string toSam);
    }
}
