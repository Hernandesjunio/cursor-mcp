using System.Text.Json;

namespace CursorMcp.Server;

internal static class AgentDebugLog
{
    private const string LogPath = @"c:\_projeto\cursor-mcp\debug-1370e5.log";

    public static void Write(string hypothesisId, string location, string message, object data)
    {
        // #region agent log
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                sessionId = "1370e5",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                hypothesisId,
                location,
                message,
                data
            });
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
        }
        // #endregion
    }
}
