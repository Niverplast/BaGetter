using System.Threading.Tasks;
using BaGetter.Core.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BaGetter.Core.Tests.Email;

public class NullEmailSenderTests
{
    public class SendAsync
    {
        [Fact]
        public async Task DropsMessageWithoutThrowing()
        {
            var sender = new NullEmailSender(NullLogger<NullEmailSender>.Instance);

            await sender.SendAsync(new EmailMessage("to@example.com", "subject", "body"));
        }
    }
}
