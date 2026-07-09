using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaGetter.Core.Email;
using BaGetter.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

public class EmailSenderProviderTests
{
    public class GetServiceFromProviders
    {
        [Fact]
        public void ReturnsSmtpSenderWhenTypeIsSmtp()
        {
            var services = BuildProvider(new Dictionary<string, string> { ["Email:Type"] = "Smtp" });

            var sender = DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>(services);

            Assert.IsType<SmtpEmailSender>(sender);
        }

        [Fact]
        public void ReturnsNullSenderWhenTypeIsNull()
        {
            var services = BuildProvider(new Dictionary<string, string> { ["Email:Type"] = "Null" });

            var sender = DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>(services);

            Assert.IsType<NullEmailSender>(sender);
        }

        [Fact]
        public void ReturnsNullSenderWhenUnconfigured()
        {
            var services = BuildProvider(new Dictionary<string, string>());

            var sender = DependencyInjectionExtensions.GetServiceFromProviders<IEmailSender>(services);

            Assert.IsType<NullEmailSender>(sender);
        }

        private static IServiceProvider BuildProvider(Dictionary<string, string> config)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .Build();

            return new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddOptions()
                .AddBaGetterApplication(_ => { })
                .Services
                .BuildServiceProvider();
        }
    }
}
