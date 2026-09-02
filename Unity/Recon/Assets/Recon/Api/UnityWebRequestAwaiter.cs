using System;
using System.Runtime.CompilerServices;
using UnityEngine.Networking;

namespace Recon.Api
{
    /// <summary>
    /// Lets <c>await request.SendWebRequest()</c> work without pulling in a coroutine wrapper or a
    /// third-party async package. Small on purpose: the whole thing is "call the continuation when
    /// the operation reports done".
    ///
    /// The continuation runs on Unity's main thread, because
    /// <see cref="UnityWebRequestAsyncOperation.completed"/> is raised there. That matters — every
    /// caller of <see cref="ReconApiClient"/> goes on to touch Transforms and GameObjects.
    ///
    /// Downloads use a polling loop instead (see ReconApiClient.DownloadAssetAsync) because an
    /// awaiter reports nothing until it is finished, and a 45 MB pull over the tailnet needs a
    /// progress bar.
    /// </summary>
    public readonly struct UnityWebRequestAwaiter : INotifyCompletion
    {
        readonly UnityWebRequestAsyncOperation m_operation;

        public UnityWebRequestAwaiter(UnityWebRequestAsyncOperation operation)
        {
            m_operation = operation;
        }

        public bool IsCompleted => m_operation == null || m_operation.isDone;

        public void OnCompleted(Action continuation)
        {
            if (continuation == null) return;
            if (m_operation == null || m_operation.isDone)
            {
                continuation();
                return;
            }
            m_operation.completed += _ => continuation();
        }

        /// <summary>
        /// Nothing to return and nothing to throw: the request object carries the result, and
        /// ReconApiClient turns a non-2xx into an <see cref="ApiException"/> with the server's own
        /// message so the throw happens where the URL is still in scope.
        /// </summary>
        public void GetResult() { }
    }

    public static class UnityWebRequestAwaiterExtensions
    {
        public static UnityWebRequestAwaiter GetAwaiter(this UnityWebRequestAsyncOperation operation) =>
            new UnityWebRequestAwaiter(operation);
    }
}
