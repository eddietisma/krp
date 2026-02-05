using System.Threading;
using System.Threading.Tasks;

namespace Krp.Validation;

public class ValidationState
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        return _completion.Task.WaitAsync(cancellationToken);
    }

    public void MarkCompleted(bool succeeded)
    {
        _completion.TrySetResult(succeeded);
    }

    public bool IsCompleted => _completion.Task.IsCompleted;
    public bool Succeeded => _completion.Task is { IsCompletedSuccessfully: true, Result: true };
}
