using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CursorMcp.Server.Tools;

[McpServerToolType]
public sealed class HelloWorldTools
{
    [McpServerTool(Name = "hello_world"), Description("Returns a hello world greeting from the authenticated MCP spike.")]
    public static string HelloWorld() => "Hello, world!";
}
