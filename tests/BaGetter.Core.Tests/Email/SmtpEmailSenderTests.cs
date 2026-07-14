using System.Linq;
using BaGetter.Core.Configuration;
using BaGetter.Core.Email;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace BaGetter.Core.Tests.Email;

public class SmtpEmailSenderTests
{
    public class BuildMimeMessage
    {
        private static SmtpEmailSender BuildSender()
        {
            var emailOptions = Options.Create(new EmailOptions { FromAddress = "noreply@bagetter.test", FromName = "BaGetter" });
            var smtpOptions = Options.Create(new SmtpEmailOptions());
            return new SmtpEmailSender(emailOptions, smtpOptions);
        }

        [Fact]
        public void SetsSenderFromConfiguration()
        {
            var mime = BuildSender().BuildMimeMessage(new EmailMessage("to@test.com", "s", "b"));

            var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
            Assert.Equal("BaGetter", from.Name);
            Assert.Equal("noreply@bagetter.test", from.Address);
        }

        [Fact]
        public void MapsAllRecipients()
        {
            var mime = BuildSender().BuildMimeMessage(new EmailMessage(["a@test.com", "b@test.com"], "s", "b"));

            Assert.Equal(["a@test.com", "b@test.com"], mime.To.Mailboxes.Select(m => m.Address));
        }

        [Fact]
        public void SetsSubject()
        {
            var mime = BuildSender().BuildMimeMessage(new EmailMessage("to@test.com", "Hello", "b"));

            Assert.Equal("Hello", mime.Subject);
        }

        [Fact]
        public void UsesHtmlBodyForHtmlMessage()
        {
            var mime = BuildSender().BuildMimeMessage(new EmailMessage("to@test.com", "s", "<p>hi</p>", isBodyHtml: true));

            Assert.Equal("<p>hi</p>", mime.HtmlBody);
            Assert.Null(mime.TextBody);
        }

        [Fact]
        public void UsesTextBodyForPlainTextMessage()
        {
            var mime = BuildSender().BuildMimeMessage(new EmailMessage("to@test.com", "s", "hi", isBodyHtml: false));

            Assert.Equal("hi", mime.TextBody);
            Assert.Null(mime.HtmlBody);
        }
    }
}
