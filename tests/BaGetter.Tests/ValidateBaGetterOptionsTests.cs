using System.Linq;
using BaGetter.Core.Configuration;
using Xunit;

namespace BaGetter.Tests;

public class ValidateBaGetterOptionsTests
{
    public class ValidateEmail
    {
        private static bool HasEmailTypeFailure(BaGetterOptions options)
        {
            var result = new ValidateBaGetterOptions().Validate(null, options);
            return result.Failed
                && result.Failures.Any(f => f.Contains($"{nameof(BaGetterOptions.Email)}:{nameof(EmailOptions.Type)}"));
        }

        [Theory]
        [InlineData("Smtp")]
        [InlineData("Graph")]
        [InlineData("Null")]
        [InlineData("smtp")] // case-insensitive
        public void AcceptsValidEmailType(string type)
        {
            Assert.False(HasEmailTypeFailure(new BaGetterOptions { Email = new EmailOptions { Type = type } }));
        }

        [Fact]
        public void AcceptsMissingEmailSection()
        {
            Assert.False(HasEmailTypeFailure(new BaGetterOptions()));
        }

        [Fact]
        public void AcceptsEmptyEmailType()
        {
            Assert.False(HasEmailTypeFailure(new BaGetterOptions { Email = new EmailOptions { Type = "" } }));
        }

        [Theory]
        [InlineData("smpt")]
        [InlineData("sendgrid")]
        public void RejectsInvalidEmailType(string type)
        {
            Assert.True(HasEmailTypeFailure(new BaGetterOptions { Email = new EmailOptions { Type = type } }));
        }
    }
}
