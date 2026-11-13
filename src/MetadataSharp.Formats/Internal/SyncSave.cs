namespace MetadataSharp.Formats.Internal;

/// <summary>
/// Runs the saves of one <c>SaveAll</c> call for a container whose families are written by separate
/// passes: every pass but the last writes to memory, and the next reads what it wrote. Only the last
/// pass touches the destination, so a pass that fails leaves the destination as it was.
/// </summary>
internal static class SyncSave
{
    // Keep Run focused on metadata and its format-specific validation contract.
    internal static void Run<T>(T document, Func<Stream, T> reopen, Stream? destination, string? path,
        List<(Action<T, Stream> ToStream, Action<T, string> ToPath)> steps) where T : class, IDisposable
    {
        T current = document;
        try
        {
            for (int index = 0; index < steps.Count; index++)
            {
                if (index == steps.Count - 1)
                {
                    if (destination is not null) steps[index].ToStream(current, destination);
                    else steps[index].ToPath(current, path!);
                    return;
                }
                var between = new MemoryStream();
                steps[index].ToStream(current, between);
                if (!ReferenceEquals(current, document)) current.Dispose();
                current = document;
                between.Position = 0;
                current = reopen(between);
            }
        }
        finally
        {
            if (!ReferenceEquals(current, document)) current.Dispose();
        }
    }
}
