//
// LimitedConcurrencyTaskScheduler.cs
//
// Author:
//       Daniel Lacroix <dlacroix@erasme.org>
//
// Copyright (c) 2013 Departement du Rhone
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Erasme.Cloud.Utils
{
	public class LimitedConcurrencyTaskScheduler: TaskScheduler
	{
		[ThreadStatic]
		static bool isProcessing;
		readonly int maximumConcurrency;

		object instanceLock = new object();
		List<Task> tasks = new List<Task>();
		int currentCount = 0;

		public LimitedConcurrencyTaskScheduler(int maximumConcurrency)
		{
			if(maximumConcurrency < 1)
				throw new ArgumentOutOfRangeException("maximumConcurrency");
			this.maximumConcurrency = maximumConcurrency;
		}

		protected sealed override void QueueTask(Task task)
		{
			lock(instanceLock) {
				tasks.Add(task);
				if(currentCount < maximumConcurrency) {
					currentCount++;
					NotifyThreadPoolOfPendingWork();
				}
			}
		}

		private void NotifyThreadPoolOfPendingWork()
		{
			ThreadPool.UnsafeQueueUserWorkItem(_ => {
				isProcessing = true;
				try {
					while(true) {
						Task task;
						lock(instanceLock) {
							if(tasks.Count == 0) {
								currentCount--;
								break;
							}
							task = tasks[0];
							tasks.RemoveAt(0);
						}
						base.TryExecuteTask(task);
					}
				}
				finally {
					isProcessing = false;
				}
			}, null);
		}

		protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
		{
			if(!isProcessing && taskWasPreviouslyQueued && TryDequeue(task))
				return base.TryExecuteTask(task);
			else
				return false;
		}

		protected sealed override bool TryDequeue(Task task)
		{
			lock(instanceLock) {
				return tasks.Remove(task);
			}
		}

		public sealed override int MaximumConcurrencyLevel {
			get {
				return maximumConcurrency;
			}
		}

		protected sealed override IEnumerable<Task> GetScheduledTasks()
		{
			bool lockTaken = false;
			try	{
				Monitor.TryEnter(instanceLock, ref lockTaken);
				if(lockTaken)
					return tasks.ToArray();
				else
					throw new NotSupportedException();
			}
			finally {
				if(lockTaken)
					Monitor.Exit(instanceLock);
			}
		}
	}
}

