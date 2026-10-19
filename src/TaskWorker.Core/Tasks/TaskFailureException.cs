namespace TaskWorker.Core.Tasks;

public abstract class TaskFailureException : Exception
{
    protected TaskFailureException(string message)
        : base(message)
    {
    }

    protected TaskFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public abstract bool IsPermanent { get; }
}

public sealed class TransientTaskException : TaskFailureException
{
    public TransientTaskException(string message)
        : base(message)
    {
    }

    public TransientTaskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override bool IsPermanent => false;
}

public sealed class PermanentTaskException : TaskFailureException
{
    public PermanentTaskException(string message)
        : base(message)
    {
    }

    public PermanentTaskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override bool IsPermanent => true;
}
