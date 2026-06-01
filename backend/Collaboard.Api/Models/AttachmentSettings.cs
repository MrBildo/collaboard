namespace Collaboard.Api.Models;

public record AttachmentSettings
{
    public const string SectionName = "Attachments";

    // The base64/MCP upload cap. MCP payloads arrive base64-encoded inside a JSON tool
    // argument, so they are bounded tighter than the REST multipart path. Overridable
    // via Attachments__MaxMcpUploadBytes.
    public int MaxMcpUploadBytes { get; init; } = 5 * 1024 * 1024;

    // The multipart/form-data REST upload cap. Larger than the MCP cap because REST
    // streams the file rather than carrying it base64-inflated in a JSON body; the
    // Kestrel/FormOptions request-body limits in Program.cs must stay >= this value.
    // Overridable via Attachments__MaxRestUploadBytes.
    public int MaxRestUploadBytes { get; init; } = 50 * 1024 * 1024;
}
