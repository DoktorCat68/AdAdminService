using AdAdminPortal.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices;

namespace AdAdminPortal.Services
{
    public class AdService : IAdService
    {
        private readonly IConfiguration _config;

        public AdService(IConfiguration config)
        {
            _config = config;
        }

        private PrincipalContext GetContext()
        {
            var domain = _config["Ad:Domain"];

            var user = _config["Ad:User"];
            var pass = _config["Ad:Password"];

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                return new PrincipalContext(
                    ContextType.Domain,
                    domain,
                    user,
                    pass
                );
            }

            return new PrincipalContext(ContextType.Domain, domain);
        }

        public IEnumerable<AdUserDto> FindUsers(string filter)
        {
            using var ctx = GetContext();

            // шаблон
            var userQuery = new UserPrincipal(ctx);
            using var searcher = new PrincipalSearcher(userQuery);

            var normalized = filter?.Trim().ToLowerInvariant();

            foreach (var result in searcher.FindAll())
            {
                if (result is UserPrincipal u)
                {
                    var sam = u.SamAccountName ?? "";
                    var name = u.DisplayName ?? "";
                    var mail = u.EmailAddress ?? "";

                    if (!string.IsNullOrEmpty(normalized))
                    {
                        if (!sam.ToLowerInvariant().Contains(normalized) &&
                            !name.ToLowerInvariant().Contains(normalized) &&
                            !mail.ToLowerInvariant().Contains(normalized))
                        {
                            continue;
                        }
                    }

                    yield return new AdUserDto
                    {
                        Sam = sam,
                        DisplayName = name,
                        Email = mail,
                        Enabled = u.Enabled ?? false
                    };
                }
            }
        }

        public AdUserDto? GetUser(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null) return null;

