using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using TClient.Game;
using TClient.Protocol;

namespace TClient;

public abstract class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        });

        // 注册会话管理器
        builder.Services.AddSingleton<SessionManager>();

        var app = builder.Build();

        // 静态文件
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwrootPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath)
            });
        }
        else
        {
            // 开发环境
            var devPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            if (Directory.Exists(devPath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(devPath)
                });
            }
        }

        // 默认页面
        app.MapGet("/", () => Results.Redirect("/index.html"));

        // API 路由
        var api = app.MapGroup("/api/game");

        // 加入游戏
        api.MapPost("/join", async (JoinRequest request, SessionManager sessions) =>
        {
            var session = sessions.CreateSession();
            var success = await session.ConnectAsync(request.PlayerName, request.ServerHost);

            if (success) return Results.Ok(new JoinResponse { SessionId = session.SessionId });
            
            await sessions.RemoveSessionAsync(session.SessionId);
            return Results.BadRequest("连接服务器失败"); // 400

        });

        // 获取状态
        api.MapGet("/state", (string sessionId, SessionManager sessions) =>
        {
            var session = sessions.GetSession(sessionId);
            return session == null ? Results.NotFound("会话不存在") : Results.Ok(session.GetState());
        });

        // 执行操作
        api.MapPost("/action", async (ActionRequest request, SessionManager sessions) =>
        {
            var session = sessions.GetSession(request.SessionId);
            if (session == null)
                return Results.NotFound("会话不存在");

            if (!Enum.TryParse<ActionType>(request.Action, true, out var actionType))
                return Results.BadRequest("无效的操作类型");

            await session.SendActionAsync(actionType, request.Amount);
            return Results.Ok();
        });

        // 亮牌
        api.MapPost("/showCards", async (SessionRequest request, SessionManager sessions) =>
        {
            var session = sessions.GetSession(request.SessionId);
            if (session == null)
                return Results.NotFound("会话不存在");

            await session.ShowCardsAsync();
            return Results.Ok();
        });

        // 盖牌
        api.MapPost("/muckCards", async (SessionRequest request, SessionManager sessions) =>
        {
            var session = sessions.GetSession(request.SessionId);
            if (session == null)
                return Results.NotFound("会话不存在");

            await session.MuckCardsAsync();
            return Results.Ok();
        });

        // 获取日志
        api.MapGet("/logs", (string sessionId, SessionManager sessions) =>
        {
            var session = sessions.GetSession(sessionId);
            return session == null ? Results.NotFound("会话不存在") : Results.Ok(session.GetLogs());
        });

        app.Run();
    }
}

public class JoinRequest
{
    public string PlayerName { get; set; } = string.Empty;
    public string? ServerHost { get; set; }
}

public class JoinResponse
{
    public string SessionId { get; set; } = string.Empty;
}

public class ActionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int Amount { get; set; }
}

public class SessionRequest
{
    public string SessionId { get; set; } = string.Empty;
}

[JsonSerializable(typeof(JoinRequest))]
[JsonSerializable(typeof(JoinResponse))]
[JsonSerializable(typeof(ActionRequest))]
[JsonSerializable(typeof(SessionRequest))]
[JsonSerializable(typeof(GameStateDto))]
[JsonSerializable(typeof(List<LogViewDto>))]
[JsonSerializable(typeof(CardViewDto))]
[JsonSerializable(typeof(PlayerViewDto))]
[JsonSerializable(typeof(PotViewDto))]
[JsonSerializable(typeof(ActionViewDto))]
[JsonSerializable(typeof(LogViewDto))]
[JsonSerializable(typeof(List<CardViewDto>))]
[JsonSerializable(typeof(List<PlayerViewDto>))]
[JsonSerializable(typeof(List<PotViewDto>))]
[JsonSerializable(typeof(List<ActionViewDto>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;