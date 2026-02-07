using System.Threading;
using System.Threading.Tasks;

namespace Krp.Validation;

public class ValidationState
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource<bool> _firstCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> _validSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isValid;

    /// <summary>
    /// Wait until validation of required components is successful. This is used to delay starting of components.
    /// </summary>
    public Task WaitForValidAsync(CancellationToken cancellationToken)
    {
        return _validSignal.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Completes after the first validation run (true on success, false on failure).
    /// </summary>
    public Task<bool> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        return _firstCompletion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Records the first validation result and updates the current validity state.
    /// </summary>
    public void MarkCompleted(bool succeeded)
    {
        _firstCompletion.TrySetResult(succeeded);
        
        lock (_lock)
        {
            if (succeeded)
            {
                _isValid = true;
                _validSignal.TrySetResult(true);
                return;
            }

            if (_isValid)
            {
                _isValid = false;
                _validSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return;
            }

            _isValid = false;
        }
    }
    
    public bool IsCompleted => _firstCompletion.Task.IsCompleted;
    public bool IsValid => _isValid;
}