            return new AdUserDto
            {
                Sam = user.SamAccountName,
                DisplayName = user.DisplayName,
                Email = user.EmailAddress,
                Enabled = user.Enabled ?? false
            };
        }
        public IEnumerable<string> GetUserGroups(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null)
                yield break;

            // 1) сначала обычные группы из memberOf
            var de = user.GetUnderlyingObject() as System.DirectoryServices.DirectoryEntry;
            var memberOf = de?.Properties["memberOf"];
            if (memberOf != null)
            {
                foreach (var item in memberOf)
                {
                    var dn = item.ToString();
                    using var groupEntry = new System.DirectoryServices.DirectoryEntry("LDAP://" + dn);
                    var samGroup = groupEntry.Properties["sAMAccountName"]?.Value?.ToString();
                    if (!string.IsNullOrEmpty(samGroup))
                        yield return samGroup;
                    else
                        yield return dn;
                }
            }

            // 2) добавим primary group (типа "Domain Users")
            // у юзера есть objectSid и primaryGroupID
            var objectSid = de?.Properties["objectSid"]?.Value as byte[];
            var primaryGroupId = de?.Properties["primaryGroupID"]?.Value;

            if (objectSid != null && primaryGroupId != null)
            {
                // SID юзера -> SID домена
                var userSid = new System.Security.Principal.SecurityIdentifier(objectSid, 0);
                // доменный SID — это всё кроме последнего RID
                var domainSid = userSid.AccountDomainSid;
                // а primaryGroupID — это как раз RID группы
                var primaryGroupSid = domainSid + "-" + primaryGroupId.ToString();

                // теперь найдём группу по этому SID
                using var searchRoot = new System.DirectoryServices.DirectoryEntry($"LDAP://{_config["Ad:Domain"]}");
                using var searcher = new System.DirectoryServices.DirectorySearcher(searchRoot);
                searcher.Filter = $"(objectSid={primaryGroupSid})";
                searcher.PropertiesToLoad.Add("sAMAccountName");

                var res = searcher.FindOne();
                if (res != null && res.Properties["sAMAccountName"].Count > 0)
                {
                    var pg = res.Properties["sAMAccountName"][0]?.ToString();
                    if (!string.IsNullOrEmpty(pg))
                        yield return pg;
                }
            }
        }

        public void DisableUser(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user != null)
            {
                user.Enabled = false;
                user.Save();
            }
        }

        public void EnableUser(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user != null)
            {
                user.Enabled = true;
                user.Save();
            }
        }
        public IEnumerable<string> GetAllGroups(string? filter = null)
        {
            using var ctx = GetContext();

            var groupQuery = new GroupPrincipal(ctx);
            using var searcher = new PrincipalSearcher(groupQuery);

            var norm = filter?.Trim().ToLowerInvariant();

            foreach (var r in searcher.FindAll())
            {
                if (r is GroupPrincipal g)
                {
                    var sam = g.SamAccountName ?? g.Name ?? "";
                    if (!string.IsNullOrEmpty(norm))
                    {
                        if (!sam.ToLowerInvariant().Contains(norm))
                            continue;
                    }
                    if (!string.IsNullOrEmpty(sam))
                        yield return sam;
                }
            }
        }

        public void AddUserToGroup(string sam, string groupSam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            var group = GroupPrincipal.FindByIdentity(ctx, groupSam);

            if (user == null || group == null)
                return;

            if (!group.Members.Contains(user))
            {
                group.Members.Add(user);
                group.Save();
            }
        }

        public void RemoveUserFromGroup(string sam, string groupSam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            var group = GroupPrincipal.FindByIdentity(ctx, groupSam);

            if (user == null || group == null)
                return;

            if (group.Members.Contains(user))
            {
                group.Members.Remove(user);
                group.Save();
            }
        }
        public void CreateUser(
    string sam,
    string password,
    string? displayName,
    string? email,
    string? ouDn,
    string? firstName,
    string? middleName,
    string? lastName,
    string? jobTitle = null,
    string? telephone = null)
        {
            var domain = _config["Ad:Domain"] ?? "kvsspb.lan";
            var svcUser = _config["Ad:User"];
            var svcPass = _config["Ad:Password"];

            PrincipalContext ctx;
            if (!string.IsNullOrWhiteSpace(ouDn))
            {
                ctx = (!string.IsNullOrWhiteSpace(svcUser) && !string.IsNullOrWhiteSpace(svcPass))
                    ? new PrincipalContext(ContextType.Domain, domain, ouDn, svcUser, svcPass)
                    : new PrincipalContext(ContextType.Domain, domain, ouDn);
            }
            else
            {
                ctx = (!string.IsNullOrWhiteSpace(svcUser) && !string.IsNullOrWhiteSpace(svcPass))
                    ? new PrincipalContext(ContextType.Domain, domain, svcUser, svcPass)
                    : new PrincipalContext(ContextType.Domain, domain);
            }

            try
            {
                var existing = UserPrincipal.FindByIdentity(ctx, sam);
                if (existing != null)
                    throw new ApplicationException($"Пользователь '{sam}' уже существует.");

                var user = new UserPrincipal(ctx)
                {
                    SamAccountName = sam,
                    DisplayName = displayName,
                    EmailAddress = email,
                    Enabled = true,
                    UserPrincipalName = $"{sam}@{domain}"   // UPN ("User logon name")
                };

                user.SetPassword(password);
                user.Save();

                // Дожмём атрибуты через DirectoryEntry
                if (user.GetUnderlyingObject() is System.DirectoryServices.DirectoryEntry de)
                {
                    if (!string.IsNullOrWhiteSpace(firstName))
                        de.Properties["givenName"].Value = firstName;

                    if (!string.IsNullOrWhiteSpace(lastName))
                        de.Properties["sn"].Value = lastName;

                    if (!string.IsNullOrWhiteSpace(middleName))
                        de.Properties["initials"].Value = middleName.Substring(0, 1).ToUpper();

                    if (!string.IsNullOrWhiteSpace(jobTitle))
                        de.Properties["title"].Value = jobTitle;

                    if (!string.IsNullOrWhiteSpace(telephone))
                        de.Properties["telephoneNumber"].Value = telephone;

                    // на случай если displayName пуст — соберём
                    if (string.IsNullOrWhiteSpace(displayName) &&
                        !string.IsNullOrWhiteSpace(firstName) &&
                        !string.IsNullOrWhiteSpace(lastName))
                    {
                        if (!string.IsNullOrWhiteSpace(middleName))
                        {
                            var init = middleName[0];
                            de.Properties["displayName"].Value = $"{firstName} {init}. {lastName}";
                        }
                        else
                        {
                            de.Properties["displayName"].Value = $"{firstName} {lastName}";
                        }
                    }

                    de.CommitChanges();
                }

                user.ExpirePasswordNow();
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Не удалось создать пользователя в AD: " + ex.Message, ex);
            }
        }
        public IEnumerable<string> GetOrganizationalUnits()
        {
            var domain = _config["Ad:Domain"] ?? "kvsspb.lan";
            using var root = new System.DirectoryServices.DirectoryEntry($"LDAP://{domain}");
            using var searcher = new System.DirectoryServices.DirectorySearcher(root);

            searcher.Filter = "(objectClass=organizationalUnit)";
            searcher.SearchScope = System.DirectoryServices.SearchScope.Subtree;
            searcher.PropertiesToLoad.Add("distinguishedName");

            foreach (System.DirectoryServices.SearchResult res in searcher.FindAll())
            {
                if (res.Properties["distinguishedName"].Count > 0)
                {
                    yield return res.Properties["distinguishedName"][0]?.ToString() ?? "";
                }
            }
        }
        public void ResetPassword(string sam, string newPassword, bool mustChangeAtLogon)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null)
                throw new ApplicationException("Пользователь не найден");

            user.SetPassword(newPassword);
            if (mustChangeAtLogon)
            {
                user.ExpirePasswordNow();
            }
            user.Save();
        }

        public void UnlockUser(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null)
                throw new ApplicationException("Пользователь не найден");

            // в UserPrincipal есть метод для разблокировки
            user.UnlockAccount();
            user.Save();
        }

        public void UpdateUser(string sam, string? displayName, string? email)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null)
                throw new ApplicationException("Пользователь не найден");

            if (!string.IsNullOrWhiteSpace(displayName))
                user.DisplayName = displayName;

            if (!string.IsNullOrWhiteSpace(email))
                user.EmailAddress = email;

            user.Save();
        }
        public string? GetUserOuDn(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null) return null;

            if (user.GetUnderlyingObject() is System.DirectoryServices.DirectoryEntry de)
            {
                var dn = de.Properties["distinguishedName"]?.Value?.ToString();
                if (string.IsNullOrEmpty(dn)) return null;

                // DN = "CN=...,OU=...,OU=...,DC=..." → выкидываем первую часть "CN=..."
                var parts = dn.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 1) return null;

                return string.Join(",", parts.Skip(1)); // "OU=...,OU=...,DC=..."
            }

            return null;
        }

        public string? GetUserJobTitle(string sam)
        {
            using var ctx = GetContext();
            var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null) return null;

            if (user.GetUnderlyingObject() is System.DirectoryServices.DirectoryEntry de)
            {
                return de.Properties["title"]?.Value?.ToString();
            }

            return null;
        }

        public void CopyGroups(string fromSam, string toSam)
        {
            using var ctx = GetContext();
            var source = UserPrincipal.FindByIdentity(ctx, fromSam);
            var target = UserPrincipal.FindByIdentity(ctx, toSam);

            if (source == null || target == null)
                throw new ApplicationException("Не удалось найти пользователя-источник или нового пользователя при копировании групп.");

            // читаем memberOf у исходника (как мы делали ранее)
            if (source.GetUnderlyingObject() is not System.DirectoryServices.DirectoryEntry de)
                return;

            var memberOf = de.Properties["memberOf"];
            if (memberOf == null) return;

            foreach (var item in memberOf)
            {
                var dn = item?.ToString();
                if (string.IsNullOrEmpty(dn)) continue;

                using var groupEntry = new System.DirectoryServices.DirectoryEntry("LDAP://" + dn);
                var groupSam = groupEntry.Properties["sAMAccountName"]?.Value?.ToString();
                if (string.IsNullOrEmpty(groupSam)) continue;

                // добавляем нового пользователя в те же группы
                AddUserToGroup(toSam, groupSam);
            }
        }
        public IEnumerable<AdGroupDto> FindGroups(string? filter = null)
        {
            using var ctx = GetContext();

            var groupQuery = new GroupPrincipal(ctx);
            using var searcher = new PrincipalSearcher(groupQuery);

            var norm = filter?.Trim().ToLowerInvariant();

            foreach (var r in searcher.FindAll())
            {
                if (r is GroupPrincipal g)
                {
                    var sam = g.SamAccountName ?? g.Name ?? "";
                    if (string.IsNullOrEmpty(sam))
                        continue;

                    var displayName = g.Name ?? sam;

                    if (!string.IsNullOrEmpty(norm))
                    {
                        var dnLower = displayName.ToLowerInvariant();
                        if (!sam.ToLowerInvariant().Contains(norm) &&
                            !dnLower.Contains(norm))
                        {
                            continue;
                        }
                    }

                    string? description = null;
                    if (g.GetUnderlyingObject() is DirectoryEntry de)
                    {
                        description = de.Properties["description"]?.Value?.ToString();
                    }

                    yield return new AdGroupDto
                    {
                        Sam = sam,
                        Name = displayName,
                        Description = description
                    };
                }
            }
        }
        public void CreateGroup(string sam, string? description, string? ouDn)
        {
            var domain = _config["Ad:Domain"] ?? "kvsspb.lan";
            var svcUser = _config["Ad:User"];
            var svcPass = _config["Ad:Password"];

            PrincipalContext ctx;

            if (!string.IsNullOrWhiteSpace(ouDn))
            {
                ctx = (!string.IsNullOrWhiteSpace(svcUser) && !string.IsNullOrWhiteSpace(svcPass))
                    ? new PrincipalContext(ContextType.Domain, domain, ouDn, svcUser, svcPass)
                    : new PrincipalContext(ContextType.Domain, domain, ouDn);
            }
            else
            {
                ctx = (!string.IsNullOrWhiteSpace(svcUser) && !string.IsNullOrWhiteSpace(svcPass))
                    ? new PrincipalContext(ContextType.Domain, domain, svcUser, svcPass)
                    : new PrincipalContext(ContextType.Domain, domain);
            }

            try
            {
                var existing = GroupPrincipal.FindByIdentity(ctx, sam);
                if (existing != null)
                    throw new ApplicationException($"Группа '{sam}' уже существует.");

                var group = new GroupPrincipal(ctx)
                {
                    SamAccountName = sam,
                    Name = sam,
                    Description = description,
                    IsSecurityGroup = true,
                    GroupScope = GroupScope.Global
                };

                group.Save();
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Не удалось создать группу в AD: " + ex.Message, ex);
            }
        }

        public void DeleteGroup(string sam)
        {
            using var ctx = GetContext();
            var group = GroupPrincipal.FindByIdentity(ctx, sam);
            if (group == null)
                return;

            try
            {
                group.Delete();
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Не удалось удалить группу: " + ex.Message, ex);
            }
        }
        public IEnumerable<GroupMemberDto> GetGroupMembers(string groupSam)
        {
            using var ctx = GetContext();
            var group = GroupPrincipal.FindByIdentity(ctx, groupSam);
            if (group == null)
                yield break;

            // только прямые члены (false), чтобы не тащить всё дерево
            foreach (var m in group.GetMembers(false))
            {
                if (m is UserPrincipal up)
                {
                    var sam = up.SamAccountName ?? "";
                    if (string.IsNullOrEmpty(sam)) continue;

                    yield return new GroupMemberDto
                    {
                        Sam = sam,
                        DisplayName = up.DisplayName ?? sam,
                        Type = GroupMemberType.User
                    };
                }
                else if (m is GroupPrincipal gp)
                {
                    var sam = gp.SamAccountName ?? gp.Name ?? "";
                    if (string.IsNullOrEmpty(sam)) continue;

                    yield return new GroupMemberDto
                    {
                        Sam = sam,
                        DisplayName = gp.Name ?? sam,
                        Type = GroupMemberType.Group
                    };
                }
            }
        }

        public void AddGroupToGroup(string childGroupSam, string parentGroupSam)
        {
            using var ctx = GetContext();
            var child = GroupPrincipal.FindByIdentity(ctx, childGroupSam);
            var parent = GroupPrincipal.FindByIdentity(ctx, parentGroupSam);

            if (child == null || parent == null)
                return;

            if (!parent.Members.Contains(child))
            {
                parent.Members.Add(child);
                parent.Save();
            }
        }

        public void RemoveGroupFromGroup(string childGroupSam, string parentGroupSam)
        {
            using var ctx = GetContext();
            var child = GroupPrincipal.FindByIdentity(ctx, childGroupSam);
            var parent = GroupPrincipal.FindByIdentity(ctx, parentGroupSam);

            if (child == null || parent == null)
                return;

            if (parent.Members.Contains(child))
            {
                parent.Members.Remove(child);
                parent.Save();
            }
        }
    }
}
