namespace MkvFontMux;

public sealed class MuxRunResult
{
    public bool Success { get; set; }
    public int ProcessedFiles { get; set; }
    public int SucceededFiles { get; set; }
    public int FailedFiles { get; set; }
    public List<string> Messages { get; } = [];
    public List<MkvFileRunResult> Files { get; } = [];

    public static MuxRunResult Fail(string message)
    {
        var result = new MuxRunResult { Success = false };
        result.Messages.Add(message);
        return result;
    }
}