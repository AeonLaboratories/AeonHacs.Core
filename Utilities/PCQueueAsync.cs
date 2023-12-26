using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;


namespace AeonHacs.Utilities
{
	public class PCQueueAsync<T>
	{
		private readonly SemaphoreSlim semaphore;
		private readonly ConcurrentQueue<T> queue;

		public PCQueueAsync()
		{
			semaphore = new SemaphoreSlim(0);
			queue = new ConcurrentQueue<T>();
		}

		public void Enqueue(T item)
		{
			queue.Enqueue(item);
			semaphore.Release();
		}

		public async Task<T> DequeueAsync(CancellationToken cancellationToken = default)
		{
			while (true)
			{
				await semaphore.WaitAsync(cancellationToken);
				if (queue.TryDequeue(out T item))
					return item;
			}
		}
	}
}
