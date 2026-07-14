using System;
using BaGetter.Core.Notifications;
using Xunit;

namespace BaGetter.Core.Tests.Notifications;

public class PatExpiryThresholdTests
{
    private static readonly int[] Thresholds = [14, 7, 2, 0];

    public class GetDueThreshold
    {
        [Theory]
        [InlineData(20, null)]   // further out than every threshold
        [InlineData(15, null)]
        [InlineData(14, 14)]     // exactly at the widest threshold
        [InlineData(10, 14)]
        [InlineData(8, 14)]
        [InlineData(7, 7)]
        [InlineData(5, 7)]
        [InlineData(2, 2)]
        [InlineData(1, 2)]
        [InlineData(0, 0)]       // expiry day itself
        [InlineData(-1, 0)]      // already expired still maps to the 0-day stage
        public void ReturnsSmallestReachedThreshold(int daysUntil, int? expected)
        {
            Assert.Equal(expected, PatExpiryNotificationService.GetDueThreshold(daysUntil, Thresholds));
        }
    }

    public class DaysUntil
    {
        [Fact]
        public void CountsWholeCalendarDaysWithExpiryDayAsZero()
        {
            var now = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);

            // Same calendar day (later time) is 0, not -1.
            Assert.Equal(0, PatExpiryNotificationService.DaysUntil(new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), now));
            Assert.Equal(14, PatExpiryNotificationService.DaysUntil(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), now));
            Assert.Equal(-1, PatExpiryNotificationService.DaysUntil(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc), now));
        }
    }
}
