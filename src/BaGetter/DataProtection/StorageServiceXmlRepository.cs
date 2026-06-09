using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BaGetter.Core.Storage;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace BaGetter.DataProtection;

/// <summary>
/// Persists the Data Protection key ring through BaGetter's <see cref="IStorageService"/>, so the
/// keys live wherever packages live — for every storage backend (FileSystem, Azure Blob, S3, GCS,
/// Aliyun OSS, Tencent COS). This keeps antiforgery and auth cookies valid across container restarts
/// and new revisions; without it the key ring lives in the container's ephemeral filesystem and is
/// regenerated on every restart, invalidating all existing cookies (form POSTs then fail with 400).
/// </summary>
/// <remarks>
/// <see cref="IStorageService"/> has no list operation, so all key elements are kept in a single
/// aggregate document. Key-ring writes are rare (a new key is created roughly every 90 days), and
/// BaGetter runs a single replica, so the read-modify-write here is not a practical contention point.
/// </remarks>
internal sealed class StorageServiceXmlRepository : IXmlRepository
{
    private const string KeyRingPath = "dataprotection/keyring.xml";
    private const string RootElementName = "keyRing";

    private readonly IServiceProvider _services;

    public StorageServiceXmlRepository(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        // IXmlRepository is a synchronous contract. ASP.NET Core has no synchronization context,
        // so blocking on the async storage call here cannot deadlock.
        var document = LoadAsync().GetAwaiter().GetResult();
        return document?.Root?.Elements().ToList() ?? (IReadOnlyCollection<XElement>)Array.Empty<XElement>();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        StoreAsync(element).GetAwaiter().GetResult();
    }

    private async Task<XDocument> LoadAsync()
    {
        using var scope = _services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        await using var stream = await storage.GetAsync(KeyRingPath, CancellationToken.None);
        if (stream == null)
        {
            return null;
        }

        return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }

    private async Task StoreAsync(XElement element)
    {
        using var scope = _services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        XDocument document;
        bool exists;
        await using (var existing = await storage.GetAsync(KeyRingPath, CancellationToken.None))
        {
            exists = existing != null;
            document = exists
                ? await XDocument.LoadAsync(existing, LoadOptions.None, CancellationToken.None)
                : new XDocument(new XElement(RootElementName));
        }

        document.Root.Add(element);

        // PutAsync refuses to overwrite existing content (packages are immutable), so replace the
        // key ring by deleting it first.
        if (exists)
        {
            await storage.DeleteAsync(KeyRingPath, CancellationToken.None);
        }

        using var buffer = new MemoryStream();
        document.Save(buffer);
        buffer.Position = 0;

        await storage.PutAsync(KeyRingPath, buffer, "application/xml", CancellationToken.None);
    }
}
