using CleanArchCqrs.Application.Users.Commands.Login;
using FluentValidation;
using FluentValidation.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Users.Validators.Login
{
    public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
    {
        public LoginCommandValidator()
        {
            RuleFor(x => x.Email)
           .NotEmpty()
           .EmailAddress()
           .MaximumLength(320);

            RuleFor(x => x.Password)
                .NotEmpty()
                .MaximumLength(20);
        }
    }
}
