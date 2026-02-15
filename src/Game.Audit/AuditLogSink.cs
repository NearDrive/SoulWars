namespace Game.Audit;

public sealed class AuditLogSink : IAuditSink, IDisposable
{
    private readonly AuditLogWriter _writer;

    public AuditLogSink(Stream output)
    {
        _writer = new AuditLogWriter(output);
    }

    public void Emit(AuditEvent evt)
    {
        _writer.Append(in evt);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
