using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CursorMcp.Server.Resources;

[McpServerResourceType]
public sealed class HelloWorldResources
{
    [McpServerResource(UriTemplate = "hello://world", Name = "Hello World", MimeType = "text/plain")]
    [Description("A basic hello world resource.")]
    public static string HelloWorld() => "Hello, world!";
}
