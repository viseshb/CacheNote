using System.IO;
using CacheNote.Core.Data;
using CacheNote.Core.Infrastructure;
using CacheNote.Core.Models;

namespace CacheNote.Core.Services;

public interface IAttachmentService
{
    IReadOnlyList<Attachment> GetForNote(long noteId);

    /// <summary>Copy image bytes into the attachments folder + record it. Returns the row (with AbsolutePath set via <see cref="AbsolutePath"/>).</summary>
    Attachment SaveImage(long noteId, byte[] bytes, string extension, string? mime = null);

    /// <summary>Absolute path on disk for an attachment (attachments dir + rel path).</summary>
    string AbsolutePath(Attachment a);

    void Remove(long id);
}

public sealed class AttachmentService : IAttachmentService
{
    private readonly IAttachmentRepository _repo;
    private readonly IAppPaths _paths;

    public AttachmentService(IAttachmentRepository repo, IAppPaths paths)
    {
        _repo = repo;
        _paths = paths;
    }

    public IReadOnlyList<Attachment> GetForNote(long noteId) => _repo.GetByNote(noteId);

    public Attachment SaveImage(long noteId, byte[] bytes, string extension, string? mime = null)
    {
        Directory.CreateDirectory(_paths.AttachmentsDir);
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        // Unique, collision-proof file name (no Guid-in-script concerns; this runs in the app).
        var name = $"img_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Random.Shared.Next(1000, 9999)}{extension.ToLowerInvariant()}";
        var abs = Path.Combine(_paths.AttachmentsDir, name);
        File.WriteAllBytes(abs, bytes);

        var a = new Attachment
        {
            NoteId = noteId,
            Filename = name,
            RelPath = name,             // relative to AttachmentsDir
            Mime = mime ?? MimeFor(extension),
            SizeBytes = bytes.LongLength,
        };
        a.Id = _repo.Insert(a);
        return a;
    }

    public string AbsolutePath(Attachment a) => Path.Combine(_paths.AttachmentsDir, a.RelPath);

    public void Remove(long id)
    {
        var a = _repo.GetById(id);
        if (a is not null)
        {
            try
            {
                var abs = AbsolutePath(a);
                if (File.Exists(abs))
                    File.Delete(abs);
            }
            catch { /* leave the row removal to proceed even if the file is locked */ }
        }
        _repo.Delete(id);
    }

    private static string MimeFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };
}
