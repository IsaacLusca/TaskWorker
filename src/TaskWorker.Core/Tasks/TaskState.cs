namespace TaskWorker.Core.Tasks;

public enum TaskState
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
