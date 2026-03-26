using System;

namespace BaGetter.Core.Entities;

public class FeedPermission
{
    public Guid Id { get; set; }
    public string FeedId { get; set; }
    public PrincipalType PrincipalType { get; set; }
    public Guid PrincipalId { get; set; }
    public bool CanPush { get; set; }
    public bool CanPull { get; set; }
}
