using Microsoft.EntityFrameworkCore;

namespace Mu.Server.Data;

// Additive module tables leave the account schema and its existing migrations intact.
public static class WishPoolSchema
{
    public static Task InitializeAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "Wishes" (
            "Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NULL, "SubmissionId" TEXT NOT NULL,
            "Title" TEXT NOT NULL, "Content" TEXT NOT NULL, "Status" TEXT NOT NULL,
            "Author" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL, "IsDemo" INTEGER NOT NULL,
            "DemoVotes" INTEGER NOT NULL, "ImageData" TEXT NULL,
            FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Wishes_UserId_SubmissionId" ON "Wishes" ("UserId", "SubmissionId");
        CREATE INDEX IF NOT EXISTS "IX_Wishes_CreatedAt" ON "Wishes" ("CreatedAt");
        CREATE TABLE IF NOT EXISTS "WishVotes" (
            "WishId" TEXT NOT NULL, "UserId" TEXT NOT NULL, PRIMARY KEY ("WishId", "UserId"),
            FOREIGN KEY ("WishId") REFERENCES "Wishes" ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS "IX_WishVotes_UserId" ON "WishVotes" ("UserId");
        CREATE TABLE IF NOT EXISTS "WishComments" (
            "Id" TEXT NOT NULL PRIMARY KEY, "WishId" TEXT NOT NULL, "UserId" TEXT NULL,
            "Author" TEXT NOT NULL, "Content" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL, "IsDemo" INTEGER NOT NULL,
            FOREIGN KEY ("WishId") REFERENCES "Wishes" ("Id") ON DELETE CASCADE,
            FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT);
        CREATE INDEX IF NOT EXISTS "IX_WishComments_UserId" ON "WishComments" ("UserId");
        CREATE INDEX IF NOT EXISTS "IX_WishComments_WishId_CreatedAt" ON "WishComments" ("WishId", "CreatedAt");
        """);

    public static async Task SeedAsync(AppDbContext db)
    {
        var samples = new[]
        {
            ("自动选择最优渲染方案", "根据显卡型号、游戏类型和当前场景，自动推荐合适的渲染方案，无需手动调整。", "planned", 256, "玩家小熊"),
            ("支持更多游戏的自动识别", "希望能自动识别 Epic、GOG、战网中的游戏，并显示游戏封面。", "developing", 189, "夜色"),
            ("游戏内菜单自定义快捷键", "希望可以自定义呼出菜单的快捷键。目前默认 End，有些游戏会冲突。", "completed", 142, "Void"),
            ("增加游戏画质对比截图", "希望在游戏详情页查看开启前后的画质对比截图，方便直观了解效果。", "planned", 98, "糖醋排骨"),
            ("为每个游戏保存独立配置", "不同游戏的超分、帧生成和锐化参数可以分别保存，下次启动自动恢复。", "pending", 87, "星河"),
            ("下载任务支持断点续传", "网络中断后继续下载组件，显示下载速度和剩余时间。", "developing", 76, "北极光"),
            ("增加更清晰的安装指引", "初次配置时展示安装步骤和常见问题说明，让新用户更容易上手。", "pending", 64, "橘子汽水"),
            ("增加游戏内性能面板", "希望能查看帧率、帧时间和显存占用，帮助判断配置效果。", "pending", 53, "远山")
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        for (var i = 0; i < samples.Length; i++)
        {
            var id = $"demo-wish-{i + 1:00}";
            if (await db.Wishes.AnyAsync(x => x.Id == id)) continue;
            var (title, content, status, votes, author) = samples[i];
            db.Wishes.Add(new() { Id = id, SubmissionId = id, Title = title, Content = content, Status = status,
                Author = author, DemoVotes = votes, IsDemo = true,
                CreatedAt = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero).AddDays(-i).ToUnixTimeSeconds() });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
