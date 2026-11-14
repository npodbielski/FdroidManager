using System.Text.RegularExpressions;
using FluentValidation;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;

namespace FdroidMgr.Api.Validation;

[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedType.Global", Justification = "instantiated by the FluentValidation DI scanner via IValidator<T>")]
public partial class CreateAppRequestValidator : AbstractValidator<CreateAppRequest>
{
    public CreateAppRequestValidator()
    {
        RuleFor(x => x.PackageName).NotEmpty().Matches(PackageRegex());
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Summary).MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(4000);
    }

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")]
    private static partial Regex PackageRegex();
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedType.Global", Justification = "instantiated by the FluentValidation DI scanner via IValidator<T>")]
public class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200)
            .WithMessage("name is required");
        RuleFor(x => x.Scopes).NotNull().NotEmpty().WithMessage("at least one scope is required");
        RuleForEach(x => x.Scopes).Must(Scopes.IsValid)
            .WithMessage(s => $"invalid scope: {s}");
        RuleFor(x => x.ExpiresDays).GreaterThan(0).When(x => x.ExpiresDays is not null);
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedType.Global", Justification = "instantiated by the FluentValidation DI scanner via IValidator<T>")]
public class UpdateAppRequestValidator : AbstractValidator<UpdateAppRequest>
{
    public UpdateAppRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(50);
        RuleFor(x => x.Summary).MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}
