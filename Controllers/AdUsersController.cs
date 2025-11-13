using AdAdminPortal.Models;
using AdAdminPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AdAdminPortal.Controllers
{
    [Authorize(Policy = "AdAdminsOnly")]
    public class AdUsersController : Controller
    {
        private readonly IAdService _ad;

        public AdUsersController(IAdService ad)
        {
            _ad = ad;
        }

        public IActionResult Index(string? q)
        {
            var users = string.IsNullOrWhiteSpace(q)
                ? Enumerable.Empty<AdAdminPortal.Models.AdUserDto>()
                : _ad.FindUsers(q);

            ViewBag.Query = q;
            return View(users);
        }

        public IActionResult Details(string id)
        {
            var user = _ad.GetUser(id);
            if (user == null) return NotFound();

            var userGroups = _ad.GetUserGroups(id).OrderBy(g => g).ToList();
            var allGroups = _ad.GetAllGroups().OrderBy(g => g).ToList();

            var available = allGroups
                .Where(g => !userGroups.Contains(g, StringComparer.OrdinalIgnoreCase))
                .ToList();

            ViewBag.UserGroups = userGroups;
            ViewBag.AvailableGroups = available;

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Disable(string id)
        {
            _ad.DisableUser(id);
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Enable(string id)
        {
            _ad.EnableUser(id);
            return RedirectToAction("Details", new { id });
        }

        // массовое ДОБАВЛЕНИЕ: из правой таблицы → пользователю
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddGroups(string id, string groups)
        {
            var arr = (groups ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var g in arr)
                _ad.AddUserToGroup(id, g);

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RemoveGroups(string id, string groups)
        {
            var arr = (groups ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var g in arr)
                _ad.RemoveUserFromGroup(id, g);

            return RedirectToAction("Details", new { id });
        }

        [HttpGet]
        public IActionResult Create()
        {
            var ousDn = _ad.GetOrganizationalUnits().ToList();
            var model = new CreateAdUserViewModel
            {
                OuTree = BuildOuTree(ousDn)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CreateAdUserViewModel model)
        {
            var ousDn = _ad.GetOrganizationalUnits().ToList();
            model.OuTree = BuildOuTree(ousDn);

            if (string.IsNullOrWhiteSpace(model.Sam))
                ModelState.AddModelError(nameof(model.Sam), "Логин обязателен");
            if (string.IsNullOrWhiteSpace(model.Password))
                ModelState.AddModelError(nameof(model.Password), "Пароль обязателен");
            if (model.Password != model.ConfirmPassword)
                ModelState.AddModelError(nameof(model.ConfirmPassword), "Пароли не совпадают");

            if (!ModelState.IsValid)
                return View(model);

            // Имя для displayName: Имя И. Фамилия
            string? displayName = null;
            if (!string.IsNullOrWhiteSpace(model.FirstName) && !string.IsNullOrWhiteSpace(model.LastName))
            {
                if (!string.IsNullOrWhiteSpace(model.MiddleName))
                    displayName = $"{model.FirstName!.Trim()} {model.MiddleName!.Trim()[0]}. {model.LastName!.Trim()}";
                else
                    displayName = $"{model.FirstName!.Trim()} {model.LastName!.Trim()}";
            }

            try
            {
                _ad.CreateUser(
                    model.Sam,
                    model.Password,
                    displayName,
                    model.Email,
                    model.SelectedOu,
                    model.FirstName,
                    model.MiddleName,
                    model.LastName,
                    model.JobTitle,
                    model.Telephone
                );

                // Если это "создать как..." и включено "копировать группы" — докинем группы
                if (!string.IsNullOrWhiteSpace(model.TemplateSam) && model.CloneGroups)
                {
                    _ad.CopyGroups(model.TemplateSam, model.Sam);
                }
            }
            catch (ApplicationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(model);
            }

            return RedirectToAction("Details", new { id = model.Sam });
        }
        [HttpGet]
        public IActionResult CreateLike(string id)
        {
            // id — это SAM пользователя, с которого копируем
            var ousDn = _ad.GetOrganizationalUnits().ToList();

            var model = new CreateAdUserViewModel
            {
                OuTree = BuildOuTree(ousDn),
                TemplateSam = id,
                CloneGroups = true
            };

            // Подтянем OU источника
            var ouDn = _ad.GetUserOuDn(id);
            if (!string.IsNullOrWhiteSpace(ouDn))
                model.SelectedOu = ouDn;

            // Подтянем должность источника (телефон НЕ копируем)
            var title = _ad.GetUserJobTitle(id);
            if (!string.IsNullOrWhiteSpace(title))
                model.JobTitle = title;

            // Email и телефон остаются пустыми — админ сам введёт
            return View("Create", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetPassword(string id, string newPassword, string confirmPassword, bool mustChange = true)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                TempData["Error"] = "Пароль не может быть пустым.";
                return RedirectToAction("Details", new { id });
            }

            if (newPassword != confirmPassword)
            {
                TempData["Error"] = "Пароли не совпадают.";
                return RedirectToAction("Details", new { id });
            }

            try
            {
                _ad.ResetPassword(id, newPassword, mustChange);
                TempData["Message"] = "Пароль успешно изменён.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Unlock(string id)
        {
            try
            {
                _ad.UnlockUser(id);
                TempData["Message"] = "Учётная запись разблокирована.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateUser(string id, string? displayName, string? email)
        {
            try
            {
                _ad.UpdateUser(id, displayName, email);
                TempData["Message"] = "Пользователь обновлён.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction("Details", new { id });
        }
        private static string FriendlyOuLabel(string dn)
        {
            // Превращаем "OU=Dev,OU=SPB,DC=kvsspb,DC=lan" -> "SPB / Dev"
            var parts = dn.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(s => s.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                          .Select(s => s.Substring(3))
                          .Reverse(); // от корня к листу
            var label = string.Join(" / ", parts);
            return string.IsNullOrWhiteSpace(label) ? dn : label;
        }
        private static string ExtractOuName(string dn)
        {
            // "OU=Dev,OU=Users,DC=kvsspb,DC=lan" -> "Dev"
            var first = dn.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (first.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                return first.Substring(3);
            return first;
        }

        private static string? GetParentOuDn(string dn)
        {
            // Родитель DN — это DN без первой части: "OU=Dev,OU=Users,..." -> "OU=Users,..." 
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

            // создаём узлы
            foreach (var dn in dnArray)
            {
                nodes[dn] = new OuTreeNode
                {
                    Name = ExtractOuName(dn),
                    DistinguishedName = dn
                };
            }

            var roots = new List<OuTreeNode>();

            // связываем родители/дети
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
                    // нет OU-родителя — считаем корнем
                    roots.Add(node);
                }
            }

            // сортировка для красоты
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