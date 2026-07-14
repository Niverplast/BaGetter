using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BaGetter.Core.Configuration;
using Xunit;

namespace BaGetter.Core.Tests.Configuration;

public class PatExpiryNotificationOptionsTests
{
    private static IReadOnlyList<ValidationResult> RunValidation(PatExpiryNotificationOptions options)
    {
        return options.Validate(new ValidationContext(options)).ToList();
    }

    public class EffectiveNotificationDays
    {
        [Fact]
        public void FallsBackToDefaultsWhenNull()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = null };

            Assert.Equal(PatExpiryNotificationOptions.DefaultNotificationDaysBeforeExpiry, options.EffectiveNotificationDays);
        }

        [Fact]
        public void FallsBackToDefaultsWhenEmpty()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = [] };

            Assert.Equal(PatExpiryNotificationOptions.DefaultNotificationDaysBeforeExpiry, options.EffectiveNotificationDays);
        }

        [Fact]
        public void UsesConfiguredValuesWhenProvided()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = [30, 3] };

            Assert.Equal([30, 3], options.EffectiveNotificationDays);
        }
    }

    public class Validate
    {
        [Fact]
        public void AcceptsNullThresholds()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = null };

            Assert.Empty(RunValidation(options));
        }

        [Fact]
        public void AcceptsDistinctThresholds()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = [14, 7, 2, 0] };

            Assert.Empty(RunValidation(options));
        }

        [Fact]
        public void RejectsDuplicateThresholds()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = [14, 14] };

            Assert.Contains(RunValidation(options), r => r.ErrorMessage.Contains("distinct"));
        }

        [Fact]
        public void RejectsNegativeThresholds()
        {
            var options = new PatExpiryNotificationOptions { NotificationDaysBeforeExpiry = [-1] };

            Assert.Contains(RunValidation(options), r => r.ErrorMessage.Contains("zero or greater"));
        }

        [Fact]
        public void RejectsNonAbsoluteWebBaseUrl()
        {
            var options = new PatExpiryNotificationOptions { WebBaseUrl = "not-a-url" };

            Assert.Contains(RunValidation(options), r => r.ErrorMessage.Contains("WebBaseUrl"));
        }
    }
}
