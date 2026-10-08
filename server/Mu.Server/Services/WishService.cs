using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Mu.Server.Data;
using Mu.Wishes;

namespace Mu.Server.Services;

public sealed class WishService(AppDbContext db, TimeProvider clock)
{
    static string Author(string userId) => "玩家 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..6].ToLowerInvariant();
    long Now => clock.GetUtcNow().ToUnixTimeSeconds();

    IQueryable<WishItem> Items(string userId, IQueryable<WishEntry> query) => query.Select(w => new WishItem(w.Id, w.Title, w.Content,
        w.Status, w.Author, w.CreatedAt, w.DemoVotes + db.WishVotes.Count(v => v.WishId == w.Id),
        db.WishComments.Count(c => c.WishId == w.Id), db.WishVotes.Any(v => v.WishId == w.Id && v.UserId == userId),
        w.UserId == userId, w.IsDemo, w.ImageData != null));

    public async Task<WishList> ListAsync(string userId, string? filter, string? q, string? sort, int page, CancellationToken ct)
    {
        filter = string.IsNullOrEmpty(filter) ? "all" : filter;
        sort = string.IsNullOrEmpty(sort) ? "newest" : sort;
        if (filter is not ("all" or "hot" or "planned" or "developing" or "completed" or "mine")
            || sort is not ("newest" or "votes") || page < 1 || page > 10000 || q?.Length > 100)
            throw new ApiException(400, "invalid_wish_query", "筛选条件无效。");
        var counts = new WishCounts(await db.Wishes.CountAsync(ct),
            await db.Wishes.CountAsync(w => w.DemoVotes + db.WishVotes.Count(v => v.WishId == w.Id) >= 50, ct),
            await db.Wishes.CountAsync(w => w.Status == "planned", ct),
            await db.Wishes.CountAsync(w => w.Status == "developing", ct),
            await db.Wishes.CountAsync(w => w.Status == "completed", ct),
            await db.Wishes.CountAsync(w => w.UserId == userId, ct));
        var query = db.Wishes.AsNoTracking();
        if (filter is "planned" or "developing" or "completed") query = query.Where(w => w.Status == filter);
        if (filter == "mine") query = query.Where(w => w.UserId == userId);
        if (filter == "hot") query = query.Where(w => w.DemoVotes + db.WishVotes.Count(v => v.WishId == w.Id) >= 50);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(w => w.Content.Contains(q.Trim()) || w.Title.Contains(q.Trim()));
        var total = await query.CountAsync(ct);
        query = sort == "votes" || filter == "hot"
            ? query.OrderByDescending(w => w.DemoVotes + db.WishVotes.Count(v => v.WishId == w.Id)).ThenByDescending(w => w.CreatedAt).ThenBy(w => w.Id)
            : query.OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id);
        var ids = await query.Skip((page - 1) * 20).Take(20).Select(w => w.Id).ToArrayAsync(ct);
        var items = await Items(userId, db.Wishes.Where(w => ids.Contains(w.Id))).ToArrayAsync(ct);
        return new(items.OrderBy(w => Array.IndexOf(ids, w.Id)).ToArray(), counts, total, page, 20);
    }

    public async Task<WishDetail> DetailAsync(string userId, string id, CancellationToken ct)
    {
        var wish = await Items(userId, db.Wishes.Where(w => w.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw new ApiException(404, "wish_not_found", "愿望不存在。");
        var image = await db.Wishes.Where(w => w.Id == id).Select(w => w.ImageData).SingleAsync(ct);
        // Return the latest discussion in chronological order, with a bounded response.
        var comments = await db.WishComments.Where(c => c.WishId == id).OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Take(100).Select(c => new WishComment(c.Id, c.Author, c.Content, c.CreatedAt, c.IsDemo)).ToArrayAsync(ct);
        return new(wish, image, comments.Reverse().ToArray());
    }

    public async Task<WishDetail> SubmitAsync(string userId, WishSubmission input, CancellationToken ct)
    {
        var content = input.Content?.Trim() ?? "";
        if (!Guid.TryParseExact(input.SubmissionId, "N", out _) || content.Length is < 5 or > 500)
            throw new ApiException(400, "invalid_wish", "请填写 5 至 500 字的愿望。");
        ValidateImage(input.ImageData);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.Wishes.AsNoTracking().SingleOrDefaultAsync(w => w.UserId == userId && w.SubmissionId == input.SubmissionId, ct);
        if (existing != null)
        {
            if (existing.Content != content || existing.ImageData != input.ImageData)
                throw new ApiException(409, "wish_conflict", "提交标识已用于另一条愿望，请重新提交。");
            return await DetailAsync(userId, existing.Id, ct);
        }
        if (await db.Wishes.CountAsync(w => w.UserId == userId && w.CreatedAt > Now - 86400, ct) >= 10)
            throw new ApiException(429, "wish_rate_limited", "每天最多提交 10 条愿望，请稍后再试。", 3600);
        var title = content.Split(new[] { '\r', '\n', '。', '！', '？' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? content;
        var entry = new WishEntry { UserId = userId, SubmissionId = input.SubmissionId, Content = content,
            Title = title.Length > 32 ? title[..32] + "…" : title, Author = Author(userId), CreatedAt = Now, ImageData = input.ImageData };
        db.Wishes.Add(entry);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await DetailAsync(userId, entry.Id, ct);
    }

    public async Task<WishItem> VoteAsync(string userId, string id, bool voted, CancellationToken ct)
    {
        if (!await db.Wishes.AnyAsync(w => w.Id == id, ct)) throw new ApiException(404, "wish_not_found", "愿望不存在。");
        if (voted)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"WishVotes\" (\"WishId\", \"UserId\") VALUES ({id}, {userId}) ON CONFLICT DO NOTHING", ct);
        else await db.WishVotes.Where(v => v.WishId == id && v.UserId == userId).ExecuteDeleteAsync(ct);
        return await Items(userId, db.Wishes.Where(w => w.Id == id)).SingleAsync(ct);
    }

    public async Task<WishComment> CommentAsync(string userId, string id, WishCommentRequest input, CancellationToken ct)
    {
        var content = input.Content?.Trim() ?? "";
        if (content.Length is < 1 or > 300) throw new ApiException(400, "invalid_wish_comment", "评论须为 1 至 300 字。");
        if (!await db.Wishes.AnyAsync(w => w.Id == id, ct)) throw new ApiException(404, "wish_not_found", "愿望不存在。");
        if (await db.WishComments.CountAsync(c => c.UserId == userId && c.CreatedAt > Now - 3600, ct) >= 30)
            throw new ApiException(429, "wish_comment_rate_limited", "评论过于频繁，请稍后再试。", 3600);
        var entry = new WishCommentEntry { WishId = id, UserId = userId, Author = Author(userId), Content = content, CreatedAt = Now };
        db.WishComments.Add(entry);
        await db.SaveChangesAsync(ct);
        return new(entry.Id, entry.Author, entry.Content, entry.CreatedAt, false);
    }

    static void ValidateImage(string? data)
    {
        if (data == null) return;
        if (data.Length > 350000) throw new ApiException(400, "invalid_wish_image", "图片过大，请选择较小的图片。");
        var jpeg = data.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal);
        var png = data.StartsWith("data:image/png;base64,", StringComparison.Ordinal);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]); }
        catch (FormatException) { throw new ApiException(400, "invalid_wish_image", "图片格式无效。"); }
        var validPng = png && bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            && ValidDimensions(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        if (!(jpeg && ValidJpeg(bytes)) && !validPng)
            throw new ApiException(400, "invalid_wish_image", "仅支持 PNG / JPEG 图片。");
    }

    static bool ValidDimensions(int width, int height) => width is > 0 and <= 4096 && height is > 0 and <= 4096 && (long)width * height <= 4000000;

    static bool ValidJpeg(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9) return false;
        for (var offset = 2; offset + 3 < bytes.Length;)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset + 2 >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (marker is 0xda or 0xd9) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
                return length >= 8 && ValidDimensions(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2)));
            offset += length;
        }
        return false;
    }
}
