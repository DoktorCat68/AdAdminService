using AdAdminPortal.Models;
using AdAdminPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AdAdminPortal.Controllers
{
    [Authorize(Policy = "AdAdminsOnly")]
    public class AdGroupsController : Controller
    {
        private readonly IAdService _ad;

        public AdGroupsController(IAdService ad)
        {
            _ad = ad;
        }

        // Список групп + поиск
        [HttpGet]
        public IActionResult Index(string? q)
        {
            var groups = _ad.GetAllGroups(q).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
            ViewBag.Query = q;
            return View(groups);
        }

        // --- СОЗДАНИЕ ГРУППЫ ---

        [HttpGet]
        public IActionResult Create()
        {
            var ousDn = _ad.GetOrganizationalUnits().ToList();
            var model = new CreateGroupViewModel
            {
                OuTree = BuildOuTree(ousDn)
            };
            return View(model);
        }

        [HttpPost]
        public IActionResult Create(CreateGroupViewModel model)
        {
            var ousDn = _ad.GetOrganizationalUnits().ToList();
            model.OuTree = BuildOuTree(ousDn);

            if (string.IsNullOrWhiteSpace(model.Sam))
                ModelState.AddModelError(nameof(model.Sam), "Имя группы (sAMAccountName) обязательно.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                _ad.CreateGroup(model.Sam, model.Description, model.SelectedOu);
                return RedirectToAction("Details", new { id = model.Sam });
            }
            catch (ApplicationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }
        }

        // --- СТРОКА ГРУППЫ + ЧЛЕНЫ ---

        [HttpGet]
        public IActionResult Details(string id)
        {
            // члены группы (юзеры + группы)
            var members = _ad.GetGroupMembers(id)
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // все пользователи
            var allUsers = _ad.FindUsers("").ToList();
            // все группы как возможные nested
            var allGroups = _ad.GetAllGroups().ToList();

            var memberUserSams = members
                .Where(m => m.Type == GroupMemberType.User)
                .Select(m => m.Sam)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var memberGroupSams = members
                .Where(m => m.Type == GroupMemberType.Group)
                .Select(m => m.Sam)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var available = new List<GroupMemberDto>();

            // доступные пользователи
            foreach (var u in allUsers)
            {
                if (string.IsNullOrEmpty(u.Sam)) continue;
                if (memberUserSams.Contains(u.Sam)) continue;

                available.Add(new GroupMemberDto
                {
                    Sam = u.Sam!,
                    DisplayName = string.IsNullOrWhiteSpace(u.DisplayName)
                        ? u.Sam!
                        : u.DisplayName!,
                    Type = GroupMemberType.User
                });
            }

            // доступные группы
            foreach (var gSam in allGroups)
            {
                if (string.IsNullOrWhiteSpace(gSam)) continue;
                if (string.Equals(gSam, id, StringComparison.OrdinalIgnoreCase)) continue; // не добавляем саму себя
                if (memberGroupSams.Contains(gSam)) continue;

                available.Add(new GroupMemberDto
                {
                    Sam = gSam,
                    DisplayName = gSam,
                    Type = GroupMemberType.Group
                });
            }
            ViewBag.GroupSam = id;
            ViewBag.Members = members;
            ViewBag.Available = available;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddMembers(string id, string members)
        {
            var tokens = (members ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var token in tokens)
            {
                var parts = token.Split(':', 2);
                if (parts.Length != 2) continue;

                var type = parts[0];
                var sam = parts[1];

                if (string.Equals(type, "user", StringComparison.OrdinalIgnoreCase))
                {
                    _ad.AddUserToGroup(sam, id);
                }
                else if (string.Equals(type, "group", StringComparison.OrdinalIgnoreCase))
                {
                    _ad.AddGroupToGroup(sam, id);
                }
            }

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveMembers(string id, string members)
        {
            var tokens = (members ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var token in tokens)
            {
                var parts = token.Split(':', 2);
                if (parts.Length != 2) continue;

                var type = parts[0];
                var sam = parts[1];

                if (string.Equals(type, "user", StringComparison.OrdinalIgnoreCase))
                {
                    _ad.RemoveUserFromGroup(sam, id);
                }
                else if (string.Equals(type, "group", StringComparison.OrdinalIgnoreCase))
                {
                    _ad.RemoveGroupFromGroup(sam, id);
                }
            }

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string id)
        {
            try
            {
                _ad.DeleteGroup(id);
                TempData["Message"] = "Группа удалена.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Index");
        }

        // --- ХЕЛПЕРЫ ДЛЯ OU-ДЕРЕВА (скопированы из AdUsersController) ---

        private static string ExtractOuName(string dn)
        {
            var first = dn.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (first.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                return first.Substring(3);
            return first;
        }

        private static string? GetParentOuDn(string dn)
        {
            var parts = dn.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(p => p.Trim())
                          .ToArray();
            if (parts.Length <= 1)
                return null;

            var parentDn = string.Join(",", parts.Skip(1));
            return parentDn;
        }

        private static List<OuTreeNode> BuildOuTree(IEnumerable<string> dnList)
        {
            var dnArray = dnList.Distinct().ToArray();
            var nodes = new Dictionary<string, OuTreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var dn in dnArray)
            {
                nodes[dn] = new OuTreeNode
                {
                    Name = ExtractOuName(dn),
                    DistinguishedName = dn
                };
            }

            var roots = new List<OuTreeNode>();

            foreach (var kvp in nodes)
            {
                var dn = kvp.Key;
                var node = kvp.Value;
                var parentDn = GetParentOuDn(dn);
                if (parentDn != null && nodes.TryGetValue(parentDn, out var parentNode))
                {
                    parentNode.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            void Sort(List<OuTreeNode> list)
            {
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var n in list)
                {
                    if (n.Children.Count > 0)
                        Sort(n.Children);
                }
            }

            Sort(roots);
            return roots;
        }
    }
}