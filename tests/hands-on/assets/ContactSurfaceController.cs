using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Cms.Web.Website.Controllers;

namespace UmbracoSite.Controllers;

public class ContactFormModel
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Subject { get; set; }

    [Required, StringLength(4000)]
    public string Message { get; set; } = string.Empty;
}

public class SignUpFormModel
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(10)]
    public string Password { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Company { get; set; }

    public bool MarketingOptIn { get; set; }
}

public class ContactSurfaceController : SurfaceController
{
    public const string SubmissionsFolderAlias = "formSubmissions";
    public const string SubmissionAlias = "contactSubmission";
    public const string MemberTypeAlias = "siteMember";
    public const string MemberGroup = "Subscribers";

    private readonly IContentService _contentService;
    private readonly IMemberManager _memberManager;
    private readonly IMemberService _memberService;
    private readonly IMemberSignInManager _memberSignInManager;
    private readonly ILogger<ContactSurfaceController> _logger;

    public ContactSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IContentService contentService,
        IMemberManager memberManager,
        IMemberService memberService,
        IMemberSignInManager memberSignInManager,
        ILogger<ContactSurfaceController> logger
    )
        : base(
            umbracoContextAccessor,
            databaseFactory,
            services,
            appCaches,
            profilingLogger,
            publishedUrlProvider
        )
    {
        _contentService = contentService;
        _memberManager = memberManager;
        _memberService = memberService;
        _memberSignInManager = memberSignInManager;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(ContactFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return CurrentUmbracoPage();
        }

        var folder = _contentService
            .GetRootContent()
            .FirstOrDefault(x => x.ContentType.Alias == SubmissionsFolderAlias);
        if (folder is null)
        {
            _logger.LogError(
                "No '{Alias}' root node to store contact submissions in",
                SubmissionsFolderAlias
            );
            ModelState.AddModelError(string.Empty, "Sorry, your message could not be sent.");
            return CurrentUmbracoPage();
        }

        var submittedAt = DateTime.UtcNow;
        var submission = _contentService.Create(
            // Invariant so the node name doesn't depend on the visitor's culture (da-DK uses "22.09.48").
            $"{model.Name} ({submittedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)})",
            folder.Key,
            SubmissionAlias
        );
        submission.SetValue("fullName", model.Name);
        submission.SetValue("email", model.Email);
        submission.SetValue("subject", model.Subject);
        submission.SetValue("message", model.Message);
        submission.SetValue("submittedAt", submittedAt);
        submission.SetValue("culture", CultureInfo.CurrentUICulture.Name);
        _contentService.Save(submission);

        TempData["contactSuccess"] = true;
        return RedirectToCurrentUmbracoPage();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignUp(SignUpFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return CurrentUmbracoPage();
        }

        if (await _memberManager.FindByEmailAsync(model.Email) is not null)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "A member with this email already exists."
            );
            return CurrentUmbracoPage();
        }

        var identityUser = MemberIdentityUser.CreateNew(
            model.Email,
            model.Email,
            MemberTypeAlias,
            isApproved: true,
            model.Name
        );
        var result = await _memberManager.CreateAsync(identityUser, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(model.Password), error.Description);
            }
            return CurrentUmbracoPage();
        }

        var member = _memberService.GetByKey(identityUser.Key);
        if (member is not null)
        {
            member.SetValue("company", model.Company);
            member.SetValue("marketingOptIn", model.MarketingOptIn);
            member.SetValue("signupSource", $"Contact page ({CultureInfo.CurrentUICulture.Name})");
            _memberService.Save(member);
            _memberService.AssignRole(member.Username, MemberGroup);
        }

        await _memberSignInManager.SignInAsync(identityUser, isPersistent: false);

        TempData["signupSuccess"] = true;
        return RedirectToCurrentUmbracoPage();
    }
}
